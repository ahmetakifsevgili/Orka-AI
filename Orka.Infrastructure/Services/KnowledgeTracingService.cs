using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class KnowledgeTracingService : IKnowledgeTracingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const decimal DefaultPrior = 0.35m;
    private const decimal DefaultLearnRate = 0.18m;
    private const decimal DefaultSlip = 0.10m;
    private const decimal DefaultGuess = 0.20m;
    private const decimal DefaultDecay = 0.02m;

    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService? _redis;
    private readonly ILogger<KnowledgeTracingService> _logger;

    public KnowledgeTracingService(
        OrkaDbContext db,
        IRedisMemoryService? redis,
        ILogger<KnowledgeTracingService> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<KnowledgeTracingStateDto?> UpdateFromAttemptAsync(
        QuizAttempt attempt,
        CancellationToken ct = default)
    {
        var conceptKey = ExtractMetadata(attempt.SourceRefsJson, "conceptKey") ??
                         ExtractMetadata(attempt.SourceRefsJson, "conceptTag") ??
                         attempt.SkillTag;
        if (string.IsNullOrWhiteSpace(conceptKey))
        {
            return null;
        }

        var assessmentItem = attempt.AssessmentItemId.HasValue
            ? await _db.AssessmentItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == attempt.AssessmentItemId.Value, ct)
            : null;
        var itemStat = attempt.AssessmentItemId.HasValue
            ? await _db.AssessmentItemStats.AsNoTracking().FirstOrDefaultAsync(s => s.AssessmentItemId == attempt.AssessmentItemId.Value, ct)
            : null;
        var label = assessmentItem?.ConceptLabel ??
                    ExtractMetadata(attempt.SourceRefsJson, "conceptLabel") ??
                    attempt.SkillTag ??
                    conceptKey;

        var state = await _db.KnowledgeTracingStates
            .FirstOrDefaultAsync(s =>
                s.UserId == attempt.UserId &&
                s.TopicId == attempt.TopicId &&
                s.ConceptKey == conceptKey,
                ct);
        if (state == null)
        {
            state = new KnowledgeTracingState
            {
                Id = Guid.NewGuid(),
                UserId = attempt.UserId,
                TopicId = attempt.TopicId,
                ConceptGraphSnapshotId = assessmentItem?.ConceptGraphSnapshotId,
                ConceptKey = conceptKey,
                Label = label,
                PriorMastery = DefaultPrior,
                LearnRate = DefaultLearnRate,
                Slip = DefaultSlip,
                Guess = DefaultGuess,
                Decay = DefaultDecay,
                MasteryProbability = DefaultPrior,
                Confidence = 0.20m,
                CreatedAt = DateTime.UtcNow
            };
            _db.KnowledgeTracingStates.Add(state);
        }

        var guess = CalibrateGuess(itemStat);
        var slip = CalibrateSlip(itemStat);
        state.Guess = guess;
        state.Slip = slip;

        var current = ApplyDecay(state.MasteryProbability, state.Decay, state.LastEvidenceAt, attempt.CreatedAt);
        var posterior = attempt.IsCorrect
            ? SafeDivide(current * (1m - slip), current * (1m - slip) + (1m - current) * guess)
            : SafeDivide(current * slip, current * slip + (1m - current) * (1m - guess));
        var learned = posterior + (1m - posterior) * state.LearnRate;
        if (attempt.WasSkipped)
        {
            learned = Math.Min(learned, current);
        }

        state.Label = label;
        state.ConceptGraphSnapshotId ??= assessmentItem?.ConceptGraphSnapshotId;
        state.EvidenceCount += 1;
        if (attempt.IsCorrect) state.CorrectCount += 1;
        else state.IncorrectCount += 1;
        state.MasteryProbability = Clamp(Math.Round(learned, 4), 0.02m, 0.98m);
        state.Confidence = ComputeConfidence(state.EvidenceCount, state.CorrectCount, state.IncorrectCount, attempt.ConfidenceSelfRating);
        state.RemediationNeed = ComputeRemediation(state.MasteryProbability, state.Confidence, state.EvidenceCount, state.IncorrectCount);
        state.PracticeReadiness = ComputeReadiness(state.MasteryProbability, state.Confidence, state.EvidenceCount);
        state.LastEvidenceAt = attempt.CreatedAt;
        state.UpdatedAt = DateTime.UtcNow;

        await UpsertConceptMasteryAsync(state, ct);
        await _db.SaveChangesAsync(ct);

        var dto = ToDto(state);
        await CacheLearnerStateAsync(state.UserId, state.TopicId, ct);
        return dto;
    }

    public async Task<IReadOnlyList<KnowledgeTracingStateDto>> UpdateFromDiagnosticProfileAsync(
        DiagnosticProfileDto profile,
        CancellationToken ct = default)
    {
        var updated = new List<KnowledgeTracingStateDto>();
        foreach (var mastery in profile.ConceptMasteries.Where(m => !string.IsNullOrWhiteSpace(m.ConceptKey)))
        {
            var state = await _db.KnowledgeTracingStates
                .FirstOrDefaultAsync(s =>
                    s.UserId == profile.UserId &&
                    s.TopicId == profile.TopicId &&
                    s.ConceptKey == mastery.ConceptKey,
                    ct);
            if (state == null)
            {
                state = new KnowledgeTracingState
                {
                    Id = Guid.NewGuid(),
                    UserId = profile.UserId,
                    TopicId = profile.TopicId,
                    ConceptGraphSnapshotId = profile.ConceptGraphSnapshotId,
                    ConceptKey = mastery.ConceptKey,
                    CreatedAt = DateTime.UtcNow
                };
                _db.KnowledgeTracingStates.Add(state);
            }

            state.Label = string.IsNullOrWhiteSpace(mastery.Label) ? mastery.ConceptKey : mastery.Label;
            state.ConceptGraphSnapshotId ??= profile.ConceptGraphSnapshotId;
            state.PriorMastery = DefaultPrior;
            state.LearnRate = DefaultLearnRate;
            state.Slip = DefaultSlip;
            state.Guess = DefaultGuess;
            state.Decay = DefaultDecay;
            state.EvidenceCount = Math.Max(state.EvidenceCount, mastery.Attempts);
            state.CorrectCount = Math.Max(state.CorrectCount, mastery.Correct);
            state.IncorrectCount = Math.Max(state.IncorrectCount, Math.Max(0, mastery.Attempts - mastery.Correct));
            state.MasteryProbability = Clamp(Math.Round(mastery.MasteryScore / 100m, 4), 0.02m, 0.98m);
            state.Confidence = Clamp(mastery.Confidence, 0m, state.EvidenceCount < 3 ? 0.55m : 0.95m);
            state.RemediationNeed = ComputeRemediation(state.MasteryProbability, state.Confidence, state.EvidenceCount, state.IncorrectCount);
            state.PracticeReadiness = ComputeReadiness(state.MasteryProbability, state.Confidence, state.EvidenceCount);
            state.LastEvidenceAt = DateTime.UtcNow;
            state.UpdatedAt = DateTime.UtcNow;

            await UpsertConceptMasteryAsync(state, ct);
            updated.Add(ToDto(state));
        }

        await _db.SaveChangesAsync(ct);
        await CacheLearnerStateAsync(profile.UserId, profile.TopicId, ct);
        return updated;
    }

    public async Task<IReadOnlyList<KnowledgeTracingStateDto>> GetRecentStatesAsync(
        Guid userId,
        Guid? topicId,
        int take = 12,
        CancellationToken ct = default)
    {
        var query = _db.KnowledgeTracingStates.AsNoTracking().Where(s => s.UserId == userId);
        if (topicId.HasValue) query = query.Where(s => s.TopicId == topicId.Value);
        var rows = await query
            .OrderByDescending(s => s.UpdatedAt)
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    private async Task UpsertConceptMasteryAsync(KnowledgeTracingState state, CancellationToken ct)
    {
        var mastery = await _db.ConceptMasteries
            .FirstOrDefaultAsync(m =>
                m.UserId == state.UserId &&
                m.TopicId == state.TopicId &&
                m.ConceptKey == state.ConceptKey,
                ct);
        if (mastery == null)
        {
            mastery = new ConceptMastery
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                TopicId = state.TopicId,
                ConceptGraphSnapshotId = state.ConceptGraphSnapshotId,
                ConceptKey = state.ConceptKey,
                CreatedAt = DateTime.UtcNow
            };
            _db.ConceptMasteries.Add(mastery);
        }

        mastery.Label = state.Label;
        mastery.ConceptGraphSnapshotId ??= state.ConceptGraphSnapshotId;
        mastery.MasteryScore = Math.Round(state.MasteryProbability * 100m, 2);
        mastery.Confidence = state.Confidence;
        mastery.RemediationNeed = state.RemediationNeed;
        mastery.PracticeReadiness = state.PracticeReadiness;
        mastery.MisconceptionEvidenceJson = "[]";
        mastery.Attempts = state.EvidenceCount;
        mastery.Correct = state.CorrectCount;
        mastery.LastEvidenceAt = state.LastEvidenceAt;
        mastery.UpdatedAt = DateTime.UtcNow;
    }

    private async Task CacheLearnerStateAsync(Guid userId, Guid? topicId, CancellationToken ct)
    {
        if (_redis == null) return;
        try
        {
            var states = await GetRecentStatesAsync(userId, topicId, 20, ct);
            var key = $"orka:v2:learner-state:{userId:N}:{(topicId.HasValue ? topicId.Value.ToString("N") : "global")}";
            await _redis.SetJsonAsync(key, JsonSerializer.Serialize(states, JsonOptions), TimeSpan.FromHours(6));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[KnowledgeTracing] Redis learner state write skipped. UserId={UserId} TopicId={TopicId}", userId, topicId);
        }
    }

    private static decimal ApplyDecay(decimal masteryProbability, decimal decay, DateTime? lastEvidenceAt, DateTime now)
    {
        if (!lastEvidenceAt.HasValue) return masteryProbability;
        var days = Math.Max(0, (now - lastEvidenceAt.Value).TotalDays);
        if (days <= 0) return masteryProbability;
        var factor = 1m - Math.Min(0.40m, decay * (decimal)days);
        return Clamp(masteryProbability * factor, 0.02m, 0.98m);
    }

    private static decimal CalibrateGuess(AssessmentItemStat? itemStat)
    {
        var correctRate = itemStat?.CorrectRate ?? 0.50m;
        return Clamp(Math.Round(0.10m + correctRate * 0.15m, 4), 0.10m, 0.30m);
    }

    private static decimal CalibrateSlip(AssessmentItemStat? itemStat)
    {
        var correctRate = itemStat?.CorrectRate ?? 0.50m;
        return Clamp(Math.Round(0.05m + (1m - correctRate) * 0.15m, 4), 0.05m, 0.25m);
    }

    private static decimal ComputeConfidence(int evidenceCount, int correct, int incorrect, decimal? selfRating = null)
    {
        var confidence = 0.20m + evidenceCount * 0.12m;
        if (evidenceCount >= 3) confidence += 0.15m;
        if (correct > 0 && incorrect > 0) confidence += 0.05m;
        if (selfRating.HasValue)
        {
            confidence = (confidence * 0.85m) + (Clamp(selfRating.Value, 0m, 1m) * 0.15m);
        }
        return Clamp(Math.Round(confidence, 4), 0m, evidenceCount < 3 ? 0.55m : 0.95m);
    }

    private static string ComputeRemediation(decimal masteryProbability, decimal confidence, int evidenceCount, int incorrectCount = 0)
    {
        if (evidenceCount < 3 || confidence < 0.60m) return "evidence_insufficient";
        if (incorrectCount >= 2 && confidence >= 0.60m && masteryProbability < 0.70m) return "high";
        if (masteryProbability >= 0.80m) return "none";
        if (masteryProbability >= 0.55m) return "medium";
        return "high";
    }

    private static string ComputeReadiness(decimal masteryProbability, decimal confidence, int evidenceCount)
    {
        if (evidenceCount < 3 || confidence < 0.60m) return "evidence_insufficient";
        if (masteryProbability >= 0.80m) return "independent";
        if (masteryProbability >= 0.55m) return "guided";
        return "remedial";
    }

    private static decimal SafeDivide(decimal numerator, decimal denominator) =>
        denominator == 0m ? 0m : numerator / denominator;

    private static decimal Clamp(decimal value, decimal min, decimal max) =>
        Math.Min(max, Math.Max(min, value));

    private static KnowledgeTracingStateDto ToDto(KnowledgeTracingState state) => new()
    {
        Id = state.Id,
        UserId = state.UserId,
        TopicId = state.TopicId,
        ConceptGraphSnapshotId = state.ConceptGraphSnapshotId,
        ConceptKey = state.ConceptKey,
        Label = state.Label,
        PriorMastery = state.PriorMastery,
        LearnRate = state.LearnRate,
        Slip = state.Slip,
        Guess = state.Guess,
        Decay = state.Decay,
        EvidenceCount = state.EvidenceCount,
        CorrectCount = state.CorrectCount,
        IncorrectCount = state.IncorrectCount,
        MasteryProbability = state.MasteryProbability,
        Confidence = state.Confidence,
        RemediationNeed = state.RemediationNeed,
        PracticeReadiness = state.PracticeReadiness,
        LastEvidenceAt = state.LastEvidenceAt
    };

    private static string? ExtractMetadata(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(key, out var value)
                ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}

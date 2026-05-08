using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class DiagnosticProfileBuilder : IDiagnosticProfileBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService? _redis;
    private readonly IConceptMasteryService _mastery;
    private readonly IKnowledgeTracingService? _knowledgeTracing;
    private readonly ILogger<DiagnosticProfileBuilder> _logger;

    public DiagnosticProfileBuilder(
        OrkaDbContext db,
        IRedisMemoryService? redis,
        IConceptMasteryService mastery,
        ILogger<DiagnosticProfileBuilder> logger,
        IKnowledgeTracingService? knowledgeTracing = null)
    {
        _db = db;
        _redis = redis;
        _mastery = mastery;
        _knowledgeTracing = knowledgeTracing;
        _logger = logger;
    }

    public async Task<DiagnosticProfileDto> BuildAndSaveAsync(
        PlanDiagnosticStateDto state,
        IReadOnlyList<QuizAttempt> attempts,
        CancellationToken ct = default)
    {
        var profile = BuildProfile(state, attempts);
        var entity = await _db.DiagnosticProfiles
            .FirstOrDefaultAsync(p => p.UserId == state.UserId && p.PlanRequestId == state.PlanRequestId, ct);
        if (entity == null)
        {
            entity = new DiagnosticProfile
            {
                Id = profile.Id,
                UserId = state.UserId,
                TopicId = state.TopicId,
                QuizRunId = state.QuizRunId,
                PlanRequestId = state.PlanRequestId,
                ConceptGraphSnapshotId = state.ConceptGraphSnapshotId,
                CreatedAt = DateTime.UtcNow
            };
            _db.DiagnosticProfiles.Add(entity);
        }

        entity.AnsweredCount = profile.AnsweredCount;
        entity.CorrectCount = profile.CorrectCount;
        entity.AccuracyPercent = profile.AccuracyPercent;
        entity.MeasuredLevel = profile.MeasuredLevel;
        entity.ProfileJson = JsonSerializer.Serialize(profile, JsonOptions);
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        profile.Id = entity.Id;
        await _mastery.UpdateFromDiagnosticProfileAsync(profile, ct);
        if (_knowledgeTracing != null)
        {
            await _knowledgeTracing.UpdateFromDiagnosticProfileAsync(profile, ct);
        }

        var activeDiagnosticKey = $"orka:v2:active-diagnostic:{state.PlanRequestId:N}";
        if (_redis != null)
        {
            try
            {
                await _redis.SetJsonAsync(activeDiagnosticKey, JsonSerializer.Serialize(profile, JsonOptions), TimeSpan.FromHours(6));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[DiagnosticProfile] Redis write skipped. Key={Key}", activeDiagnosticKey);
            }
        }

        return profile;
    }

    public string BuildPromptBlock(DiagnosticProfileDto profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[DIAGNOSTIC PROFILE - CONCEPT MASTERY]");
        sb.AppendLine($"DiagnosticProfileId: {profile.Id}");
        sb.AppendLine($"Answered: {profile.AnsweredCount}");
        sb.AppendLine($"Correct: {profile.CorrectCount}");
        sb.AppendLine($"AccuracyPercent: {profile.AccuracyPercent}");
        sb.AppendLine($"MeasuredLevel: {profile.MeasuredLevel}");
        sb.AppendLine("ConceptMastery:");
        foreach (var mastery in profile.ConceptMasteries.OrderBy(m => m.MasteryScore).ThenByDescending(m => m.Attempts).Take(16))
        {
            sb.AppendLine($"- {mastery.ConceptKey}: score={mastery.MasteryScore:0}; confidence={mastery.Confidence:0.00}; remediation={mastery.RemediationNeed}; readiness={mastery.PracticeReadiness}; attempts={mastery.Attempts}; correct={mastery.Correct}; misconception={string.Join(" / ", mastery.MisconceptionEvidence.Take(2))}");
        }
        sb.AppendLine("Instruction: Fast-track high mastery concepts. Slow down, repair misconceptions, and add guided practice for concepts with high remediation need.");
        return sb.ToString();
    }

    private static DiagnosticProfileDto BuildProfile(PlanDiagnosticStateDto state, IReadOnlyList<QuizAttempt> attempts)
    {
        var answered = attempts.Count;
        var correct = attempts.Count(a => a.IsCorrect);
        var accuracy = answered == 0 ? 0 : (int)Math.Round(correct * 100.0 / answered);
        var groups = attempts
            .GroupBy(ConceptKeyOf, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(g =>
            {
                var total = g.Count();
                var ok = g.Count(a => a.IsCorrect);
                var score = total == 0 ? 0 : Math.Round(ok * 100m / total, 2);
                var confidence = Math.Min(0.95m, 0.30m + total * 0.16m + (total >= 3 ? 0.15m : 0m));
                var wrong = g.Where(a => !a.IsCorrect).ToList();
                var misconceptions = wrong
                    .Select(MisconceptionOf)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList();
                return new ConceptMasteryDto
                {
                    ConceptKey = g.Key,
                    Label = FirstNonBlank(g.Select(LabelOf).ToArray()) ?? g.Key,
                    MasteryScore = score,
                    Confidence = Math.Round(confidence, 2),
                    RemediationNeed = score >= 80 ? "none" : score >= 50 ? "medium" : "high",
                    PracticeReadiness = score >= 80 ? "independent" : score >= 50 ? "guided" : "remedial",
                    MisconceptionEvidence = misconceptions,
                    Attempts = total,
                    Correct = ok
                };
            })
            .OrderBy(m => m.MasteryScore)
            .ThenByDescending(m => m.Attempts)
            .ToList();

        return new DiagnosticProfileDto
        {
            Id = Guid.NewGuid(),
            UserId = state.UserId,
            TopicId = state.TopicId,
            QuizRunId = state.QuizRunId,
            PlanRequestId = state.PlanRequestId,
            ConceptGraphSnapshotId = state.ConceptGraphSnapshotId,
            AnsweredCount = answered,
            CorrectCount = correct,
            AccuracyPercent = accuracy,
            MeasuredLevel = accuracy switch
            {
                >= 85 => "advanced",
                >= 65 => "intermediate",
                >= 40 => "developing",
                _ => "beginner"
            },
            ConceptMasteries = groups,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static string ConceptKeyOf(QuizAttempt attempt) =>
        ExtractMetadata(attempt.SourceRefsJson, "conceptKey") ??
        ExtractMetadata(attempt.SourceRefsJson, "conceptTag") ??
        attempt.SkillTag ??
        attempt.TopicPath ??
        "unknown";

    private static string LabelOf(QuizAttempt attempt) =>
        ExtractMetadata(attempt.SourceRefsJson, "conceptLabel") ??
        attempt.SkillTag ??
        attempt.TopicPath ??
        ConceptKeyOf(attempt);

    private static string MisconceptionOf(QuizAttempt attempt) =>
        ExtractMetadata(attempt.SourceRefsJson, "misconceptionTarget") ??
        ExtractMetadata(attempt.SourceRefsJson, "expectedMisconceptionCategory") ??
        ExtractMetadata(attempt.SourceRefsJson, "mistakeCategory") ??
        attempt.CognitiveType ??
        "IncorrectAnswer";

    private static string? ExtractMetadata(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(key, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                if (value.ValueKind == JsonValueKind.Array)
                {
                    return string.Join(",", value.EnumerateArray().Select(v => v.ToString()));
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

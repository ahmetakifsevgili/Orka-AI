using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class AssessmentQualityService : IAssessmentQualityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService? _redis;
    private readonly ILogger<AssessmentQualityService> _logger;

    public AssessmentQualityService(
        OrkaDbContext db,
        IRedisMemoryService? redis,
        ILogger<AssessmentQualityService> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<AssessmentQualityDto> EvaluateAndSaveAsync(
        Guid userId,
        Guid? topicId,
        Guid? planRequestId,
        Guid? quizRunId,
        AssessmentGrammarDto grammar,
        ConceptGraphDto graph,
        CancellationToken ct = default)
    {
        var items = grammar.Items.Where(i => !string.IsNullOrWhiteSpace(i.ConceptKey)).ToList();
        var conceptDenominator = graph.Concepts.Count > 0
            ? graph.Concepts.Select(c => c.StableKey).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            : Math.Max(items.Select(i => i.ConceptKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(), 1);
        var itemCount = Math.Max(items.Count, 1);
        var conceptCoverage = Ratio(items.Select(i => i.ConceptKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Math.Max(conceptDenominator, 1));
        conceptCoverage = Math.Min(1m, conceptCoverage);
        var outcomeCoverage = Ratio(items.Count(i => i.LearningOutcomeKeys.Count > 0), itemCount);
        var cognitiveSpread = items.Select(i => i.CognitiveSkill).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var difficultySpread = items.Select(i => i.Difficulty).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var misconceptionRatio = Ratio(items.Count(i => !string.IsNullOrWhiteSpace(i.MisconceptionTarget)), itemCount);
        var optionQualityRatio = Ratio(items.Count(i => i.OptionQualityRules.Count >= 3), itemCount);
        var scoringRatio = Ratio(items.Count(i => !string.IsNullOrWhiteSpace(i.ScoringRule)), itemCount);

        var failures = new List<string>();
        if (items.Count == 0) failures.Add("assessment_items_empty");
        if (conceptCoverage < 0.80m) failures.Add("concept_coverage_low");
        if (outcomeCoverage < 0.80m) failures.Add("learning_outcome_coverage_low");
        if (difficultySpread < 2) failures.Add("difficulty_spread_low");
        if (cognitiveSpread < 3) failures.Add("cognitive_skill_spread_low");
        if (misconceptionRatio < 0.10m) failures.Add("misconception_targeting_low");
        if (optionQualityRatio < 0.90m) failures.Add("option_quality_low");
        if (scoringRatio < 1.00m) failures.Add("scoring_rule_missing");

        var status = items.Count == 0
            ? "critical"
            : failures.Count == 0 ? "healthy" : "degraded";

        var entity = new AssessmentQualityRun
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            AssessmentDraftId = grammar.DraftId,
            PlanRequestId = planRequestId,
            QuizRunId = quizRunId,
            ConceptGraphSnapshotId = grammar.ConceptGraphSnapshotId == Guid.Empty ? null : grammar.ConceptGraphSnapshotId,
            QualityStatus = status,
            ConceptCoverage = conceptCoverage,
            LearningOutcomeCoverage = outcomeCoverage,
            CognitiveSkillSpread = cognitiveSpread,
            DifficultySpread = difficultySpread,
            MisconceptionTargetingRatio = misconceptionRatio,
            OptionQualityRatio = optionQualityRatio,
            ScoringRulePresenceRatio = scoringRatio,
            FailuresJson = JsonSerializer.Serialize(failures, JsonOptions),
            CreatedAt = DateTime.UtcNow
        };

        _db.AssessmentQualityRuns.Add(entity);
        await _db.SaveChangesAsync(ct);

        var dto = ToDto(entity);
        await TryCacheAsync($"orka:v2:assessment-quality:{grammar.DraftId:N}", dto, TimeSpan.FromHours(6));
        return dto;
    }

    public async Task<AssessmentQualityDto?> GetLatestAsync(
        Guid userId,
        Guid? topicId,
        Guid? draftId = null,
        CancellationToken ct = default)
    {
        var query = _db.AssessmentQualityRuns.AsNoTracking().Where(q => q.UserId == userId);
        if (topicId.HasValue) query = query.Where(q => q.TopicId == topicId.Value);
        if (draftId.HasValue) query = query.Where(q => q.AssessmentDraftId == draftId.Value);

        var entity = await query.OrderByDescending(q => q.CreatedAt).FirstOrDefaultAsync(ct);
        return entity == null ? null : ToDto(entity);
    }

    public async Task<AssessmentItemStatDto?> UpdateItemStatsAsync(
        QuizAttempt attempt,
        CancellationToken ct = default)
    {
        if (!attempt.AssessmentItemId.HasValue)
        {
            return null;
        }

        var assessmentItem = await _db.AssessmentItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == attempt.AssessmentItemId.Value, ct);

        var stat = await _db.AssessmentItemStats
            .FirstOrDefaultAsync(s => s.AssessmentItemId == attempt.AssessmentItemId.Value, ct);
        if (stat == null)
        {
            stat = new AssessmentItemStat
            {
                Id = Guid.NewGuid(),
                AssessmentItemId = attempt.AssessmentItemId.Value,
                UserId = attempt.UserId,
                TopicId = attempt.TopicId,
                ConceptGraphSnapshotId = assessmentItem?.ConceptGraphSnapshotId,
                ConceptKey = assessmentItem?.ConceptKey ?? ExtractMetadata(attempt.SourceRefsJson, "conceptKey") ?? attempt.SkillTag ?? string.Empty,
                CreatedAt = DateTime.UtcNow
            };
            _db.AssessmentItemStats.Add(stat);
        }

        stat.Attempts += 1;
        if (attempt.WasSkipped) stat.Skipped += 1;
        if (attempt.IsCorrect) stat.Correct += 1;
        else stat.Incorrect += 1;
        stat.CorrectRate = stat.Attempts == 0 ? 0m : Math.Round(stat.Correct / (decimal)stat.Attempts, 4);
        stat.SkipRate = stat.Attempts == 0 ? 0m : Math.Round(stat.Skipped / (decimal)stat.Attempts, 4);
        if (attempt.ResponseTimeMs.HasValue && attempt.ResponseTimeMs.Value > 0)
        {
            var responseSeconds = Math.Round(attempt.ResponseTimeMs.Value / 1000m, 2);
            stat.LastResponseTimeSeconds = responseSeconds;
            stat.TotalTimeSeconds += responseSeconds;
            stat.AverageTimeSeconds = Math.Round(stat.TotalTimeSeconds / Math.Max(1, stat.Attempts - stat.Skipped), 2);
        }

        var skipPenalty = Math.Min(0.25m, stat.SkipRate * 0.25m);
        stat.DiscriminationProxy = Math.Round((stat.CorrectRate - 0.50m) - skipPenalty, 4);
        stat.QualityStatus = stat.Attempts < 5 ? "insufficient_data" :
            stat.SkipRate > 0.35m || stat.CorrectRate is < 0.20m or > 0.95m ? "needs_review" : "healthy";
        AssessmentCalibrationService.ApplyCalibration(stat);
        stat.LastAttemptAt = attempt.CreatedAt;
        stat.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new AssessmentItemStatDto
        {
            Id = stat.Id,
            AssessmentItemId = stat.AssessmentItemId,
            Attempts = stat.Attempts,
            Correct = stat.Correct,
            Incorrect = stat.Incorrect,
            Skipped = stat.Skipped,
            CorrectRate = stat.CorrectRate,
            DiscriminationProxy = stat.DiscriminationProxy,
            TotalTimeSeconds = stat.TotalTimeSeconds,
            LastResponseTimeSeconds = stat.LastResponseTimeSeconds,
            AverageTimeSeconds = stat.AverageTimeSeconds,
            SkipRate = stat.SkipRate,
            QualityStatus = stat.QualityStatus,
            DifficultyEstimate = stat.DifficultyEstimate,
            DiscriminationEstimate = stat.DiscriminationEstimate,
            ExposureCount = stat.ExposureCount,
            LastSelectedAt = stat.LastSelectedAt,
            CalibrationStatus = stat.CalibrationStatus
        };
    }

    private async Task TryCacheAsync(string key, AssessmentQualityDto dto, TimeSpan ttl)
    {
        if (_redis == null) return;
        try
        {
            await _redis.SetJsonAsync(key, JsonSerializer.Serialize(dto, JsonOptions), ttl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[AssessmentQuality] Redis write skipped. Key={Key}", key);
        }
    }

    private static AssessmentQualityDto ToDto(AssessmentQualityRun entity) => new()
    {
        Id = entity.Id,
        AssessmentDraftId = entity.AssessmentDraftId,
        PlanRequestId = entity.PlanRequestId,
        QuizRunId = entity.QuizRunId,
        ConceptGraphSnapshotId = entity.ConceptGraphSnapshotId,
        QualityStatus = entity.QualityStatus,
        ConceptCoverage = entity.ConceptCoverage,
        LearningOutcomeCoverage = entity.LearningOutcomeCoverage,
        CognitiveSkillSpread = entity.CognitiveSkillSpread,
        DifficultySpread = entity.DifficultySpread,
        MisconceptionTargetingRatio = entity.MisconceptionTargetingRatio,
        OptionQualityRatio = entity.OptionQualityRatio,
        ScoringRulePresenceRatio = entity.ScoringRulePresenceRatio,
        Failures = DeserializeList(entity.FailuresJson),
        GeneratedAt = entity.CreatedAt
    };

    private static decimal Ratio(int numerator, int denominator) =>
        denominator <= 0 ? 0m : Math.Round(numerator / (decimal)denominator, 4);

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string? ExtractMetadata(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(key, out var value)
                ? value.ToString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}

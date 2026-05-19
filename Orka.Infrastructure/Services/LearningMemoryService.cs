using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Chat;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class LearningMemoryService : ILearningMemoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly OrkaDbContext _db;

    public LearningMemoryService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<LearningMemoryLiteDto> BuildAsync(
        Guid userId,
        IReadOnlyCollection<Guid> topicScopeIds,
        EvidenceQualityDto? evidenceQuality = null,
        CancellationToken ct = default)
    {
        var scope = topicScopeIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        var states = await _db.KnowledgeTracingStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && (scope.Length == 0 || (s.TopicId.HasValue && scope.Contains(s.TopicId.Value))))
            .OrderByDescending(s => s.UpdatedAt)
            .Take(80)
            .Select(s => new
            {
                s.TopicId,
                s.ConceptKey,
                s.Label,
                s.MasteryProbability,
                s.Confidence,
                s.EvidenceCount,
                s.CorrectCount,
                s.IncorrectCount,
                s.RemediationNeed,
                s.PracticeReadiness,
                s.LastEvidenceAt,
                s.UpdatedAt
            })
            .ToListAsync(ct);

        var masteries = await _db.ConceptMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId && (scope.Length == 0 || (m.TopicId.HasValue && scope.Contains(m.TopicId.Value))))
            .OrderByDescending(m => m.UpdatedAt)
            .Take(80)
            .Select(m => new
            {
                m.TopicId,
                m.ConceptKey,
                m.Label,
                m.MasteryScore,
                m.Confidence,
                m.RemediationNeed,
                m.MisconceptionEvidenceJson,
                m.Attempts,
                m.Correct,
                m.LastEvidenceAt,
                m.UpdatedAt
            })
            .ToListAsync(ct);

        var signals = await _db.LearningSignals
            .AsNoTracking()
            .Where(s => s.UserId == userId && (scope.Length == 0 || (s.TopicId.HasValue && scope.Contains(s.TopicId.Value))))
            .OrderByDescending(s => s.CreatedAt)
            .Take(120)
            .Select(s => new
            {
                s.TopicId,
                s.SignalType,
                s.SkillTag,
                s.TopicPath,
                s.IsPositive,
                s.PayloadJson,
                s.CreatedAt
            })
            .ToListAsync(ct);

        var attempts = await _db.QuizAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && (scope.Length == 0 || (a.TopicId.HasValue && scope.Contains(a.TopicId.Value))))
            .OrderByDescending(a => a.CreatedAt)
            .Take(120)
            .Select(a => new
            {
                a.TopicId,
                a.SkillTag,
                a.TopicPath,
                a.IsCorrect,
                a.SourceRefsJson,
                a.CreatedAt
            })
            .ToListAsync(ct);

        var topicIds = states.Select(s => s.TopicId)
            .Concat(masteries.Select(m => m.TopicId))
            .Concat(signals.Select(s => s.TopicId))
            .Concat(attempts.Select(a => a.TopicId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var topicTitles = await _db.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId && topicIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Title })
            .ToDictionaryAsync(t => t.Id, t => t.Title, ct);

        var signalProjections = signals.Select(s => ParseSignal(s.PayloadJson)).ToList();
        var usableCount = signalProjections.Count(s => s.Confidence?.Status == "usable");
        var observedCount = signalProjections.Count(s => s.Confidence?.Status == "observed_only");
        var ignoredCount = signalProjections.Count(s => s.Confidence?.Status == "ignored");

        var strongTopics = states
            .Where(s => s.MasteryProbability >= 0.80m && s.Confidence >= 0.60m)
            .GroupBy(s => s.TopicId)
            .Select(g => BuildTopicItem(g.Key, TopicLabel(g.Key, topicTitles, g.First().Label), g.Average(s => s.MasteryProbability), g.Average(s => s.Confidence), "Bu alanda güvenilir öğrenme kanıtı oluştu.", ["knowledge_tracing", "mastery_confident"]))
            .OrderByDescending(i => i.MasteryProbability)
            .Take(3)
            .ToArray();

        var weakTopics = states
            .Where(s => s.MasteryProbability < 0.55m || s.RemediationNeed is "high" or "medium")
            .GroupBy(s => s.TopicId)
            .Select(g => BuildTopicItem(g.Key, TopicLabel(g.Key, topicTitles, g.First().Label), g.Average(s => s.MasteryProbability), g.Average(s => s.Confidence), "Bu alanda tekrar sinyali var.", ["knowledge_tracing", "weak_mastery"]))
            .OrderBy(i => i.MasteryProbability)
            .Take(3)
            .ToArray();

        var weakConcepts = states
            .Where(s => s.MasteryProbability < 0.60m || s.IncorrectCount >= 1 || s.RemediationNeed is "high" or "medium" or "evidence_insufficient")
            .OrderBy(s => s.MasteryProbability)
            .ThenByDescending(s => s.IncorrectCount)
            .Take(5)
            .Select(s =>
            {
                var intelligence = MisconceptionIntelligenceEvaluator.FromWeakConcept(
                    s.TopicId,
                    s.ConceptKey,
                    string.IsNullOrWhiteSpace(s.Label) ? s.ConceptKey : s.Label,
                    s.MasteryProbability,
                    s.Confidence,
                    s.IncorrectCount,
                    s.RemediationNeed);
                return new LearningMemoryConceptDto
                {
                    TopicId = s.TopicId,
                    ConceptKey = s.ConceptKey,
                    Label = string.IsNullOrWhiteSpace(s.Label) ? s.ConceptKey : s.Label,
                    Confidence = s.Confidence,
                    ConfidenceStatus = ConfidenceStatus(s.Confidence, s.IncorrectCount),
                    UserSafeReason = s.Confidence < 0.60m
                        ? "Sinyal sınırlı; birkaç pratik daha netleştirir."
                        : "Bu kavram için tekrar faydalı görünüyor.",
                    EvidenceBasis = s.IncorrectCount >= 2 ? ["knowledge_tracing", "repeated_wrong_concept"] : ["knowledge_tracing"],
                    RemediationSeed = intelligence.RemediationSeed
                };
            })
            .ToArray();

        var recentMisconceptions = signalProjections
            .Where(s => s.Misconception is not null && s.Confidence?.Status is "usable" or "observed_only")
            .Select(s => new LearningMemoryConceptDto
            {
                TopicId = s.Misconception!.TopicId,
                ConceptKey = s.Misconception.ConceptKey,
                Label = string.IsNullOrWhiteSpace(s.Misconception.Label) ? s.Misconception.ConceptKey : s.Misconception.Label,
                Confidence = s.Misconception.Confidence,
                ConfidenceStatus = s.Misconception.ConfidenceStatus,
                UserSafeReason = s.Misconception.UserSafeLabel,
                EvidenceBasis = s.Misconception.EvidenceBasis,
                RemediationSeed = s.Remediation
            })
            .Take(5)
            .ToArray();

        var remediationReady = signalProjections
            .Where(s => s.Remediation is not null && s.Confidence?.Status == "usable")
            .Select(s => new LearningMemoryConceptDto
            {
                TopicId = s.Remediation!.TopicId,
                ConceptKey = s.Remediation.ConceptKey,
                Label = string.IsNullOrWhiteSpace(s.Remediation.Label) ? s.Remediation.ConceptKey : s.Remediation.Label,
                Confidence = s.Remediation.Confidence,
                ConfidenceStatus = s.Remediation.ConfidenceStatus,
                UserSafeReason = s.Remediation.Reason,
                EvidenceBasis = s.Remediation.EvidenceBasis,
                RemediationSeed = s.Remediation
            })
            .Concat(weakConcepts.Where(c => c.ConfidenceStatus == "usable"))
            .GroupBy(c => string.IsNullOrWhiteSpace(c.ConceptKey) ? c.Label : c.ConceptKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(3)
            .ToArray();

        var recentProgressSignals = signals
            .Take(5)
            .Select(s => $"{s.SignalType}: {FirstNonEmpty(s.SkillTag, s.TopicPath, "genel")}")
            .ToArray();

        var sourceReadiness = SourceReadiness(evidenceQuality);
        if (evidenceQuality?.Status is "weak" or "missing")
        {
            observedCount += 1;
        }

        var hasSignals = states.Count + masteries.Count + signals.Count + attempts.Count > 0;
        var hasEnoughSignals = states.Any(s => s.EvidenceCount >= 2 || s.Confidence >= 0.60m) ||
                               usableCount > 0 ||
                               attempts.Count >= 3;
        var confidenceStatus = usableCount > 0 || strongTopics.Length > 0
            ? "usable"
            : hasSignals ? "observed_only" : "observed_only";

        var confidenceSummary = new LearningMemoryConfidenceSummaryDto
        {
            UsableSignalCount = usableCount,
            ObservedOnlySignalCount = observedCount + states.Count(s => s.Confidence < 0.60m),
            IgnoredSignalCount = ignoredCount,
            StrongAreaCount = strongTopics.Length,
            WeakAreaCount = weakConcepts.Length,
            UserSafeSummary = hasEnoughSignals
                ? "Orka bazı öğrenme sinyallerini güvenle kullanabilir."
                : "Orka'nın öğrenci profili için daha fazla pratik kanıtı gerekiyor."
        };

        var goalReadiness = BuildGoalReadiness(strongTopics, weakConcepts, remediationReady, confidenceSummary, evidenceQuality);
        var hygiene = BuildMemoryHygiene(
            hasEnoughSignals,
            sourceReadiness,
            weakConcepts,
            recentMisconceptions,
            remediationReady,
            recentProgressSignals,
            confidenceSummary);
        return new LearningMemoryLiteDto
        {
            Summary = BuildSummary(hasSignals, strongTopics, weakConcepts, remediationReady),
            ConfidenceStatus = confidenceStatus,
            StrongTopics = strongTopics,
            WeakTopics = weakTopics,
            WeakConcepts = weakConcepts,
            RecentMisconceptions = recentMisconceptions,
            RemediationReadyItems = remediationReady,
            ConfidenceSummary = confidenceSummary,
            SourceReadiness = sourceReadiness,
            RecentProgressSignals = recentProgressSignals,
            GoalReadiness = goalReadiness,
            Hygiene = hygiene,
            LastUpdatedAt = LastUpdatedAt(states.Select(s => s.UpdatedAt), masteries.Select(m => m.UpdatedAt), signals.Select(s => s.CreatedAt), attempts.Select(a => a.CreatedAt)),
            HasEnoughSignals = hasEnoughSignals
        };
    }

    private static LearningMemoryHygieneDto BuildMemoryHygiene(
        bool hasEnoughSignals,
        string sourceReadiness,
        IReadOnlyList<LearningMemoryConceptDto> weakConcepts,
        IReadOnlyList<LearningMemoryConceptDto> recentMisconceptions,
        IReadOnlyList<LearningMemoryConceptDto> remediationReady,
        IReadOnlyList<string> recentProgressSignals,
        LearningMemoryConfidenceSummaryDto confidenceSummary)
    {
        var warnings = new List<string>();
        if (!hasEnoughSignals) warnings.Add("learning_memory_observed_only");
        if (sourceReadiness is "missing" or "weak" or "evidence_insufficient" or "degraded" or "stale")
            warnings.Add("source_memory_limited");
        if (remediationReady.Count > 0) warnings.Add("repair_memory_pending");

        var mergedSignals = weakConcepts
            .GroupBy(c => string.IsNullOrWhiteSpace(c.ConceptKey) ? c.Label : c.ConceptKey, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"weak_concept:{g.Key}")
            .Take(6)
            .ToArray();

        var retained = recentProgressSignals
            .Concat(weakConcepts.Select(c => $"weak:{SafeSignalKey(c.ConceptKey, c.Label)}"))
            .Concat(recentMisconceptions.Select(c => $"misconception:{SafeSignalKey(c.ConceptKey, c.Label)}"))
            .Concat(remediationReady.Select(c => $"repair:{SafeSignalKey(c.ConceptKey, c.Label)}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        var status = remediationReady.Count > 0
            ? "needs_review"
            : weakConcepts.Count > 0
                ? "active"
                : confidenceSummary.StrongAreaCount > 0
                    ? "improving"
                    : hasEnoughSignals
                        ? "active"
                        : "observed_only";

        return new LearningMemoryHygieneDto
        {
            MemoryStatus = status,
            RetainedSignalCount = retained.Length,
            MergedWeakConceptCount = Math.Max(mergedSignals.Length, weakConcepts.Count),
            RepairPendingCount = remediationReady.Count,
            StaleSignalCount = warnings.Contains("source_memory_limited") ? 1 : 0,
            RetainedSignals = retained,
            MergedSignals = mergedSignals,
            Warnings = warnings,
            StudentVisibleSummary = status switch
            {
                "needs_review" => $"Ogrenme hafizasinda {remediationReady.Count} telafi bekleyen guvenli ozet var.",
                "improving" => "Ogrenme hafizasi gelisim sinyallerini guvenli ozet olarak tutuyor.",
                "active" => "Ogrenme hafizasi zayif/guclu kavramlari ham metin yerine ozet sinyallerle tutuyor.",
                _ => "Ogrenme hafizasi henuz gozlem modunda; ham chat metni profil olarak kullanilmiyor."
            },
            NextAction = remediationReady.Count > 0 ? "run_repair_checkpoint" : hasEnoughSignals ? "continue_learning" : "collect_more_learning_signals"
        };
    }

    private static string SafeSignalKey(string? conceptKey, string? label)
    {
        var value = string.IsNullOrWhiteSpace(conceptKey) ? label : conceptKey;
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
    }

    private static LearningMemoryTopicDto BuildTopicItem(Guid? topicId, string label, decimal mastery, decimal confidence, string reason, IReadOnlyList<string> basis) => new()
    {
        TopicId = topicId,
        Label = label,
        MasteryProbability = Math.Round(mastery, 2),
        Confidence = Math.Round(confidence, 2),
        ConfidenceStatus = ConfidenceStatus(confidence, 0),
        UserSafeReason = reason,
        EvidenceBasis = basis
    };

    private static GoalReadinessDto BuildGoalReadiness(
        IReadOnlyList<LearningMemoryTopicDto> strongTopics,
        IReadOnlyList<LearningMemoryConceptDto> weakConcepts,
        IReadOnlyList<LearningMemoryConceptDto> remediationReady,
        LearningMemoryConfidenceSummaryDto confidence,
        EvidenceQualityDto? evidenceQuality)
    {
        var plannerWarnings = new List<string>();
        if (confidence.UsableSignalCount == 0 && confidence.StrongAreaCount == 0) plannerWarnings.Add("learning_evidence_limited");
        if (evidenceQuality?.Status is "weak" or "missing") plannerWarnings.Add("source_evidence_limited");
        var readyWeak = remediationReady.Count > 0
            ? remediationReady
            : weakConcepts.Where(c => c.ConfidenceStatus == "usable").Take(3).ToArray();
        var observedLevel = strongTopics.Count >= 2 && weakConcepts.Count == 0
            ? "advanced"
            : strongTopics.Count > 0 && weakConcepts.Count > 0
                ? "intermediate"
                : weakConcepts.Count > 0 ? "foundation" : "unknown";
        var levelConfidence = Math.Clamp((confidence.UsableSignalCount + confidence.StrongAreaCount) / 6m, 0m, 1m);
        return new GoalReadinessDto
        {
            ObservedLevel = observedLevel,
            ObservedLevelConfidence = Math.Round(levelConfidence, 2),
            PlannerReadyWeakAreas = readyWeak.Take(3).ToArray(),
            PlannerReadyStrengths = strongTopics.Take(3).ToArray(),
            PlannerWarnings = plannerWarnings,
            NeedsMoreEvidence = plannerWarnings.Contains("learning_evidence_limited") || readyWeak.Count == 0,
            SuggestedDiagnosticFocus = readyWeak.Select(w => w.Label).Where(v => !string.IsNullOrWhiteSpace(v)).Take(3).ToArray()
        };
    }

    private static string BuildSummary(
        bool hasSignals,
        IReadOnlyList<LearningMemoryTopicDto> strongTopics,
        IReadOnlyList<LearningMemoryConceptDto> weakConcepts,
        IReadOnlyList<LearningMemoryConceptDto> remediationReady)
    {
        if (!hasSignals)
        {
            return "Henüz yeterli öğrenme sinyali yok. Quiz, chat ve Wiki kullandıkça profil oluşur.";
        }

        if (remediationReady.Count > 0)
        {
            return $"{remediationReady[0].Label} için güvenilir telafi sinyali var.";
        }

        if (weakConcepts.Count > 0)
        {
            return $"{weakConcepts[0].Label} tekrar gerektiren alan olarak izleniyor.";
        }

        if (strongTopics.Count > 0)
        {
            return $"{strongTopics[0].Label} güçlü ilerlediğin alanlardan biri.";
        }

        return "Öğrenme sinyalleri oluşuyor; Orka henüz kesin bir profil iddiası kurmuyor.";
    }

    private static SignalProjection ParseSignal(string? payloadJson)
    {
        var metadata = ReadMetadata(payloadJson);
        return new SignalProjection(
            TryRead<MisconceptionSignalDto>(metadata, "misconceptionSignal"),
            TryRead<LearningSignalConfidenceDto>(metadata, "learningSignalConfidence"),
            TryRead<RemediationSeedDto>(metadata, "remediationSeed"));
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static T? TryRead<T>(IReadOnlyDictionary<string, JsonElement> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var raw))
        {
            return default;
        }

        try
        {
            return raw.Deserialize<T>(JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static string TopicLabel(Guid? topicId, IReadOnlyDictionary<Guid, string> topicTitles, string fallback)
    {
        if (topicId.HasValue && topicTitles.TryGetValue(topicId.Value, out var title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        return string.IsNullOrWhiteSpace(fallback) ? "Öğrenme alanı" : fallback;
    }

    private static string ConfidenceStatus(decimal confidence, int repeatedWrongCount) =>
        confidence >= 0.60m || repeatedWrongCount >= 2 ? "usable" : "observed_only";

    private static string SourceReadiness(EvidenceQualityDto? evidenceQuality) =>
        evidenceQuality?.Status?.ToLowerInvariant() switch
        {
            "strong" => "ready",
            "partial" => "limited",
            "weak" => "weak",
            "missing" => "missing",
            _ => "unknown"
        };

    private static DateTimeOffset LastUpdatedAt(params IEnumerable<DateTime>[] dates)
    {
        var latest = dates
            .SelectMany(d => d)
            .DefaultIfEmpty(DateTime.UtcNow)
            .Max();
        return new DateTimeOffset(DateTime.SpecifyKind(latest, DateTimeKind.Utc));
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "genel";

    private sealed record SignalProjection(
        MisconceptionSignalDto? Misconception,
        LearningSignalConfidenceDto? Confidence,
        RemediationSeedDto? Remediation);
}

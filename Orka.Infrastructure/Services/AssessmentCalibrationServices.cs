using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class AssessmentCalibrationService : IAssessmentCalibrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly ILearningEventNormalizer? _events;

    public AssessmentCalibrationService(OrkaDbContext db, ILearningEventNormalizer? events = null)
    {
        _db = db;
        _events = events;
    }

    public async Task<AssessmentCalibrationRunDto> RunAsync(Guid userId, Guid? topicId, CancellationToken ct = default)
    {
        if (topicId.HasValue && !await _db.Topics.AnyAsync(t => t.Id == topicId.Value && t.UserId == userId, ct))
        {
            throw new InvalidOperationException("Assessment calibration topic was not found for the user.");
        }

        var latestSnapshot = await _db.ConceptGraphSnapshots
            .AsNoTracking()
            .Where(s => s.UserId == userId && (!topicId.HasValue || s.TopicId == topicId.Value))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var items = await _db.AssessmentItems
            .AsNoTracking()
            .Where(i => i.UserId == userId && (!topicId.HasValue || i.TopicId == topicId.Value))
            .OrderByDescending(i => i.CreatedAt)
            .Take(300)
            .ToListAsync(ct);

        var itemIds = items.Select(i => i.Id).ToArray();
        var stats = await _db.AssessmentItemStats
            .Where(s => itemIds.Contains(s.AssessmentItemId))
            .ToDictionaryAsync(s => s.AssessmentItemId, ct);

        var run = new AssessmentCalibrationRun
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = latestSnapshot?.Id,
            ItemCount = items.Count,
            ConceptCount = items.Select(i => i.ConceptKey).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            CreatedAt = DateTime.UtcNow
        };

        var calibrationItems = new List<AssessmentCalibrationItem>();
        foreach (var item in items)
        {
            stats.TryGetValue(item.Id, out var stat);
            if (stat != null)
            {
                ApplyCalibration(stat);
            }

            var status = stat?.CalibrationStatus ?? "uncalibrated";
            var calibrationItem = new AssessmentCalibrationItem
            {
                Id = Guid.NewGuid(),
                AssessmentCalibrationRunId = run.Id,
                UserId = userId,
                TopicId = topicId,
                AssessmentItemId = item.Id,
                ConceptKey = item.ConceptKey,
                DifficultyEstimate = stat?.DifficultyEstimate ?? DifficultyFromBand(item.Difficulty),
                DiscriminationEstimate = stat?.DiscriminationEstimate ?? 0m,
                ExposureCount = stat?.ExposureCount ?? 0,
                EvidenceCount = stat?.Attempts ?? 0,
                CalibrationStatus = status,
                Reason = ReasonFor(stat, item),
                CreatedAt = DateTime.UtcNow
            };
            calibrationItems.Add(calibrationItem);
        }

        run.HealthyItemCount = calibrationItems.Count(i => i.CalibrationStatus == "healthy");
        run.ReadyConceptCount = calibrationItems
            .Where(i => i.CalibrationStatus is "healthy" or "usable_low_evidence")
            .Select(i => i.ConceptKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        run.AverageDifficulty = Average(calibrationItems.Select(i => i.DifficultyEstimate));
        run.AverageDiscrimination = Average(calibrationItems.Select(i => i.DiscriminationEstimate));
        run.AverageExposure = calibrationItems.Count == 0 ? 0m : Math.Round(calibrationItems.Average(i => (decimal)i.ExposureCount), 4);
        run.ItemBankHealth = ItemBankHealth(run.ItemCount, run.HealthyItemCount);
        run.AdaptiveReadiness = AdaptiveReadiness(run.ItemCount, run.ReadyConceptCount, run.ConceptCount);
        run.CalibrationStatus = run.AdaptiveReadiness == "ready" && run.ItemBankHealth == "healthy"
            ? "healthy"
            : run.ItemCount == 0 ? "empty" : "watch";

        var dto = ToDto(run, calibrationItems);
        run.ReportJson = JsonSerializer.Serialize(dto, JsonOptions);
        _db.AssessmentCalibrationRuns.Add(run);
        _db.AssessmentCalibrationItems.AddRange(calibrationItems);
        await _db.SaveChangesAsync(ct);
        if (_events != null)
        {
            await _events.RecordSignalEventAsync(userId, topicId, null, "assessment.calibration.updated", payloadJson: JsonSerializer.Serialize(new
            {
                runId = run.Id,
                run.CalibrationStatus,
                run.AdaptiveReadiness,
                run.ItemBankHealth,
                run.ItemCount,
                run.HealthyItemCount,
                run.ReadyConceptCount
            }, JsonOptions), ct: ct);
        }

        return dto;
    }

    public async Task<AssessmentCalibrationRunDto?> GetLatestAsync(Guid userId, Guid? topicId, CancellationToken ct = default)
    {
        if (topicId.HasValue && !await _db.Topics.AnyAsync(t => t.Id == topicId.Value && t.UserId == userId, ct))
        {
            return null;
        }

        var run = await _db.AssessmentCalibrationRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.TopicId == topicId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (run == null) return null;

        var items = await _db.AssessmentCalibrationItems
            .AsNoTracking()
            .Where(i => i.AssessmentCalibrationRunId == run.Id)
            .OrderByDescending(i => i.DiscriminationEstimate)
            .Take(80)
            .ToListAsync(ct);
        return ToDto(run, items);
    }

    internal static void ApplyCalibration(AssessmentItemStat stat)
    {
        stat.DifficultyEstimate = Clamp(Math.Round(1m - stat.CorrectRate, 4), 0.05m, 0.95m);
        // Point-Biserial binomial variance proxy to avoid discrimination inversion:
        decimal varianceFactor = 4m * stat.CorrectRate * (1m - stat.CorrectRate); // Peak at 1.0 when CorrectRate = 0.5
        stat.DiscriminationEstimate = Clamp(Math.Round(varianceFactor * (1m - stat.SkipRate), 4), 0m, 1m);
        stat.CalibrationStatus = stat.Attempts < 3
            ? "usable_low_evidence"
            : stat.QualityStatus == "healthy" && stat.DiscriminationEstimate >= 0.10m
                ? "healthy"
                : "needs_review";
        stat.UpdatedAt = DateTime.UtcNow;
    }

    internal static decimal DifficultyFromBand(string? band)
    {
        var normalized = (band ?? string.Empty).ToLowerInvariant();
        if (normalized.Contains("kolay") || normalized.Contains("easy")) return 0.25m;
        if (normalized.Contains("zor") || normalized.Contains("hard") || normalized.Contains("ileri")) return 0.75m;
        return 0.50m;
    }

    private static string ReasonFor(AssessmentItemStat? stat, AssessmentItem item)
    {
        if (stat == null) return $"Soru bankada var ama henüz deneme kanıtı yok. Difficulty band: {item.Difficulty}.";
        if (stat.Attempts < 3) return "Kanıt az; adaptive seçimde kullanılabilir ama mastery kararı için tek başına yeterli değil.";
        if (stat.SkipRate > 0.35m) return "Skip oranı yüksek; soru ifadesi veya zorluk bandı gözden geçirilmeli.";
        if (stat.CorrectRate is < 0.20m or > 0.95m) return "Correct-rate uçta; ayırt edicilik zayıf olabilir.";
        return "Soru adaptive seçim için sağlıklı görünüyor.";
    }

    private static string ItemBankHealth(int itemCount, int healthyCount)
    {
        if (itemCount == 0) return "empty";
        if (itemCount < 8) return "thin";
        var ratio = healthyCount / (decimal)Math.Max(1, itemCount);
        return ratio >= 0.60m ? "healthy" : "watch";
    }

    private static string AdaptiveReadiness(int itemCount, int readyConceptCount, int conceptCount)
    {
        if (itemCount < 4) return "not_ready";
        if (itemCount < 8) return "limited";
        if (conceptCount == 0) return "limited";
        return readyConceptCount >= Math.Max(1, Math.Min(conceptCount, 3)) ? "ready" : "limited";
    }

    private static decimal Average(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0m : Math.Round(list.Average(), 4);
    }

    private static decimal Clamp(decimal value, decimal min, decimal max) => Math.Min(max, Math.Max(min, value));

    private static AssessmentCalibrationRunDto ToDto(AssessmentCalibrationRun run, IReadOnlyList<AssessmentCalibrationItem> items) => new()
    {
        Id = run.Id,
        UserId = run.UserId,
        TopicId = run.TopicId,
        ConceptGraphSnapshotId = run.ConceptGraphSnapshotId,
        CalibrationStatus = run.CalibrationStatus,
        AdaptiveReadiness = run.AdaptiveReadiness,
        ItemBankHealth = run.ItemBankHealth,
        ItemCount = run.ItemCount,
        HealthyItemCount = run.HealthyItemCount,
        ConceptCount = run.ConceptCount,
        ReadyConceptCount = run.ReadyConceptCount,
        AverageDifficulty = run.AverageDifficulty,
        AverageDiscrimination = run.AverageDiscrimination,
        AverageExposure = run.AverageExposure,
        Items = items.Select(i => new AssessmentCalibrationItemDto
        {
            Id = i.Id,
            AssessmentItemId = i.AssessmentItemId,
            ConceptKey = i.ConceptKey,
            DifficultyEstimate = i.DifficultyEstimate,
            DiscriminationEstimate = i.DiscriminationEstimate,
            ExposureCount = i.ExposureCount,
            EvidenceCount = i.EvidenceCount,
            CalibrationStatus = i.CalibrationStatus,
            Reason = i.Reason
        }).ToArray(),
        CreatedAt = run.CreatedAt
    };
}

public sealed class AdaptiveAssessmentSelector : IAdaptiveAssessmentSelector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;

    public AdaptiveAssessmentSelector(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<AdaptiveAssessmentDecisionDto?> SelectNextAsync(AdaptiveAssessmentSession session, CancellationToken ct = default)
    {
        var answeredItemIds = await _db.AdaptiveAssessmentDecisions
            .AsNoTracking()
            .Where(d => d.AdaptiveAssessmentSessionId == session.Id)
            .Select(d => d.AssessmentItemId)
            .ToListAsync(ct);

        var targetConcepts = DeserializeList(session.TargetConceptsJson);
        var itemsQuery = _db.AssessmentItems
            .AsNoTracking()
            .Where(i => i.UserId == session.UserId && !answeredItemIds.Contains(i.Id));
        if (session.TopicId.HasValue) itemsQuery = itemsQuery.Where(i => i.TopicId == session.TopicId.Value);
        if (targetConcepts.Count > 0) itemsQuery = itemsQuery.Where(i => targetConcepts.Contains(i.ConceptKey));

        var items = await itemsQuery
            .OrderBy(i => i.Order)
            .ThenByDescending(i => i.CreatedAt)
            .Take(120)
            .ToListAsync(ct);
        if (items.Count == 0)
        {
            var fallback = await BuildFallbackItemAsync(session, targetConcepts, ct);
            if (fallback == null) return null;
            items.Add(fallback);
        }

        var itemIds = items.Select(i => i.Id).ToArray();
        var stats = await _db.AssessmentItemStats
            .Where(s => itemIds.Contains(s.AssessmentItemId))
            .ToDictionaryAsync(s => s.AssessmentItemId, ct);
        var states = await _db.KnowledgeTracingStates
            .AsNoTracking()
            .Where(s => s.UserId == session.UserId && s.TopicId == session.TopicId)
            .ToDictionaryAsync(s => s.ConceptKey, StringComparer.OrdinalIgnoreCase, ct);

        var ranked = items
            .Select(item =>
            {
                stats.TryGetValue(item.Id, out var stat);
                if (stat != null && stat.CalibrationStatus == "uncalibrated")
                {
                    AssessmentCalibrationService.ApplyCalibration(stat);
                }

                states.TryGetValue(item.ConceptKey, out var state);
                var mastery = state?.MasteryProbability ?? 0.35m;
                var confidence = state?.Confidence ?? 0.20m;
                var uncertainty = 1m - Math.Abs(mastery - 0.50m) * 2m;
                var weakBoost = mastery < 0.65m || confidence < 0.60m ? 0.25m : 0m;
                var qualityScore = ItemQualityScore(stat, item, mastery);
                var exposurePenalty = Math.Min(0.35m, (stat?.ExposureCount ?? 0) * 0.03m);
                var score = Math.Round(uncertainty * 0.45m + weakBoost + qualityScore * 0.35m - exposurePenalty, 4);
                return new
                {
                    Item = item,
                    Stat = stat,
                    State = state,
                    Score = Math.Max(0m, score),
                    QualityScore = qualityScore,
                    ExposurePenalty = exposurePenalty,
                    Mastery = mastery,
                    Confidence = confidence
                };
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Stat?.ExposureCount ?? 0)
            .ToList();

        var selected = ranked.FirstOrDefault();
        if (selected == null) return null;

        if (selected.Stat != null)
        {
            selected.Stat.ExposureCount += 1;
            selected.Stat.LastSelectedAt = DateTime.UtcNow;
            selected.Stat.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        var question = StripAnswerKey(BuildQuizData(selected.Item, session.QuizRunId));
        return new AdaptiveAssessmentDecisionDto
        {
            Id = Guid.NewGuid(),
            AssessmentItemId = selected.Item.Id,
            ConceptKey = selected.Item.ConceptKey,
            AssessmentMode = AssessmentModeFor(selected.Item),
            SelectionScore = selected.Score,
            MasteryProbability = selected.Mastery,
            MasteryConfidence = selected.Confidence,
            ItemQualityScore = selected.QualityScore,
            ExposurePenalty = selected.ExposurePenalty,
            DecisionReason = BuildDecisionReason(selected.Mastery, selected.Confidence, selected.QualityScore, selected.ExposurePenalty),
            Question = question
        };
    }

    private async Task<AssessmentItem?> BuildFallbackItemAsync(AdaptiveAssessmentSession session, IReadOnlyList<string> targetConcepts, CancellationToken ct)
    {
        var concept = targetConcepts.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(concept))
        {
            concept = await _db.LearningConcepts
                .AsNoTracking()
                .Where(c => c.ConceptGraphSnapshotId == session.ConceptGraphSnapshotId)
                .OrderBy(c => c.Order)
                .Select(c => c.StableKey)
                .FirstOrDefaultAsync(ct);
        }
        if (string.IsNullOrWhiteSpace(concept)) return null;

        var snapshotId = session.ConceptGraphSnapshotId ??
            await _db.ConceptGraphSnapshots
                .Where(s => s.UserId == session.UserId && s.TopicId == session.TopicId)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => s.Id)
                .FirstOrDefaultAsync(ct);
        if (snapshotId == Guid.Empty) return null;

        var item = new AssessmentItem
        {
            Id = Guid.NewGuid(),
            UserId = session.UserId,
            TopicId = session.TopicId,
            QuizRunId = session.QuizRunId,
            ConceptGraphSnapshotId = snapshotId,
            AssessmentItemKey = $"adaptive-fallback:{session.Id:N}:{StableKey(concept)}",
            ConceptKey = concept,
            ConceptLabel = concept.Replace('-', ' '),
            QuestionType = "conceptual",
            CognitiveSkill = "explain",
            Difficulty = "orta",
            MisconceptionTarget = "Eksik kavram bağlantısı",
            EvidenceExpected = "Öğrenci kavramı örnek ve karşı-örnekle ayırt eder.",
            OptionQualityRulesJson = JsonSerializer.Serialize(new[] { "one_best_answer", "common_misconception_distractor", "clear_language" }, JsonOptions),
            ScoringRuleJson = JsonSerializer.Serialize(new { correctOptionId = "A", score = 1 }, JsonOptions),
            LearningOutcomeKeysJson = "[]",
            PromptSpecJson = JsonSerializer.Serialize(new { source = "adaptive_fallback", schemaVersion = "orka.assessment-item.v1" }, JsonOptions),
            GeneratedQuestionJson = JsonSerializer.Serialize(BuildQuizDataTemplate(concept), JsonOptions),
            CreatedAt = DateTime.UtcNow
        };
        _db.AssessmentItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return item;
    }

    internal static QuizDataDto BuildQuizData(AssessmentItem item, Guid? quizRunId)
    {
        if (!string.IsNullOrWhiteSpace(item.GeneratedQuestionJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<QuizDataDto>(item.GeneratedQuestionJson, JsonOptions);
                if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Question))
                {
                    NormalizeOptions(parsed);
                    parsed.QuizRunId = quizRunId;
                    parsed.AssessmentItemId = item.Id;
                    parsed.AssessmentItemKey = item.AssessmentItemKey;
                    parsed.ConceptKey = item.ConceptKey;
                    parsed.ConceptTag = item.ConceptKey;
                    parsed.CognitiveSkill = item.CognitiveSkill;
                    parsed.AssessmentMode = AssessmentModeFor(item);
                    parsed.MisconceptionTarget = item.MisconceptionTarget;
                    parsed.EvidenceExpected = item.EvidenceExpected;
                    parsed.ScoringRule = item.ScoringRuleJson;
                    parsed.LearningOutcomeIds = DeserializeList(item.LearningOutcomeKeysJson);
                    parsed.QuestionHash ??= StableHash($"{item.AssessmentItemKey}:{parsed.Question}");
                    return parsed;
                }
            }
            catch
            {
                // Fall through to deterministic item rendering.
            }
        }

        var dto = BuildQuizDataTemplate(item.ConceptLabel);
        dto.QuizRunId = quizRunId;
        dto.AssessmentItemId = item.Id;
        dto.AssessmentItemKey = item.AssessmentItemKey;
        dto.ConceptKey = item.ConceptKey;
        dto.ConceptTag = item.ConceptKey;
        dto.SkillTag = item.ConceptLabel;
        dto.TopicPath = item.ConceptLabel;
        dto.Difficulty = item.Difficulty;
        dto.CognitiveType = item.CognitiveSkill;
        dto.CognitiveSkill = item.CognitiveSkill;
        dto.AssessmentMode = AssessmentModeFor(item);
        dto.MisconceptionTarget = item.MisconceptionTarget;
        dto.EvidenceExpected = item.EvidenceExpected;
        dto.ScoringRule = item.ScoringRuleJson;
        dto.LearningOutcomeIds = DeserializeList(item.LearningOutcomeKeysJson);
        dto.QuestionHash = StableHash($"{item.AssessmentItemKey}:{dto.Question}");
        return dto;
    }

    private static void NormalizeOptions(QuizDataDto dto)
    {
        if (dto.Options.Count == 0)
        {
            dto.Options = new[]
            {
                new QuizOptionDto("A", "Doğru açıklama", true),
                new QuizOptionDto("B", "Eksik açıklama", false),
                new QuizOptionDto("C", "Bağlam dışı açıklama", false),
                new QuizOptionDto("D", "Sadece ezber", false)
            };
            return;
        }

        dto.Options = dto.Options.Select((option, index) =>
        {
            var id = string.IsNullOrWhiteSpace(option.Id)
                ? ((char)('A' + Math.Min(index, 25))).ToString()
                : option.Id;
            return new QuizOptionDto(id, option.Text, option.IsCorrect);
        }).ToArray();
    }

    private static QuizDataDto StripAnswerKey(QuizDataDto dto)
    {
        dto.Explanation = string.Empty;
        dto.Options = dto.Options.Select(option => new QuizOptionDto(option.Id, option.Text, false)).ToArray();
        return dto;
    }

    private static QuizDataDto BuildQuizDataTemplate(string concept)
    {
        var label = string.IsNullOrWhiteSpace(concept) ? "bu kavram" : concept.Trim();
        return new QuizDataDto
        {
            Type = "multiple_choice",
            QuestionId = StableHash($"adaptive:{label}"),
            Question = $"{label} için en sağlam öğrenme kanıtı hangisidir?",
            Options = new[]
            {
                new QuizOptionDto("A", "Kavramı kendi cümlemle açıklayıp yeni bir örneğe uygulayabilmem.", true),
                new QuizOptionDto("B", "Terimi bir kez görmüş olmam.", false),
                new QuizOptionDto("C", "Cevabı ezberleyip bağlam değişince tekrar bakmam.", false),
                new QuizOptionDto("D", "Sadece başlığı hatırlamam.", false)
            },
            Explanation = "Orka mastery kararını ezberden değil, kavramı açıklama ve yeni bağlama uygulama kanıtından güçlendirir.",
            Topic = label,
            SkillTag = label,
            TopicPath = label,
            Difficulty = "orta",
            CognitiveType = "conceptual",
            AssessmentMode = "retrieval_practice",
            SourceReadiness = "evidence_insufficient"
        };
    }

    private static string AssessmentModeFor(AssessmentItem item)
    {
        var cognitive = $"{item.QuestionType} {item.CognitiveSkill}".ToLowerInvariant();
        if (cognitive.Contains("misconception") || cognitive.Contains("probe")) return "misconception_probe";
        if (cognitive.Contains("diagnostic")) return "diagnostic_check";
        if (cognitive.Contains("readiness")) return "readiness_check";
        if (cognitive.Contains("review") || cognitive.Contains("retrieval")) return "retrieval_practice";
        return "micro_quiz";
    }

    private static decimal ItemQualityScore(AssessmentItemStat? stat, AssessmentItem item, decimal mastery = 0.50m)
    {
        if (stat == null) return 0.45m;
        var statusBoost = stat.CalibrationStatus == "healthy" ? 0.25m : stat.CalibrationStatus == "usable_low_evidence" ? 0.10m : -0.10m;
        var discrimination = stat.DiscriminationEstimate == 0m ? Math.Max(0m, stat.DiscriminationProxy) : stat.DiscriminationEstimate;
        var difficultyFit = 1m - Math.Abs((stat.DifficultyEstimate == 0m ? AssessmentCalibrationService.DifficultyFromBand(item.Difficulty) : stat.DifficultyEstimate) - mastery);
        return Math.Clamp(Math.Round(0.35m + discrimination * 0.30m + difficultyFit * 0.20m + statusBoost, 4), 0m, 1m);
    }

    private static string BuildDecisionReason(decimal mastery, decimal confidence, decimal quality, decimal exposurePenalty)
    {
        if (confidence < 0.60m) return "Kanıt düşük olduğu için bu kavramdan ek ölçüm seçildi.";
        if (mastery < 0.55m) return "Zayıf kavram için remediation ölçümü seçildi.";
        if (quality >= 0.70m && exposurePenalty < 0.10m) return "Sağlıklı ve az gösterilmiş soru seçildi.";
        return "Mastery belirsizliği ve item kalitesi dengelenerek seçildi.";
    }

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

    private static string StableHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..24];
    }

    private static string StableKey(string value) =>
        new string(value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
}

public sealed class AdaptiveAssessmentSessionService : IAdaptiveAssessmentSessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IAdaptiveAssessmentSelector _selector;
    private readonly IAssessmentCalibrationService _calibration;
    private readonly IQuizAttemptRecorder _recorder;
    private readonly ILearningEventNormalizer _events;

    public AdaptiveAssessmentSessionService(
        OrkaDbContext db,
        IAdaptiveAssessmentSelector selector,
        IAssessmentCalibrationService calibration,
        IQuizAttemptRecorder recorder,
        ILearningEventNormalizer events)
    {
        _db = db;
        _selector = selector;
        _calibration = calibration;
        _recorder = recorder;
        _events = events;
    }

    public async Task<AdaptiveAssessmentSessionDto> StartAsync(Guid userId, AdaptiveAssessmentStartRequest request, CancellationToken ct = default)
    {
        var topicId = request.TopicId;
        var snapshot = await _db.ConceptGraphSnapshots
            .AsNoTracking()
            .Where(s => s.UserId == userId && (!topicId.HasValue || s.TopicId == topicId.Value))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var targetConcepts = request.TargetConceptKeys?.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            ?? await BuildTargetConceptsAsync(userId, topicId, snapshot?.Id, ct);

        var minItems = Math.Clamp(request.MinItems ?? 8, 4, 20);
        var maxItems = Math.Clamp(request.MaxItems ?? 20, minItems, 30);
        var quizRun = new QuizRun
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = request.SessionId,
            QuizType = "adaptive",
            Status = "active",
            TotalQuestions = maxItems,
            MetadataJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.adaptive-assessment.v1",
                targetConcepts,
                minItems,
                maxItems,
                assessmentMode = NormalizeAssessmentMode(request.AssessmentMode, targetConcepts.Count > 0 ? "retrieval_practice" : "diagnostic_check")
            }, JsonOptions),
            CreatedAt = DateTime.UtcNow
        };
        var session = new AdaptiveAssessmentSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = request.SessionId,
            QuizRunId = quizRun.Id,
            ConceptGraphSnapshotId = snapshot?.Id,
            TargetConceptsJson = JsonSerializer.Serialize(targetConcepts, JsonOptions),
            MinItems = minItems,
            MaxItems = maxItems,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.QuizRuns.Add(quizRun);
        _db.AdaptiveAssessmentSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        await _events.RecordSignalEventAsync(userId, topicId, request.SessionId, "assessment.adaptive.started", payloadJson: JsonSerializer.Serialize(new
        {
            schemaVersion = "orka.adaptive-assessment.v1",
            adaptiveSessionId = session.Id,
            targetConcepts,
            assessmentMode = NormalizeAssessmentMode(request.AssessmentMode, targetConcepts.Count > 0 ? "retrieval_practice" : "diagnostic_check")
        }, JsonOptions), ct: ct);
        _ = await _calibration.RunAsync(userId, topicId, ct);
        return ToDto(session);
    }

    public async Task<AdaptiveAssessmentNextItemDto> GetNextAsync(Guid userId, Guid adaptiveSessionId, CancellationToken ct = default)
    {
        var session = await _db.AdaptiveAssessmentSessions.FirstOrDefaultAsync(s => s.Id == adaptiveSessionId && s.UserId == userId, ct);
        if (session == null) throw new InvalidOperationException("Adaptive session not found.");
        if (session.Status != "active")
        {
            return new AdaptiveAssessmentNextItemDto { SessionId = session.Id, Status = session.Status, IsComplete = true, StopReason = session.StopReason };
        }

        var stopReason = await GetStopReasonAsync(session, ct);
        if (!string.IsNullOrWhiteSpace(stopReason))
        {
            await CompleteAsync(session, stopReason, ct);
            return new AdaptiveAssessmentNextItemDto { SessionId = session.Id, Status = session.Status, IsComplete = true, StopReason = session.StopReason };
        }

        var pending = await _db.AdaptiveAssessmentDecisions
            .AsNoTracking()
            .Where(d => d.AdaptiveAssessmentSessionId == session.Id && !d.WasAnswered)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (pending != null)
        {
            return new AdaptiveAssessmentNextItemDto
            {
                SessionId = session.Id,
                Status = session.Status,
                Decision = ToDecisionDto(pending)
            };
        }

        var selected = await _selector.SelectNextAsync(session, ct);
        if (selected == null)
        {
            await CompleteAsync(session, "item_bank_empty", ct);
            return new AdaptiveAssessmentNextItemDto { SessionId = session.Id, Status = session.Status, IsComplete = true, StopReason = session.StopReason };
        }

        var decision = new AdaptiveAssessmentDecision
        {
            Id = selected.Id,
            AdaptiveAssessmentSessionId = session.Id,
            UserId = userId,
            TopicId = session.TopicId,
            AssessmentItemId = selected.AssessmentItemId,
            ConceptKey = selected.ConceptKey,
            SelectionScore = selected.SelectionScore,
            MasteryProbability = selected.MasteryProbability,
            MasteryConfidence = selected.MasteryConfidence,
            ItemQualityScore = selected.ItemQualityScore,
            ExposurePenalty = selected.ExposurePenalty,
            DecisionReason = selected.DecisionReason,
            AssessmentMode = selected.AssessmentMode,
            SelectedQuestionJson = JsonSerializer.Serialize(selected.Question, JsonOptions),
            CreatedAt = DateTime.UtcNow
        };
        _db.AdaptiveAssessmentDecisions.Add(decision);
        await _db.SaveChangesAsync(ct);
        await _events.RecordSignalEventAsync(userId, session.TopicId, session.SessionId, "assessment.item.selected", selected.ConceptKey, payloadJson: JsonSerializer.Serialize(new
        {
            schemaVersion = "orka.adaptive-assessment.v1",
            adaptiveSessionId = session.Id,
            decisionId = decision.Id,
            decision.SelectionScore,
            decision.DecisionReason
        }, JsonOptions), ct: ct);
        return new AdaptiveAssessmentNextItemDto { SessionId = session.Id, Status = session.Status, Decision = selected };
    }

    public async Task<AdaptiveAssessmentNextItemDto> RecordAnswerAsync(Guid userId, Guid adaptiveSessionId, AdaptiveAssessmentAnswerRequest request, CancellationToken ct = default)
    {
        var session = await _db.AdaptiveAssessmentSessions.FirstOrDefaultAsync(s => s.Id == adaptiveSessionId && s.UserId == userId, ct);
        if (session == null) throw new InvalidOperationException("Adaptive session not found.");

        var decision = await _db.AdaptiveAssessmentDecisions
            .FirstOrDefaultAsync(d => d.Id == request.DecisionId && d.AdaptiveAssessmentSessionId == adaptiveSessionId && d.UserId == userId, ct);
        if (decision == null) throw new InvalidOperationException("Adaptive decision not found.");
        if (decision.WasAnswered) return await GetNextAsync(userId, adaptiveSessionId, ct);

        request.QuizRunId = session.QuizRunId;
        request.TopicId ??= session.TopicId;
        request.SessionId ??= session.SessionId;
        request.AssessmentItemId ??= decision.AssessmentItemId;
        request.ConceptKey ??= decision.ConceptKey;
        request.AssessmentMode = NormalizeAssessmentMode(request.AssessmentMode, decision.AssessmentMode, "retrieval_practice");
        request.SourceRefsJson = MergeAdaptiveSourceRefs(request.SourceRefsJson, session.Id, decision.Id, request.AssessmentMode);
        var result = await _recorder.RecordAsync(userId, request, ct);

        decision.WasAnswered = true;
        decision.QuizAttemptId = result.Attempt.Id;
        decision.AnsweredAt = DateTime.UtcNow;
        session.AnsweredCount += 1;
        if (result.Attempt.IsCorrect) session.CorrectCount += 1;
        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _ = await _calibration.RunAsync(userId, session.TopicId, ct);

        var next = await GetNextAsync(userId, adaptiveSessionId, ct);
        next.LatestLearningImpact = result.LearningImpact;
        return next;
    }

    private async Task<List<string>> BuildTargetConceptsAsync(Guid userId, Guid? topicId, Guid? snapshotId, CancellationToken ct)
    {
        var weakStates = await _db.KnowledgeTracingStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId && (s.MasteryProbability < 0.70m || s.Confidence < 0.60m))
            .OrderBy(s => s.Confidence)
            .ThenBy(s => s.MasteryProbability)
            .Select(s => s.ConceptKey)
            .Take(8)
            .ToListAsync(ct);
        if (weakStates.Count > 0) return weakStates;

        if (snapshotId.HasValue)
        {
            var graphConcepts = await _db.LearningConcepts
                .AsNoTracking()
                .Where(c => c.ConceptGraphSnapshotId == snapshotId.Value)
                .OrderBy(c => c.Order)
                .Select(c => c.StableKey)
                .Take(8)
                .ToListAsync(ct);
            if (graphConcepts.Count > 0) return graphConcepts;
        }

        return await _db.AssessmentItems
            .AsNoTracking()
            .Where(i => i.UserId == userId && i.TopicId == topicId)
            .OrderBy(i => i.Order)
            .Select(i => i.ConceptKey)
            .Distinct()
            .Take(8)
            .ToListAsync(ct);
    }

    private async Task<string> GetStopReasonAsync(AdaptiveAssessmentSession session, CancellationToken ct)
    {
        if (session.AnsweredCount >= session.MaxItems) return "max_items_reached";
        if (session.AnsweredCount < session.MinItems) return string.Empty;
        var targets = DeserializeList(session.TargetConceptsJson);
        if (targets.Count == 0) return "minimum_items_completed";
        var states = await _db.KnowledgeTracingStates
            .AsNoTracking()
            .Where(s => s.UserId == session.UserId && s.TopicId == session.TopicId && targets.Contains(s.ConceptKey))
            .ToListAsync(ct);
        
        if (states.Count > 0 && states.All(s => s.EvidenceCount >= 3 && s.Confidence >= 0.60m))
        {
            if (states.Any(s => s.IncorrectCount >= 2 || s.MasteryProbability < 0.50m))
            {
                return "remediation_required";
            }
            return "evidence_sufficient";
        }
        return string.Empty;
    }

    private async Task CompleteAsync(AdaptiveAssessmentSession session, string reason, CancellationToken ct)
    {
        session.Status = "completed";
        session.StopReason = reason;
        session.CompletedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        if (session.QuizRunId.HasValue)
        {
            var quizRun = await _db.QuizRuns.FirstOrDefaultAsync(q => q.Id == session.QuizRunId.Value, ct);
            if (quizRun != null)
            {
                quizRun.Status = "completed";
                quizRun.CompletedAt ??= DateTime.UtcNow;
                quizRun.CorrectCount = session.CorrectCount;
            }
        }
        await _db.SaveChangesAsync(ct);
        await _events.RecordSignalEventAsync(session.UserId, session.TopicId, session.SessionId, "assessment.adaptive.completed", payloadJson: JsonSerializer.Serialize(new
        {
            schemaVersion = "orka.adaptive-assessment.v1",
            adaptiveSessionId = session.Id,
            reason,
            session.AnsweredCount,
            session.CorrectCount
        }, JsonOptions), ct: ct);
    }

    private static AdaptiveAssessmentSessionDto ToDto(AdaptiveAssessmentSession session) => new()
    {
        Id = session.Id,
        UserId = session.UserId,
        TopicId = session.TopicId,
        SessionId = session.SessionId,
        QuizRunId = session.QuizRunId,
        Status = session.Status,
        TargetConcepts = DeserializeList(session.TargetConceptsJson),
        StopReason = session.StopReason,
        MinItems = session.MinItems,
        MaxItems = session.MaxItems,
        AnsweredCount = session.AnsweredCount,
        CorrectCount = session.CorrectCount,
        CreatedAt = session.CreatedAt
    };

    private static AdaptiveAssessmentDecisionDto ToDecisionDto(AdaptiveAssessmentDecision decision)
    {
        var question = StripDecisionAnswerKey(JsonSerializer.Deserialize<QuizDataDto>(decision.SelectedQuestionJson, JsonOptions) ?? new QuizDataDto());
        return new AdaptiveAssessmentDecisionDto
        {
            Id = decision.Id,
            AssessmentItemId = decision.AssessmentItemId,
            ConceptKey = decision.ConceptKey,
            AssessmentMode = decision.AssessmentMode,
            SelectionScore = decision.SelectionScore,
            MasteryProbability = decision.MasteryProbability,
            MasteryConfidence = decision.MasteryConfidence,
            ItemQualityScore = decision.ItemQualityScore,
            ExposurePenalty = decision.ExposurePenalty,
            DecisionReason = decision.DecisionReason,
            Question = question
        };
    }

    private static QuizDataDto StripDecisionAnswerKey(QuizDataDto dto)
    {
        dto.Explanation = string.Empty;
        dto.Options = dto.Options.Select(option => new QuizOptionDto(option.Id, option.Text, false)).ToArray();
        return dto;
    }

    private static string MergeAdaptiveSourceRefs(string? sourceRefsJson, Guid adaptiveSessionId, Guid decisionId, string? assessmentMode)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(sourceRefsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(sourceRefsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        dict[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
                    }
                }
            }
            catch
            {
                dict["sourceRefsParseStatus"] = "invalid_json_ignored";
            }
        }
        dict["adaptiveSessionId"] = adaptiveSessionId;
        dict["adaptiveDecisionId"] = decisionId;
        dict["assessmentMode"] = NormalizeAssessmentMode(assessmentMode, dict.TryGetValue("assessmentMode", out var existing) ? existing?.ToString() : null, "retrieval_practice");
        return JsonSerializer.Serialize(dict, JsonOptions);
    }

    private static string NormalizeAssessmentMode(params string?[] values)
    {
        foreach (var value in values)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized is "diagnostic_check" or "micro_quiz" or "misconception_probe" or "retrieval_practice" or "readiness_check" or "review_check")
            {
                return normalized;
            }
            if (normalized is "adaptive") return "retrieval_practice";
        }

        return "retrieval_practice";
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? []; }
        catch { return []; }
    }
}

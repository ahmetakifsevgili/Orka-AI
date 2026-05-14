using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class AdaptiveStudyPlannerService : IAdaptiveStudyPlannerService
{
    public Task<AdaptiveStudyPlanDto> BuildAsync(
        Guid userId,
        AdaptiveStudyPlanRequestDto? request,
        LearningMemoryLiteDto? learningMemory,
        IReadOnlyList<DashboardWeakConceptDto> weakConcepts,
        DashboardSourceHealthDto sourceHealth,
        DashboardActivePlanDto? activePlan,
        int dueReviewCount,
        IReadOnlyCollection<Guid> topicScopeIds,
        CancellationToken ct = default)
    {
        _ = userId;
        _ = topicScopeIds;
        ct.ThrowIfCancellationRequested();

        var effectiveRequest = NormalizeRequest(request);
        var warnings = BuildWarnings(effectiveRequest, sourceHealth, activePlan);
        var diagnostic = BuildDiagnostic(effectiveRequest, learningMemory);
        if (effectiveRequest.WeeklyAvailableMinutes <= 0)
        {
            return Task.FromResult(new AdaptiveStudyPlanDto
            {
                Summary = "Plan üretmek için haftalık çalışma süresi 0'dan büyük olmalı.",
                WindowDays = 7,
                Items = Array.Empty<AdaptiveStudyPlanItemDto>(),
                Warnings = warnings,
                Diagnostic = diagnostic,
                HasEnoughSignals = false,
                GeneratedAt = DateTimeOffset.UtcNow
            });
        }

        var items = new List<AdaptiveStudyPlanItemDto>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var minutes = EstimateItemMinutes(effectiveRequest.WeeklyAvailableMinutes);

        if (dueReviewCount > 0)
        {
            AddItem(items, seenKeys, new AdaptiveStudyPlanItemDto
            {
                Title = "Bekleyen tekrarları kapat",
                Reason = $"{dueReviewCount} tekrar hazır. Önce bunları bitirmek yeni konudan daha verimli olur.",
                ActionType = "continue_lesson",
                EstimatedMinutes = minutes,
                Priority = 100,
                EvidenceBasis = ["due_review"],
                ConfidenceStatus = "usable"
            }, "due-review");
        }

        foreach (var concept in learningMemory?.RemediationReadyItems ?? Array.Empty<LearningMemoryConceptDto>())
        {
            var seed = concept.RemediationSeed;
            AddItem(items, seenKeys, new AdaptiveStudyPlanItemDto
            {
                Title = RemediationTitle(seed?.FirstAction, concept.Label),
                Reason = UserSafeReason(seed?.Reason, concept.ConfidenceStatus),
                TopicId = concept.TopicId,
                ActionType = NormalizeAction(seed?.FirstAction, "tutor_explain"),
                EstimatedMinutes = minutes,
                Priority = concept.ConfidenceStatus == "usable" ? 90 : 58,
                EvidenceBasis = MergeBasis(concept.EvidenceBasis, seed?.EvidenceBasis, ["remediation_seed"]),
                ConfidenceStatus = concept.ConfidenceStatus ?? seed?.ConfidenceStatus ?? "observed_only"
            }, $"remediation-{concept.ConceptKey}-{concept.TopicId}");
        }

        foreach (var concept in weakConcepts)
        {
            AddItem(items, seenKeys, new AdaptiveStudyPlanItemDto
            {
                Title = RemediationTitle(concept.RemediationSeed?.FirstAction, concept.Label),
                Reason = UserSafeReason(concept.RemediationSeed?.Reason, concept.LearningSignalConfidence?.Status),
                TopicId = concept.TopicId,
                ActionType = NormalizeAction(concept.RemediationSeed?.FirstAction, "tutor_explain"),
                EstimatedMinutes = minutes,
                Priority = concept.LearningSignalConfidence?.Status == "usable" ? 85 : 52,
                EvidenceBasis = MergeBasis(concept.RemediationSeed?.EvidenceBasis, ["weak_concept"]),
                ConfidenceStatus = concept.LearningSignalConfidence?.Status ?? concept.RemediationSeed?.ConfidenceStatus ?? "observed_only"
            }, $"weak-{concept.ConceptKey}-{concept.TopicId}");
        }

        if (activePlan is not null)
        {
            AddItem(items, seenKeys, new AdaptiveStudyPlanItemDto
            {
                Title = "Kaldığın dersten devam et",
                Reason = "Bu ders son çalıştığın konu; ritmi bozmadan kısa bir blokla devam edebilirsin.",
                TopicId = activePlan.TopicId,
                ActionType = "continue_lesson",
                EstimatedMinutes = minutes,
                Priority = 70,
                EvidenceBasis = ["active_lesson"],
                ConfidenceStatus = learningMemory?.ConfidenceStatus ?? "observed_only"
            }, "active-lesson");
        }

        if (SourceNeedsAttention(sourceHealth))
        {
            AddItem(items, seenKeys, new AdaptiveStudyPlanItemDto
            {
                Title = "Kaynakları kontrol et",
                Reason = "Bu konuda kaynak güveni sınırlı; kaynak eklemek veya kontrol etmek yanıt kalitesini artırır.",
                TopicId = activePlan?.TopicId,
                ActionType = "source_check",
                EstimatedMinutes = Math.Min(20, minutes),
                Priority = 62,
                EvidenceBasis = ["source_readiness", sourceHealth.EvidenceQuality?.Status ?? sourceHealth.Status],
                ConfidenceStatus = "observed_only"
            }, "source-check");
        }

        if (diagnostic.ShouldRunDiagnostic)
        {
            AddItem(items, seenKeys, new AdaptiveStudyPlanItemDto
            {
                Title = "Kısa seviye tespiti yap",
                Reason = diagnostic.UserSafeReason,
                TopicId = activePlan?.TopicId,
                ActionType = "diagnostic_check",
                EstimatedMinutes = Math.Min(15, minutes),
                Priority = items.Count == 0 ? 80 : 45,
                EvidenceBasis = ["goal_readiness", "diagnostic_intake"],
                ConfidenceStatus = "observed_only"
            }, "diagnostic");
        }

        if (items.Count == 0)
        {
            AddFallbackItems(items, seenKeys, activePlan, minutes);
        }

        var ordered = items
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        return Task.FromResult(new AdaptiveStudyPlanDto
        {
            Summary = BuildSummary(effectiveRequest, learningMemory, ordered),
            WindowDays = 7,
            Items = ordered,
            Warnings = warnings,
            Diagnostic = diagnostic,
            HasEnoughSignals = learningMemory?.HasEnoughSignals == true,
            GeneratedAt = DateTimeOffset.UtcNow
        });
    }

    private static AdaptiveStudyPlanRequestDto NormalizeRequest(AdaptiveStudyPlanRequestDto? request)
    {
        if (request is null)
        {
            return new AdaptiveStudyPlanRequestDto();
        }

        return new AdaptiveStudyPlanRequestDto
        {
            GoalType = NormalizeGoalType(request.GoalType),
            TargetDate = request.TargetDate,
            WeeklyAvailableMinutes = request.WeeklyAvailableMinutes,
            CurrentLevel = NormalizeLevel(request.CurrentLevel),
            ExamName = TrimToLength(request.ExamName, 64),
            CareerTarget = TrimToLength(request.CareerTarget, 80),
            PriorityTopicIds = request.PriorityTopicIds?.Where(id => id != Guid.Empty).Distinct().Take(8).ToArray() ?? Array.Empty<Guid>(),
            PrioritySkills = request.PrioritySkills?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => TrimToLength(s, 48)!).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray() ?? Array.Empty<string>()
        };
    }

    private static List<string> BuildWarnings(AdaptiveStudyPlanRequestDto request, DashboardSourceHealthDto sourceHealth, DashboardActivePlanDto? activePlan)
    {
        var warnings = new List<string>();
        if (request.WeeklyAvailableMinutes <= 0)
        {
            warnings.Add("Haftalık çalışma süresi 0'dan büyük olmalı.");
        }

        if (request.TargetDate.HasValue && request.TargetDate.Value.Date < DateTimeOffset.UtcNow.Date)
        {
            warnings.Add("Hedef tarih geçmişte görünüyor; planı güncel bir tarihle tekrar oluştur.");
        }

        if (request.GoalType == "exam")
        {
            warnings.Add("Bu plan mevcut konu ağına ve öğrenme sinyallerine göre hazırlanır; resmi sınav duyuruları için kurum kaynaklarını kontrol et.");
        }

        if (request.GoalType == "career")
        {
            warnings.Add("Bu rota mevcut konu ağına göre profesyonel gelişim planıdır; işe giriş garantisi değildir.");
        }

        if (activePlan is null)
        {
            warnings.Add("Aktif konu ağı yok; plan kısa seviye tespiti ve başlangıç adımlarıyla sınırlı.");
        }

        if (SourceNeedsAttention(sourceHealth))
        {
            warnings.Add("Kaynak güveni sınırlı; kaynakları kontrol etmek plan kalitesini artırır.");
        }

        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static DiagnosticResultDto BuildDiagnostic(AdaptiveStudyPlanRequestDto request, LearningMemoryLiteDto? memory)
    {
        var readiness = memory?.GoalReadiness ?? new GoalReadinessDto();
        var selfDeclared = NormalizeLevel(request.CurrentLevel);
        var observed = NormalizeLevel(readiness.ObservedLevel);
        var weakAreas = readiness.SuggestedDiagnosticFocus
            .Concat(readiness.PlannerReadyWeakAreas.Select(a => a.Label))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
        var mismatch = selfDeclared != "unknown" &&
                       observed != "unknown" &&
                       selfDeclared != observed;
        var shouldRun = readiness.NeedsMoreEvidence ||
                        readiness.ObservedLevelConfidence < 0.45m ||
                        mismatch ||
                        weakAreas.Length == 0;

        var reason = mismatch
            ? "Beyan ettiğin seviye ile Orka'nın gözlediği sinyal tam örtüşmüyor; kısa seviye tespiti iyi olur."
            : shouldRun
                ? "Bu konuda yeterli sinyal yok; kısa seviye tespiti iyi olur."
                : "Mevcut öğrenme sinyalleri plan için yeterli görünüyor.";

        return new DiagnosticResultDto
        {
            Intake = new DiagnosticIntakeDto
            {
                SelfDeclaredLevel = selfDeclared,
                ObservedLevel = observed,
                ObservedLevelConfidence = readiness.ObservedLevelConfidence,
                NeedsMoreEvidence = readiness.NeedsMoreEvidence,
                WeakAreas = weakAreas
            },
            RecommendedStartingPoint = weakAreas.FirstOrDefault() ?? "Kısa seviye tespiti",
            ShouldRunDiagnostic = shouldRun,
            UserSafeReason = reason
        };
    }

    private static void AddFallbackItems(List<AdaptiveStudyPlanItemDto> items, HashSet<string> seenKeys, DashboardActivePlanDto? activePlan, int minutes)
    {
        AddItem(items, seenKeys, new AdaptiveStudyPlanItemDto
        {
            Title = "Kısa seviye tespiti yap",
            Reason = "Bu konuda yeterli sinyal yok; kısa seviye tespiti iyi olur.",
            TopicId = activePlan?.TopicId,
            ActionType = "diagnostic_check",
            EstimatedMinutes = Math.Min(15, minutes),
            Priority = 80,
            EvidenceBasis = ["diagnostic_intake"],
            ConfidenceStatus = "observed_only"
        }, "fallback-diagnostic");

        AddItem(items, seenKeys, new AdaptiveStudyPlanItemDto
        {
            Title = activePlan is null ? "İlk dersi başlat" : "Kaldığın dersten devam et",
            Reason = activePlan is null
                ? "Henüz konu kanıtı yok; ilk ders planın temelini oluşturur."
                : "Bu ders son çalıştığın konu; kısa bir blokla devam edebilirsin.",
            TopicId = activePlan?.TopicId,
            ActionType = "continue_lesson",
            EstimatedMinutes = minutes,
            Priority = 60,
            EvidenceBasis = ["onboarding"],
            ConfidenceStatus = "observed_only"
        }, "fallback-lesson");
    }

    private static void AddItem(List<AdaptiveStudyPlanItemDto> items, HashSet<string> seenKeys, AdaptiveStudyPlanItemDto item, string key)
    {
        if (string.IsNullOrWhiteSpace(item.Title) || !seenKeys.Add(key))
        {
            return;
        }

        items.Add(item);
    }

    private static string BuildSummary(AdaptiveStudyPlanRequestDto request, LearningMemoryLiteDto? memory, IReadOnlyList<AdaptiveStudyPlanItemDto> items)
    {
        if (items.Count == 0)
        {
            return "Plan üretmek için hedef bilgisi ve çalışma süresi güncellenmeli.";
        }

        var domain = request.GoalType switch
        {
            "exam" => string.IsNullOrWhiteSpace(request.ExamName) ? "sınav hedefi" : request.ExamName,
            "career" => string.IsNullOrWhiteSpace(request.CareerTarget) ? "kariyer hedefi" : request.CareerTarget,
            _ => "öğrenme hedefi"
        };
        var memorySummary = memory?.HasEnoughSignals == true
            ? "öğrenme sinyallerine göre"
            : "sınırlı sinyalle";
        return $"{domain} için {memorySummary} {items.Count} adımlık çalışma rotası hazır.";
    }

    private static string RemediationTitle(string? action, string label)
    {
        var safeLabel = string.IsNullOrWhiteSpace(label) ? "Bu kavram" : label.Trim();
        return NormalizeAction(action, "tutor_explain") switch
        {
            "wiki_review" => $"{safeLabel}: Wiki'den tekrar et",
            "practice_quiz" => $"{safeLabel}: pratik çöz",
            "source_check" => $"{safeLabel}: kaynakları kontrol et",
            "prerequisite_review" => $"{safeLabel}: ön koşulu tekrar et",
            _ => $"{safeLabel}: Tutor'da telafi et"
        };
    }

    private static string UserSafeReason(string? reason, string? confidenceStatus)
    {
        if (!string.IsNullOrWhiteSpace(reason))
        {
            return reason.Trim();
        }

        return confidenceStatus == "usable"
            ? "Bu kavram için telafi önerisi hazır."
            : "Bu konuda zayıf sinyal olabilir; kısa kontrol iyi olur.";
    }

    private static IReadOnlyList<string> MergeBasis(params IEnumerable<string?>?[] values) =>
        values
            .Where(v => v is not null)
            .SelectMany(v => v!)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

    private static string NormalizeAction(string? action, string fallback)
    {
        return (action ?? fallback).ToLowerInvariant() switch
        {
            "wiki_review" => "wiki_review",
            "practice_quiz" => "practice_quiz",
            "source_check" => "source_check",
            "prerequisite_review" => "prerequisite_review",
            "diagnostic_check" => "diagnostic_check",
            "continue_lesson" => "continue_lesson",
            _ => "tutor_explain"
        };
    }

    private static bool SourceNeedsAttention(DashboardSourceHealthDto sourceHealth)
    {
        var evidenceStatus = sourceHealth.EvidenceQuality?.Status?.ToLowerInvariant();
        var status = sourceHealth.Status?.ToLowerInvariant();
        return evidenceStatus is "weak" or "missing" ||
               status is "unknown" or "source_retrieval_empty" or "degraded" or "citation_missing" or "citation_unsupported";
    }

    private static int EstimateItemMinutes(int weeklyAvailableMinutes)
    {
        if (weeklyAvailableMinutes <= 0)
        {
            return 0;
        }

        return Math.Clamp(weeklyAvailableMinutes / 9, 10, 45);
    }

    private static string NormalizeGoalType(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "exam" => "exam",
            "career" => "career",
            _ => "general_learning"
        };
    }

    private static string NormalizeLevel(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "advanced" or "ileri" or "high" or "yüksek") return "advanced";
        if (normalized is "intermediate" or "orta" or "medium") return "intermediate";
        if (normalized is "beginner" or "foundation" or "başlangıç" or "baslangic" or "low") return "foundation";
        return "unknown";
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

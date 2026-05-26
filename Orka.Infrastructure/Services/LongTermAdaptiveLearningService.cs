using Microsoft.EntityFrameworkCore;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class LongTermAdaptiveLearningService : ILongTermAdaptiveLearningService
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan ForgettingWindow = TimeSpan.FromDays(21);

    private readonly OrkaDbContext _db;

    public LongTermAdaptiveLearningService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<LongTermLearningProfileDto> BuildProfileAsync(
        Guid userId,
        IReadOnlyCollection<Guid> topicScopeIds,
        DashboardSourceHealthDto? sourceHealth = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var scopedTopicIds = topicScopeIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        var hasScope = scopedTopicIds.Length > 0;

        var states = await _db.KnowledgeTracingStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && (!hasScope || (s.TopicId.HasValue && scopedTopicIds.Contains(s.TopicId.Value))))
            .OrderByDescending(s => s.UpdatedAt)
            .Take(80)
            .ToListAsync(ct);

        var masteries = await _db.ConceptMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId && (!hasScope || (m.TopicId.HasValue && scopedTopicIds.Contains(m.TopicId.Value))))
            .OrderByDescending(m => m.UpdatedAt)
            .Take(80)
            .ToListAsync(ct);

        var attempts = await _db.QuizAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && (!hasScope || (a.TopicId.HasValue && scopedTopicIds.Contains(a.TopicId.Value))))
            .OrderByDescending(a => a.CreatedAt)
            .Take(240)
            .ToListAsync(ct);

        var reviews = await _db.ReviewItems
            .AsNoTracking()
            .Where(r => r.UserId == userId &&
                        r.Status == "active" &&
                        (!hasScope || (r.TopicId.HasValue && scopedTopicIds.Contains(r.TopicId.Value))))
            .OrderBy(r => r.DueAt)
            .Take(120)
            .ToListAsync(ct);

        var signals = await _db.LearningSignals
            .AsNoTracking()
            .Where(s => s.UserId == userId && (!hasScope || (s.TopicId.HasValue && scopedTopicIds.Contains(s.TopicId.Value))))
            .OrderByDescending(s => s.CreatedAt)
            .Take(240)
            .ToListAsync(ct);

        var repairBlocks = await _db.WikiBlocks
            .AsNoTracking()
            .Where(b => !b.IsDeleted && (b.BlockType == WikiBlockType.RepairNote || b.BlockType == WikiBlockType.MisconceptionNote))
            .Join(
                _db.WikiPages.AsNoTracking().Where(p => p.UserId == userId && !p.IsDeleted && (!hasScope || scopedTopicIds.Contains(p.TopicId))),
                block => block.WikiPageId,
                page => page.Id,
                (block, page) => new
                {
                    page.TopicId,
                    ConceptKey = block.ConceptKey ?? page.ConceptKey ?? page.PageKey,
                    Label = string.IsNullOrWhiteSpace(block.Title) ? page.Title : block.Title,
                    block.CreatedAt
                })
            .OrderByDescending(b => b.CreatedAt)
            .Take(80)
            .ToListAsync(ct);

        var latestBundle = await _db.SourceEvidenceBundles
            .AsNoTracking()
            .Where(b => b.UserId == userId && !b.IsDeleted && (!hasScope || (b.TopicId.HasValue && scopedTopicIds.Contains(b.TopicId.Value))))
            .OrderByDescending(b => b.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var concepts = new Dictionary<string, ConceptEvidence>(StringComparer.OrdinalIgnoreCase);
        ConceptEvidence Upsert(Guid? topicId, string? conceptKey, string? label)
        {
            var safeKey = SafeConceptKey(conceptKey, label);
            var composite = $"{(topicId ?? Guid.Empty):N}:{safeKey}";
            if (!concepts.TryGetValue(composite, out var concept))
            {
                concept = new ConceptEvidence(topicId, safeKey, SafeLabel(label, safeKey));
                concepts[composite] = concept;
            }
            else if (string.IsNullOrWhiteSpace(concept.Label) || string.Equals(concept.Label, concept.ConceptKey, StringComparison.OrdinalIgnoreCase))
            {
                concept.Label = SafeLabel(label, safeKey);
            }

            return concept;
        }

        foreach (var state in states)
        {
            var concept = Upsert(state.TopicId, state.ConceptKey, state.Label);
            concept.EvidenceCount = Math.Max(concept.EvidenceCount, state.EvidenceCount);
            concept.CorrectCount = Math.Max(concept.CorrectCount, state.CorrectCount);
            concept.WrongCount = Math.Max(concept.WrongCount, state.IncorrectCount);
            concept.MasteryProbability = MaxNullable(concept.MasteryProbability, state.MasteryProbability);
            concept.Confidence = MaxNullable(concept.Confidence, state.Confidence);
            concept.RemediationNeed = StrongerRemediation(concept.RemediationNeed, state.RemediationNeed);
            concept.PracticeReadiness = string.IsNullOrWhiteSpace(state.PracticeReadiness) ? concept.PracticeReadiness : state.PracticeReadiness;
            concept.LastPracticedAt = MaxDate(concept.LastPracticedAt, state.LastEvidenceAt);
            concept.EvidenceBasis.Add("knowledge_tracing");
        }

        foreach (var mastery in masteries)
        {
            var concept = Upsert(mastery.TopicId, mastery.ConceptKey, mastery.Label);
            concept.EvidenceCount = Math.Max(concept.EvidenceCount, mastery.Attempts);
            concept.CorrectCount = Math.Max(concept.CorrectCount, mastery.Correct);
            concept.WrongCount = Math.Max(concept.WrongCount, Math.Max(0, mastery.Attempts - mastery.Correct));
            concept.MasteryProbability = MaxNullable(concept.MasteryProbability, mastery.MasteryScore / 100m);
            concept.Confidence = MaxNullable(concept.Confidence, mastery.Confidence);
            concept.RemediationNeed = StrongerRemediation(concept.RemediationNeed, mastery.RemediationNeed);
            concept.PracticeReadiness = string.IsNullOrWhiteSpace(mastery.PracticeReadiness) ? concept.PracticeReadiness : mastery.PracticeReadiness;
            concept.LastPracticedAt = MaxDate(concept.LastPracticedAt, mastery.LastEvidenceAt);
            concept.EvidenceBasis.Add("concept_mastery");
        }

        foreach (var attempt in attempts)
        {
            var concept = Upsert(attempt.TopicId, attempt.SkillTag ?? attempt.TopicPath ?? attempt.QuestionHash, attempt.SkillTag ?? attempt.TopicPath);
            concept.EvidenceCount++;
            concept.LastPracticedAt = MaxDate(concept.LastPracticedAt, attempt.CreatedAt);
            concept.EvidenceBasis.Add("quiz_attempt");
            if (attempt.IsCorrect)
            {
                concept.CorrectCount++;
                concept.LastSuccessAt = MaxDate(concept.LastSuccessAt, attempt.CreatedAt);
            }
            else if (attempt.WasSkipped || string.IsNullOrWhiteSpace(attempt.UserAnswer))
            {
                concept.BlankOrSkippedCount++;
                concept.LastFailureAt = MaxDate(concept.LastFailureAt, attempt.CreatedAt);
            }
            else
            {
                concept.WrongCount++;
                concept.LastFailureAt = MaxDate(concept.LastFailureAt, attempt.CreatedAt);
            }
        }

        foreach (var review in reviews)
        {
            var concept = Upsert(review.TopicId, ReviewConceptKey(review), ReviewConceptLabel(review));
            concept.DueAt = MinDate(concept.DueAt, review.DueAt);
            concept.ReviewLapseCount += review.LapseCount;
            concept.ReviewSuccessStreak = Math.Max(concept.ReviewSuccessStreak, review.SuccessStreak);
            concept.EvidenceBasis.Add("srs_review");
        }

        foreach (var signal in signals)
        {
            var conceptKey = signal.SkillTag ?? signal.TopicPath;
            if (string.IsNullOrWhiteSpace(conceptKey))
            {
                continue;
            }

            var concept = Upsert(signal.TopicId, conceptKey, signal.SkillTag ?? signal.TopicPath);
            concept.EvidenceBasis.Add("learning_signal");
            if (IsRepairSignal(signal.SignalType))
            {
                concept.RepairCount++;
            }
            if (IsWeaknessSignal(signal.SignalType))
            {
                concept.WrongCount = Math.Max(concept.WrongCount, 1);
            }
        }

        foreach (var block in repairBlocks)
        {
            var concept = Upsert(block.TopicId, block.ConceptKey, block.Label);
            concept.RepairPending = true;
            concept.RepairCount = Math.Max(concept.RepairCount, 1);
            concept.EvidenceBasis.Add("wiki_repair_note");
        }

        var sourceLimited = SourceNeedsReview(sourceHealth, latestBundle);
        var warnings = BuildWarnings(sourceHealth, latestBundle, sourceLimited);
        var profileConcepts = concepts.Values
            .Select(c => BuildConcept(c, now, sourceLimited))
            .OrderByDescending(c => PriorityScore(c.ReviewPriority))
            .ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();

        var reviewPressure = profileConcepts
            .Where(c => c.ReviewPriority != "none")
            .Select(c => new AdaptiveReviewPressureDto
            {
                TopicId = c.TopicId,
                ConceptKey = c.ConceptKey,
                Label = c.Label,
                Priority = c.ReviewPriority,
                RecommendedAction = c.RecommendedAction,
                UserSafeReason = c.UserSafeReason,
                DaysOverdue = DaysOverdue(c.EvidenceBasis.Contains("srs_review") ? concepts.Values.FirstOrDefault(v => SameConcept(v, c))?.DueAt : null, now),
                DueAt = ToOffset(concepts.Values.FirstOrDefault(v => SameConcept(v, c))?.DueAt),
                ConfidenceStatus = c.ConfidenceStatus,
                ReasonCodes = c.ReasonCodes,
                EvidenceBasis = c.EvidenceBasis
            })
            .Take(12)
            .ToArray();

        var nextActions = BuildNextActions(profileConcepts, sourceLimited, warnings);
        var rhythm = BuildRhythm(profileConcepts, nextActions, warnings);
        var reasonCodes = profileConcepts
            .SelectMany(c => c.ReasonCodes)
            .Concat(warnings.Select(w => w.Contains("Kaynak", StringComparison.OrdinalIgnoreCase) ? "source_evidence_limited" : "thin_evidence"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        var uniqueQuizAttempts = attempts.Select(a => a.Id).Union(signals.Where(s => s.QuizAttemptId.HasValue).Select(s => s.QuizAttemptId!.Value)).Distinct().Count();
        var uniqueActiveReviews = reviews.Count;
        var uniqueRepairBlocks = repairBlocks.Count;
        var uniqueNonLinkedSignals = signals.Where(s => !s.QuizAttemptId.HasValue).Select(s => s.Id).Distinct().Count();
        var evidenceCount = uniqueQuizAttempts + uniqueActiveReviews + uniqueRepairBlocks + uniqueNonLinkedSignals;

        return new LongTermLearningProfileDto
        {
            Summary = BuildSummary(profileConcepts, reviewPressure, sourceLimited, evidenceCount),
            WindowDays = 7,
            HasEnoughEvidence = evidenceCount >= 3 || profileConcepts.Any(c => c.ConfidenceStatus is "medium" or "high"),
            EvidenceCount = evidenceCount,
            Concepts = profileConcepts,
            ReviewPressure = reviewPressure,
            WeeklyRhythm = rhythm,
            NextActions = nextActions,
            ReasonCodes = reasonCodes,
            Warnings = warnings,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static LongTermLearningConceptDto BuildConcept(ConceptEvidence concept, DateTime now, bool sourceLimited)
    {
        var reasons = new List<string>();
        if (concept.DueAt.HasValue && concept.DueAt.Value <= now) reasons.Add("due_srs");
        if (concept.WrongCount >= 1 && concept.LastFailureAt.HasValue && now - concept.LastFailureAt.Value <= RecentWindow) reasons.Add("recent_wrong_answer");
        if (concept.WrongCount >= 2) reasons.Add("prerequisite_gap");
        if (concept.BlankOrSkippedCount >= 2) reasons.Add("repeated_blank");
        if (concept.RepairPending || concept.RemediationNeed is "high" or "medium") reasons.Add("repair_pending");
        if (IsWeak(concept)) reasons.Add("weak_concept");
        if (IsLikelyForgotten(concept, now)) reasons.Add("likely_forgotten");
        if (IsStable(concept, now)) reasons.Add("stable_recent_success");

        var priority = ReviewPriority(concept, reasons, now);
        var action = RecommendedAction(concept, reasons, priority);
        var state = ConceptState(concept, reasons, now);
        var confidenceStatus = ConfidenceStatus(concept.Confidence, concept.EvidenceCount);

        return new LongTermLearningConceptDto
        {
            TopicId = concept.TopicId,
            ConceptKey = concept.ConceptKey,
            Label = concept.Label,
            State = state,
            MasteryProbability = concept.MasteryProbability,
            Confidence = concept.Confidence,
            ConfidenceStatus = confidenceStatus,
            EvidenceCount = concept.EvidenceCount,
            CorrectCount = concept.CorrectCount,
            WrongCount = concept.WrongCount,
            BlankOrSkippedCount = concept.BlankOrSkippedCount,
            RepairCount = concept.RepairCount,
            LastPracticedAt = ToOffset(concept.LastPracticedAt),
            LastSuccessAt = ToOffset(concept.LastSuccessAt),
            LastFailureAt = ToOffset(concept.LastFailureAt),
            ReviewPriority = priority,
            RecommendedAction = action,
            UserSafeReason = UserSafeReason(reasons, priority, action),
            ReasonCodes = reasons.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(),
            EvidenceBasis = concept.EvidenceBasis.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray()
        };
    }

    private static IReadOnlyList<AdaptiveNextStudyActionDto> BuildNextActions(
        IReadOnlyList<LongTermLearningConceptDto> concepts,
        bool sourceLimited,
        IReadOnlyList<string> warnings)
    {
        var actions = concepts
            .Where(c => c.ReviewPriority != "none")
            .OrderByDescending(c => PriorityScore(c.ReviewPriority))
            .ThenByDescending(c => c.ReasonCodes.Contains("repair_pending"))
            .Take(5)
            .Select(c => new AdaptiveNextStudyActionDto
            {
                ActionType = c.RecommendedAction,
                Label = ActionLabel(c),
                Reason = c.UserSafeReason,
                TopicId = c.TopicId,
                ConceptKey = c.ConceptKey,
                Priority = c.ReviewPriority,
                ReasonCodes = c.ReasonCodes
            })
            .ToList();

        if (sourceLimited && actions.All(a => a.ActionType != "source_review"))
        {
            actions.Add(new AdaptiveNextStudyActionDto
            {
                ActionType = "source_review",
                Label = "Kaynak kanıtını kontrol et",
                Reason = "Kaynak kanıtı sınırlı; kaynaklı çalışma öncesi kanıt durumunu kontrol etmek güvenli olur.",
                Priority = actions.Count == 0 ? "medium" : "low",
                ReasonCodes = ["source_evidence_limited"]
            });
        }

        if (actions.Count == 0)
        {
            actions.Add(new AdaptiveNextStudyActionDto
            {
                ActionType = "continue_plan",
                Label = "Plana devam et",
                Reason = warnings.Count > 0
                    ? "Kanıt sınırlı; kısa kontrolle plana devam etmek uygun."
                    : "Belirgin telafi veya tekrar baskısı yok; plana devam edebilirsin.",
                Priority = "normal",
                ReasonCodes = warnings.Count > 0 ? ["thin_evidence"] : ["stable_recent_success"]
            });
        }

        return actions
            .GroupBy(a => $"{a.ActionType}:{a.ConceptKey}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(6)
            .ToArray();
    }

    private static AdaptiveLearningRhythmDto BuildRhythm(
        IReadOnlyList<LongTermLearningConceptDto> concepts,
        IReadOnlyList<AdaptiveNextStudyActionDto> nextActions,
        IReadOnlyList<string> warnings)
    {
        var weak = concepts
            .Where(c => (c.State == "weak" || c.State == "learning") && c.ReviewPriority is "medium" or "high" or "urgent")
            .Take(5)
            .ToArray();
        var due = concepts.Where(c => c.ReasonCodes.Contains("due_srs") || c.State is "due_for_review" or "likely_forgotten").Take(5).ToArray();
        var stable = concepts.Where(c => c.State == "stable").Take(5).ToArray();
        var repairCount = concepts.Count(c => c.RecommendedAction == "repair");
        var reviewCount = concepts.Count(c => c.RecommendedAction is "review" or "checkpoint");

        return new AdaptiveLearningRhythmDto
        {
            TodayFocus = nextActions.FirstOrDefault()?.Label ?? "Kısa seviye tespiti",
            ThisWeekFocus = repairCount > 0
                ? "Önce telafi ve ön koşul kapatma"
                : reviewCount > 0
                    ? "Tekrarları kapatıp plana devam et"
                    : "Yeni öğrenme ve kısa kontrol dengesi",
            ReviewLoad = LoadLabel(reviewCount),
            NewLearningLoad = repairCount + reviewCount >= 5 ? "light" : "normal",
            RepairLoad = LoadLabel(repairCount),
            WeakConcepts = weak.Select(c => c.Label).ToArray(),
            DueConcepts = due.Select(c => c.Label).ToArray(),
            StableConcepts = stable.Select(c => c.Label).ToArray(),
            NextBestAction = nextActions.FirstOrDefault() ?? new AdaptiveNextStudyActionDto(),
            ReasonCodes = concepts.SelectMany(c => c.ReasonCodes).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            Warnings = warnings
        };
    }

    private static IReadOnlyList<string> BuildWarnings(DashboardSourceHealthDto? sourceHealth, SourceEvidenceBundle? bundle, bool sourceLimited)
    {
        var warnings = new List<string>();
        if (sourceLimited)
        {
            warnings.Add("Kaynak kanıtı sınırlı; kaynaklı çalışma için önce kaynak durumunu kontrol et.");
        }

        if (sourceHealth?.Status is "unknown" or "source_retrieval_empty")
        {
            warnings.Add("Kaynak durumu tam net değil; model cevabı kaynak kanıtı sayılmaz.");
        }

        if (bundle?.EvidenceStatus is "stale" or "degraded" or "evidence_insufficient")
        {
            warnings.Add("Kaynak evidence bundle sınırlı veya eski görünüyor.");
        }

        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
    }

    private static bool SourceNeedsReview(DashboardSourceHealthDto? sourceHealth, SourceEvidenceBundle? bundle)
    {
        var status = Normalize(sourceHealth?.Status);
        var evidenceStatus = Normalize(sourceHealth?.EvidenceQuality?.Status);
        var bundleStatus = Normalize(bundle?.EvidenceStatus);
        return status is "unknown" or "source_retrieval_empty" or "degraded" or "citation_missing" or "citation_unsupported" ||
               evidenceStatus is "missing" or "weak" ||
               bundleStatus is "stale" or "degraded" or "evidence_insufficient" ||
               bundle is { StaleEvidenceCount: > 0 } or { DeletedEvidenceCount: > 0 };
    }

    private static string BuildSummary(
        IReadOnlyList<LongTermLearningConceptDto> concepts,
        IReadOnlyList<AdaptiveReviewPressureDto> reviewPressure,
        bool sourceLimited,
        int evidenceCount)
    {
        if (evidenceCount == 0)
        {
            return "Henüz uzun vadeli öğrenme ritmi için yeterli kanıt yok; kısa tanı ve ilk quiz iyi başlangıç olur.";
        }

        var urgent = reviewPressure.Count(r => r.Priority == "urgent");
        var repair = concepts.Count(c => c.RecommendedAction == "repair");
        if (urgent > 0)
        {
            return $"{urgent} acil tekrar/telafi sinyali var; yeni konuya geçmeden önce bunları kapatmak daha güvenli.";
        }

        if (repair > 0)
        {
            return $"{repair} kavramda telafi baskısı var; Tutor kısa onarım ve kontrol sorusuyla ilerlemeli.";
        }

        if (reviewPressure.Count > 0)
        {
            return $"{reviewPressure.Count} kavramda tekrar baskısı var; kısa tekrar sonrası plana dönülebilir.";
        }

        return sourceLimited
            ? "Öğrenme ritmi dengeli; kaynak kanıtı sınırlı olduğu için kaynaklı çalışmada dikkat gerekiyor."
            : "Öğrenme ritmi dengeli; belirgin telafi baskısı yok.";
    }

    private static string ConceptState(ConceptEvidence concept, IReadOnlyList<string> reasons, DateTime now)
    {
        if (reasons.Contains("repair_pending") || reasons.Contains("prerequisite_gap") || IsWeak(concept)) return "weak";
        if (reasons.Contains("likely_forgotten")) return "likely_forgotten";
        if (reasons.Contains("due_srs")) return "due_for_review";
        if (concept.RepairCount > 0 && concept.CorrectCount >= Math.Max(1, concept.WrongCount) && (concept.MasteryProbability ?? 0m) >= 0.55m) return "repaired";
        if (IsStable(concept, now)) return "stable";
        if (concept.EvidenceCount > 0) return "learning";
        return "new";
    }

    private static string ReviewPriority(ConceptEvidence concept, IReadOnlyList<string> reasons, DateTime now)
    {
        var daysOverdue = concept.DueAt.HasValue ? Math.Max(0, (int)(now.Date - concept.DueAt.Value.Date).TotalDays) : 0;
        if (reasons.Contains("likely_forgotten") || concept.WrongCount >= 3 || concept.BlankOrSkippedCount >= 3 || daysOverdue >= 7) return "urgent";
        if (reasons.Contains("repair_pending") || concept.WrongCount >= 2 || concept.BlankOrSkippedCount >= 2 || reasons.Contains("due_srs")) return "high";
        if (reasons.Contains("weak_concept") || reasons.Contains("recent_wrong_answer") || reasons.Contains("source_evidence_limited")) return "medium";
        if (concept.DueAt.HasValue && concept.DueAt.Value <= now.AddDays(1)) return "low";
        return "none";
    }

    private static string RecommendedAction(ConceptEvidence concept, IReadOnlyList<string> reasons, string priority)
    {
        if (reasons.Contains("repair_pending") || reasons.Contains("prerequisite_gap") || concept.BlankOrSkippedCount >= 2) return "repair";
        if (reasons.Contains("due_srs") || reasons.Contains("likely_forgotten")) return "review";
        if (reasons.Contains("source_evidence_limited") && priority is "medium" or "high") return "source_review";
        if (reasons.Contains("recent_wrong_answer") || reasons.Contains("weak_concept")) return "checkpoint";
        if (concept.EvidenceCount <= 1) return "take_quiz";
        return "continue_plan";
    }

    private static string UserSafeReason(IReadOnlyList<string> reasons, string priority, string action)
    {
        if (reasons.Contains("repeated_blank")) return "Boş veya atlanan cevaplar tekrar etmiş; önce ön koşulu kısa ve rehberli kontrol etmek güvenli.";
        if (reasons.Contains("prerequisite_gap")) return "Yanlış cevaplar tekrar etmiş; yeni konu yerine kısa telafi ve ön koşul tekrarı önerilir.";
        if (reasons.Contains("repair_pending")) return "Wiki veya öğrenme hafızasında telafi bekleyen not var; önce onu kapatmak iyi olur.";
        if (reasons.Contains("likely_forgotten")) return "Kavram uzun süredir pratik edilmemiş veya tekrar zamanı geçmiş; unutma riski olabilir.";
        if (reasons.Contains("due_srs")) return "Zamanı gelen tekrar var; kısa tekrar hafızayı güçlendirir.";
        if (reasons.Contains("recent_wrong_answer")) return "Son denemede zorlanma sinyali var; kısa kontrol sorusu yeterli olabilir.";
        if (reasons.Contains("source_evidence_limited")) return "Kaynak kanıtı sınırlı; kaynaklı ilerlemeden önce kanıt durumunu kontrol et.";
        if (reasons.Contains("stable_recent_success")) return "Son kanıtlar dengeli; plana devam edilebilir.";
        return priority == "none" && action == "continue_plan"
            ? "Belirgin tekrar veya telafi baskısı yok."
            : "Bu kavram için kısa ve güvenli bir sonraki adım önerildi.";
    }

    private static string ActionLabel(LongTermLearningConceptDto concept) =>
        concept.RecommendedAction switch
        {
            "repair" => $"{concept.Label}: kısa telafi yap",
            "review" => $"{concept.Label}: tekrar et",
            "source_review" => $"{concept.Label}: kaynak kanıtını kontrol et",
            "checkpoint" => $"{concept.Label}: kontrol sorusu çöz",
            "take_quiz" => $"{concept.Label}: kısa quiz çöz",
            _ => $"{concept.Label}: plana devam et"
        };

    private static bool IsStable(ConceptEvidence concept, DateTime now) =>
        concept.EvidenceCount >= 3 &&
        concept.CorrectCount >= 2 &&
        (concept.MasteryProbability ?? 0m) >= 0.75m &&
        (concept.Confidence ?? 0m) >= 0.60m &&
        (!concept.DueAt.HasValue || concept.DueAt.Value > now) &&
        concept.WrongCount <= 1 &&
        concept.BlankOrSkippedCount == 0;

    private static bool IsWeak(ConceptEvidence concept) =>
        concept.RemediationNeed is "high" or "medium" ||
        (concept.MasteryProbability.HasValue && concept.MasteryProbability.Value < 0.55m && concept.EvidenceCount >= 2) ||
        concept.WrongCount >= 2 ||
        concept.BlankOrSkippedCount >= 2;

    private static bool IsLikelyForgotten(ConceptEvidence concept, DateTime now)
    {
        if (concept.DueAt.HasValue && concept.DueAt.Value <= now.AddDays(-7)) return true;
        if (!concept.LastSuccessAt.HasValue) return false;
        return now - concept.LastSuccessAt.Value >= ForgettingWindow &&
               (concept.MasteryProbability ?? 0.50m) < 0.75m;
    }

    private static string ConfidenceStatus(decimal? confidence, int evidenceCount)
    {
        if (evidenceCount < 2) return "observed_only";
        if (confidence >= 0.70m) return "high";
        if (confidence >= 0.45m) return "medium";
        return "low";
    }

    private static int DaysOverdue(DateTime? dueAt, DateTime now) =>
        dueAt.HasValue ? Math.Max(0, (int)(now.Date - dueAt.Value.Date).TotalDays) : 0;

    private static bool SameConcept(ConceptEvidence evidence, LongTermLearningConceptDto concept) =>
        evidence.TopicId == concept.TopicId &&
        string.Equals(evidence.ConceptKey, concept.ConceptKey, StringComparison.OrdinalIgnoreCase);

    private static int PriorityScore(string priority) => Normalize(priority) switch
    {
        "urgent" => 5,
        "high" => 4,
        "medium" => 3,
        "low" => 2,
        "normal" => 1,
        _ => 0
    };

    private static string LoadLabel(int count) =>
        count >= 5 ? "heavy" : count >= 3 ? "medium" : count >= 1 ? "light" : "none";

    private static bool IsRepairSignal(string signalType) =>
        string.Equals(signalType, LearningSignalTypes.RemediationStarted, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(signalType, LearningSignalTypes.RemediationCompleted, StringComparison.OrdinalIgnoreCase);

    private static bool IsWeaknessSignal(string signalType) =>
        string.Equals(signalType, LearningSignalTypes.WeaknessDetected, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(signalType, LearningSignalTypes.CentralExamWeaknessDetected, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(signalType, LearningSignalTypes.CentralExamDenemeWeaknessDetected, StringComparison.OrdinalIgnoreCase);

    private static string ReviewConceptKey(ReviewItem review) =>
        SafeConceptKey(review.ConceptTag ?? review.SkillTag ?? review.LearningObjective ?? review.ReviewKey, review.SkillTag);

    private static string ReviewConceptLabel(ReviewItem review) =>
        SafeLabel(review.LearningObjective ?? review.SkillTag ?? review.ConceptTag ?? review.ReviewKey, review.ReviewKey);

    private static string SafeConceptKey(string? conceptKey, string? label)
    {
        var value = FirstNonBlank(conceptKey, label, "unknown-concept");
        return string.Join('-', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToLowerInvariant();
    }

    private static string SafeLabel(string? label, string fallback)
    {
        var value = FirstNonBlank(label, fallback, "Kavram");
        value = string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return value.Length <= 80 ? value : value[..80];
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static decimal? MaxNullable(decimal? current, decimal next) =>
        current.HasValue ? Math.Max(current.Value, next) : next;

    private static DateTime? MaxDate(DateTime? current, DateTime? next)
    {
        if (!next.HasValue) return current;
        if (!current.HasValue) return next;
        return next.Value > current.Value ? next : current;
    }

    private static DateTime? MinDate(DateTime? current, DateTime? next)
    {
        if (!next.HasValue) return current;
        if (!current.HasValue) return next;
        return next.Value < current.Value ? next : current;
    }

    private static string StrongerRemediation(string current, string? next)
    {
        var currentScore = RemediationScore(current);
        var nextScore = RemediationScore(next);
        return nextScore > currentScore ? Normalize(next) : Normalize(current);
    }

    private static int RemediationScore(string? value) => Normalize(value) switch
    {
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0
    };

    private static DateTimeOffset? ToOffset(DateTime? value)
    {
        if (!value.HasValue) return null;
        var utc = value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value.ToUniversalTime();
        return new DateTimeOffset(utc);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private sealed class ConceptEvidence
    {
        public ConceptEvidence(Guid? topicId, string conceptKey, string label)
        {
            TopicId = topicId;
            ConceptKey = conceptKey;
            Label = label;
        }

        public Guid? TopicId { get; }
        public string ConceptKey { get; }
        public string Label { get; set; }
        public decimal? MasteryProbability { get; set; }
        public decimal? Confidence { get; set; }
        public int EvidenceCount { get; set; }
        public int CorrectCount { get; set; }
        public int WrongCount { get; set; }
        public int BlankOrSkippedCount { get; set; }
        public int RepairCount { get; set; }
        public int ReviewLapseCount { get; set; }
        public int ReviewSuccessStreak { get; set; }
        public bool RepairPending { get; set; }
        public string RemediationNeed { get; set; } = "none";
        public string PracticeReadiness { get; set; } = "guided";
        public DateTime? DueAt { get; set; }
        public DateTime? LastPracticedAt { get; set; }
        public DateTime? LastSuccessAt { get; set; }
        public DateTime? LastFailureAt { get; set; }
        public HashSet<string> EvidenceBasis { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

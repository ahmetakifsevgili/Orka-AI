using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Core.Services;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class OrkaLearningStateService : IOrkaLearningStateService
{
    private readonly OrkaDbContext _db;
    private readonly ITopicScopeResolver _topicScopeResolver;
    private readonly ILongTermAdaptiveLearningService _longTermAdaptiveLearning;
    private readonly IExamLearningProfileService _examLearningProfile;
    private readonly ISourceWikiIntelligenceService _sourceWikiIntelligence;

    public OrkaLearningStateService(
        OrkaDbContext db,
        ITopicScopeResolver topicScopeResolver,
        ILongTermAdaptiveLearningService longTermAdaptiveLearning,
        IExamLearningProfileService examLearningProfile,
        ISourceWikiIntelligenceService sourceWikiIntelligence)
    {
        _db = db;
        _topicScopeResolver = topicScopeResolver;
        _longTermAdaptiveLearning = longTermAdaptiveLearning;
        _examLearningProfile = examLearningProfile;
        _sourceWikiIntelligence = sourceWikiIntelligence;
    }

    public async Task<OrkaLearningStateDto?> BuildStateAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sessionId = null,
        string? examCode = "KPSS",
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var resolved = await ResolveScopeAsync(userId, topicId, sessionId, ct);
        if (resolved == null)
        {
            return null;
        }

        var scopeIds = resolved.TopicId.HasValue
            ? await ResolveTopicScopeIdsAsync(userId, resolved.TopicId.Value, ct)
            : Array.Empty<Guid>();

        var sourceHealth = await BuildSourceHealthAsync(userId, scopeIds, ct);
        var signalSummary = await BuildSignalSummaryAsync(userId, resolved.TopicId, resolved.SessionId, scopeIds, ct);
        var longTermProfile = await _longTermAdaptiveLearning.BuildProfileAsync(userId, scopeIds, sourceHealth, ct);
        var examProfile = await _examLearningProfile.BuildProfileAsync(
            userId,
            string.IsNullOrWhiteSpace(examCode) ? "KPSS" : examCode.Trim(),
            variantCode,
            examTopicId: null,
            examOutcomeId: null,
            ct);
        var sourceWikiProfile = await _sourceWikiIntelligence.BuildProfileAsync(
            userId,
            resolved.TopicId,
            sourceId: null,
            wikiPageId: null,
            ct);

        var candidates = BuildCandidates(resolved.TopicId, signalSummary, longTermProfile, examProfile, sourceWikiProfile);
        var conflicts = BuildConflicts(candidates, signalSummary, longTermProfile, examProfile, sourceWikiProfile, resolved.TopicId);
        var ordered = candidates
            .GroupBy(c => $"{c.ActionType}:{c.ConceptKey}", StringComparer.OrdinalIgnoreCase)
            .Select(g => MergeCandidate(g.First(), g.SelectMany(c => c.AppliesTo)))
            .OrderByDescending(c => PriorityScore(c.Priority))
            .ThenBy(c => ActionRank(c.ActionType))
            .Take(8)
            .ToArray();

        var primary = ordered.FirstOrDefault() ?? DefaultAction(resolved.TopicId, signalSummary);
        var secondary = ordered
            .Skip(1)
            .Where(a => !string.Equals(a.ActionType, primary.ActionType, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(a.ConceptKey, primary.ConceptKey, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToArray();

        var reasonCodes = longTermProfile.ReasonCodes
            .Concat(examProfile?.ReasonCodes ?? [])
            .Concat(sourceWikiProfile?.ReasonCodes ?? [])
            .Concat(primary.ReasonCodes)
            .Concat(conflicts.SelectMany(c => c.ReasonCodes))
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();

        return new OrkaLearningStateDto
        {
            TopicId = resolved.TopicId,
            SessionId = resolved.SessionId,
            ScopeStatus = resolved.SessionId.HasValue ? "session" : resolved.TopicId.HasValue ? "topic" : "global",
            SignalSummary = signalSummary,
            SourceHealth = sourceHealth,
            LongTermLearningProfile = longTermProfile,
            ExamLearningProfile = examProfile,
            SourceWikiIntelligenceProfile = sourceWikiProfile,
            PrimaryNextAction = primary,
            SecondaryNextActions = secondary,
            FeatureReadiness = BuildFeatureReadiness(signalSummary, longTermProfile, examProfile, sourceWikiProfile),
            ConflictWarnings = conflicts,
            ReasonCodes = reasonCodes,
            SafetyWarnings = BuildSafetyWarnings(sourceWikiProfile, examProfile, conflicts),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<ResolvedScope?> ResolveScopeAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
        if (sessionId.HasValue)
        {
            var session = await _db.Sessions.AsNoTracking()
                .Where(s => s.Id == sessionId.Value && s.UserId == userId)
                .Select(s => new { s.TopicId })
                .FirstOrDefaultAsync(ct);
            if (session == null)
            {
                return null;
            }

            if (!topicId.HasValue)
            {
                topicId = session.TopicId;
            }
            else if (session.TopicId != topicId.Value)
            {
                return null;
            }
        }

        if (topicId.HasValue)
        {
            var ownsTopic = await _db.Topics.AsNoTracking()
                .AnyAsync(t => t.Id == topicId.Value && t.UserId == userId && !t.IsArchived, ct);
            if (!ownsTopic)
            {
                return null;
            }
        }
        else
        {
            topicId = await _db.Topics.AsNoTracking()
                .Where(t => t.UserId == userId && !t.IsArchived && t.ParentTopicId == null)
                .OrderByDescending(t => t.LastAccessedAt)
                .ThenByDescending(t => t.CreatedAt)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(ct);
        }

        return new ResolvedScope(topicId, sessionId);
    }

    private async Task<Guid[]> ResolveTopicScopeIdsAsync(Guid userId, Guid topicId, CancellationToken ct)
    {
        var scope = await _topicScopeResolver.ResolveAsync(userId, topicId, ct);
        return scope.IsValid && scope.TreeTopicIds.Count > 0
            ? scope.TreeTopicIds.ToArray()
            : [topicId];
    }

    private async Task<DashboardSourceHealthDto> BuildSourceHealthAsync(Guid userId, IReadOnlyCollection<Guid> scopeIds, CancellationToken ct)
    {
        var scopedSourceCount = await _db.LearningSources.AsNoTracking()
            .CountAsync(s => s.UserId == userId && !s.IsDeleted && (scopeIds.Count == 0 || (s.TopicId.HasValue && scopeIds.Contains(s.TopicId.Value))), ct);
        var readySourceCount = await _db.LearningSources.AsNoTracking()
            .CountAsync(s => s.UserId == userId && !s.IsDeleted && s.Status == "ready" && (scopeIds.Count == 0 || (s.TopicId.HasValue && scopeIds.Contains(s.TopicId.Value))), ct);
        var report = await _db.SourceQualityReports.AsNoTracking()
            .Where(r => r.UserId == userId && (scopeIds.Count == 0 || (r.TopicId.HasValue && scopeIds.Contains(r.TopicId.Value))))
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (report is not null)
        {
            return new DashboardSourceHealthDto
            {
                Status = report.QualityStatus,
                UserSafeLabel = report.QualityStatus switch
                {
                    "healthy" => "Kaynaklar destekli",
                    "degraded" => "Kaynaklar sinirli",
                    "source_retrieval_empty" => "Kaynakta cevap yok",
                    "citation_missing" => "Citation eksik",
                    "citation_unsupported" => "Citation desteklenmiyor",
                    _ => "Kaynak durumu izleniyor"
                },
                UserSafeDetail = report.QualityStatus switch
                {
                    "healthy" => "Cevaplar kaynak kanitiyla eslesiyor.",
                    "degraded" => "Kaynak kaniti sinirli; dikkatli ilerle.",
                    "source_retrieval_empty" => "Soru icin kaynaklarda net parca bulunamadi.",
                    "citation_missing" => "Bazi kaynakli iddialarda citation yok.",
                    "citation_unsupported" => "Bazi citation etiketleri kaynak kanitiyla eslesmiyor.",
                    _ => "Kaynak kalitesi izleniyor."
                },
                CitationCoverage = report.CitationCoverage,
                UnsupportedCitationCount = report.UnsupportedCitationCount,
                EvidenceQuality = EvidenceQualityEvaluator.Build(
                    scopedSourceCount,
                    readySourceCount,
                    report.RetrievalRunCount,
                    report.CitationCoverage,
                    report.UnsupportedCitationCount,
                    report.CitationMissingCount,
                    report.RetrievalHealthStatus,
                    report.CitationCoverageStatus)
            };
        }

        return new DashboardSourceHealthDto
        {
            Status = scopedSourceCount == 0 ? "unknown" : "unverified",
            UserSafeLabel = scopedSourceCount == 0 ? "Kaynak yok" : "Kaynaklar hazir",
            UserSafeDetail = scopedSourceCount == 0
                ? "Kaynak eklenirse Tutor, Wiki ve sinav calismasi daha guvenli olur."
                : "Kaynaklar var; citation kalitesi ilk kullanimda olculecek.",
            EvidenceQuality = EvidenceQualityEvaluator.Build(
                scopedSourceCount,
                readySourceCount,
                retrievedEvidenceCount: 0,
                citationCoverage: 0m,
                unsupportedCitationCount: 0,
                citationMissingCount: 0,
                retrievalHealthStatus: scopedSourceCount == 0 ? "no_source" : "unverified",
                citationCoverageStatus: scopedSourceCount == 0 ? "unknown" : "unverified")
        };
    }

    private async Task<OrkaLearningSignalSummaryDto> BuildSignalSummaryAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        IReadOnlyCollection<Guid> scopeIds,
        CancellationToken ct)
    {
        var quizQuery = _db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == userId &&
                        (!sessionId.HasValue || a.SessionId == sessionId.Value) &&
                        (scopeIds.Count == 0 || (a.TopicId.HasValue && scopeIds.Contains(a.TopicId.Value))));
        var signalQuery = _db.LearningSignals.AsNoTracking()
            .Where(s => s.UserId == userId &&
                        (!sessionId.HasValue || s.SessionId == sessionId.Value) &&
                        (scopeIds.Count == 0 || (s.TopicId.HasValue && scopeIds.Contains(s.TopicId.Value))));

        var quizAttempts = await quizQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Correct = g.Count(a => a.IsCorrect && !a.WasSkipped),
                Wrong = g.Count(a => !a.IsCorrect && !a.WasSkipped),
                Blank = g.Count(a => a.WasSkipped || a.UserAnswer == string.Empty)
            })
            .FirstOrDefaultAsync(ct);

        var sourceCount = await _db.LearningSources.AsNoTracking()
            .CountAsync(s => s.UserId == userId && !s.IsDeleted && (scopeIds.Count == 0 || (s.TopicId.HasValue && scopeIds.Contains(s.TopicId.Value))), ct);
        var readySourceCount = await _db.LearningSources.AsNoTracking()
            .CountAsync(s => s.UserId == userId && !s.IsDeleted && s.Status == "ready" && (scopeIds.Count == 0 || (s.TopicId.HasValue && scopeIds.Contains(s.TopicId.Value))), ct);
        var wikiPageCount = await _db.WikiPages.AsNoTracking()
            .CountAsync(p => p.UserId == userId && !p.IsDeleted && (scopeIds.Count == 0 || scopeIds.Contains(p.TopicId)), ct);
        var dueReviewCount = await _db.ReviewItems.AsNoTracking()
            .CountAsync(r => r.UserId == userId && r.Status == "active" && r.DueAt <= DateTime.UtcNow && (!topicId.HasValue || r.TopicId == topicId.Value || (r.TopicId.HasValue && scopeIds.Contains(r.TopicId.Value))), ct);
        var learningSignalCount = await signalQuery.CountAsync(ct);
        var studyRoomSessionCount = await _db.ClassroomSessions.AsNoTracking()
            .CountAsync(c => c.UserId == userId &&
                             (!topicId.HasValue || c.TopicId == topicId.Value) &&
                             (!sessionId.HasValue || c.SessionId == sessionId.Value), ct);
        var studyRoomQuestionCount = await _db.ClassroomInteractions.AsNoTracking()
            .Include(i => i.ClassroomSession)
            .CountAsync(i => i.ClassroomSession.UserId == userId &&
                             (!topicId.HasValue || i.ClassroomSession.TopicId == topicId.Value) &&
                             (!sessionId.HasValue || i.ClassroomSession.SessionId == sessionId.Value), ct);

        var totalEvidence =
            (quizAttempts?.Total ?? 0) +
            learningSignalCount +
            dueReviewCount +
            sourceCount +
            wikiPageCount +
            studyRoomSessionCount +
            studyRoomQuestionCount;

        return new OrkaLearningSignalSummaryDto
        {
            EvidenceCount = totalEvidence,
            QuizAttemptCount = quizAttempts?.Total ?? 0,
            CorrectAttemptCount = quizAttempts?.Correct ?? 0,
            WrongAttemptCount = quizAttempts?.Wrong ?? 0,
            BlankOrSkippedAttemptCount = quizAttempts?.Blank ?? 0,
            DueReviewCount = dueReviewCount,
            LearningSignalCount = learningSignalCount,
            SourceCount = sourceCount,
            ReadySourceCount = readySourceCount,
            WikiPageCount = wikiPageCount,
            StudyRoomSessionCount = studyRoomSessionCount,
            StudyRoomQuestionCount = studyRoomQuestionCount,
            HasRealLearningData = totalEvidence > 0
        };
    }

    private static IReadOnlyList<OrkaUnifiedNextActionDto> BuildCandidates(
        Guid? topicId,
        OrkaLearningSignalSummaryDto signals,
        LongTermLearningProfileDto longTerm,
        ExamLearningProfileDto? exam,
        SourceWikiIntelligenceProfileDto? sourceWiki)
    {
        var candidates = new List<OrkaUnifiedNextActionDto>();

        if (!signals.HasRealLearningData)
        {
            candidates.Add(new OrkaUnifiedNextActionDto
            {
                ActionType = "start_diagnostic",
                Label = "Kisa tani ile basla",
                Reason = "Henuz yeterli ogrenme kaniti yok; once guvenli kisa seviye tespiti gerekir.",
                Priority = "high",
                TopicId = topicId,
                Source = "thin_evidence",
                ReasonCodes = ["thin_evidence", "plan_needs_diagnostic"],
                AppliesTo = ["dashboard", "tutor", "learning"]
            });
        }

        if (signals.DueReviewCount > 0)
        {
            candidates.Add(new OrkaUnifiedNextActionDto
            {
                ActionType = "review_due_concept",
                Label = "Zamani gelen tekrari bitir",
                Reason = "SRS/review kuyrugunda bekleyen tekrar var.",
                Priority = "high",
                TopicId = topicId,
                Source = "review",
                ReasonCodes = ["due_review"],
                AppliesTo = ["dashboard", "tutor", "review"]
            });
        }

        candidates.AddRange(longTerm.NextActions.Select(a => FromLongTerm(topicId, a)));
        if (exam != null)
        {
            candidates.AddRange(exam.NextActions.Select(a => FromExam(topicId, a)));
        }

        if (sourceWiki != null)
        {
            candidates.AddRange(sourceWiki.NextActions.Select(a => FromSourceWiki(topicId, a)));
            if (sourceWiki.SourceCount > 0 && !sourceWiki.CanClaimSourceGrounded)
            {
                candidates.Add(new OrkaUnifiedNextActionDto
                {
                    ActionType = "source_review",
                    Label = "Kaynak kanitini toparla",
                    Reason = "Kaynak/Wiki kaniti source-grounded iddia icin yeterli degil.",
                    Priority = sourceWiki.Warnings.Contains("source_grounded_claim_blocked", StringComparer.OrdinalIgnoreCase) ? "high" : "medium",
                    TopicId = topicId ?? sourceWiki.TopicId,
                    Source = "source_wiki",
                    ReasonCodes = ["source_evidence_limited", "source_grounding_blocked"],
                    AppliesTo = ["sources", "wiki", "tutor", "dashboard"]
                });
            }
        }

        var hasRepairOrReview = candidates.Any(c =>
            c.ActionType is "repair_concept" or "repair_prerequisite" or "review_due_concept" or "practice_exam_outcome" or "review_deneme_mistakes" &&
            PriorityScore(c.Priority) >= 3);
        if (topicId.HasValue && hasRepairOrReview)
        {
            candidates.Add(new OrkaUnifiedNextActionDto
            {
                ActionType = "open_study_room",
                Label = "Study Room'da sesli calis",
                Reason = "Bu konu icin kisisel AI study room repair/review dersine donusebilir.",
                Priority = "medium",
                TopicId = topicId,
                Source = "study_room",
                ReasonCodes = ["study_room_available"],
                AppliesTo = ["study_room", "dashboard", "tutor"]
            });
        }

        if (candidates.Count == 0 || candidates.All(c => c.ActionType == "continue_plan"))
        {
            candidates.Add(DefaultAction(topicId, signals));
        }

        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.ActionType))
            .ToArray();
    }

    private static OrkaUnifiedNextActionDto FromLongTerm(Guid? topicId, AdaptiveNextStudyActionDto action)
    {
        var reasonCodes = action.ReasonCodes ?? [];
        var actionType = action.ActionType switch
        {
            "repair" when reasonCodes.Contains("repeated_blank", StringComparer.OrdinalIgnoreCase) ||
                          reasonCodes.Contains("prerequisite_gap", StringComparer.OrdinalIgnoreCase) => "repair_prerequisite",
            "repair" => "repair_concept",
            "review" => "review_due_concept",
            "checkpoint" => "take_checkpoint_quiz",
            "source_review" => "source_review",
            "take_quiz" => "take_checkpoint_quiz",
            "create_flashcards" => "create_flashcards",
            _ => "continue_plan"
        };

        return new OrkaUnifiedNextActionDto
        {
            ActionType = actionType,
            Label = action.Label,
            Reason = action.Reason,
            Priority = NormalizePriority(action.Priority),
            TopicId = action.TopicId ?? topicId,
            ConceptKey = action.ConceptKey,
            Source = "long_term_learning",
            ReasonCodes = reasonCodes,
            AppliesTo = ["long_term", "dashboard", "tutor", "review"]
        };
    }

    private static OrkaUnifiedNextActionDto FromExam(Guid? topicId, ExamNextActionDto action)
    {
        var actionType = action.ActionType switch
        {
            "repair_outcome" => "practice_exam_outcome",
            "review_deneme_mistakes" => "review_deneme_mistakes",
            "review_due_outcome" => "review_due_concept",
            "run_diagnostic" => "start_diagnostic",
            "source_review" => "source_review",
            "practice_question_type" => "practice_exam_outcome",
            "create_flashcards" => "create_flashcards",
            _ => "continue_plan"
        };

        var concept = action.OutcomeCode ?? action.TopicCode ?? action.QuestionType;
        var reasonCodes = action.ReasonCodes
            .Concat(actionType == "practice_exam_outcome" ? new[] { "exam_weak_outcome" } : Array.Empty<string>())
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OrkaUnifiedNextActionDto
        {
            ActionType = actionType,
            Label = action.Label,
            Reason = action.Reason,
            Priority = NormalizePriority(action.Priority),
            TopicId = topicId,
            ConceptKey = concept,
            Source = "exam_learning",
            ReasonCodes = reasonCodes,
            AppliesTo = ["exam", "dashboard", "tutor"]
        };
    }

    private static OrkaUnifiedNextActionDto FromSourceWiki(Guid? topicId, SourceWikiNextActionDto action)
    {
        var actionType = action.ActionType switch
        {
            "repair_concept" => "repair_concept",
            "review_source" => "source_review",
            "citation_review" => "citation_review",
            "review_source_questions" => "source_review",
            "compare_sources" => "citation_review",
            "sync_source_concepts" => "update_wiki_note",
            "open_notebook_pack" => "update_wiki_note",
            "add_source" => "source_review",
            _ => "continue_plan"
        };

        return new OrkaUnifiedNextActionDto
        {
            ActionType = actionType,
            Label = action.Label,
            Reason = action.ReasonCodes.Contains("source_grounded_claim_blocked", StringComparer.OrdinalIgnoreCase)
                ? "Kaynak kaniti yeterli olmadigi icin once kaynak/citation kontrolu gerekir."
                : "Kaynak/Wiki durumu sonraki calisma adimina baglandi.",
            Priority = NormalizePriority(action.Priority),
            TopicId = topicId,
            ConceptKey = action.ConceptKey,
            Source = "source_wiki",
            ReasonCodes = action.ReasonCodes,
            AppliesTo = ["sources", "wiki", "dashboard", "tutor"]
        };
    }

    private static IReadOnlyList<OrkaLearningStateConflictDto> BuildConflicts(
        IReadOnlyList<OrkaUnifiedNextActionDto> candidates,
        OrkaLearningSignalSummaryDto signals,
        LongTermLearningProfileDto longTerm,
        ExamLearningProfileDto? exam,
        SourceWikiIntelligenceProfileDto? sourceWiki,
        Guid? topicId)
    {
        var conflicts = new List<OrkaLearningStateConflictDto>();
        var hasHighRepair = candidates.Any(c =>
            c.ActionType is "repair_concept" or "repair_prerequisite" or "practice_exam_outcome" &&
            PriorityScore(c.Priority) >= 4);
        var hasContinuePlan = candidates.Any(c => c.ActionType == "continue_plan");

        if (hasHighRepair && hasContinuePlan)
        {
            conflicts.Add(Conflict(
                "next_action_conflict",
                "warning",
                "Repair/review baskisi varken bazi moduller plana devam sinyali de uretiyor; Orka repair onceligini secer.",
                ["weak_concept", "next_action_conflict"]));
        }

        if (sourceWiki is { CanClaimSourceGrounded: false, SourceCount: > 0 } &&
            (sourceWiki.Warnings.Contains("source_grounded_claim_blocked", StringComparer.OrdinalIgnoreCase) ||
             sourceWiki.SourceReadiness is "stale" or "deleted" or "degraded" or "evidence_insufficient"))
        {
            conflicts.Add(Conflict(
                "source_grounding_blocked",
                "warning",
                "Kaynak kaniti sinirli; Tutor ve Dashboard source-grounded iddiayi sinirlamali.",
                ["source_evidence_limited", "source_grounding_blocked"]));
        }

        if (exam?.StableOutcomes.Count > 0 == true &&
            longTerm.Concepts.Any(c => c.WrongCount >= 2 && c.State is "weak" or "likely_forgotten"))
        {
            conflicts.Add(Conflict(
                "exam_learning_conflict",
                "warning",
                "Sinav profili stabil sinyal tasirken uzun vadeli profil yakin zamanda zayiflik gosteriyor; repair baskisi korunur.",
                ["exam_learning_conflict", "repeated_wrong"]));
        }

        if (!topicId.HasValue && signals.StudyRoomSessionCount == 0 &&
            candidates.Any(c => c.ActionType == "open_study_room"))
        {
            conflicts.Add(Conflict(
                "missing_topic_context",
                "warning",
                "Study Room icin guvenli konu/session baglami yok.",
                ["missing_topic_context"]));
        }

        if (signals.EvidenceCount <= 1)
        {
            conflicts.Add(Conflict(
                "thin_evidence",
                "info",
                "Ogrenme kaniti henuz ince; Orka kesin ustalik ya da basari iddiasi kurmaz.",
                ["thin_evidence"]));
        }

        return conflicts
            .GroupBy(c => c.ConflictCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    private static IReadOnlyList<OrkaFeatureReadinessDto> BuildFeatureReadiness(
        OrkaLearningSignalSummaryDto signals,
        LongTermLearningProfileDto longTerm,
        ExamLearningProfileDto? exam,
        SourceWikiIntelligenceProfileDto? sourceWiki) =>
        [
            Feature("long_term_learning", longTerm.HasEnoughEvidence ? "ready" : "limited", longTerm.Summary, longTerm.ReasonCodes),
            Feature("exam_learning", exam?.HasEnoughEvidence == true ? "ready" : exam is { EvidenceCount: > 0 } ? "limited" : "not_available", exam?.NextActions.FirstOrDefault()?.Reason ?? "Sinav kaniti henuz sinirli.", exam?.ReasonCodes ?? []),
            Feature("source_wiki", sourceWiki?.ProfileStatus == "ready" ? "ready" : sourceWiki?.SourceCount > 0 || sourceWiki?.WikiPageCount > 0 ? "limited" : "not_available", sourceWiki?.NextActions.FirstOrDefault()?.Label ?? "Kaynak/Wiki kaniti henuz sinirli.", sourceWiki?.ReasonCodes ?? []),
            Feature("review", signals.DueReviewCount > 0 ? "ready" : "not_available", signals.DueReviewCount > 0 ? "Zamani gelen tekrar var." : "Zamani gelen tekrar yok.", signals.DueReviewCount > 0 ? ["due_review"] : []),
            Feature("study_room", signals.StudyRoomSessionCount > 0 ? "ready" : "available", "Study Room kisisel AI sesli calisma odasi olarak baglama gore onerilir.", ["study_room_available"]),
            Feature("quiz_mastery", signals.QuizAttemptCount > 0 ? "ready" : "limited", signals.QuizAttemptCount > 0 ? "Quiz/mastery kaniti var." : "Quiz kaniti henuz sinirli.", signals.QuizAttemptCount > 0 ? ["quiz_evidence"] : ["thin_evidence"]),
            Feature("dashboard", "ready", "Dashboard unified next action'i tuketebilir.", ["dashboard_consumes_unified_state"]),
            Feature("tutor", "ready", "Tutor unified next action'i guvenli metadata olarak tuketebilir.", ["tutor_consumes_unified_state"])
        ];

    private static IReadOnlyList<string> BuildSafetyWarnings(
        SourceWikiIntelligenceProfileDto? sourceWiki,
        ExamLearningProfileDto? exam,
        IReadOnlyList<OrkaLearningStateConflictDto> conflicts)
    {
        var warnings = new List<string>();
        if (sourceWiki is { CanClaimSourceGrounded: false, SourceCount: > 0 })
        {
            warnings.Add("source_grounded_claim_blocked");
        }

        if (exam is { CanClaimOfficial: false })
        {
            warnings.Add("official_claim_blocked_without_verified_metadata");
        }

        warnings.AddRange(conflicts.Select(c => c.ConflictCode));
        return warnings
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static OrkaUnifiedNextActionDto DefaultAction(Guid? topicId, OrkaLearningSignalSummaryDto signals) =>
        signals.HasRealLearningData
            ? new OrkaUnifiedNextActionDto
            {
                ActionType = "continue_plan",
                Label = "Plana devam et",
                Reason = "Mevcut sinyaller tehlikeli bir celiski gostermiyor.",
                Priority = "normal",
                TopicId = topicId,
                Source = "orka_state",
                ReasonCodes = ["stable_recent_success"],
                AppliesTo = ["dashboard", "tutor", "learning"]
            }
            : new OrkaUnifiedNextActionDto
            {
                ActionType = "start_diagnostic",
                Label = "Kisa tani ile basla",
                Reason = "Henuz yeterli kanit yok.",
                Priority = "high",
                TopicId = topicId,
                Source = "thin_evidence",
                ReasonCodes = ["thin_evidence", "plan_needs_diagnostic"],
                AppliesTo = ["dashboard", "tutor", "learning"]
            };

    private static OrkaUnifiedNextActionDto MergeCandidate(OrkaUnifiedNextActionDto candidate, IEnumerable<string> appliesTo)
    {
        candidate.AppliesTo = candidate.AppliesTo
            .Concat(appliesTo)
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        return candidate;
    }

    private static OrkaFeatureReadinessDto Feature(string key, string status, string summary, IReadOnlyList<string> reasons) => new()
    {
        FeatureKey = key,
        Status = status,
        UserSafeSummary = summary,
        ReasonCodes = reasons.Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray()
    };

    private static OrkaLearningStateConflictDto Conflict(string code, string severity, string summary, IReadOnlyList<string> reasons) => new()
    {
        ConflictCode = code,
        Severity = severity,
        UserSafeSummary = summary,
        ReasonCodes = reasons
    };

    private static string NormalizePriority(string? priority) => (priority ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "urgent" => "urgent",
        "high" => "high",
        "medium" => "medium",
        "normal" => "normal",
        "low" => "low",
        _ => "normal"
    };

    private static int PriorityScore(string? priority) => NormalizePriority(priority) switch
    {
        "urgent" => 5,
        "high" => 4,
        "medium" => 3,
        "normal" => 3,
        "low" => 2,
        _ => 1
    };

    private static int ActionRank(string? actionType) => actionType switch
    {
        "repair_prerequisite" => 0,
        "repair_concept" => 1,
        "practice_exam_outcome" => 2,
        "review_deneme_mistakes" => 3,
        "review_due_concept" => 4,
        "source_review" => 5,
        "citation_review" => 6,
        "take_checkpoint_quiz" => 7,
        "open_study_room" => 8,
        "start_diagnostic" => 9,
        _ => 20
    };

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    private sealed record ResolvedScope(Guid? TopicId, Guid? SessionId);
}

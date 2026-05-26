using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class ExamLearningProfileService : IExamLearningProfileService
{
    private const int LowCoverageThreshold = 3;
    private static readonly TimeSpan ReviewWindow = TimeSpan.FromDays(14);

    private readonly OrkaDbContext _db;
    private readonly IExamFrameworkService _examFramework;

    public ExamLearningProfileService(OrkaDbContext db, IExamFrameworkService examFramework)
    {
        _db = db;
        _examFramework = examFramework;
    }

    public async Task<ExamLearningProfileDto?> BuildProfileAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        Guid? examTopicId = null,
        Guid? examOutcomeId = null,
        CancellationToken ct = default)
    {
        var normalizedExamCode = NormalizeCode(examCode);
        var normalizedVariantCode = NormalizeCodeOrNull(variantCode);
        if (string.IsNullOrWhiteSpace(normalizedExamCode))
        {
            return null;
        }

        var tree = await _examFramework.CreateSystemSkeletonAsync(normalizedExamCode, ct);
        if (tree is null)
        {
            return null;
        }

        var paths = Flatten(tree, normalizedVariantCode)
            .Where(p => !examTopicId.HasValue || p.Topic.Id == examTopicId.Value)
            .Where(p => !examOutcomeId.HasValue || p.Outcome.Id == examOutcomeId.Value)
            .ToArray();
        if (paths.Length == 0)
        {
            return null;
        }

        var outcomeIds = paths.Select(p => p.Outcome.Id).Distinct().ToArray();
        var visibleQuestionQuery = _db.QuestionItems
            .AsNoTracking()
            .Where(q => !q.IsDeleted
                        && q.ExamDefinitionId == tree.Id
                        && q.QualityStatus == "published"
                        && (q.OwnerUserId == null || q.OwnerUserId == userId));
        if (!string.IsNullOrWhiteSpace(normalizedVariantCode))
        {
            var variantId = paths[0].Variant.Id;
            visibleQuestionQuery = visibleQuestionQuery
                .Where(q => q.ExamVariantId == null || q.ExamVariantId == variantId);
        }

        var visibleQuestions = await visibleQuestionQuery
            .Select(q => new QuestionFact(
                q.Id,
                q.ExamTopicId,
                q.ExamOutcomeId,
                string.IsNullOrWhiteSpace(q.QuestionType) ? "multiple_choice" : q.QuestionType))
            .ToListAsync(ct);

        var questionIds = visibleQuestions.Select(q => q.Id).ToArray();
        IReadOnlyList<QuestionOutcomeFact> linkedOutcomes = questionIds.Length == 0
            ? Array.Empty<QuestionOutcomeFact>()
            : await _db.QuestionOutcomeLinks
                .AsNoTracking()
                .Where(l => !l.IsDeleted && questionIds.Contains(l.QuestionItemId) && outcomeIds.Contains(l.ExamOutcomeId))
                .Select(l => new QuestionOutcomeFact(l.QuestionItemId, l.ExamOutcomeId))
                .ToListAsync(ct);

        var practiceAnswers = await _db.CentralExamPracticeAnswers
            .AsNoTracking()
            .Where(a => a.PracticeAttempt.UserId == userId
                        && a.PracticeAttempt.ExamDefinitionId == tree.Id
                        && a.PracticeAttempt.Status == "submitted"
                        && !a.PracticeAttempt.IsDeleted)
            .Select(a => new AnswerFact(
                a.ExamOutcomeId,
                a.ExamTopicId,
                a.OutcomeCode,
                a.TopicCode,
                string.IsNullOrWhiteSpace(a.QuestionType) ? "multiple_choice" : a.QuestionType,
                a.IsCorrect,
                a.IsBlank,
                false,
                a.SubmittedAt ?? a.PracticeAttempt.SubmittedAt ?? a.PracticeAttempt.UpdatedAt))
            .ToListAsync(ct);

        var denemeAnswers = await _db.CentralExamDenemeAnswers
            .AsNoTracking()
            .Where(a => a.DenemeAttempt.UserId == userId
                        && a.DenemeAttempt.ExamDefinitionId == tree.Id
                        && a.DenemeAttempt.Status == "submitted"
                        && !a.DenemeAttempt.IsDeleted)
            .Select(a => new AnswerFact(
                a.ExamOutcomeId,
                a.ExamTopicId,
                a.OutcomeCode,
                a.TopicCode,
                string.IsNullOrWhiteSpace(a.QuestionType) ? "multiple_choice" : a.QuestionType,
                a.IsCorrect,
                a.IsBlank,
                true,
                a.SubmittedAt ?? a.DenemeAttempt.SubmittedAt ?? a.DenemeAttempt.UpdatedAt))
            .ToListAsync(ct);

        var answers = practiceAnswers.Concat(denemeAnswers)
            .Where(a => (!examTopicId.HasValue || a.ExamTopicId == examTopicId.Value)
                        && (!examOutcomeId.HasValue || a.ExamOutcomeId == examOutcomeId.Value))
            .ToArray();

        var mappings = await _db.CurriculumOutcomeMappings
            .AsNoTracking()
            .Where(m => !m.IsDeleted && outcomeIds.Contains(m.ExamOutcomeId))
            .Select(m => new MappingFact(
                m.ExamOutcomeId,
                m.VerificationStatus,
                m.OfficialClaimAllowed,
                m.SourceRegistryItem != null ? m.SourceRegistryItem.VerificationStatus : null,
                m.SourceRegistryItem != null && m.SourceRegistryItem.OfficialClaimAllowed))
            .ToListAsync(ct);

        var questionById = visibleQuestions.ToDictionary(q => q.Id);
        var linkedQuestionIdsByOutcome = linkedOutcomes
            .GroupBy(l => l.ExamOutcomeId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.QuestionItemId).Distinct().ToArray());
        var directQuestionsByOutcome = visibleQuestions
            .Where(q => q.ExamOutcomeId.HasValue)
            .GroupBy(q => q.ExamOutcomeId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(q => q.Id).Distinct().ToArray());
        var mappingsByOutcome = mappings
            .GroupBy(m => m.ExamOutcomeId)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var outcomeDtos = paths
            .Select(path => BuildOutcome(path, questionById, directQuestionsByOutcome, linkedQuestionIdsByOutcome, answers, mappingsByOutcome))
            .OrderByDescending(o => PriorityScore(o.ReviewPriority))
            .ThenBy(o => o.TopicCode)
            .ThenBy(o => o.OutcomeCode)
            .ToArray();

        var practiceReadiness = BuildPracticeReadiness(visibleQuestions, answers);
        var nextActions = BuildNextActions(tree, outcomeDtos, practiceReadiness);
        var warnings = BuildWarnings(tree, outcomeDtos);

        return new ExamLearningProfileDto
        {
            ExamCode = tree.Code,
            VariantCode = normalizedVariantCode,
            DisplayName = tree.Name,
            VerificationStatus = tree.VerificationStatus,
            CanClaimOfficial = tree.CanClaimOfficial,
            UserSafeVerificationLabel = tree.UserSafeVerificationLabel,
            HasEnoughEvidence = answers.Length >= 2 || outcomeDtos.Any(o => o.PublishedQuestionCount >= LowCoverageThreshold),
            EvidenceCount = answers.Length,
            OutcomeCount = outcomeDtos.Length,
            PublishedQuestionCount = visibleQuestions.Count,
            Outcomes = outcomeDtos,
            PracticeReadiness = practiceReadiness,
            NextActions = nextActions,
            WeakOutcomes = outcomeDtos
                .Where(o => o.ReadinessStatus is "weak" or "prerequisite_gap")
                .Select(o => o.OutcomeCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DueOutcomes = outcomeDtos
                .Where(o => o.ReadinessStatus == "due_for_review")
                .Select(o => o.OutcomeCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StableOutcomes = outcomeDtos
                .Where(o => o.ReadinessStatus == "stable")
                .Select(o => o.OutcomeCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Warnings = warnings,
            ReasonCodes = outcomeDtos
                .SelectMany(o => o.ReasonCodes)
                .Concat(practiceReadiness.SelectMany(p => p.ReasonCodes))
                .Concat(warnings)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray(),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static ExamOutcomeReadinessDto BuildOutcome(
        OutcomePath path,
        IReadOnlyDictionary<Guid, QuestionFact> questionById,
        IReadOnlyDictionary<Guid, Guid[]> directQuestionsByOutcome,
        IReadOnlyDictionary<Guid, Guid[]> linkedQuestionIdsByOutcome,
        IReadOnlyCollection<AnswerFact> answers,
        IReadOnlyDictionary<Guid, MappingFact[]> mappingsByOutcome)
    {
        var direct = directQuestionsByOutcome.TryGetValue(path.Outcome.Id, out var directIds) ? directIds : [];
        var linked = linkedQuestionIdsByOutcome.TryGetValue(path.Outcome.Id, out var linkedIds) ? linkedIds : [];
        var outcomeQuestionIds = direct.Concat(linked).Distinct().ToArray();
        var outcomeQuestions = outcomeQuestionIds
            .Where(questionById.ContainsKey)
            .Select(id => questionById[id])
            .ToArray();
        var outcomeAnswers = answers
            .Where(a => a.ExamOutcomeId == path.Outcome.Id || (!a.ExamOutcomeId.HasValue && a.ExamTopicId == path.Topic.Id))
            .ToArray();

        var correct = outcomeAnswers.Count(a => a.IsCorrect);
        var wrong = outcomeAnswers.Count(a => !a.IsCorrect && !a.IsBlank);
        var blank = outcomeAnswers.Count(a => a.IsBlank);
        var denemeMistakes = outcomeAnswers.Count(a => a.IsDeneme && (!a.IsCorrect || a.IsBlank));
        var answered = outcomeAnswers.Count(a => !a.IsBlank);
        var correctness = answered == 0 ? 0m : Math.Round(correct / (decimal)answered, 4);
        var latest = outcomeAnswers
            .Where(a => a.SubmittedAt.HasValue)
            .Select(a => a.SubmittedAt!.Value)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        var isDue = latest != DateTime.MinValue && DateTime.UtcNow - latest > ReviewWindow && correct > 0;
        var mappings = mappingsByOutcome.TryGetValue(path.Outcome.Id, out var mapped) ? mapped : [];
        var sourceStatus = SourceEvidenceStatus(mappings);
        var coverage = QuestionCoverageStatus(outcomeQuestions.Length);
        var reasonCodes = new List<string>();

        if (outcomeQuestions.Length == 0)
        {
            reasonCodes.Add("question_coverage_limited");
        }
        else if (outcomeQuestions.Length < LowCoverageThreshold)
        {
            reasonCodes.Add("coverage_limited");
        }

        if (mappings.Length == 0)
        {
            reasonCodes.Add("source_unverified");
        }
        else if (sourceStatus != "official_verified")
        {
            reasonCodes.Add("source_evidence_limited");
        }

        var status = "learning";
        var priority = "low";
        var action = "practice_question_type";
        var evidence = outcomeAnswers.Length == 0 ? "thin" : "attempts";

        if (outcomeQuestions.Length == 0)
        {
            status = "coverage_limited";
            priority = "medium";
            action = "source_review";
        }
        else if (outcomeAnswers.Length == 0)
        {
            status = "diagnostic_needed";
            priority = "medium";
            action = "run_diagnostic";
            reasonCodes.Add("diagnostic_needed");
        }
        else if (blank >= 2)
        {
            status = "prerequisite_gap";
            priority = "high";
            action = "run_diagnostic";
            evidence = "repeated_blank";
            reasonCodes.Add("repeated_blank");
            reasonCodes.Add("prerequisite_gap");
        }
        else if (denemeMistakes >= 2)
        {
            status = "weak";
            priority = "urgent";
            action = "review_deneme_mistakes";
            evidence = "deneme_cluster";
            reasonCodes.Add("deneme_mistake_cluster");
        }
        else if (wrong + blank >= 2)
        {
            status = "weak";
            priority = "high";
            action = "repair_outcome";
            evidence = "repeated_wrong";
            reasonCodes.Add("repeated_wrong");
            reasonCodes.Add("weak_outcome");
        }
        else if (isDue)
        {
            status = "due_for_review";
            priority = "high";
            action = "review_due_outcome";
            evidence = "review_window";
            reasonCodes.Add("due_review");
        }
        else if (correct >= 3 && correctness >= 0.75m && wrong + blank <= 1)
        {
            status = "stable";
            priority = "low";
            action = "continue_exam_plan";
            evidence = "repeated_success";
            reasonCodes.Add("stable_recent_success");
        }
        else if (wrong == 1 || blank == 1)
        {
            status = "watch";
            priority = "medium";
            action = "practice_question_type";
            evidence = wrong == 1 ? "single_wrong" : "single_blank";
            reasonCodes.Add(wrong == 1 ? "weak_signal_observed" : "blank_observed");
        }

        return new ExamOutcomeReadinessDto
        {
            ExamOutcomeId = path.Outcome.Id,
            OutcomeCode = path.Outcome.Code,
            Label = path.Outcome.Name,
            TopicCode = path.Topic.Code,
            TopicLabel = path.Topic.Name,
            ReadinessStatus = status,
            ReviewPriority = priority,
            RecommendedAction = action,
            EvidenceBasis = evidence,
            PublishedQuestionCount = outcomeQuestions.Length,
            AttemptCount = outcomeAnswers.Length,
            CorrectCount = correct,
            WrongCount = wrong,
            BlankCount = blank,
            DenemeMistakeCount = denemeMistakes,
            CorrectnessRate = correctness,
            QuestionCoverageStatus = coverage,
            SourceEvidenceStatus = sourceStatus,
            QuestionTypes = outcomeQuestions
                .Select(q => q.QuestionType)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(q => q)
                .ToArray(),
            ReasonCodes = reasonCodes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            UserSafeSummary = BuildOutcomeSummary(status, action, path.Outcome.Name)
        };
    }

    private static IReadOnlyList<ExamPracticeReadinessDto> BuildPracticeReadiness(
        IReadOnlyCollection<QuestionFact> questions,
        IReadOnlyCollection<AnswerFact> answers)
    {
        var questionTypes = questions
            .Select(q => q.QuestionType)
            .Concat(answers.Select(a => a.QuestionType))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToArray();

        return questionTypes.Select(type =>
        {
            var typeQuestions = questions.Count(q => string.Equals(q.QuestionType, type, StringComparison.OrdinalIgnoreCase));
            var typeAnswers = answers.Where(a => string.Equals(a.QuestionType, type, StringComparison.OrdinalIgnoreCase)).ToArray();
            var correct = typeAnswers.Count(a => a.IsCorrect);
            var wrong = typeAnswers.Count(a => !a.IsCorrect && !a.IsBlank);
            var blank = typeAnswers.Count(a => a.IsBlank);
            var reasons = new List<string>();
            var status = "ready";
            var action = "practice_question_type";

            if (typeQuestions == 0)
            {
                status = "coverage_limited";
                action = "source_review";
                reasons.Add("question_coverage_limited");
            }
            else if (typeQuestions < LowCoverageThreshold)
            {
                status = "coverage_limited";
                reasons.Add("coverage_limited");
            }

            if (wrong + blank >= 2)
            {
                status = "weak";
                action = blank >= 2 ? "run_diagnostic" : "practice_question_type";
                reasons.Add(blank >= 2 ? "repeated_blank" : "repeated_wrong");
                reasons.Add("question_type_gap");
            }
            else if (correct >= 3 && wrong + blank <= 1)
            {
                status = "stable";
                action = "continue_exam_plan";
                reasons.Add("stable_recent_success");
            }
            else if (typeAnswers.Length == 0)
            {
                status = status == "coverage_limited" ? status : "diagnostic_needed";
                reasons.Add("diagnostic_needed");
            }

            return new ExamPracticeReadinessDto
            {
                QuestionType = type,
                PublishedQuestionCount = typeQuestions,
                AttemptCount = typeAnswers.Length,
                CorrectCount = correct,
                WrongCount = wrong,
                BlankCount = blank,
                ReadinessStatus = status,
                RecommendedAction = action,
                ReasonCodes = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }).ToArray();
    }

    private static IReadOnlyList<ExamNextActionDto> BuildNextActions(
        ExamDefinitionDto tree,
        IReadOnlyList<ExamOutcomeReadinessDto> outcomes,
        IReadOnlyList<ExamPracticeReadinessDto> practice)
    {
        var actions = new List<ExamNextActionDto>();
        foreach (var outcome in outcomes
            .Where(o => o.RecommendedAction != "continue_exam_plan")
            .OrderByDescending(o => PriorityScore(o.ReviewPriority))
            .ThenByDescending(o => o.AttemptCount)
            .Take(5))
        {
            actions.Add(new ExamNextActionDto
            {
                ActionType = outcome.RecommendedAction,
                Label = ActionLabel(outcome.RecommendedAction, outcome.Label),
                Reason = ActionReason(outcome),
                Priority = outcome.ReviewPriority,
                OutcomeCode = outcome.OutcomeCode,
                TopicCode = outcome.TopicCode,
                QuestionType = outcome.QuestionTypes.FirstOrDefault(),
                ReasonCodes = outcome.ReasonCodes,
                ExamContext = new ExamLearningContextDto
                {
                    ExamDefinitionId = tree.Id,
                    ExamCode = tree.Code,
                    ExamOutcomeId = outcome.ExamOutcomeId,
                    OutcomeCode = outcome.OutcomeCode,
                    TopicCode = outcome.TopicCode
                }
            });
        }

        foreach (var item in practice
            .Where(p => p.ReadinessStatus == "weak")
            .Take(2))
        {
            actions.Add(new ExamNextActionDto
            {
                ActionType = item.RecommendedAction,
                Label = $"{item.QuestionType}: soru tipi calismasi",
                Reason = "Bu soru tipinde zayif cevap sinyali birikti.",
                Priority = "high",
                QuestionType = item.QuestionType,
                ReasonCodes = item.ReasonCodes,
                ExamContext = new ExamLearningContextDto { ExamDefinitionId = tree.Id, ExamCode = tree.Code }
            });
        }

        if (actions.Count == 0)
        {
            actions.Add(new ExamNextActionDto
            {
                ActionType = "continue_exam_plan",
                Label = "Sinav planina devam et",
                Reason = "Belirgin zayif sinyal yok; yeni konu, tekrar ve kisa pratik dengesi korunabilir.",
                Priority = "medium",
                ReasonCodes = ["stable_recent_success"],
                ExamContext = new ExamLearningContextDto { ExamDefinitionId = tree.Id, ExamCode = tree.Code }
            });
        }

        return actions
            .GroupBy(a => $"{a.ActionType}:{a.OutcomeCode}:{a.QuestionType}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(a => PriorityScore(a.Priority))
            .Take(6)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildWarnings(ExamDefinitionDto tree, IReadOnlyList<ExamOutcomeReadinessDto> outcomes)
    {
        var warnings = new List<string>();
        if (!tree.CanClaimOfficial)
        {
            warnings.Add("source_unverified");
        }

        if (outcomes.Any(o => o.QuestionCoverageStatus is "no_content" or "low_content"))
        {
            warnings.Add("question_coverage_limited");
        }

        if (outcomes.Any(o => o.SourceEvidenceStatus is "unverified" or "source_backed"))
        {
            warnings.Add("source_evidence_limited");
        }

        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<OutcomePath> Flatten(ExamDefinitionDto tree, string? variantCode)
    {
        var paths = new List<OutcomePath>();
        foreach (var variant in tree.Variants.Where(v => string.IsNullOrWhiteSpace(variantCode) || string.Equals(v.Code, variantCode, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var section in variant.Sections)
            foreach (var subject in section.Subjects)
            foreach (var topic in FlattenTopic(subject.Topics))
            foreach (var outcome in topic.Outcomes)
            {
                paths.Add(new OutcomePath(variant, section, subject, topic, outcome));
            }
        }

        return paths;
    }

    private static IEnumerable<ExamTopicDto> FlattenTopic(IEnumerable<ExamTopicDto> topics)
    {
        foreach (var topic in topics)
        {
            yield return topic;
            foreach (var child in FlattenTopic(topic.Children))
            {
                yield return child;
            }
        }
    }

    private static string SourceEvidenceStatus(IReadOnlyCollection<MappingFact> mappings)
    {
        if (mappings.Count == 0)
        {
            return "unverified";
        }

        if (mappings.Any(m => IsOfficial(m.VerificationStatus, m.OfficialClaimAllowed) ||
                              IsOfficial(m.SourceVerificationStatus, m.SourceOfficialClaimAllowed)))
        {
            return "official_verified";
        }

        if (mappings.Any(m => string.Equals(m.VerificationStatus, "source_backed", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(m.SourceVerificationStatus, "source_backed", StringComparison.OrdinalIgnoreCase)))
        {
            return "source_backed";
        }

        return "unverified";
    }

    private static bool IsOfficial(string? status, bool allowed) =>
        allowed && string.Equals(status, "official_verified", StringComparison.OrdinalIgnoreCase);

    private static string QuestionCoverageStatus(int count) =>
        count == 0 ? "no_content" : count < LowCoverageThreshold ? "low_content" : "sufficient";

    private static string BuildOutcomeSummary(string status, string action, string label) =>
        status switch
        {
            "stable" => $"{label}: simdilik stabil; plan ilerleyebilir.",
            "weak" => $"{label}: hedefli pratik veya telafi onerilir.",
            "prerequisite_gap" => $"{label}: bos cevaplar on kosul kontrolu gerektiriyor.",
            "due_for_review" => $"{label}: tekrar zamani geldi.",
            "diagnostic_needed" => $"{label}: once kisa tani/pratik gerekir.",
            "coverage_limited" => $"{label}: soru/kaynak kapsami sinirli.",
            _ => $"{label}: {action} onerilir."
        };

    private static string ActionLabel(string action, string label) =>
        action switch
        {
            "repair_outcome" => $"{label}: telafi yap",
            "review_deneme_mistakes" => $"{label}: deneme hatalarini incele",
            "review_due_outcome" => $"{label}: tekrar yap",
            "run_diagnostic" => $"{label}: kisa tani kontrolu",
            "source_review" => $"{label}: kaynak/kapsam kontrolu",
            "practice_question_type" => $"{label}: soru tipi pratigi",
            _ => $"{label}: plana devam"
        };

    private static string ActionReason(ExamOutcomeReadinessDto outcome)
    {
        if (outcome.ReasonCodes.Contains("repeated_blank", StringComparer.OrdinalIgnoreCase))
        {
            return "Tekrarlanan bos cevap var; sistem bunu kesin yanilgi saymadan on kosul/tani kontrolu onerir.";
        }

        if (outcome.ReasonCodes.Contains("deneme_mistake_cluster", StringComparer.OrdinalIgnoreCase))
        {
            return "Deneme cevaplarinda ayni bolgede hata kumelendi.";
        }

        if (outcome.ReasonCodes.Contains("repeated_wrong", StringComparer.OrdinalIgnoreCase))
        {
            return "Birden fazla yanlis cevap bu kazanimi telafiye aday yapiyor.";
        }

        if (outcome.ReasonCodes.Contains("question_coverage_limited", StringComparer.OrdinalIgnoreCase))
        {
            return "Bu kazanimin soru kapsami sinirli; sonuc kesin basari iddiasi degildir.";
        }

        return outcome.UserSafeSummary;
    }

    private static int PriorityScore(string? priority) =>
        priority switch
        {
            "urgent" => 4,
            "high" => 3,
            "medium" or "normal" => 2,
            _ => 1
        };

    private static string NormalizeCode(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string? NormalizeCodeOrNull(string? value)
    {
        var normalized = NormalizeCode(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private sealed record OutcomePath(
        ExamVariantDto Variant,
        ExamSectionDto Section,
        ExamSubjectDto Subject,
        ExamTopicDto Topic,
        ExamOutcomeDto Outcome);

    private sealed record QuestionFact(Guid Id, Guid? ExamTopicId, Guid? ExamOutcomeId, string QuestionType);

    private sealed record QuestionOutcomeFact(Guid QuestionItemId, Guid ExamOutcomeId);

    private sealed record AnswerFact(
        Guid? ExamOutcomeId,
        Guid? ExamTopicId,
        string? OutcomeCode,
        string? TopicCode,
        string QuestionType,
        bool IsCorrect,
        bool IsBlank,
        bool IsDeneme,
        DateTime? SubmittedAt);

    private sealed record MappingFact(
        Guid ExamOutcomeId,
        string VerificationStatus,
        bool OfficialClaimAllowed,
        string? SourceVerificationStatus,
        bool SourceOfficialClaimAllowed);
}

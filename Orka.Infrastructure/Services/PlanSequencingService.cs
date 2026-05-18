using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Korteks;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class PlanSequencingService : IPlanSequencingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly OrkaDbContext _db;
    private readonly IActiveLessonSnapshotService? _snapshots;
    private readonly ISourceEvidenceLifecycleService? _sourceLifecycle;
    private readonly IKorteksSynthesisService? _korteksSynthesis;
    private readonly ILogger<PlanSequencingService> _logger;

    public PlanSequencingService(
        OrkaDbContext db,
        ILogger<PlanSequencingService> logger,
        IActiveLessonSnapshotService? snapshots = null,
        ISourceEvidenceLifecycleService? sourceLifecycle = null,
        IKorteksSynthesisService? korteksSynthesis = null)
    {
        _db = db;
        _logger = logger;
        _snapshots = snapshots;
        _sourceLifecycle = sourceLifecycle;
        _korteksSynthesis = korteksSynthesis;
    }

    public async Task<PlanCurriculumSequenceDto> BuildPlanSequenceAsync(
        Guid userId,
        PlanQualityEvaluationRequestDto request,
        CancellationToken ct = default)
    {
        var topic = await EnsureTopicAsync(userId, request.TopicId, ct);
        var graph = await LoadLatestConceptGraphAsync(userId, request.TopicId, ct);
        var concepts = graph == null
            ? []
            : await _db.LearningConcepts
                .AsNoTracking()
                .Where(c => c.ConceptGraphSnapshotId == graph.Id)
                .OrderBy(c => c.Order)
                .ToListAsync(ct);
        var relations = graph == null
            ? []
            : await _db.ConceptRelations
                .AsNoTracking()
                .Where(r => r.ConceptGraphSnapshotId == graph.Id)
                .ToListAsync(ct);
        var tracing = await _db.KnowledgeTracingStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == request.TopicId)
            .ToListAsync(ct);
        var masteries = await _db.ConceptMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.TopicId == request.TopicId)
            .ToListAsync(ct);
        var activeSnapshot = request.ActiveLessonSnapshotId.HasValue
            ? await GetActiveSnapshotByIdAsync(userId, request.ActiveLessonSnapshotId.Value, ct)
            : _snapshots == null ? null : await _snapshots.GetActiveLessonSnapshotAsync(userId, request.TopicId, request.SessionId, ct);
        var studentSnapshot = request.StudentContextSnapshotId.HasValue
            ? await GetStudentSnapshotByIdAsync(userId, request.StudentContextSnapshotId.Value, ct)
            : _snapshots == null ? null : await _snapshots.GetStudentContextSnapshotAsync(userId, request.TopicId, request.SessionId, ct);
        var sourceBundle = _sourceLifecycle == null
            ? null
            : await _sourceLifecycle.GetLatestSourceEvidenceBundleAsync(userId, request.TopicId, request.SessionId, ct);
        var notebook = _sourceLifecycle == null
            ? null
            : await _sourceLifecycle.GetLatestWikiKnowledgeNotebookAsync(userId, request.TopicId, ct);
        var korteks = _korteksSynthesis == null
            ? null
            : await _korteksSynthesis.GetLatestWorkflowAsync(userId, request.TopicId, request.SessionId, ct);

        var sourceReadiness = sourceBundle?.EvidenceStatus
                              ?? studentSnapshot?.SourceReadiness
                              ?? activeSnapshot?.EvidenceSummary.EvidenceStatus
                              ?? "evidence_insufficient";
        var learnerState = activeSnapshot?.LearnerState
                           ?? (studentSnapshot?.ConfidenceStatus == "usable" ? "needs_scaffold" : "evidence_insufficient");
        var remediationNeed = activeSnapshot?.RemediationNeed ?? ResolveRemediationNeed(tracing, masteries);
        var orderedConcepts = OrderConcepts(concepts, relations, tracing, masteries);
        var steps = orderedConcepts.Count > 0
            ? orderedConcepts
                .Take(12)
                .Select((concept, index) => BuildStep(
                    topic,
                    concept,
                    index,
                    relations,
                    tracing,
                    masteries,
                    sourceBundle,
                    notebook,
                    korteks,
                    sourceReadiness,
                    learnerState,
                    remediationNeed))
                .ToList()
            : BuildFallbackSteps(topic, korteks, sourceBundle, sourceReadiness, learnerState, remediationNeed);

        if (NoLearnerEvidence(tracing, masteries, studentSnapshot, activeSnapshot))
        {
            steps.Insert(0, BuildDiagnosticFirstStep(topic, sourceBundle, notebook, korteks, sourceReadiness));
            steps = steps
                .GroupBy(s => s.StepId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(12)
                .ToList();
        }

        return new PlanCurriculumSequenceDto
        {
            TopicId = request.TopicId,
            TopicTitle = topic.Title,
            ConfidenceStatus = DetermineSequenceConfidence(graph, korteks, sourceBundle, tracing, masteries),
            SequenceStatus = steps.Count == 0 ? "needs_revision" : "usable",
            SourceReadiness = sourceReadiness,
            Steps = steps,
            SequencingGraph = new PlanSequencingGraphDto
            {
                Nodes = concepts
                    .OrderBy(c => c.Order)
                    .Take(40)
                    .Select(c => new PlanSequencingNodeDto
                    {
                        ConceptKey = Clean(c.StableKey, 120) ?? string.Empty,
                        Label = Clean(c.Label, 160) ?? string.Empty,
                        Order = c.Order,
                        DifficultyBand = NormalizeDifficulty(c.DifficultyBand)
                    })
                    .ToArray(),
                Edges = relations
                    .Take(80)
                    .Select(r => new PlanSequencingEdgeDto
                    {
                        SourceConceptKey = Clean(r.SourceConceptKey, 120) ?? string.Empty,
                        TargetConceptKey = Clean(r.TargetConceptKey, 120) ?? string.Empty,
                        RelationType = Clean(r.RelationType, 80) ?? "prerequisite",
                        Weight = (decimal)Math.Clamp(r.Weight, 0, 1)
                    })
                    .ToArray()
            },
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<PlanQualityEvaluationDto> EvaluatePlanSequenceAsync(
        Guid userId,
        PlanQualityEvaluationRequestDto request,
        CancellationToken ct = default)
    {
        var sequence = request.ProposedSteps.Count > 0
            ? await BuildSequenceFromProposedAsync(userId, request, ct)
            : await BuildPlanSequenceAsync(userId, request, ct);

        var blocking = new List<PlanQualityIssueDto>();
        var warnings = new List<PlanQualityIssueDto>();
        EvaluateUnsafeCopy(request.PlanTitle, request.PlanSummary, sequence.Steps, blocking);
        EvaluateGenericAndThinPlan(sequence, blocking, warnings);
        EvaluateStepContracts(sequence, blocking, warnings);
        EvaluatePrerequisiteOrder(sequence, blocking, warnings);
        EvaluateSourceHumility(sequence, warnings);

        var specificity = ScoreSpecificity(sequence, blocking);
        var sequencing = ScoreSequencing(sequence, blocking);
        var evidence = ScoreEvidence(sequence, warnings);
        var assessment = ScoreAssessment(sequence, blocking);
        var tutor = ScoreTutor(sequence, blocking);
        var status = blocking.Count > 0
            ? "needs_revision"
            : Average(specificity, sequencing, evidence, assessment, tutor) >= 0.82m ? "strong" : "usable";

        if (sequence.Steps.Count == 0)
        {
            status = "insufficient";
        }

        var now = DateTime.UtcNow;
        var entity = new LearningPlanQualitySnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            PlanRequestId = request.PlanRequestId,
            ActiveLessonSnapshotId = request.ActiveLessonSnapshotId,
            StudentContextSnapshotId = request.StudentContextSnapshotId,
            QualityStatus = status,
            SpecificityScore = specificity,
            SequencingScore = sequencing,
            EvidenceAlignmentScore = evidence,
            AssessmentAlignmentScore = assessment,
            TutorAlignmentScore = tutor,
            BlockingIssuesJson = JsonSerializer.Serialize(blocking, JsonOptions),
            WarningIssuesJson = JsonSerializer.Serialize(warnings, JsonOptions),
            PlanContractJson = JsonSerializer.Serialize(sequence, JsonOptions),
            CreatedAt = now
        };

        _db.LearningPlanQualitySnapshots.Add(entity);
        await _db.SaveChangesAsync(ct);

        return ToDto(entity);
    }

    public async Task<PlanReadinessDto> GetPlanReadinessAsync(
        Guid userId,
        Guid topicId,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var topic = await EnsureTopicAsync(userId, topicId, ct);
        var graph = await LoadLatestConceptGraphAsync(userId, topicId, ct);
        var korteks = _korteksSynthesis == null ? null : await _korteksSynthesis.GetLatestWorkflowAsync(userId, topicId, sessionId, ct);
        var source = _sourceLifecycle == null ? null : await _sourceLifecycle.GetLatestSourceEvidenceBundleAsync(userId, topicId, sessionId, ct);
        var student = _snapshots == null ? null : await _snapshots.GetStudentContextSnapshotAsync(userId, topicId, sessionId, ct);
        var latest = await _db.LearningPlanQualitySnapshots
            .AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDeleted && s.TopicId == topicId && (!sessionId.HasValue || s.SessionId == sessionId))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var warnings = new List<string>();
        if (graph == null) warnings.Add("Concept graph eksik; plan daha dusuk guvenle kurulur.");
        if (source == null || source.EvidenceStatus is "stale" or "degraded" or "evidence_insufficient") warnings.Add("Kaynak hazirligi sinirli; plan kaynak iddiasi tasimaz.");
        if (student == null || student.ConfidenceStatus is "none" or "observed_only") warnings.Add("Ogrenci sinyali sinirli; ilk adim kisa teshis olmalidir.");

        return new PlanReadinessDto
        {
            TopicId = topicId,
            TopicTitle = topic.Title,
            HasConceptGraph = graph != null,
            HasKorteksSynthesis = korteks != null,
            HasSourceEvidence = source?.EvidenceStatus is "source_grounded" or "wiki_backed" or "mixed",
            SourceReadiness = source?.EvidenceStatus ?? student?.SourceReadiness ?? "evidence_insufficient",
            LearnerEvidenceStatus = student?.ConfidenceStatus ?? "observed_only",
            RecommendedFirstAction = student?.ConfidenceStatus == "usable" ? "continue_lesson" : "diagnostic_check",
            LatestQualitySnapshotId = latest?.Id,
            Warnings = warnings
        };
    }

    public async Task<PlanStepContractDto> BuildPlanStepContractAsync(
        Guid userId,
        Guid topicId,
        string conceptKey,
        CancellationToken ct = default)
    {
        var sequence = await BuildPlanSequenceAsync(userId, new PlanQualityEvaluationRequestDto { TopicId = topicId }, ct);
        var normalized = NormalizeKey(conceptKey);
        return sequence.Steps.FirstOrDefault(s => NormalizeKey(s.ConceptKey) == normalized)
               ?? sequence.Steps.FirstOrDefault()
               ?? BuildDiagnosticFirstStep(
                   await EnsureTopicAsync(userId, topicId, ct),
                   null,
                   null,
                   null,
                   "evidence_insufficient");
    }

    public async Task<PlanQualityEvaluationDto?> GetPlanQualitySnapshotAsync(
        Guid userId,
        Guid snapshotId,
        CancellationToken ct = default)
    {
        var entity = await _db.LearningPlanQualitySnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == snapshotId && s.UserId == userId && !s.IsDeleted, ct);
        return entity == null ? null : ToDto(entity);
    }

    public async Task<PlanQualityEvaluationDto?> GetLatestPlanQualitySnapshotAsync(
        Guid userId,
        Guid topicId,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var entity = await _db.LearningPlanQualitySnapshots
            .AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDeleted && s.TopicId == topicId && (!sessionId.HasValue || s.SessionId == sessionId))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return entity == null ? null : ToDto(entity);
    }

    private async Task<PlanCurriculumSequenceDto> BuildSequenceFromProposedAsync(Guid userId, PlanQualityEvaluationRequestDto request, CancellationToken ct)
    {
        var topic = await EnsureTopicAsync(userId, request.TopicId, ct);
        var source = _sourceLifecycle == null ? null : await _sourceLifecycle.GetLatestSourceEvidenceBundleAsync(userId, request.TopicId, request.SessionId, ct);
        return new PlanCurriculumSequenceDto
        {
            TopicId = request.TopicId,
            TopicTitle = topic.Title,
            ConfidenceStatus = "observed_only",
            SourceReadiness = source?.EvidenceStatus ?? "evidence_insufficient",
            SequenceStatus = "provided_for_evaluation",
            Steps = request.ProposedSteps.Select((step, index) => NormalizeProposedStep(step, topic, source, index)).ToArray(),
            SequencingGraph = new PlanSequencingGraphDto
            {
                Nodes = request.ProposedSteps.Select((step, index) => new PlanSequencingNodeDto
                {
                    ConceptKey = Clean(step.ConceptKey, 120) ?? $"proposed-{index + 1}",
                    Label = Clean(FirstNonEmpty(step.ConceptLabel, step.Title), 160) ?? $"Adim {index + 1}",
                    Order = index,
                    DifficultyBand = NormalizeDifficulty(step.DifficultyBand)
                }).ToArray()
            }
        };
    }

    private static PlanStepContractDto NormalizeProposedStep(PlanStepContractDto step, Topic topic, SourceEvidenceBundleDto? source, int index)
    {
        var conceptKey = Clean(step.ConceptKey, 120) ?? StableKey(FirstNonEmpty(step.ConceptLabel, step.Title, topic.Title));
        var title = Clean(step.Title, 180) ?? $"Adim {index + 1}";
        return new PlanStepContractDto
        {
            StepId = Clean(step.StepId, 80) ?? $"step-{index + 1}",
            Title = title,
            Objective = Clean(step.Objective, 300) ?? string.Empty,
            ConceptKey = conceptKey,
            ConceptLabel = Clean(FirstNonEmpty(step.ConceptLabel, title), 180) ?? title,
            PrerequisiteConceptKeys = step.PrerequisiteConceptKeys.Take(12).ToArray(),
            TargetMisconceptions = step.TargetMisconceptions.Take(8).ToArray(),
            MasteryTarget = Clean(step.MasteryTarget, 96) ?? "understand",
            EstimatedMinutes = Math.Clamp(step.EstimatedMinutes <= 0 ? 20 : step.EstimatedMinutes, 5, 90),
            LearnerState = Clean(step.LearnerState, 96) ?? "unknown",
            RemediationNeed = Clean(step.RemediationNeed, 96) ?? "none",
            DifficultyBand = NormalizeDifficulty(step.DifficultyBand),
            SequenceReason = Clean(step.SequenceReason, 500) ?? string.Empty,
            Evidence = step.Evidence ?? new PlanStepEvidenceDto { SourceReadiness = source?.EvidenceStatus ?? "evidence_insufficient" },
            QuizHook = step.QuizHook ?? new PlanStepAssessmentHookDto { ConceptKey = conceptKey },
            TutorHook = step.TutorHook ?? new PlanStepTutorHookDto { ActiveConceptKey = conceptKey },
            WikiHook = step.WikiHook ?? new PlanStepWikiHookDto { SourceReadiness = source?.EvidenceStatus ?? "evidence_insufficient" },
            SuccessCriteria = step.SuccessCriteria.Take(8).ToArray(),
            NextStepTrigger = Clean(step.NextStepTrigger, 120) ?? "micro_check_passed",
            FallbackIfEvidenceWeak = Clean(step.FallbackIfEvidenceWeak, 300) ?? "Run a short diagnostic check before claiming mastery."
        };
    }

    private async Task<Topic> EnsureTopicAsync(Guid userId, Guid topicId, CancellationToken ct)
    {
        var topic = await _db.Topics
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId && !t.IsArchived, ct);
        if (topic == null)
        {
            throw new InvalidOperationException("Topic not found for plan quality.");
        }

        return topic;
    }

    private async Task<ConceptGraphSnapshot?> LoadLatestConceptGraphAsync(Guid userId, Guid topicId, CancellationToken ct) =>
        await _db.ConceptGraphSnapshots
            .AsNoTracking()
            .Where(g => g.UserId == userId && g.TopicId == topicId)
            .OrderByDescending(g => g.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<ActiveLessonSnapshotDto?> GetActiveSnapshotByIdAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var entity = await _db.ActiveLessonSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId && !s.IsDeleted, ct);
        if (entity == null)
        {
            return null;
        }

        return new ActiveLessonSnapshotDto
        {
            Id = entity.Id,
            TopicId = entity.TopicId,
            SessionId = entity.SessionId,
            PlanRequestId = entity.PlanRequestId,
            QuizRunId = entity.QuizRunId,
            ConceptGraphSnapshotId = entity.ConceptGraphSnapshotId,
            SourceBundleHash = entity.SourceBundleHash,
            SnapshotVersion = entity.SnapshotVersion,
            Status = entity.Status,
            ActiveConceptKey = entity.ActiveConceptKey,
            ActiveConceptLabel = entity.ActiveConceptLabel,
            ApprovedIntent = entity.ApprovedIntent,
            ApprovedMainTopic = entity.ApprovedMainTopic,
            ApprovedFocusArea = entity.ApprovedFocusArea,
            ApprovedStudyGoal = entity.ApprovedStudyGoal,
            GroundingMode = entity.GroundingMode,
            EvidenceSummary = Parse(entity.EvidenceSummaryJson, new LearningSnapshotEvidenceSummaryDto()),
            RemediationNeed = entity.RemediationNeed,
            LearnerState = entity.LearnerState,
            Confidence = entity.Confidence,
            MasteryProbability = entity.MasteryProbability,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            ExpiresAt = entity.ExpiresAt
        };
    }

    private async Task<StudentContextSnapshotDto?> GetStudentSnapshotByIdAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var entity = await _db.StudentContextSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId && !s.IsDeleted, ct);
        if (entity == null)
        {
            return null;
        }

        return new StudentContextSnapshotDto
        {
            Id = entity.Id,
            TopicId = entity.TopicId,
            SessionId = entity.SessionId,
            SnapshotVersion = entity.SnapshotVersion,
            ConfidenceStatus = entity.ConfidenceStatus,
            StrongConcepts = Parse<IReadOnlyList<LearningSnapshotConceptDto>>(entity.StrongConceptsJson, Array.Empty<LearningSnapshotConceptDto>()),
            WeakConcepts = Parse<IReadOnlyList<LearningSnapshotConceptDto>>(entity.WeakConceptsJson, Array.Empty<LearningSnapshotConceptDto>()),
            RecentMisconceptions = Parse<IReadOnlyList<LearningSnapshotConceptDto>>(entity.RecentMisconceptionsJson, Array.Empty<LearningSnapshotConceptDto>()),
            RemediationReady = Parse<IReadOnlyList<LearningSnapshotRemediationDto>>(entity.RemediationReadyJson, Array.Empty<LearningSnapshotRemediationDto>()),
            ReviewPressure = Parse<IReadOnlyList<string>>(entity.ReviewPressureJson, Array.Empty<string>()),
            SourceReadiness = entity.SourceReadiness ?? "unknown",
            GoalReadiness = Parse(entity.GoalReadinessJson, new GoalReadinessDto()),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            ExpiresAt = entity.ExpiresAt
        };
    }

    private static List<LearningConcept> OrderConcepts(
        IReadOnlyList<LearningConcept> concepts,
        IReadOnlyList<ConceptRelation> relations,
        IReadOnlyList<KnowledgeTracingState> tracing,
        IReadOnlyList<ConceptMastery> masteries)
    {
        var weakKeys = tracing
            .Where(t => t.MasteryProbability < 0.55m || t.RemediationNeed is "medium" or "high")
            .Select(t => NormalizeKey(t.ConceptKey))
            .Concat(masteries.Where(m => m.MasteryScore < 55m || m.RemediationNeed is "medium" or "high").Select(m => NormalizeKey(m.ConceptKey)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prerequisiteTargets = relations
            .Where(r => IsPrerequisite(r.RelationType))
            .Select(r => NormalizeKey(r.TargetConceptKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return concepts
            .OrderBy(c => prerequisiteTargets.Contains(NormalizeKey(c.StableKey)) ? 1 : 0)
            .ThenBy(c => weakKeys.Contains(NormalizeKey(c.StableKey)) ? 0 : 1)
            .ThenBy(c => c.Order)
            .ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PlanStepContractDto BuildStep(
        Topic topic,
        LearningConcept concept,
        int index,
        IReadOnlyList<ConceptRelation> relations,
        IReadOnlyList<KnowledgeTracingState> tracing,
        IReadOnlyList<ConceptMastery> masteries,
        SourceEvidenceBundleDto? sourceBundle,
        WikiKnowledgeNotebookDto? notebook,
        KorteksResearchWorkflowDto? korteks,
        string sourceReadiness,
        string learnerState,
        string remediationNeed)
    {
        var conceptKey = Clean(concept.StableKey, 120) ?? $"concept-{index + 1}";
        var misconceptions = Parse<IReadOnlyList<string>>(concept.MisconceptionsJson, Array.Empty<string>()).Take(5).ToArray();
        var prerequisites = Parse<IReadOnlyList<string>>(concept.PrerequisitesJson, Array.Empty<string>())
            .Concat(relations.Where(r => IsPrerequisite(r.RelationType) && NormalizeKey(r.TargetConceptKey) == NormalizeKey(concept.StableKey)).Select(r => r.SourceConceptKey))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var tracingState = tracing.FirstOrDefault(t => NormalizeKey(t.ConceptKey) == NormalizeKey(conceptKey));
        var mastery = masteries.FirstOrDefault(m => NormalizeKey(m.ConceptKey) == NormalizeKey(conceptKey));
        var isWeak = tracingState?.MasteryProbability < 0.55m || mastery?.MasteryScore < 55m || remediationNeed is "medium" or "high";
        var tutorMove = misconceptions.Length > 0 && isWeak ? "misconception_repair" : isWeak ? "scaffold" : "explain";
        var quizHook = isWeak ? "misconception_probe" : index == 0 ? "diagnostic_check" : "micro_quiz";
        var section = notebook?.Sections.FirstOrDefault(s =>
            string.Equals(s.ConceptKey, conceptKey, StringComparison.OrdinalIgnoreCase) ||
            ContainsNormalized(s.Title, concept.Label));
        var warnings = new List<string>();
        if (sourceReadiness is "stale" or "degraded" or "evidence_insufficient")
        {
            warnings.Add("Kaynak hazirligi sinirli; bu adim kaynak iddiasi tasimaz.");
        }

        return new PlanStepContractDto
        {
            StepId = $"step-{index + 1}-{StableKey(conceptKey)}",
            Title = $"{Clean(concept.Label, 140) ?? topic.Title}: {StepTitleSuffix(tutorMove)}",
            Objective = $"{Clean(concept.Label, 160) ?? topic.Title} kavramini olculebilir sekilde anlamak ve sonraki adima gecmeden kisa kontrol yapmak.",
            ConceptKey = conceptKey,
            ConceptLabel = Clean(concept.Label, 180) ?? conceptKey,
            PrerequisiteConceptKeys = prerequisites,
            TargetMisconceptions = misconceptions,
            MasteryTarget = isWeak ? "repair_to_guided_practice" : "explain_and_micro_check",
            EstimatedMinutes = isWeak ? 25 : 18,
            LearnerState = learnerState,
            RemediationNeed = isWeak ? "medium" : remediationNeed,
            DifficultyBand = NormalizeDifficulty(concept.DifficultyBand),
            SequenceReason = prerequisites.Length > 0
                ? "Bu adim, onkosul kavramlar netlestikten sonra ana kavrami olcmek icin siraya alindi."
                : index == 0 ? "Ilk adim konu haritasini ve baslangic kanitini olcmek icin siraya alindi." : "Bu adim concept graph sirasi, mastery sinyali ve onceki adim baglantisina gore secildi.",
            Evidence = new PlanStepEvidenceDto
            {
                EvidenceBasis = BuildEvidenceBasis(sourceBundle, notebook, korteks, tracingState, mastery),
                SourceReadiness = sourceReadiness,
                SourceEvidenceBundleId = sourceBundle?.Id,
                WikiNotebookSectionKey = section?.SectionKey,
                KorteksWorkflowId = korteks?.Id,
                Warnings = warnings
            },
            QuizHook = new PlanStepAssessmentHookDto
            {
                HookType = quizHook,
                ConceptKey = conceptKey,
                TargetMisconceptions = misconceptions,
                DifficultyBand = NormalizeDifficulty(concept.DifficultyBand),
                UserSafeReason = isWeak
                    ? "Bu kavramda zayiflik/telafi sinyali oldugu icin yanilgi yoklamasi gerekir."
                    : "Bu adimdan sonra kisa bir mikro quiz yeterlidir."
            },
            TutorHook = new PlanStepTutorHookDto
            {
                TutorMove = tutorMove,
                ActiveConceptKey = conceptKey,
                TargetMisconception = misconceptions.FirstOrDefault(),
                UserSafeReason = tutorMove == "misconception_repair"
                    ? "Tutor once olasi yanilgiyi sinirlar, sonra adim adim onarir."
                    : "Tutor kavrami kisa anlatip mikro kontrolle ilerler."
            },
            WikiHook = new PlanStepWikiHookDto
            {
                SectionKey = section?.SectionKey,
                SourceReadiness = sourceReadiness,
                UserSafeWarning = warnings.FirstOrDefault()
            },
            SuccessCriteria =
            [
                "Kavrami kendi cumlesiyle aciklar.",
                "En az bir mikro kontrol sorusunu dogru cevaplar.",
                "Varsa hedef yanilgiyi ayirt eder."
            ],
            NextStepTrigger = "micro_check_passed",
            FallbackIfEvidenceWeak = "Kaynak/ogrenci kaniti zayifsa once kisa diagnostic check ve Tutor scaffold uygulanir."
        };
    }

    private static List<PlanStepContractDto> BuildFallbackSteps(
        Topic topic,
        KorteksResearchWorkflowDto? korteks,
        SourceEvidenceBundleDto? sourceBundle,
        string sourceReadiness,
        string learnerState,
        string remediationNeed)
    {
        var labels = korteks?.Synthesis.LearningRoute.Select(i => i.Text).Where(v => !string.IsNullOrWhiteSpace(v)).Take(5).ToList() ?? [];
        if (labels.Count == 0)
        {
            labels = [$"{topic.Title} onkosul haritasi", $"{topic.Title} ana kavram", $"{topic.Title} pratik kontrol"];
        }

        return labels.Select((label, index) =>
        {
            var conceptKey = StableKey(label);
            return new PlanStepContractDto
            {
                StepId = $"fallback-{index + 1}-{conceptKey}",
                Title = Clean(label, 160) ?? $"Adim {index + 1}",
                Objective = $"{Clean(label, 160) ?? topic.Title} icin olculebilir bir baslangic anlayisi kurmak.",
                ConceptKey = conceptKey,
                ConceptLabel = Clean(label, 160) ?? topic.Title,
                MasteryTarget = index == 0 ? "diagnostic_ready" : "micro_check_ready",
                EstimatedMinutes = index == 0 ? 15 : 20,
                LearnerState = learnerState,
                RemediationNeed = remediationNeed,
                DifficultyBand = index == 0 ? "foundation" : "core",
                SequenceReason = index == 0
                    ? "Concept graph yoksa ilk adim diagnostic-first baslamalidir."
                    : "Kavram grafigi eksikken Korteks/konu basligi sinyaliyle dusuk guvenli fallback sira kuruldu.",
                Evidence = new PlanStepEvidenceDto
                {
                    EvidenceBasis = sourceBundle == null ? ["topic_title", "fallback_sequence"] : ["source_evidence_bundle", "fallback_sequence"],
                    SourceReadiness = sourceReadiness,
                    SourceEvidenceBundleId = sourceBundle?.Id,
                    KorteksWorkflowId = korteks?.Id,
                    Warnings = ["Concept graph eksik; bu adim dusuk guvenli fallback olarak isaretlendi."]
                },
                QuizHook = new PlanStepAssessmentHookDto
                {
                    HookType = index == 0 ? "diagnostic_check" : "micro_quiz",
                    ConceptKey = conceptKey,
                    DifficultyBand = index == 0 ? "foundation" : "core",
                    UserSafeReason = "Concept graph eksik oldugu icin plan kisa kontrolle dogrulanir."
                },
                TutorHook = new PlanStepTutorHookDto
                {
                    TutorMove = index == 0 ? "scaffold" : "example",
                    ActiveConceptKey = conceptKey,
                    UserSafeReason = "Tutor once konuyu daraltir ve ogrenci kaniti toplar."
                },
                WikiHook = new PlanStepWikiHookDto
                {
                    SourceReadiness = sourceReadiness,
                    UserSafeWarning = "Kaynak/kavram kaniti sinirli olabilir."
                },
                SuccessCriteria = ["Kisa kontrol tamamlanir.", "Bir sonraki adim icin yeterli sinyal olusur."],
                NextStepTrigger = "diagnostic_or_micro_check_completed",
                FallbackIfEvidenceWeak = "Once diagnostic check yap; mastery iddiasi kurma."
            };
        }).ToList();
    }

    private static PlanStepContractDto BuildDiagnosticFirstStep(
        Topic topic,
        SourceEvidenceBundleDto? sourceBundle,
        WikiKnowledgeNotebookDto? notebook,
        KorteksResearchWorkflowDto? korteks,
        string sourceReadiness)
    {
        var conceptKey = StableKey($"{topic.Title} diagnostic");
        return new PlanStepContractDto
        {
            StepId = "diagnostic-first",
            Title = $"{topic.Title}: kisa seviye tespiti",
            Objective = "Ogrenci kaniti sinirli oldugu icin once baslangic seviyesini ve onkosul eksigini olcmek.",
            ConceptKey = conceptKey,
            ConceptLabel = $"{topic.Title} baslangic teshisi",
            MasteryTarget = "evidence_collection",
            EstimatedMinutes = 12,
            LearnerState = "evidence_insufficient",
            RemediationNeed = "evidence_insufficient",
            DifficultyBand = "foundation",
            SequenceReason = "Ogrenci sinyali yok veya dusuk; profesyonel plan once diagnostic-first baslar.",
            Evidence = new PlanStepEvidenceDto
            {
                EvidenceBasis = ["student_context", "diagnostic_intake"],
                SourceReadiness = sourceReadiness,
                SourceEvidenceBundleId = sourceBundle?.Id,
                WikiNotebookSectionKey = notebook?.Sections.FirstOrDefault()?.SectionKey,
                KorteksWorkflowId = korteks?.Id
            },
            QuizHook = new PlanStepAssessmentHookDto
            {
                HookType = "diagnostic_check",
                ConceptKey = conceptKey,
                DifficultyBand = "foundation",
                UserSafeReason = "Ogrenci kaniti sinirli; ilk adim kisa seviye tespitidir."
            },
            TutorHook = new PlanStepTutorHookDto
            {
                TutorMove = "scaffold",
                ActiveConceptKey = conceptKey,
                UserSafeReason = "Tutor once seviyeyi nazikce yoklar, kesin mastery iddiasi kurmaz."
            },
            WikiHook = new PlanStepWikiHookDto
            {
                SourceReadiness = sourceReadiness,
                UserSafeWarning = sourceReadiness == "evidence_insufficient" ? "Kaynak kaniti sinirli." : null
            },
            SuccessCriteria = ["Kisa diagnostic tamamlanir.", "Zayif/on kosul alanlari belirlenir."],
            NextStepTrigger = "diagnostic_completed",
            FallbackIfEvidenceWeak = "Tutor soru-cevapla baslangic seviyesini daraltir."
        };
    }

    private static IReadOnlyList<string> BuildEvidenceBasis(
        SourceEvidenceBundleDto? sourceBundle,
        WikiKnowledgeNotebookDto? notebook,
        KorteksResearchWorkflowDto? korteks,
        KnowledgeTracingState? tracing,
        ConceptMastery? mastery)
    {
        var basis = new List<string> { "concept_graph" };
        if (sourceBundle != null) basis.Add($"source:{sourceBundle.EvidenceStatus}");
        if (notebook != null) basis.Add($"wiki:{notebook.EvidenceStatus}");
        if (korteks != null) basis.Add("korteks_synthesis");
        if (tracing != null) basis.Add("knowledge_tracing");
        if (mastery != null) basis.Add("concept_mastery");
        return basis.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
    }

    private static void EvaluateUnsafeCopy(string? title, string? summary, IReadOnlyList<PlanStepContractDto> steps, List<PlanQualityIssueDto> blocking)
    {
        var text = string.Join("\n", new[] { title, summary }.Where(v => !string.IsNullOrWhiteSpace(v)).Concat(steps.SelectMany(s => new[] { s.Title, s.Objective, s.SequenceReason })));
        var normalized = NormalizeText(text);
        if (ContainsAny(normalized, "garanti", "kazanma garantisi", "basari garantisi", "success guarantee"))
        {
            blocking.Add(Issue("unsafe_success_claim", "Plan basari/kazanma garantisi veremez.", "blocking"));
        }

        if (ContainsAny(normalized, "resmi mufredat tamam", "official curriculum complete", "osym simulasyonu", "meb simulasyonu", "official simulation"))
        {
            blocking.Add(Issue("unsafe_official_claim", "Plan dogrulanmamis resmi kapsam/simulasyon iddiasi tasiyamaz.", "blocking"));
        }

        if (ContainsAny(normalized, "teacher dashboard", "classroom workflow", "dershane paneli", "ogretmen paneli"))
        {
            blocking.Add(Issue("teacher_workflow_copy", "Plan ogrenci odakli kalmali; ogretmen/dershane is akisi tasiyamaz.", "blocking"));
        }

        if (ContainsAny(normalized, "raw prompt", "provider payload", "debug trace", "stack trace"))
        {
            blocking.Add(Issue("internal_payload_leak", "Plan public sozlesmesi prompt/provider/debug verisi tasiyamaz.", "blocking"));
        }
    }

    private static void EvaluateGenericAndThinPlan(PlanCurriculumSequenceDto sequence, List<PlanQualityIssueDto> blocking, List<PlanQualityIssueDto> warnings)
    {
        if (sequence.Steps.Count == 0)
        {
            blocking.Add(Issue("plan_empty", "Plan en az bir olculebilir adim tasimali.", "blocking"));
            return;
        }

        var joined = NormalizeText(string.Join(" ", sequence.Steps.SelectMany(s => new[] { s.Title, s.Objective })));
        if (ContainsAny(joined, "study harder", "review basics", "genel tekrar", "konuyu calis", "read notes") && sequence.Steps.Count <= 2)
        {
            blocking.Add(Issue("plan_too_generic", "Plan konuya ozel kavram ve olcum hedefi tasimiyor.", "blocking"));
        }

        if (sequence.Steps.Count < 2)
        {
            warnings.Add(Issue("plan_short", "Plan cok kisa; sonraki adim icin ek kavram sirasi gerekebilir.", "warning"));
        }
    }

    private static void EvaluateStepContracts(PlanCurriculumSequenceDto sequence, List<PlanQualityIssueDto> blocking, List<PlanQualityIssueDto> warnings)
    {
        foreach (var step in sequence.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.ConceptKey) || string.IsNullOrWhiteSpace(step.Objective))
            {
                blocking.Add(Issue("step_missing_concept_or_objective", "Plan adimi conceptKey ve olculebilir objective tasimali.", "blocking", step.StepId));
            }

            if (string.IsNullOrWhiteSpace(step.SequenceReason))
            {
                blocking.Add(Issue("step_missing_sequence_reason", "Plan adimi neden bu sirada oldugunu aciklamali.", "blocking", step.StepId));
            }

            if (step.QuizHook == null || string.IsNullOrWhiteSpace(step.QuizHook.HookType) || string.IsNullOrWhiteSpace(step.QuizHook.ConceptKey))
            {
                blocking.Add(Issue("step_missing_quiz_hook", "Olculebilir adim quiz/assessment hook tasimali.", "blocking", step.StepId));
            }

            if (step.TutorHook == null || string.IsNullOrWhiteSpace(step.TutorHook.TutorMove))
            {
                blocking.Add(Issue("step_missing_tutor_hook", "Plan adimi Tutor ogretim hareketi tasimali.", "blocking", step.StepId));
            }

            if (step.SuccessCriteria.Count == 0)
            {
                warnings.Add(Issue("step_missing_success_criteria", "Plan adimi user-safe basari kriteri tasimiyor.", "warning", step.StepId));
            }
        }
    }

    private static void EvaluatePrerequisiteOrder(PlanCurriculumSequenceDto sequence, List<PlanQualityIssueDto> blocking, List<PlanQualityIssueDto> warnings)
    {
        var positions = sequence.Steps
            .Select((step, index) => new { step, index })
            .ToDictionary(x => NormalizeKey(x.step.ConceptKey), x => x.index, StringComparer.OrdinalIgnoreCase);
        foreach (var current in sequence.Steps)
        {
            var currentIndex = positions.GetValueOrDefault(NormalizeKey(current.ConceptKey), -1);
            foreach (var prerequisite in current.PrerequisiteConceptKeys)
            {
                var key = NormalizeKey(prerequisite);
                if (!positions.TryGetValue(key, out var prerequisiteIndex))
                {
                    warnings.Add(Issue("prerequisite_not_in_plan", "Onkosul kavram plan adimlarinda yok; fallback/dusuk guven gerekebilir.", "warning", current.StepId));
                }
                else if (prerequisiteIndex > currentIndex)
                {
                    blocking.Add(Issue("prerequisite_order_violation", "Onkosul kavram bagimli kavramdan sonra gelmis.", "blocking", current.StepId));
                }
            }
        }
    }

    private static void EvaluateSourceHumility(PlanCurriculumSequenceDto sequence, List<PlanQualityIssueDto> warnings)
    {
        if (sequence.SourceReadiness is "stale" or "degraded" or "evidence_insufficient" or "unknown")
        {
            warnings.Add(Issue("source_readiness_limited", "Kaynak hazirligi sinirli; plan kaynak destekli kesinlik iddiasi kurmuyor.", "warning"));
        }
    }

    private static decimal ScoreSpecificity(PlanCurriculumSequenceDto sequence, List<PlanQualityIssueDto> blocking)
    {
        if (sequence.Steps.Count == 0 || blocking.Any(i => i.Code == "plan_too_generic")) return 0.2m;
        var good = sequence.Steps.Count(s => !string.IsNullOrWhiteSpace(s.ConceptKey) && !string.IsNullOrWhiteSpace(s.Objective) && s.Objective.Length > 24);
        return Ratio(good, sequence.Steps.Count);
    }

    private static decimal ScoreSequencing(PlanCurriculumSequenceDto sequence, List<PlanQualityIssueDto> blocking)
    {
        if (sequence.Steps.Count == 0 || blocking.Any(i => i.Code == "prerequisite_order_violation")) return 0.35m;
        var good = sequence.Steps.Count(s => !string.IsNullOrWhiteSpace(s.SequenceReason));
        return Ratio(good, sequence.Steps.Count);
    }

    private static decimal ScoreEvidence(PlanCurriculumSequenceDto sequence, List<PlanQualityIssueDto> warnings)
    {
        if (sequence.Steps.Count == 0) return 0m;
        var good = sequence.Steps.Count(s => s.Evidence.EvidenceBasis.Count > 0);
        var score = Ratio(good, sequence.Steps.Count);
        return warnings.Any(i => i.Code == "source_readiness_limited") ? Math.Min(score, 0.72m) : score;
    }

    private static decimal ScoreAssessment(PlanCurriculumSequenceDto sequence, List<PlanQualityIssueDto> blocking)
    {
        if (sequence.Steps.Count == 0 || blocking.Any(i => i.Code == "step_missing_quiz_hook")) return 0.25m;
        var good = sequence.Steps.Count(s => s.QuizHook != null && !string.IsNullOrWhiteSpace(s.QuizHook.HookType) && !string.IsNullOrWhiteSpace(s.QuizHook.ConceptKey));
        return Ratio(good, sequence.Steps.Count);
    }

    private static decimal ScoreTutor(PlanCurriculumSequenceDto sequence, List<PlanQualityIssueDto> blocking)
    {
        if (sequence.Steps.Count == 0 || blocking.Any(i => i.Code == "step_missing_tutor_hook")) return 0.25m;
        var good = sequence.Steps.Count(s => s.TutorHook != null && !string.IsNullOrWhiteSpace(s.TutorHook.TutorMove));
        return Ratio(good, sequence.Steps.Count);
    }

    private static PlanQualityEvaluationDto ToDto(LearningPlanQualitySnapshot entity) => new()
    {
        SnapshotId = entity.Id,
        TopicId = entity.TopicId ?? Guid.Empty,
        SessionId = entity.SessionId,
        PlanRequestId = entity.PlanRequestId,
        ActiveLessonSnapshotId = entity.ActiveLessonSnapshotId,
        StudentContextSnapshotId = entity.StudentContextSnapshotId,
        QualityStatus = entity.QualityStatus,
        SpecificityScore = entity.SpecificityScore,
        SequencingScore = entity.SequencingScore,
        EvidenceAlignmentScore = entity.EvidenceAlignmentScore,
        AssessmentAlignmentScore = entity.AssessmentAlignmentScore,
        TutorAlignmentScore = entity.TutorAlignmentScore,
        BlockingIssues = Parse<IReadOnlyList<PlanQualityIssueDto>>(entity.BlockingIssuesJson, Array.Empty<PlanQualityIssueDto>()),
        WarningIssues = Parse<IReadOnlyList<PlanQualityIssueDto>>(entity.WarningIssuesJson, Array.Empty<PlanQualityIssueDto>()),
        PlanContract = Parse(entity.PlanContractJson, new PlanCurriculumSequenceDto()),
        GeneratedAt = entity.CreatedAt
    };

    private static string ResolveRemediationNeed(IReadOnlyList<KnowledgeTracingState> tracing, IReadOnlyList<ConceptMastery> masteries)
    {
        if (tracing.Any(t => t.RemediationNeed is "high" or "medium") || masteries.Any(m => m.RemediationNeed is "high" or "medium"))
        {
            return "medium";
        }

        return tracing.Count + masteries.Count == 0 ? "evidence_insufficient" : "none";
    }

    private static bool NoLearnerEvidence(
        IReadOnlyList<KnowledgeTracingState> tracing,
        IReadOnlyList<ConceptMastery> masteries,
        StudentContextSnapshotDto? student,
        ActiveLessonSnapshotDto? active) =>
        tracing.Count == 0 &&
        masteries.Count == 0 &&
        (student == null || student.ConfidenceStatus is "none" or "observed_only") &&
        (active == null || active.EvidenceSummary.RecentAttemptCount == 0);

    private static string DetermineSequenceConfidence(
        ConceptGraphSnapshot? graph,
        KorteksResearchWorkflowDto? korteks,
        SourceEvidenceBundleDto? source,
        IReadOnlyList<KnowledgeTracingState> tracing,
        IReadOnlyList<ConceptMastery> masteries)
    {
        var score = 0;
        if (graph != null) score += 2;
        if (korteks != null) score += 1;
        if (source?.EvidenceStatus is "source_grounded" or "wiki_backed" or "mixed") score += 1;
        if (tracing.Count + masteries.Count > 0) score += 1;
        return score switch
        {
            >= 4 => "strong",
            >= 2 => "usable",
            _ => "observed_only"
        };
    }

    private static bool IsPrerequisite(string? relationType) =>
        string.Equals(relationType, "prerequisite", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(relationType, "requires", StringComparison.OrdinalIgnoreCase);

    private static string StepTitleSuffix(string tutorMove) => tutorMove switch
    {
        "misconception_repair" => "yanilgi onarimi",
        "scaffold" => "adim adim kurulum",
        "example" => "ornekleme",
        _ => "kavram kontrolu"
    };

    private static PlanQualityIssueDto Issue(string code, string message, string severity, string? stepId = null) => new()
    {
        Code = code,
        Severity = severity,
        Message = message,
        StepId = stepId
    };

    private static decimal Ratio(int part, int total) => total <= 0 ? 0m : Math.Clamp((decimal)part / total, 0m, 1m);

    private static decimal Average(params decimal[] values) => values.Length == 0 ? 0m : values.Sum() / values.Length;

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsNormalized(string? text, string? needle) =>
        NormalizeText(text).Contains(NormalizeText(needle), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeKey(string? value) => StableKey(value);

    private static string StableKey(string? value)
    {
        var normalized = NormalizeText(value)
            .Replace("ı", "i", StringComparison.Ordinal)
            .Replace("ğ", "g", StringComparison.Ordinal)
            .Replace("ü", "u", StringComparison.Ordinal)
            .Replace("ş", "s", StringComparison.Ordinal)
            .Replace("ö", "o", StringComparison.Ordinal)
            .Replace("ç", "c", StringComparison.Ordinal);
        var chars = normalized.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var key = Whitespace.Replace(new string(chars).Replace("--", "-", StringComparison.Ordinal), "-").Trim('-');
        return string.IsNullOrWhiteSpace(key) ? "concept" : key.Length <= 80 ? key : key[..80].Trim('-');
    }

    private static string NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : Whitespace.Replace(value.Trim().ToLowerInvariant(), " ");

    private static string NormalizeDifficulty(string? value)
    {
        var normalized = NormalizeText(value);
        return normalized switch
        {
            "foundation" or "basic" or "beginner" or "baslangic" => "foundation",
            "advanced" or "challenge" or "zor" => "advanced",
            "remediation" => "remediation",
            _ => "core"
        };
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = Whitespace.Replace(value.Trim(), " ");
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private static T Parse<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

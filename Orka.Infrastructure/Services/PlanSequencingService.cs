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

        var sequence = new PlanCurriculumSequenceDto
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

        AttachAdaptivePlanMetadata(
            sequence,
            topic,
            request,
            graph,
            sourceBundle,
            studentSnapshot,
            activeSnapshot,
            tracing,
            masteries,
            remediationNeed);

        return sequence;
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
        EvaluateAdaptivePlanQuality(sequence, warnings);

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
        var latestDto = latest == null ? null : ToDto(latest);
        var adaptiveDiagnostic = latestDto?.PlanContract.AdaptiveDiagnostic
            ?? BuildAdaptiveDiagnostic(
                topic,
                new PlanQualityEvaluationRequestDto { TopicId = topicId, SessionId = sessionId },
                graph,
                source,
                student,
                active: null,
                tracing: Array.Empty<KnowledgeTracingState>(),
                masteries: Array.Empty<ConceptMastery>(),
                steps: Array.Empty<PlanStepContractDto>(),
                remediationNeed: student?.RemediationReady.Count > 0 ? "medium" : "evidence_insufficient");
        var coursePlanQuality = latestDto?.PlanContract.CoursePlanQuality
            ?? BuildCoursePlanQuality(
                new PlanCurriculumSequenceDto
                {
                    TopicId = topicId,
                    TopicTitle = topic.Title,
                    ConfidenceStatus = graph == null ? "observed_only" : "usable",
                    SourceReadiness = source?.EvidenceStatus ?? student?.SourceReadiness ?? "evidence_insufficient",
                    SequenceStatus = graph == null ? "needs_revision" : "usable",
                    Steps = Array.Empty<PlanStepContractDto>()
                },
                adaptiveDiagnostic,
                graph,
                source,
                student,
                active: null,
                Array.Empty<KnowledgeTracingState>(),
                Array.Empty<ConceptMastery>());
        warnings.AddRange(adaptiveDiagnostic.Warnings);
        warnings.AddRange(coursePlanQuality.Warnings);

        return new PlanReadinessDto
        {
            TopicId = topicId,
            TopicTitle = topic.Title,
            HasConceptGraph = graph != null,
            HasKorteksSynthesis = korteks != null,
            HasSourceEvidence = source?.EvidenceStatus is "source_grounded" or "wiki_backed" or "mixed",
            SourceReadiness = source?.EvidenceStatus ?? student?.SourceReadiness ?? "evidence_insufficient",
            LearnerEvidenceStatus = student?.ConfidenceStatus ?? "observed_only",
            PlanReadinessStatus = coursePlanQuality.ReadinessStatus,
            RecommendedFirstAction = coursePlanQuality.RecommendedNextAction,
            LatestQualitySnapshotId = latest?.Id,
            AdaptiveDiagnostic = adaptiveDiagnostic,
            CoursePlanQuality = coursePlanQuality,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray()
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
        var steps = request.ProposedSteps.Select((step, index) => NormalizeProposedStep(step, topic, source, index)).ToArray();
        var sequence = new PlanCurriculumSequenceDto
        {
            TopicId = request.TopicId,
            TopicTitle = topic.Title,
            ConfidenceStatus = "observed_only",
            SourceReadiness = source?.EvidenceStatus ?? "evidence_insufficient",
            SequenceStatus = "provided_for_evaluation",
            Steps = steps,
            SequencingGraph = new PlanSequencingGraphDto
            {
                Nodes = steps.Select((step, index) => new PlanSequencingNodeDto
                {
                    ConceptKey = Clean(step.ConceptKey, 120) ?? $"proposed-{index + 1}",
                    Label = Clean(FirstNonEmpty(step.ConceptLabel, step.Title), 160) ?? $"Adim {index + 1}",
                    Order = index,
                    DifficultyBand = NormalizeDifficulty(step.DifficultyBand)
                }).ToArray()
            }
        };

        AttachAdaptivePlanMetadata(
            sequence,
            topic,
            request,
            graph: null,
            source,
            student: null,
            active: null,
            tracing: Array.Empty<KnowledgeTracingState>(),
            masteries: Array.Empty<ConceptMastery>(),
            remediationNeed: ResolveRemediationNeed(Array.Empty<KnowledgeTracingState>(), Array.Empty<ConceptMastery>()));

        return sequence;
    }

    private static void AttachAdaptivePlanMetadata(
        PlanCurriculumSequenceDto sequence,
        Topic topic,
        PlanQualityEvaluationRequestDto request,
        ConceptGraphSnapshot? graph,
        SourceEvidenceBundleDto? source,
        StudentContextSnapshotDto? student,
        ActiveLessonSnapshotDto? active,
        IReadOnlyList<KnowledgeTracingState> tracing,
        IReadOnlyList<ConceptMastery> masteries,
        string remediationNeed)
    {
        sequence.AdaptiveDiagnostic = BuildAdaptiveDiagnostic(
            topic,
            request,
            graph,
            source,
            student,
            active,
            tracing,
            masteries,
            sequence.Steps,
            remediationNeed);
        sequence.CoursePlanQuality = BuildCoursePlanQuality(
            sequence,
            sequence.AdaptiveDiagnostic,
            graph,
            source,
            student,
            active,
            tracing,
            masteries);
    }

    private static AdaptiveDiagnosticDto BuildAdaptiveDiagnostic(
        Topic topic,
        PlanQualityEvaluationRequestDto request,
        ConceptGraphSnapshot? graph,
        SourceEvidenceBundleDto? source,
        StudentContextSnapshotDto? student,
        ActiveLessonSnapshotDto? active,
        IReadOnlyList<KnowledgeTracingState> tracing,
        IReadOnlyList<ConceptMastery> masteries,
        IReadOnlyList<PlanStepContractDto> steps,
        string remediationNeed)
    {
        var learnerEvidence = student?.ConfidenceStatus ?? (tracing.Count + masteries.Count > 0 ? "usable" : "observed_only");
        var intent = DetermineAdaptiveIntent(topic, request, source, active, remediationNeed);
        var placement = BuildLearnerPlacement(student, active, tracing, masteries, remediationNeed);
        var readiness = DeterminePlanReadiness(graph, source, student, active, steps, remediationNeed);
        var warnings = new List<string>();
        if (learnerEvidence is "none" or "observed_only") warnings.Add("learner_evidence_limited");
        if (graph == null) warnings.Add("concept_graph_missing");
        if (source?.EvidenceStatus is "stale" or "degraded" or "evidence_insufficient") warnings.Add("source_evidence_limited");
        if (readiness is "needs_repair" or "needs_prerequisite_check") warnings.Add("repair_or_prerequisite_check_needed");

        return new AdaptiveDiagnosticDto
        {
            DiagnosticId = request.PlanRequestId,
            TopicId = request.TopicId,
            Intent = intent,
            Confidence = DetermineIntentConfidence(intent, topic, source, active, student),
            LearnerLevel = placement.LearnerLevel,
            Placement = placement,
            PlacementBasis = BuildPlacementSignals(student, active, tracing, masteries, source, graph),
            RecommendedQuestions = BuildDiagnosticQuestions(topic, intent, placement.LearnerLevel, steps, readiness),
            PrerequisiteSignals = steps
                .SelectMany(s => s.PrerequisiteConceptKeys)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray(),
            WeakConceptSignals = student?.WeakConcepts.Select(c => c.ConceptKey).Concat(
                    tracing.Where(t => t.RemediationNeed is "high" or "medium" || t.MasteryProbability < 0.45m)
                        .Select(t => t.ConceptKey))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray() ?? Array.Empty<string>(),
            PlanReadiness = readiness,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(),
            NextAction = readiness switch
            {
                "ready" => "continue_lesson",
                "needs_repair" => "start_remediation",
                "needs_prerequisite_check" => "run_prerequisite_check",
                "source_limited" => "continue_without_source_claim",
                "thin_plan" => "build_concept_graph",
                _ => "run_diagnostic"
            }
        };
    }

    private static CoursePlanQualityDto BuildCoursePlanQuality(
        PlanCurriculumSequenceDto sequence,
        AdaptiveDiagnosticDto diagnostic,
        ConceptGraphSnapshot? graph,
        SourceEvidenceBundleDto? source,
        StudentContextSnapshotDto? student,
        ActiveLessonSnapshotDto? active,
        IReadOnlyList<KnowledgeTracingState> tracing,
        IReadOnlyList<ConceptMastery> masteries)
    {
        var milestones = BuildMilestones(sequence.Steps);
        var repairLoops = BuildRepairLoops(sequence.Steps, student, tracing, masteries);
        var checkpointCoverage = sequence.Steps.Count == 0
            ? 0m
            : Ratio(sequence.Steps.Count(s => s.QuizHook != null && !string.IsNullOrWhiteSpace(s.QuizHook.HookType)), sequence.Steps.Count);
        var prerequisiteCoverage = sequence.Steps.Any(s => s.PrerequisiteConceptKeys.Count > 0)
            ? "mapped"
            : graph == null ? "unknown" : "lightweight";
        var warnings = new List<string>();
        if (diagnostic.PlanReadiness != "ready") warnings.Add(diagnostic.PlanReadiness);
        if (checkpointCoverage < 0.80m) warnings.Add("checkpoint_coverage_low");
        if (repairLoops.Count == 0 && (diagnostic.PlanReadiness is "needs_repair" or "needs_prerequisite_check")) warnings.Add("repair_loop_missing");
        if (source?.EvidenceStatus is "stale" or "degraded" or "evidence_insufficient" or null) warnings.Add("source_limited_no_source_claim");
        if (student?.ConfidenceStatus is null or "none" or "observed_only") warnings.Add("learner_level_provisional");

        return new CoursePlanQualityDto
        {
            ReadinessStatus = diagnostic.PlanReadiness,
            GoalClarity = string.IsNullOrWhiteSpace(sequence.TopicTitle) ? "unclear" : "usable",
            LearnerLevelBasis = diagnostic.Placement.Basis,
            PrerequisiteCoverage = prerequisiteCoverage,
            SequenceCoherence = sequence.SequenceStatus == "usable" || sequence.SequenceStatus == "provided_for_evaluation" ? "usable" : "needs_review",
            MilestoneCount = milestones.Count,
            CheckpointCoverage = checkpointCoverage,
            RepairLoopCount = repairLoops.Count,
            AssessmentAlignment = checkpointCoverage >= 0.80m ? "usable" : "needs_diagnostic",
            SourceEvidenceStatus = source?.EvidenceStatus ?? sequence.SourceReadiness,
            OverclaimRisk = DetermineOverclaimRisk(diagnostic, source, student, active),
            RecommendedNextAction = diagnostic.NextAction,
            Milestones = milestones,
            RepairLoops = repairLoops,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray()
        };
    }

    private static string DetermineAdaptiveIntent(
        Topic topic,
        PlanQualityEvaluationRequestDto request,
        SourceEvidenceBundleDto? source,
        ActiveLessonSnapshotDto? active,
        string remediationNeed)
    {
        var text = NormalizeText(string.Join(" ", topic.Title, topic.Category, topic.PlanIntent, request.PlanTitle, request.PlanSummary, active?.ApprovedIntent, active?.ApprovedStudyGoal));
        if (remediationNeed is "high" or "medium" || ContainsAny(text, "telafi", "repair", "remediation", "anlamadim", "takildim")) return "remediation";
        if (source != null || ContainsAny(text, "pdf", "source", "kaynak", "dokuman", "belge")) return "source_study";
        if (ContainsAny(text, "proje", "project", "portfolio", "uygulama")) return "project_path";
        if (ContainsAny(text, "sinav", "exam", "kpss", "yks", "lgs", "ales", "yds", "deneme")) return "exam_prep";
        if (ContainsAny(text, "diagnostic", "teshis", "seviye", "placement")) return "diagnostic";
        if (ContainsAny(text, "chat", "sohbet", "casual")) return "casual";
        return string.IsNullOrWhiteSpace(topic.Title) ? "unclear" : "lesson";
    }

    private static decimal DetermineIntentConfidence(string intent, Topic topic, SourceEvidenceBundleDto? source, ActiveLessonSnapshotDto? active, StudentContextSnapshotDto? student)
    {
        var confidence = 0.45m;
        if (!string.IsNullOrWhiteSpace(topic.Title)) confidence += 0.15m;
        if (!string.IsNullOrWhiteSpace(topic.PlanIntent) || !string.IsNullOrWhiteSpace(active?.ApprovedIntent)) confidence += 0.15m;
        if (source != null && intent == "source_study") confidence += 0.15m;
        if (student?.ConfidenceStatus == "usable") confidence += 0.10m;
        return Math.Clamp(confidence, 0.20m, 0.92m);
    }

    private static AdaptiveLearnerPlacementDto BuildLearnerPlacement(
        StudentContextSnapshotDto? student,
        ActiveLessonSnapshotDto? active,
        IReadOnlyList<KnowledgeTracingState> tracing,
        IReadOnlyList<ConceptMastery> masteries,
        string remediationNeed)
    {
        var mastery = active?.MasteryProbability
            ?? tracing.OrderByDescending(t => t.UpdatedAt).Select(t => (decimal?)t.MasteryProbability).FirstOrDefault()
            ?? masteries.OrderByDescending(m => m.UpdatedAt).Select(m => (decimal?)(m.MasteryScore / 100m)).FirstOrDefault();
        var confidence = active?.Confidence
            ?? tracing.OrderByDescending(t => t.UpdatedAt).Select(t => (decimal?)t.Confidence).FirstOrDefault()
            ?? masteries.OrderByDescending(m => m.UpdatedAt).Select(m => (decimal?)m.Confidence).FirstOrDefault();
        string level;
        string basis;
        var warnings = new List<string>();
        if (mastery >= 0.72m && confidence >= 0.60m)
        {
            level = active?.LearnerState == "ready_for_challenge" ? "advanced" : "exam_ready";
            basis = "mastery_snapshot";
        }
        else if (mastery >= 0.50m && confidence >= 0.45m)
        {
            level = "developing";
            basis = "mastery_snapshot";
        }
        else if (mastery.HasValue || confidence.HasValue || student?.WeakConcepts.Count > 0 || remediationNeed is "high" or "medium")
        {
            level = "beginner";
            basis = remediationNeed is "high" or "medium" ? "weak_concept_or_repair_signal" : "limited_mastery_signal";
        }
        else
        {
            level = "unknown";
            basis = "insufficient_data";
            warnings.Add("learner_level_provisional");
        }

        return new AdaptiveLearnerPlacementDto
        {
            LearnerLevel = level,
            Confidence = Math.Clamp(confidence ?? (level == "unknown" ? 0.20m : 0.55m), 0m, 1m),
            Basis = basis,
            UserSafeLabel = level switch
            {
                "advanced" => "Guclu sinyaller var; yine de kisa kontrol korunur.",
                "exam_ready" => "Hazirlik seviyesi olumlu gorunuyor; geri cagirma kontrolu gerekir.",
                "developing" => "Gelisen seviye; plan kisa kontrol ve orneklerle ilerler.",
                "beginner" => "Temel/on kosul kontrolu ile baslamak daha guvenli.",
                _ => "Seviye icin kisa teshis onerilir."
            },
            Warnings = warnings
        };
    }

    private static IReadOnlyList<AdaptiveDiagnosticSignalDto> BuildPlacementSignals(
        StudentContextSnapshotDto? student,
        ActiveLessonSnapshotDto? active,
        IReadOnlyList<KnowledgeTracingState> tracing,
        IReadOnlyList<ConceptMastery> masteries,
        SourceEvidenceBundleDto? source,
        ConceptGraphSnapshot? graph)
    {
        var signals = new List<AdaptiveDiagnosticSignalDto>
        {
            Signal("stated_goal", string.IsNullOrWhiteSpace(active?.ApprovedStudyGoal) ? "observed_only" : "usable", string.IsNullOrWhiteSpace(active?.ApprovedStudyGoal) ? 0.25m : 0.70m, "Ogrencinin hedef ifadesi plan niyetini daraltir."),
            Signal("mastery_snapshot", tracing.Count + masteries.Count > 0 ? "usable" : "insufficient_data", tracing.Count + masteries.Count > 0 ? 0.70m : 0.20m, "Quiz ve mastery sinyali seviye tahminini destekler."),
            Signal("diagnostic_answer", student?.ConfidenceStatus == "usable" ? "usable" : "insufficient_data", student?.ConfidenceStatus == "usable" ? 0.65m : 0.20m, "Kisa teshis/ogrenci snapshot kaniti kullanilir."),
            Signal("source_context", source?.EvidenceStatus ?? "evidence_insufficient", source?.EvidenceStatus is "source_grounded" or "mixed" or "wiki_backed" ? 0.65m : 0.25m, "Kaynak hazirligi sadece kaynak destekli plan iddialarini etkiler."),
            Signal("concept_graph", graph == null ? "missing" : "usable", graph == null ? 0.20m : 0.70m, "Concept graph plan siralamasini ve prerequisite iliskilerini destekler.")
        };
        return signals;
    }

    private static AdaptiveDiagnosticSignalDto Signal(string type, string status, decimal confidence, string reason) => new()
    {
        SignalType = type,
        Status = status,
        Confidence = Math.Clamp(confidence, 0m, 1m),
        UserSafeReason = reason
    };

    private static IReadOnlyList<AdaptiveDiagnosticQuestionDto> BuildDiagnosticQuestions(
        Topic topic,
        string intent,
        string learnerLevel,
        IReadOnlyList<PlanStepContractDto> steps,
        string readiness)
    {
        var questions = new List<AdaptiveDiagnosticQuestionDto>();
        if (readiness != "ready" || learnerLevel is "unknown" or "beginner")
        {
            questions.Add(Question("goal-fit", $"Bu konuyu hangi amacla calisiyorsun: ders, sinav, proje ya da kaynak inceleme?", "intent_clarification", null, "stated_goal"));
            questions.Add(Question("starting-point", $"{topic.Title} icin kendini en cok nerede takiliyor hissediyorsun?", "placement", steps.FirstOrDefault()?.ConceptKey, "diagnostic_answer"));
        }

        var prerequisite = steps.SelectMany(s => s.PrerequisiteConceptKeys).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        if (!string.IsNullOrWhiteSpace(prerequisite) || readiness == "needs_prerequisite_check")
        {
            questions.Add(Question("prerequisite-check", "Bu plana baslamadan once temel/on kosul kavramdan mini bir kontrol yapalim mi?", "prerequisite_check", prerequisite, "diagnostic_answer"));
        }

        if (readiness == "needs_repair" || intent == "remediation")
        {
            questions.Add(Question("repair-target", "Telafi icin once hangi adimi beraber yeniden kurmamizi istersin?", "repair_focus", steps.FirstOrDefault(s => s.RemediationNeed is "high" or "medium")?.ConceptKey, "weak_concept"));
        }

        questions.Add(Question("checkpoint-preference", "Sonraki adimda kisa anlatimdan sonra tek soruluk kontrol ister misin?", "checkpoint_preference", steps.FirstOrDefault()?.ConceptKey, "diagnostic_answer", required: false));
        return questions
            .GroupBy(q => q.QuestionId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(5)
            .ToArray();
    }

    private static AdaptiveDiagnosticQuestionDto Question(string id, string prompt, string purpose, string? conceptKey, string signalType, bool required = true) => new()
    {
        QuestionId = id,
        Prompt = prompt,
        Purpose = purpose,
        TargetConceptKey = Clean(conceptKey, 120),
        SignalType = signalType,
        Required = required
    };

    private static string DeterminePlanReadiness(
        ConceptGraphSnapshot? graph,
        SourceEvidenceBundleDto? source,
        StudentContextSnapshotDto? student,
        ActiveLessonSnapshotDto? active,
        IReadOnlyList<PlanStepContractDto> steps,
        string remediationNeed)
    {
        if (steps.Count == 0) return "degraded";
        if (remediationNeed is "high" or "medium" || student?.RemediationReady.Count > 0 || active?.LearnerState.Contains("remediation", StringComparison.OrdinalIgnoreCase) == true)
            return "needs_repair";
        if (graph == null && steps.Count <= 2) return "thin_plan";
        if (student == null || student.ConfidenceStatus is "none" or "observed_only") return "needs_diagnostic";
        if (steps.Any(s => s.PrerequisiteConceptKeys.Count > 0 && s.LearnerState is "evidence_insufficient" or "unknown"))
            return "needs_prerequisite_check";
        if (source?.EvidenceStatus is "stale" or "degraded") return "source_limited";
        return "ready";
    }

    private static IReadOnlyList<CoursePlanMilestoneDto> BuildMilestones(IReadOnlyList<PlanStepContractDto> steps)
    {
        if (steps.Count == 0) return Array.Empty<CoursePlanMilestoneDto>();
        var chunkSize = Math.Clamp((int)Math.Ceiling(steps.Count / 3m), 1, 4);
        return steps
            .Select((step, index) => new { step, index })
            .GroupBy(x => x.index / chunkSize)
            .Select((group, index) =>
            {
                var groupSteps = group.Select(x => x.step).ToArray();
                return new CoursePlanMilestoneDto
                {
                    MilestoneId = $"milestone-{index + 1}",
                    Title = groupSteps.Length == 1 ? groupSteps[0].Title : $"{groupSteps[0].Title} -> {groupSteps[^1].Title}",
                    Objective = groupSteps.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Objective))?.Objective ?? "Bu bolumde kavram ve mikro kontrol birlikte ilerler.",
                    StepIds = groupSteps.Select(s => s.StepId).Where(s => !string.IsNullOrWhiteSpace(s)).Take(8).ToArray(),
                    Checkpoint = groupSteps.LastOrDefault()?.QuizHook.HookType ?? "micro_check",
                    EstimatedMinutes = groupSteps.Sum(s => Math.Clamp(s.EstimatedMinutes, 5, 90)),
                    Status = groupSteps.Any(s => s.StepId == "diagnostic-first") ? "diagnostic_first" : "planned"
                };
            })
            .Take(6)
            .ToArray();
    }

    private static IReadOnlyList<CoursePlanRepairLoopDto> BuildRepairLoops(
        IReadOnlyList<PlanStepContractDto> steps,
        StudentContextSnapshotDto? student,
        IReadOnlyList<KnowledgeTracingState> tracing,
        IReadOnlyList<ConceptMastery> masteries)
    {
        var loops = new List<CoursePlanRepairLoopDto>();
        loops.AddRange(steps
            .Where(s => s.RemediationNeed is "high" or "medium" || s.TargetMisconceptions.Count > 0)
            .Select(s => new CoursePlanRepairLoopDto
            {
                ConceptKey = s.ConceptKey,
                Label = string.IsNullOrWhiteSpace(s.ConceptLabel) ? s.Title : s.ConceptLabel,
                Trigger = s.RemediationNeed is "high" or "medium" ? "weak_concept" : "misconception_probe",
                RepairMode = s.RemediationNeed is "high" or "medium" ? "guided_repair" : "misconception_repair",
                Reason = s.RemediationNeed is "high" or "medium"
                    ? "Mastery/quiz sinyali telafi dongusu oneriyor."
                    : "Yanilgi hedefi varsa kesin tani kurmadan ayrim ornegi gerekir.",
                NextAction = "guided_repair_then_check"
            }));
        loops.AddRange(student?.RemediationReady.Select(r => new CoursePlanRepairLoopDto
        {
            ConceptKey = r.ConceptKey,
            Label = string.IsNullOrWhiteSpace(r.Label) ? r.ConceptKey : r.Label,
            Trigger = "snapshot_remediation",
            RepairMode = r.FirstAction == "prerequisite_review" ? "prerequisite_repair" : "guided_repair",
            Reason = r.Reason,
            NextAction = r.FirstAction
        }) ?? Array.Empty<CoursePlanRepairLoopDto>());
        loops.AddRange(tracing
            .Where(t => t.RemediationNeed is "high" or "medium" || t.MasteryProbability < 0.45m)
            .Select(t => new CoursePlanRepairLoopDto
            {
                ConceptKey = t.ConceptKey,
                Label = string.IsNullOrWhiteSpace(t.Label) ? t.ConceptKey : t.Label,
                Trigger = "knowledge_tracing",
                RepairMode = "guided_repair",
                Reason = "Knowledge tracing dusuk mastery sinyali verdi.",
                NextAction = "guided_repair_then_check"
            }));
        loops.AddRange(masteries
            .Where(m => m.RemediationNeed is "high" or "medium" || m.MasteryScore < 45)
            .Select(m => new CoursePlanRepairLoopDto
            {
                ConceptKey = m.ConceptKey,
                Label = string.IsNullOrWhiteSpace(m.Label) ? m.ConceptKey : m.Label,
                Trigger = "concept_mastery",
                RepairMode = "guided_repair",
                Reason = "Concept mastery dusuk oldugu icin telafi onerilir.",
                NextAction = "guided_repair_then_check"
            }));
        return loops
            .Where(l => !string.IsNullOrWhiteSpace(l.ConceptKey))
            .GroupBy(l => NormalizeKey(l.ConceptKey), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(8)
            .ToArray();
    }

    private static string DetermineOverclaimRisk(AdaptiveDiagnosticDto diagnostic, SourceEvidenceBundleDto? source, StudentContextSnapshotDto? student, ActiveLessonSnapshotDto? active)
    {
        if (diagnostic.Warnings.Count > 2 || diagnostic.PlanReadiness is "needs_diagnostic" or "thin_plan" or "degraded") return "high";
        if (source?.EvidenceStatus is "stale" or "degraded" or "evidence_insufficient" || student?.ConfidenceStatus is null or "none" or "observed_only" || active == null) return "medium";
        return "low";
    }

    private static void EvaluateAdaptivePlanQuality(PlanCurriculumSequenceDto sequence, List<PlanQualityIssueDto> warnings)
    {
        if (sequence.CoursePlanQuality.ReadinessStatus is "needs_diagnostic" or "needs_prerequisite_check" or "needs_repair" or "source_limited" or "thin_plan")
        {
            warnings.Add(Issue($"plan_{sequence.CoursePlanQuality.ReadinessStatus}", $"Plan hazirlik durumu: {sequence.CoursePlanQuality.ReadinessStatus}.", "warning"));
        }

        if (sequence.CoursePlanQuality.OverclaimRisk == "high")
        {
            warnings.Add(Issue("plan_overclaim_risk", "Plan seviyesi/iddiasi sinirli kanitla temkinli sunulmali.", "warning"));
        }
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

    private static PlanQualityEvaluationDto ToDto(LearningPlanQualitySnapshot entity)
    {
        var planContract = Parse(entity.PlanContractJson, new PlanCurriculumSequenceDto());
        return new PlanQualityEvaluationDto
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
            PlanContract = planContract,
            AdaptiveDiagnostic = planContract.AdaptiveDiagnostic,
            CoursePlanQuality = planContract.CoursePlanQuality,
            GeneratedAt = entity.CreatedAt
        };
    }

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

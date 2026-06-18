using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Korteks;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Exceptions;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;
using Orka.Infrastructure.SemanticKernel.Plugins;

namespace Orka.Infrastructure.Services;

public sealed class PlanDiagnosticService : IPlanDiagnosticService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan StateTtl = TimeSpan.FromHours(2);
    private const int MinimumPlanModules = 6;
    private const int MinimumPlanLessons = 24;
    private static readonly HashSet<string> HardPlanQualityBlockingIssueCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "unsafe_success_claim",
        "unsafe_official_claim",
        "teacher_workflow_copy",
        "internal_payload_leak",
        "plan_empty",
        "plan_too_generic",
        "step_missing_concept_or_objective",
        "step_missing_sequence_reason",
        "step_missing_quiz_hook",
        "step_missing_tutor_hook"
    };

    private readonly OrkaDbContext _db;
    private readonly IKorteksAgent _korteks;
    private readonly IPlanResearchCompressor _compressor;
    private readonly IAIAgentFactory _factory;
    private readonly IPlanDiagnosticStateStore _stateStore;
    private readonly IQuizAttemptRecorder _quizRecorder;
    private readonly IDeepPlanAgent _deepPlan;
    private readonly IConceptGraphBuilder _conceptGraphBuilder;
    private readonly IAssessmentGrammarEngine _assessmentGrammar;
    private readonly IDiagnosticProfileBuilder _diagnosticProfileBuilder;
    private readonly ILearningQualityReportService? _qualityReport;
    private readonly IActiveLessonSnapshotService? _learningSnapshots;
    private readonly IKorteksSynthesisService? _korteksSynthesis;
    private readonly IPlanSequencingService? _planSequencing;
    private readonly ILogger<PlanDiagnosticService> _logger;
    private readonly TavilySearchPlugin? _webSearch;
    private readonly WikipediaPlugin? _wikipedia;
    private readonly YouTubeTranscriptPlugin? _youtube;

    public PlanDiagnosticService(
        OrkaDbContext db,
        IKorteksAgent korteks,
        IPlanResearchCompressor compressor,
        IAIAgentFactory factory,
        IPlanDiagnosticStateStore stateStore,
        IQuizAttemptRecorder quizRecorder,
        IDeepPlanAgent deepPlan,
        IConceptGraphBuilder conceptGraphBuilder,
        IAssessmentGrammarEngine assessmentGrammar,
        IDiagnosticProfileBuilder diagnosticProfileBuilder,
        ILogger<PlanDiagnosticService> logger,
        TavilySearchPlugin? webSearch = null,
        WikipediaPlugin? wikipedia = null,
        YouTubeTranscriptPlugin? youtube = null,
        ILearningQualityReportService? qualityReport = null,
        IActiveLessonSnapshotService? learningSnapshots = null,
        IKorteksSynthesisService? korteksSynthesis = null,
        IPlanSequencingService? planSequencing = null)
    {
        _db = db;
        _korteks = korteks;
        _compressor = compressor;
        _factory = factory;
        _stateStore = stateStore;
        _quizRecorder = quizRecorder;
        _deepPlan = deepPlan;
        _conceptGraphBuilder = conceptGraphBuilder;
        _assessmentGrammar = assessmentGrammar;
        _diagnosticProfileBuilder = diagnosticProfileBuilder;
        _qualityReport = qualityReport;
        _logger = logger;
        _webSearch = webSearch;
        _wikipedia = wikipedia;
        _youtube = youtube;
        _learningSnapshots = learningSnapshots;
        _korteksSynthesis = korteksSynthesis;
        _planSequencing = planSequencing;
    }

    public async Task<StartPlanDiagnosticResponse> StartAsync(
        Guid userId,
        StartPlanDiagnosticRequest request,
        CancellationToken ct = default)
    {
        var topic = await _db.Topics.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TopicId && t.UserId == userId, ct);

        if (topic == null)
        {
            throw new InvalidOperationException("Topic not found for plan diagnostic start.");
        }

        var effectiveSessionId = await ResolvePlanSessionIdAsync(userId, request.TopicId, request.SessionId, ct);

        var approvedResearchIntent = NormalizeApprovedIntent(request.ApprovedResearchIntent);
        if (string.IsNullOrWhiteSpace(approvedResearchIntent))
        {
            throw new InvalidOperationException("Approved study intent is required before learning research.");
        }

        var approvedMainTopic = CleanOrDefault(request.ApprovedMainTopic, topic.Title);
        var approvedFocusArea = CleanOrDefault(request.ApprovedFocusArea, "genel kapsam");
        var approvedStudyGoal = CleanOrDefault(request.ApprovedStudyGoal, "öğrenme ve pratik");
        var topicTitle = BuildApprovedTopicTitle(request.TopicTitle, approvedMainTopic, approvedFocusArea, topic.Title);
        var requestedQuestionCount = DetermineDiagnosticQuestionCount(approvedMainTopic, approvedFocusArea, approvedResearchIntent);
        var planRequestId = Guid.NewGuid();
        var quizRunId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var state = new PlanDiagnosticStateDto
        {
            PlanRequestId = planRequestId,
            UserId = userId,
            TopicId = request.TopicId,
            SessionId = effectiveSessionId,
            TopicTitle = topicTitle,
            IntentRequestId = request.IntentRequestId,
            RawStudyRequest = CleanOrDefault(request.RawStudyRequest, topicTitle),
            ApprovedMainTopic = approvedMainTopic,
            ApprovedFocusArea = approvedFocusArea,
            ApprovedStudyGoal = approvedStudyGoal,
            ApprovedResearchIntent = approvedResearchIntent,
            UserLevel = string.IsNullOrWhiteSpace(request.UserLevel) ? topic.LanguageLevel ?? "Bilinmiyor" : request.UserLevel.Trim(),
            Status = PlanDiagnosticStatus.Researching,
            QuizRunId = quizRunId,
            CreatedAt = now,
            ExpiresAt = now.Add(StateTtl)
        };

        await _stateStore.SaveAsync(state, ct);
        return await CompleteStartAsync(state, ct);
    }

    public async Task<StartPlanDiagnosticResponse> StartQueuedAsync(
        Guid userId,
        StartPlanDiagnosticRequest request,
        CancellationToken ct = default)
    {
        var topic = await _db.Topics.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TopicId && t.UserId == userId, ct);

        if (topic == null)
        {
            throw new InvalidOperationException("Topic not found for plan diagnostic start.");
        }

        var effectiveSessionId = await ResolvePlanSessionIdAsync(userId, request.TopicId, request.SessionId, ct);
        var approvedResearchIntent = NormalizeApprovedIntent(request.ApprovedResearchIntent);
        if (string.IsNullOrWhiteSpace(approvedResearchIntent))
        {
            throw new InvalidOperationException("Approved study intent is required before learning research.");
        }

        var approvedMainTopic = CleanOrDefault(request.ApprovedMainTopic, topic.Title);
        var approvedFocusArea = CleanOrDefault(request.ApprovedFocusArea, "genel kapsam");
        var approvedStudyGoal = CleanOrDefault(request.ApprovedStudyGoal, "ogrenme ve pratik");
        var topicTitle = BuildApprovedTopicTitle(request.TopicTitle, approvedMainTopic, approvedFocusArea, topic.Title);
        var now = DateTime.UtcNow;

        var state = new PlanDiagnosticStateDto
        {
            PlanRequestId = Guid.NewGuid(),
            UserId = userId,
            TopicId = request.TopicId,
            SessionId = effectiveSessionId,
            TopicTitle = topicTitle,
            IntentRequestId = request.IntentRequestId,
            RawStudyRequest = CleanOrDefault(request.RawStudyRequest, topicTitle),
            ApprovedMainTopic = approvedMainTopic,
            ApprovedFocusArea = approvedFocusArea,
            ApprovedStudyGoal = approvedStudyGoal,
            ApprovedResearchIntent = approvedResearchIntent,
            UserLevel = string.IsNullOrWhiteSpace(request.UserLevel) ? topic.LanguageLevel ?? "Bilinmiyor" : request.UserLevel.Trim(),
            Status = PlanDiagnosticStatus.Researching,
            QuizRunId = Guid.NewGuid(),
            CreatedAt = now,
            ExpiresAt = now.Add(StateTtl)
        };

        await _stateStore.SaveAsync(state, ct);
        return BuildStartResponse(state, questionsJson: "[]", isAsync: true, message: "Plan diagnostic generation queued.");
    }

    public async Task RunQueuedStartAsync(
        Guid userId,
        Guid planRequestId,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, planRequestId, ct);
        if (state.Status is PlanDiagnosticStatus.QuizPending or PlanDiagnosticStatus.QuizCompleted or PlanDiagnosticStatus.PlanGenerating or PlanDiagnosticStatus.PlanGenerated)
        {
            return;
        }

        await CompleteStartAsync(state, ct);
    }

    public async Task<StartPlanDiagnosticResponse> GetStartStatusAsync(
        Guid userId,
        Guid planRequestId,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, planRequestId, ct);
        var questionsJson = state.Status is PlanDiagnosticStatus.QuizPending or PlanDiagnosticStatus.QuizCompleted or PlanDiagnosticStatus.PlanGenerating or PlanDiagnosticStatus.PlanGenerated
            ? await LoadLearnerQuizJsonAsync(userId, state, ct)
            : "[]";

        return BuildStartResponse(
            state,
            questionsJson,
            state.KorteksResearchWorkflowId.HasValue ? "completed" : "not_available",
            string.IsNullOrWhiteSpace(state.LearningBlueprintSourceConfidence) ? "low" : state.LearningBlueprintSourceConfidence,
            isAsync: true);
    }

    private async Task<StartPlanDiagnosticResponse> CompleteStartAsync(
        PlanDiagnosticStateDto state,
        CancellationToken ct)
    {
        var userId = state.UserId;
        var planRequestId = state.PlanRequestId;
        var quizRunId = state.QuizRunId;
        var effectiveSessionId = state.SessionId;
        var topicTitle = state.TopicTitle;
        var approvedMainTopic = state.ApprovedMainTopic;
        var approvedFocusArea = state.ApprovedFocusArea;
        var approvedStudyGoal = state.ApprovedStudyGoal;
        var approvedResearchIntent = state.ApprovedResearchIntent;
        var requestedQuestionCount = DetermineDiagnosticQuestionCount(approvedMainTopic, approvedFocusArea, approvedResearchIntent);
        var now = DateTime.UtcNow;
        var request = new StartPlanDiagnosticRequest
        {
            TopicId = state.TopicId,
            SessionId = effectiveSessionId,
            IntentRequestId = state.IntentRequestId,
            ApprovedMainTopic = approvedMainTopic,
            ApprovedFocusArea = approvedFocusArea,
            ApprovedStudyGoal = approvedStudyGoal,
            ApprovedResearchIntent = approvedResearchIntent,
            TopicTitle = topicTitle,
            RawStudyRequest = state.RawStudyRequest,
            UserLevel = state.UserLevel
        };

        try
        {
            var research = await BuildDirectLearningResearchAsync(
                approvedResearchIntent,
                topicTitle,
                approvedMainTopic,
                approvedFocusArea,
                request.TopicId,
                ct);
            var compressed = _compressor.Compress(research);
            KorteksResearchWorkflowDto? korteksWorkflow = null;
            if (_korteksSynthesis != null)
            {
                korteksWorkflow = await _korteksSynthesis.BuildAndSaveAsync(
                    userId,
                    research,
                    new KorteksResearchSynthesisContextDto
                    {
                        TopicId = request.TopicId,
                        SessionId = effectiveSessionId,
                        PlanRequestId = planRequestId,
                        ApprovedIntent = approvedResearchIntent,
                        ApprovedMainTopic = approvedMainTopic,
                        ApprovedFocusArea = approvedFocusArea,
                        ApprovedStudyGoal = approvedStudyGoal,
                        Purpose = "plan_diagnostic"
                    },
                    ct);
            }
            var conceptGraph = await _conceptGraphBuilder.BuildOrLoadAsync(
                userId,
                request.TopicId,
                planRequestId,
                approvedResearchIntent,
                topicTitle,
                approvedMainTopic,
                approvedFocusArea,
                compressed,
                ct);

            _db.QuizRuns.Add(new QuizRun
            {
                Id = quizRunId,
                UserId = userId,
                TopicId = request.TopicId,
                SessionId = effectiveSessionId,
                QuizType = "baseline",
                Status = "preparing",
                TotalQuestions = requestedQuestionCount,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    planRequestId,
                    intentRequestId = request.IntentRequestId,
                    approvedMainTopic,
                    approvedFocusArea,
                    approvedResearchIntent,
                    sourceCount = compressed.SourceCount
                }, JsonOptions),
                CreatedAt = now
            });
            await _db.SaveChangesAsync(ct);

            var assessmentDraft = await _assessmentGrammar.BuildOrLoadDraftAsync(
                userId,
                request.TopicId,
                planRequestId,
                quizRunId,
                conceptGraph.Graph,
                requestedQuestionCount,
                ct);
            var learningBlueprint = BuildLegacyBlueprintFromConceptGraph(
                conceptGraph.Graph,
                compressed);
            if (learningBlueprint.Concepts.Count == 0)
            {
                throw new InvalidOperationException("Concept graph did not produce a usable learning blueprint; diagnostic quiz generation is blocked instead of using a generic fallback blueprint.");
            }
            if (learningBlueprint.Concepts.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 5)
            {
                throw new InvalidOperationException("Concept graph did not produce enough measurable diagnostic concepts; quiz generation is blocked instead of accepting a shallow assessment contract.");
            }
            var effectiveQuestionCount = Math.Clamp(
                assessmentDraft.Grammar.RequestedQuestionCount > 0 ? assessmentDraft.Grammar.RequestedQuestionCount : learningBlueprint.RecommendedQuestionCount > 0 ? learningBlueprint.RecommendedQuestionCount : requestedQuestionCount,
                15,
                25);
            var learningBlueprintJson = JsonSerializer.Serialize(learningBlueprint, JsonOptions);
            var learningBlueprintHash = ComputeLegacyBlueprintHash(learningBlueprint);
            var compressedBlock = _compressor.BuildPromptBlock(compressed) +
                                  (korteksWorkflow == null ? string.Empty : "\n" + korteksWorkflow.PromptBlock) +
                                  "\n" +
                                  BuildConceptGraphPromptBlock(conceptGraph.Graph) +
                                  "\n" +
                                  _assessmentGrammar.BuildPromptBlock(assessmentDraft.Grammar) +
                                  "\n" +
                                  BuildLearningQualityPromptBlock(conceptGraph, assessmentDraft) +
                                  "\n" +
                                  BuildLegacyBlueprintPromptBlock(learningBlueprint);

            state.Status = PlanDiagnosticStatus.ResearchReady;
            state.CompressedResearchContextJson = JsonSerializer.Serialize(compressed, JsonOptions);
            state.CompressedResearchPromptBlock = compressedBlock;
            state.KorteksResearchWorkflowId = korteksWorkflow?.Id;
            state.KorteksSynthesisJson = korteksWorkflow == null ? string.Empty : JsonSerializer.Serialize(korteksWorkflow.Synthesis, JsonOptions);
            state.KorteksSynthesisPromptBlock = korteksWorkflow?.PromptBlock ?? string.Empty;
            state.LearningBlueprintJson = learningBlueprintJson;
            state.LearningBlueprintHash = learningBlueprintHash;
            state.LearningBlueprintDomain = learningBlueprint.Domain;
            state.LearningBlueprintSourceConfidence = learningBlueprint.SourceConfidence;
            state.ConceptGraphSnapshotId = conceptGraph.SnapshotId;
            state.ConceptGraphJson = JsonSerializer.Serialize(conceptGraph.Graph, JsonOptions);
            state.ConceptGraphHash = conceptGraph.Graph.IntentHash;
            state.ConceptGraphCacheKey = conceptGraph.CacheKey;
            state.ConceptGraphQualityRunId = conceptGraph.QualityRunId;
            state.ConceptGraphQualityStatus = conceptGraph.QualityStatus;
            state.SourceBundleHash = conceptGraph.Graph.SourceBundleHash;
            state.SourceBundleCacheKey = conceptGraph.SourceBundleCacheKey;
            state.AssessmentDraftId = assessmentDraft.DraftId;
            state.AssessmentGrammarJson = JsonSerializer.Serialize(assessmentDraft.Grammar, JsonOptions);
            state.AssessmentDraftCacheKey = assessmentDraft.CacheKey;
            state.AssessmentQualityRunId = assessmentDraft.QualityRunId;
            state.AssessmentQualityStatus = assessmentDraft.QualityStatus;
            state.GroundingMode = compressed.GroundingMode;
            state.SourceCount = compressed.SourceCount;
            await _stateStore.SaveAsync(state, ct);

            var quizJson = await GenerateDiagnosticQuizFromStoredContextAsync(
                topicTitle,
                compressedBlock,
                learningBlueprint,
                assessmentDraft.Grammar,
                effectiveQuestionCount,
                ct);
            var questionCount = CountQuestions(quizJson);
            await PersistGeneratedQuestionJsonAsync(userId, planRequestId, quizJson, ct);
            var learnerQuizJson = StripAnswerKeysForLearner(quizJson);

            var quizRun = await _db.QuizRuns.FirstAsync(q => q.Id == quizRunId && q.UserId == userId, ct);
            quizRun.Status = "active";
            quizRun.TotalQuestions = questionCount;
            quizRun.MetadataJson = JsonSerializer.Serialize(new
            {
                planRequestId,
                intentRequestId = request.IntentRequestId,
                approvedMainTopic,
                approvedFocusArea,
                approvedResearchIntent,
                blueprintDomain = learningBlueprint.Domain,
                blueprintConfidence = learningBlueprint.SourceConfidence,
                blueprintHash = learningBlueprintHash,
                sourceCount = compressed.SourceCount
            }, JsonOptions);
            await _db.SaveChangesAsync(ct);
            var persistedAssessmentItems = await _db.AssessmentItems
                .Where(item => item.UserId == userId && item.PlanRequestId == planRequestId)
                .ToListAsync(ct);
            foreach (var item in persistedAssessmentItems)
            {
                item.QuizRunId = quizRunId;
            }
            await _db.SaveChangesAsync(ct);

            state.Status = PlanDiagnosticStatus.QuizPending;
            state.QuizQuestionCount = questionCount;
            if (_qualityReport != null)
            {
                var report = await _qualityReport.BuildTopicReportAsync(userId, request.TopicId, planRequestId, ct);
                state.QualityReportId = report.Id;
            }
            await _stateStore.SaveAsync(state, ct);

            await RefreshLearningSnapshotsAsync(userId, state, ct);

            return BuildStartResponse(
                state,
                learnerQuizJson,
                korteksWorkflow?.Status ?? "not_available",
                korteksWorkflow?.SourceConfidence ?? "low");
        }
        catch (Exception ex)
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = ex.Message;
            await _stateStore.SaveAsync(state, ct);
            _logger.LogWarning(ex, "[PlanDiagnostic] Start failed. PlanRequestRef={PlanRequestRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(planRequestId, "plan"),
                LogPrivacyGuard.SafeExceptionType(ex));
            throw;
        }
    }

    private async Task<string> LoadLearnerQuizJsonAsync(Guid userId, PlanDiagnosticStateDto state, CancellationToken ct)
    {
        var items = await _db.AssessmentItems.AsNoTracking()
            .Where(item => item.UserId == userId &&
                           item.QuizRunId == state.QuizRunId &&
                           item.GeneratedQuestionJson != null &&
                           item.GeneratedQuestionJson != string.Empty)
            .OrderBy(item => item.Order)
            .Select(item => item.GeneratedQuestionJson!)
            .ToListAsync(ct);

        if (items.Count == 0)
        {
            return "[]";
        }

        var array = new JsonArray();
        foreach (var item in items)
        {
            try
            {
                var node = JsonNode.Parse(item);
                if (node != null)
                {
                    array.Add(node);
                }
            }
            catch (JsonException)
            {
                // A single malformed persisted item should not leak raw JSON to the learner.
            }
        }

        return StripAnswerKeysForLearner(array.ToJsonString(JsonOptions));
    }

    private async Task PersistGeneratedQuestionJsonAsync(
        Guid userId,
        Guid planRequestId,
        string quizJson,
        CancellationToken ct)
    {
        var array = JsonNode.Parse(DiagnosticQuizQualityGate.ExtractJsonArray(quizJson)) as JsonArray;
        if (array == null)
        {
            return;
        }

        var entitiesById = await _db.AssessmentItems
            .Where(item => item.UserId == userId && item.PlanRequestId == planRequestId)
            .ToDictionaryAsync(item => item.Id, ct);
        foreach (var question in array.OfType<JsonObject>())
        {
            if (!Guid.TryParse(question["assessmentItemId"]?.GetValue<string>(), out var id) ||
                !entitiesById.TryGetValue(id, out var entity))
            {
                continue;
            }

            entity.GeneratedQuestionJson = question.ToJsonString(JsonOptions);
        }

        await _db.SaveChangesAsync(ct);
    }

    private static StartPlanDiagnosticResponse BuildStartResponse(
        PlanDiagnosticStateDto state,
        string questionsJson,
        string korteksSynthesisStatus = "not_available",
        string korteksSourceConfidence = "low",
        bool isAsync = false,
        string? message = null)
    {
        var isReady = state.Status is PlanDiagnosticStatus.QuizPending or
            PlanDiagnosticStatus.QuizCompleted or
            PlanDiagnosticStatus.PlanGenerating or
            PlanDiagnosticStatus.PlanGenerated;

        return new StartPlanDiagnosticResponse
        {
            PlanRequestId = state.PlanRequestId,
            QuizRunId = state.QuizRunId,
            TopicId = state.TopicId,
            TopicTitle = state.TopicTitle,
            Status = state.Status,
            QuestionsJson = questionsJson,
            GroundingMode = state.GroundingMode,
            SourceCount = state.SourceCount,
            ConceptGraphSnapshotId = state.ConceptGraphSnapshotId,
            AssessmentDraftId = state.AssessmentDraftId,
            ConceptGraphQualityStatus = state.ConceptGraphQualityStatus,
            AssessmentQualityStatus = state.AssessmentQualityStatus,
            QualityReportId = state.QualityReportId,
            SourceBundleHash = state.SourceBundleHash,
            SourceBundleCacheKey = state.SourceBundleCacheKey,
            KorteksResearchWorkflowId = state.KorteksResearchWorkflowId,
            KorteksSynthesisStatus = korteksSynthesisStatus,
            KorteksSourceConfidence = korteksSourceConfidence,
            QuizQuestionCount = state.QuizQuestionCount,
            IntentRequestId = state.IntentRequestId,
            ApprovedMainTopic = state.ApprovedMainTopic,
            ApprovedFocusArea = state.ApprovedFocusArea,
            ApprovedStudyGoal = state.ApprovedStudyGoal,
            ApprovedResearchIntent = state.ApprovedResearchIntent,
            IsAsync = isAsync,
            IsReady = isReady,
            Message = message,
            ErrorMessage = state.Status == PlanDiagnosticStatus.Failed ? state.ErrorMessage : null
        };
    }

    private async Task<Guid?> ResolvePlanSessionIdAsync(
        Guid userId,
        Guid topicId,
        Guid? requestedSessionId,
        CancellationToken ct)
    {
        if (!requestedSessionId.HasValue)
        {
            return null;
        }

        var belongsToTopic = await _db.Sessions.AsNoTracking()
            .AnyAsync(s =>
                s.Id == requestedSessionId.Value &&
                s.UserId == userId &&
                s.TopicId == topicId,
                ct);

        if (belongsToTopic)
        {
            return requestedSessionId;
        }

        _logger.LogWarning(
            "[PlanDiagnostic] Ignoring stale session for plan start. UserRef={UserRef} TopicRef={TopicRef} SessionRef={SessionRef}",
            LogPrivacyGuard.SafeId(userId, "usr"),
            LogPrivacyGuard.SafeId(topicId, "topic"),
            LogPrivacyGuard.SafeId(requestedSessionId.Value, "session"));

        return null;
    }

    public async Task<PlanDiagnosticAnswerResponse> RecordAnswerAsync(
        Guid userId,
        Guid planRequestId,
        RecordQuizAttemptRequest request,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, planRequestId, ct);
        request.QuizRunId = state.QuizRunId;
        request.TopicId = state.TopicId;
        request.SessionId ??= state.SessionId;
        await EnrichAttemptRequestFromAssessmentAsync(state, request, ct);
        if (!request.AssessmentItemId.HasValue)
        {
            throw new InvalidOperationException("Plan diagnostic answers require a server-issued assessmentItemId.");
        }

        var recordResult = await _quizRecorder.RecordAsync(userId, request, ct);

        state.AnsweredQuestionCount = await _db.QuizAttempts
            .CountAsync(a => a.UserId == userId && a.QuizRunId == state.QuizRunId, ct);

        if (state.QuizQuestionCount > 0 && state.AnsweredQuestionCount >= state.QuizQuestionCount)
        {
            state.Status = PlanDiagnosticStatus.QuizCompleted;
            state.QuizCompletedAt ??= DateTime.UtcNow;
        }

        await _stateStore.SaveAsync(state, ct);
        await RefreshLearningSnapshotsAsync(userId, state, ct);

        return new PlanDiagnosticAnswerResponse
        {
            PlanRequestId = state.PlanRequestId,
            QuizRunId = state.QuizRunId,
            Status = state.Status,
            AnsweredQuestionCount = state.AnsweredQuestionCount,
            QuizQuestionCount = state.QuizQuestionCount,
            LearningImpact = recordResult.LearningImpact
        };
    }

    public async Task<FinalizePlanDiagnosticResponse> FinalizeAsync(
        Guid userId,
        FinalizePlanDiagnosticRequest request,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, request.PlanRequestId, ct);

        if (state.Status == PlanDiagnosticStatus.PlanGenerated && state.GeneratedPlanRootTopicId.HasValue)
        {
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = true,
                Message = "Plan was already generated.",
                GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
                GeneratedTopicIds = await GetGeneratedPlanTopicIdsAsync(userId, state.GeneratedPlanRootTopicId.Value, ct),
                PlanQuality = await GetLatestPlanQualityAsync(userId, state, ct)
            };
        }

        state.AnsweredQuestionCount = await _db.QuizAttempts
            .CountAsync(a => a.UserId == userId && a.QuizRunId == state.QuizRunId, ct);

        if (state.QuizQuestionCount <= 0 || state.AnsweredQuestionCount < state.QuizQuestionCount)
        {
            state.Status = state.AnsweredQuestionCount > 0 ? PlanDiagnosticStatus.QuizPending : state.Status;
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = "Diagnostic quiz is incomplete."
            };
        }

        var existingPlan = await TryCompleteExistingMaterializedPlanAsync(userId, state, null, ct);
        if (existingPlan != null)
        {
            return existingPlan;
        }

        state.Status = PlanDiagnosticStatus.PlanGenerating;
        await _stateStore.SaveAsync(state, ct);

        var attempts = await LoadDiagnosticAttemptsAsync(userId, state.QuizRunId, ct);
        var diagnosticProfile = await _diagnosticProfileBuilder.BuildAndSaveAsync(state, attempts, ct);
        state.DiagnosticProfileId = diagnosticProfile.Id;
        await _stateStore.SaveAsync(state, ct);
        var diagnosticSummary = await BuildCurrentDiagnosticSummaryAsync(userId, state.QuizRunId, ct) +
                                "\n" +
                                _diagnosticProfileBuilder.BuildPromptBlock(diagnosticProfile);
        DeepPlanGenerationWithGroundingResultDto planResult;
        try
        {
            planResult = await _deepPlan.GenerateAndSaveDeepPlanFromDiagnosticAsync(
                state.TopicId,
                state.TopicTitle,
                userId,
                state.CompressedResearchPromptBlock,
                diagnosticSummary,
                state.UserLevel);
        }
        catch (InvalidOperationException ex)
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = ex.Message;
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = state.ErrorMessage,
                GeneratedPlanRootTopicId = state.TopicId,
                GeneratedTopicIds = []
            };
        }

        var materialization = await ValidatePlanMaterializationAsync(userId, state.TopicId, ct);
        if (!materialization.IsValid)
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = "Generated plan has no valid module/lesson hierarchy.";
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = state.ErrorMessage,
                GeneratedPlanRootTopicId = state.TopicId,
                GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList()
            };
        }

        var professionalGate = ValidateProfessionalPlanContract(materialization, state);
        if (!professionalGate.IsValid)
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = professionalGate.Message;
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = state.ErrorMessage,
                GeneratedPlanRootTopicId = state.TopicId,
                GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList()
            };
        }

        await EnsureMinimumWikiMaterialAsync(userId, state.TopicId, state.TopicTitle, materialization, ct);

        await RefreshLearningSnapshotsAsync(userId, state, ct);
        var planQuality = await EvaluatePlanQualityAsync(userId, state, materialization.Lessons, ct);
        if (PlanQualityBlocksGeneration(planQuality))
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = "Generated plan failed professional plan quality gate.";
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = state.ErrorMessage,
                GeneratedPlanRootTopicId = state.TopicId,
                GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList(),
                PlanQuality = planQuality
            };
        }

        state.Status = PlanDiagnosticStatus.PlanGenerated;
        state.GeneratedPlanRootTopicId = state.TopicId;
        await _stateStore.SaveAsync(state, ct);

        return new FinalizePlanDiagnosticResponse
        {
            PlanRequestId = state.PlanRequestId,
            Status = state.Status,
            PlanGenerated = true,
            GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
            GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList(),
            PlanQuality = planQuality
        };
    }

    public async Task<FinalizePlanDiagnosticResponse> SkipAndGenerateAsync(
        Guid userId,
        Guid planRequestId,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, planRequestId, ct);

        state.AnsweredQuestionCount = await _db.QuizAttempts
            .CountAsync(a => a.UserId == userId && a.QuizRunId == state.QuizRunId, ct);

        if (state.Status == PlanDiagnosticStatus.PlanGenerated && state.GeneratedPlanRootTopicId.HasValue)
        {
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = true,
                Message = "Plan was already generated.",
                GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
                GeneratedTopicIds = await GetGeneratedPlanTopicIdsAsync(userId, state.GeneratedPlanRootTopicId.Value, ct),
                PlanQuality = await GetLatestPlanQualityAsync(userId, state, ct)
            };
        }

        var existingPlan = await TryCompleteExistingMaterializedPlanAsync(
            userId,
            state,
            "Diagnostic quiz skipped; beginner plan generated without fake quiz mistakes.",
            ct);
        if (existingPlan != null)
        {
            return existingPlan;
        }

        state.Status = PlanDiagnosticStatus.PlanGenerating;
        state.QuizCompletedAt ??= DateTime.UtcNow;
        await _stateStore.SaveAsync(state, ct);

        var diagnosticSummary = string.Join("\n", new[]
        {
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]",
            "Mode: StartFromZero",
            $"QuizRunId: {state.QuizRunId}",
            $"Answered: {state.AnsweredQuestionCount}",
            "Correct: 0",
            "Wrong: 0",
            "WeakConcepts: none",
            "MistakePatterns: none",
            "Instruction: The learner explicitly skipped the diagnostic quiz and chose to start from zero. Build a beginner-safe plan, but do not infer weak skills or record fake mistakes from skipped questions."
        });

        DeepPlanGenerationWithGroundingResultDto planResult;
        try
        {
            planResult = await _deepPlan.GenerateAndSaveDeepPlanFromDiagnosticAsync(
                state.TopicId,
                state.TopicTitle,
                userId,
                state.CompressedResearchPromptBlock,
                diagnosticSummary,
                state.UserLevel);
        }
        catch (InvalidOperationException ex)
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = ex.Message;
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = state.ErrorMessage,
                GeneratedPlanRootTopicId = state.TopicId,
                GeneratedTopicIds = []
            };
        }

        var materialization = await ValidatePlanMaterializationAsync(userId, state.TopicId, ct);
        if (!materialization.IsValid)
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = "Generated plan has no valid module/lesson hierarchy.";
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = state.ErrorMessage,
                GeneratedPlanRootTopicId = state.TopicId,
                GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList()
            };
        }

        var professionalGate = ValidateProfessionalPlanContract(materialization, state);
        if (!professionalGate.IsValid)
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = professionalGate.Message;
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = state.ErrorMessage,
                GeneratedPlanRootTopicId = state.TopicId,
                GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList()
            };
        }

        await EnsureMinimumWikiMaterialAsync(userId, state.TopicId, state.TopicTitle, materialization, ct);

        await RefreshLearningSnapshotsAsync(userId, state, ct);
        var planQuality = await EvaluatePlanQualityAsync(userId, state, materialization.Lessons, ct);
        if (PlanQualityBlocksGeneration(planQuality))
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = "Generated plan failed professional plan quality gate.";
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = state.ErrorMessage,
                GeneratedPlanRootTopicId = state.TopicId,
                GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList(),
                PlanQuality = planQuality
            };
        }

        state.Status = PlanDiagnosticStatus.PlanGenerated;
        state.GeneratedPlanRootTopicId = state.TopicId;
        await _stateStore.SaveAsync(state, ct);

        return new FinalizePlanDiagnosticResponse
        {
            PlanRequestId = state.PlanRequestId,
            Status = state.Status,
            PlanGenerated = true,
            Message = "Diagnostic quiz skipped; beginner plan generated without fake quiz mistakes.",
            GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
            GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList(),
            PlanQuality = planQuality
        };
    }

    private async Task<FinalizePlanDiagnosticResponse?> TryCompleteExistingMaterializedPlanAsync(
        Guid userId,
        PlanDiagnosticStateDto state,
        string? successMessage,
        CancellationToken ct)
    {
        var materialization = await ValidatePlanMaterializationAsync(userId, state.TopicId, ct);
        if (!materialization.IsValid)
        {
            return null;
        }

        var professionalGate = ValidateProfessionalPlanContract(materialization, state);
        if (!professionalGate.IsValid)
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = professionalGate.Message;
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = state.ErrorMessage,
                GeneratedPlanRootTopicId = state.TopicId,
                GeneratedTopicIds = materialization.Modules.Concat(materialization.Lessons).Select(t => t.Id).ToList()
            };
        }

        await EnsureMinimumWikiMaterialAsync(userId, state.TopicId, state.TopicTitle, materialization, ct);
        await RefreshLearningSnapshotsAsync(userId, state, ct);
        var planQuality = await EvaluatePlanQualityAsync(userId, state, materialization.Lessons, ct);
        if (PlanQualityBlocksGeneration(planQuality))
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = "Generated plan failed professional plan quality gate.";
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = state.ErrorMessage,
                GeneratedPlanRootTopicId = state.TopicId,
                GeneratedTopicIds = materialization.Modules.Concat(materialization.Lessons).Select(t => t.Id).ToList(),
                PlanQuality = planQuality
            };
        }

        state.Status = PlanDiagnosticStatus.PlanGenerated;
        state.GeneratedPlanRootTopicId = state.TopicId;
        state.ErrorMessage = null;
        await _stateStore.SaveAsync(state, ct);

        return new FinalizePlanDiagnosticResponse
        {
            PlanRequestId = state.PlanRequestId,
            Status = state.Status,
            PlanGenerated = true,
            Message = successMessage ?? "Plan was already materialized; generation was not repeated.",
            GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
            GeneratedTopicIds = materialization.Modules.Concat(materialization.Lessons).Select(t => t.Id).ToList(),
            PlanQuality = planQuality
        };
    }

    private async Task<string> GenerateDiagnosticQuizFromStoredContextAsync(
        string topicTitle,
        string compressedResearchPromptBlock,
        LearningBlueprintDto learningBlueprint,
        AssessmentGrammarDto assessmentGrammar,
        int requestedQuestionCount,
        CancellationToken ct)
    {
        var quizIntelligenceBrief = PlanIntelligenceBriefBuilder.BuildForDiagnosticQuiz(
            topicTitle,
            compressedResearchPromptBlock);

        var systemPrompt = $$"""
            Sen profesyonel bir 'Egitim Tanilama Uzmani' botusun.
            Gorevin: '{{topicTitle}}' konusunda {{requestedQuestionCount}} soruluk seviye tespit quiz'i uretmek.

            {{quizIntelligenceBrief}}

            KURALLAR:
            - Soru sayisi tam olarak {{requestedQuestionCount}} olacak; 15'ten az, 25'ten fazla olmayacak.
            - Her soru [ASSESSMENT GRAMMAR] icindeki item spec'lerden biriyle eslesmeli.
            - Her soru assessmentItemId, assessmentItemKey, conceptKey, cognitiveSkill, misconceptionTarget, evidenceExpected, scoringRule ve learningOutcomeIds alanlarini tasimali.
            - Ham arastirma raporu varsayma; sadece bu filtrelenmis brief'i ve konu basligini kullan.
            - Kaynak basliklarini veya web/video metinlerini soru kokune kopyalama.
            - Sorular onayli konu ve odak alaninda kalmali; baska teknoloji, sinav veya alan sizdirmamali.
            - Generic pipeline, "input -> transform", "tani sorusu" gibi ic sistem kalibi kullanma.
            - Orka IDE, sandbox veya urun arayuzu etiketlerini soru kokune, seceneklere ya da dogru cevaba yazma; quiz kavrami olcer, urun ozelligini degil.
            - conceptual, procedural, application, analysis ve misconception_probe soru tiplerini karisik kullan.
            - kolay, orta ve zor dagilimini dengeli kur.
            - Soru metinleri birbirinin kopyasi veya yakin tekrari olmasin.
            - En az 8 farkli conceptTag kullan.
            - En az 4 farkli questionType kullan.
            - En az 5 soru beklenen kavram yanilgisini hedeflesin.
            - En az %30 soru misconception_probe olmali; misconceptionTarget spesifik yanilgi slug'i olmali, CommonMistakes gibi genel etiket kullanma.
            - Dogru secenek konumu dengeli dagilmali; dogru cevabi her zaman ilk secenek yapma.
            - Her yanlis secenek option-level diagnostic signal tasimali: rationale ve misconceptionKey bos olmayacak.
            - Teknik konularda en az bir soru gercek kod parcasi, kod okuma veya hata ayiklama senaryosu icersin.
            - Her soru su alanlari icersin: question, options, correctAnswer, explanation, skillTag, difficulty, conceptTag, learningObjective, questionType, expectedMisconceptionCategory.
            - Yeni mimari alanlari eksik kalmamali: assessmentItemId, conceptKey, cognitiveSkill, misconceptionTarget, evidenceExpected, scoringRule, learningOutcomeIds.
            - Her soru objesi su JSON sekline birebir uymali:
              {
                "type": "multiple_choice",
                "assessmentItemId": "[ASSESSMENT GRAMMAR icindeki exact Guid]",
                "assessmentItemKey": "[ASSESSMENT GRAMMAR icindeki exact key]",
                "conceptKey": "[ASSESSMENT GRAMMAR icindeki exact conceptKey]",
                "cognitiveSkill": "conceptual|procedural|application|analysis|misconception_probe",
                "misconceptionTarget": "spesifik-yanilgi-slug veya evidence_insufficient",
                "evidenceExpected": "olculecek kanit",
                "scoringRule": "selected_option_exact_match",
                "learningOutcomeIds": ["outcome-key"],
                "question": "soru kok metni",
                "options": [
                  { "text": "secenek", "isCorrect": true, "rationale": "neden dogru", "misconceptionKey": "" },
                  { "text": "secenek", "isCorrect": false, "rationale": "hangi yanilgiyi gosterir", "misconceptionKey": "misconception-key" },
                  { "text": "secenek", "isCorrect": false, "rationale": "hangi yanilgiyi gosterir", "misconceptionKey": "misconception-key" },
                  { "text": "secenek", "isCorrect": false, "rationale": "hangi yanilgiyi gosterir", "misconceptionKey": "misconception-key" }
                ],
                "correctAnswer": "dogru secenek metni",
                "explanation": "kisa egitici aciklama",
                "skillTag": "[conceptKey ile ayni]",
                "difficulty": "kolay|orta|zor",
                "conceptTag": "[conceptKey ile ayni]",
                "learningObjective": "olculebilir hedef",
                "questionType": "conceptual|procedural|application|analysis|misconception_probe",
                "expectedMisconceptionCategory": "Conceptual|Procedural|Careless|EvidenceInsufficient"
              }
            - JSON array disinda tek karakter yazma.

            SADECE JSON array dondur.
            """;

        Exception? lastFailure = null;
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var rawQuiz = await GenerateDiagnosticQuizInBatchesAsync(
                    systemPrompt,
                    topicTitle,
                    requestedQuestionCount,
                    assessmentGrammar,
                    attempt,
                    lastFailure,
                    ct);

                var candidateQuiz = NormalizeDiagnosticQuizForDelivery(rawQuiz);
                candidateQuiz = DiagnosticQuizQualityGate.EnsureQualityOrThrow(candidateQuiz, topicTitle, requestedQuestionCount, out var quality, learningBlueprint);
                DiagnosticQuizQualityGate.EnsureAssessmentMetadataOrThrow(candidateQuiz, requestedQuestionCount);

                var validatedQuiz = NormalizeDiagnosticQuizForDelivery(
                    await _assessmentGrammar.AttachQuestionMetadataAsync(candidateQuiz, assessmentGrammar, ct));
                DiagnosticQuizQualityGate.EnsureAssessmentMetadataOrThrow(validatedQuiz, requestedQuestionCount);
                if (!quality.IsAcceptable)
                {
                    _logger.LogWarning(
                        "[PlanDiagnostic] Diagnostic quiz quality failed. TopicRef={TopicRef} FailureCount={FailureCount}",
                        LogPrivacyGuard.SafeTextRef(topicTitle, "topic"),
                        quality.Failures.Count);
                }

                return validatedQuiz;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                lastFailure = ex;
                if (attempt >= maxAttempts || IsProviderInfrastructureFailure(ex))
                {
                    _logger.LogWarning(
                        "[PlanDiagnostic] Diagnostic quiz provider unavailable; falling back to deterministic assessment blueprint. TopicRef={TopicRef} Attempt={Attempt}/{MaxAttempts} ErrorType={ErrorType}",
                        LogPrivacyGuard.SafeTextRef(topicTitle, "topic"),
                        attempt,
                        maxAttempts,
                        LogPrivacyGuard.SafeExceptionType(ex));
                    break;
                }

                _logger.LogWarning(
                    "[PlanDiagnostic] Diagnostic quiz contract attempt failed; retrying with strict regeneration. TopicRef={TopicRef} Attempt={Attempt}/{MaxAttempts} ErrorType={ErrorType} Error={Error}",
                    LogPrivacyGuard.SafeTextRef(topicTitle, "topic"),
                    attempt,
                    maxAttempts,
                    LogPrivacyGuard.SafeExceptionType(ex),
                    LogPrivacyGuard.SafeTextRef(ex.Message, "err"));
            }
        }

        if (lastFailure != null)
        {
            _logger.LogWarning(
                IsDuplicateQualityFailure(lastFailure)
                    ? "[PlanDiagnostic] Diagnostic quiz duplicate repair fallback activated. TopicRef={TopicRef}"
                    : "[PlanDiagnostic] Diagnostic quiz deterministic fallback activated. TopicRef={TopicRef}",
                LogPrivacyGuard.SafeTextRef(topicTitle, "topic"));

            return await BuildFallbackDiagnosticQuizAsync(
                topicTitle,
                assessmentGrammar,
                requestedQuestionCount,
                learningBlueprint,
                ct);
        }

        try
        {
            throw lastFailure ?? new InvalidOperationException("Diagnostic quiz provider returned no usable output.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[PlanDiagnostic] Diagnostic quiz provider failed or returned unusable output. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeTextRef(topicTitle, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));

            throw new InvalidOperationException($"Diagnostic quiz provider did not produce a valid assessment contract: {ex.Message}", ex);
        }
    }

    private async Task<string> GenerateDiagnosticQuizInBatchesAsync(
        string systemPrompt,
        string topicTitle,
        int requestedQuestionCount,
        AssessmentGrammarDto assessmentGrammar,
        int attempt,
        Exception? previousFailure,
        CancellationToken ct)
    {
        var specs = assessmentGrammar.Items
            .OrderBy(item => item.Order)
            .Take(requestedQuestionCount)
            .ToList();
        if (specs.Count != requestedQuestionCount)
        {
            throw new InvalidOperationException($"Assessment grammar item count {specs.Count}/{requestedQuestionCount}.");
        }

        const int batchSize = 5;
        const int maxConcurrentBatches = 1;
        var batches = specs
            .Chunk(batchSize)
            .Select((batch, index) => new
            {
                Index = index + 1,
                Items = batch.ToList()
            })
            .ToList();

        using var batchGate = new SemaphoreSlim(maxConcurrentBatches);
        var batchTasks = batches.Select(async batch =>
        {
            await batchGate.WaitAsync(ct);
            try
            {
                var questions = await GenerateValidatedDiagnosticBatchAsync(
                    systemPrompt,
                    topicTitle,
                    requestedQuestionCount,
                    batch.Items,
                    attempt,
                    batch.Index,
                    previousFailure,
                    ct);
                return new DiagnosticBatchResult(batch.Index, questions);
            }
            finally
            {
                batchGate.Release();
            }
        }).ToArray();

        var completedBatches = await Task.WhenAll(batchTasks);

        var merged = new JsonArray();
        foreach (var batch in completedBatches.OrderBy(batch => batch.Index))
        {
            foreach (var question in batch.Questions)
            {
                merged.Add(question);
            }
        }

        if (merged.Count != requestedQuestionCount)
        {
            throw new InvalidOperationException($"Batched diagnostic quiz question count mismatch: {merged.Count}/{requestedQuestionCount}.");
        }

        return merged.ToJsonString(JsonOptions);
    }

    private static bool IsDuplicateQualityFailure(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception.Message.Contains("Duplicate or near-duplicate questions", StringComparison.OrdinalIgnoreCase))
                return true;

            exception = exception.InnerException;
        }

        return false;
    }

    private sealed record DiagnosticBatchResult(int Index, IReadOnlyList<JsonNode> Questions);

    private async Task<IReadOnlyList<JsonNode>> GenerateValidatedDiagnosticBatchAsync(
        string systemPrompt,
        string topicTitle,
        int requestedQuestionCount,
        IReadOnlyList<AssessmentItemSpecDto> batchItems,
        int quizAttempt,
        int batchIndex,
        Exception? previousQuizFailure,
        CancellationToken ct)
    {
        const int maxBatchAttempts = 2;
        Exception? lastBatchFailure = previousQuizFailure;
        for (var batchAttempt = 1; batchAttempt <= maxBatchAttempts; batchAttempt++)
        {
            var userMessage = BuildDiagnosticBatchPrompt(
                topicTitle,
                requestedQuestionCount,
                batchItems,
                quizAttempt,
                batchAttempt,
                lastBatchFailure);

            try
            {
                var rawBatch = await _factory.CompleteChatAsync(AgentRole.Quiz, systemPrompt, userMessage, ct);
                return ParseValidatedBatch(rawBatch, batchItems);
            }
            catch (Exception ex) when ((ex is not OperationCanceledException || !ct.IsCancellationRequested) &&
                                       batchAttempt < maxBatchAttempts &&
                                       !IsProviderInfrastructureFailure(ex))
            {
                lastBatchFailure = ex;
                _logger.LogWarning(
                    "[PlanDiagnostic] Diagnostic quiz batch contract failed; retrying only failed batch. TopicRef={TopicRef} Batch={BatchIndex} Attempt={Attempt}/{MaxAttempts} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeTextRef(topicTitle, "topic"),
                    batchIndex,
                    batchAttempt,
                    maxBatchAttempts,
                    LogPrivacyGuard.SafeExceptionType(ex));
            }
        }

        throw lastBatchFailure ?? new InvalidOperationException("Diagnostic quiz batch provider returned no usable output.");
    }

    private async Task<string> BuildFallbackDiagnosticQuizAsync(
        string topicTitle,
        AssessmentGrammarDto assessmentGrammar,
        int requestedQuestionCount,
        LearningBlueprintDto learningBlueprint,
        CancellationToken ct)
    {
        var fallbackQuiz = NormalizeDiagnosticQuizForDelivery(
            DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint(topicTitle, assessmentGrammar));
        fallbackQuiz = DiagnosticQuizQualityGate.EnsureQualityOrThrow(
            fallbackQuiz,
            topicTitle,
            requestedQuestionCount,
            out _,
            learningBlueprint);
        fallbackQuiz = NormalizeDiagnosticQuizForDelivery(
            await _assessmentGrammar.AttachQuestionMetadataAsync(fallbackQuiz, assessmentGrammar, ct));
        DiagnosticQuizQualityGate.EnsureAssessmentMetadataOrThrow(fallbackQuiz, requestedQuestionCount);
        return fallbackQuiz;
    }

    private static bool IsProviderInfrastructureFailure(Exception ex)
    {
        if (ex is AiProviderCallException)
        {
            return true;
        }

        return ex.InnerException is not null && IsProviderInfrastructureFailure(ex.InnerException);
    }

    private static string BuildDiagnosticBatchPrompt(
        string topicTitle,
        int requestedQuestionCount,
        IReadOnlyList<AssessmentItemSpecDto> batchItems,
        int quizAttempt,
        int batchAttempt,
        Exception? previousFailure)
    {
        var sb = new StringBuilder();
        if (quizAttempt > 1 || batchAttempt > 1)
        {
            sb.AppendLine("Onceki provider cikti contract validation'dan gecmedi; sadece bu batch'i bastan ve daha disiplinli uret.");
            sb.AppendLine($"Hata ozeti: {previousFailure?.Message}");
            sb.AppendLine();
        }

        sb.AppendLine($"Konu: \"{topicTitle}\".");
        sb.AppendLine($"Toplam quiz {requestedQuestionCount} soru olacak; bu cagri sadece asagidaki {batchItems.Count} exact item icin {batchItems.Count} soru uretecek.");
        sb.AppendLine("Bu batch disindaki assessmentItemId'leri kullanma, fazladan soru ekleme, eksik soru birakma.");
        sb.AppendLine("Her soru assessmentItemId, assessmentItemKey, conceptKey, cognitiveSkill, difficulty, misconceptionTarget, evidenceExpected, scoringRule ve learningOutcomeIds alanlarini exact spec'ten tasiyacak.");
        sb.AppendLine("questionType cognitiveSkill ile ayni olacak; skillTag ve conceptTag conceptKey ile ayni olacak.");
        sb.AppendLine("Her soruda 4 option olacak; yanlis option'larda rationale ve misconceptionKey bos olmayacak.");
        sb.AppendLine("Dogru secenek konumunu bu batch icinde dengeli dagit; dogru cevabi surekli ilk option yapma.");
        sb.AppendLine("misconception_probe item'larda misconceptionTarget CommonMistakes gibi genel etiket degil, spesifik yanilgi slug'i olacak.");
        sb.AppendLine("SADECE JSON array dondur.");
        sb.AppendLine();
        sb.AppendLine("[BATCH ASSESSMENT ITEMS]");
        foreach (var item in batchItems)
        {
            sb.AppendLine($"- assessmentItemId={item.AssessmentItemId}; assessmentItemKey={item.AssessmentItemKey}; conceptKey={item.ConceptKey}; concept={item.ConceptLabel}; cognitiveSkill={item.CognitiveSkill}; difficulty={item.Difficulty}; misconceptionTarget={item.MisconceptionTarget}; evidenceExpected={item.EvidenceExpected}; outcomes={string.Join(",", item.LearningOutcomeKeys)}; scoringRule={item.ScoringRule}");
        }

        return sb.ToString();
    }

    private static IReadOnlyList<JsonNode> ParseValidatedBatch(string rawBatch, IReadOnlyList<AssessmentItemSpecDto> batchItems)
    {
        var cleaned = DiagnosticQuizQualityGate.ExtractJsonArray(rawBatch);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(cleaned);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Diagnostic quiz batch output is not valid JSON.", ex);
        }

        using (doc)
        {
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Diagnostic quiz batch output is not a JSON array.");
        }

        var questions = doc.RootElement.EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.Object)
            .ToList();
        if (questions.Count != batchItems.Count)
        {
            throw new InvalidOperationException($"Diagnostic quiz batch question count mismatch: {questions.Count}/{batchItems.Count}.");
        }

        var nodes = new List<JsonNode>(questions.Count);
        for (var i = 0; i < questions.Count; i++)
        {
            var question = questions[i];
            var node = JsonNode.Parse(question.GetRawText());
            if (node is null)
            {
                throw new InvalidOperationException("Diagnostic quiz batch contains an unparsable question object.");
            }

            if (node is JsonObject questionObject)
            {
                ApplyAssessmentSpecMetadata(questionObject, batchItems[i]);
            }

            nodes.Add(node);
        }

        return nodes;
        }
    }

    private static void ApplyAssessmentSpecMetadata(JsonObject question, AssessmentItemSpecDto spec)
    {
        question["assessmentItemId"] = spec.AssessmentItemId;
        question["assessmentItemKey"] = spec.AssessmentItemKey;
        question["conceptKey"] = spec.ConceptKey;
        question["conceptTag"] = spec.ConceptKey;
        question["skillTag"] = spec.ConceptKey;
        question["cognitiveSkill"] = spec.CognitiveSkill;
        question["questionType"] = spec.CognitiveSkill;
        question["difficulty"] = spec.Difficulty;
        question["misconceptionTarget"] = string.IsNullOrWhiteSpace(spec.MisconceptionTarget)
            ? "evidence_insufficient"
            : spec.MisconceptionTarget;
        question["evidenceExpected"] = spec.EvidenceExpected;
        question["scoringRule"] = spec.ScoringRule;
        if (string.IsNullOrWhiteSpace(ReadJsonString(question, "learningObjective")))
        {
            question["learningObjective"] = string.IsNullOrWhiteSpace(spec.EvidenceExpected)
                ? $"{spec.ConceptLabel} kavramini ayirt edip uygun durumda uygulamak."
                : spec.EvidenceExpected;
        }

        if (string.IsNullOrWhiteSpace(ReadJsonString(question, "expectedMisconceptionCategory")))
        {
            question["expectedMisconceptionCategory"] = spec.CognitiveSkill.Contains("procedural", StringComparison.OrdinalIgnoreCase)
                ? "Procedural"
                : spec.CognitiveSkill.Contains("application", StringComparison.OrdinalIgnoreCase)
                    ? "Application"
                    : spec.CognitiveSkill.Contains("analysis", StringComparison.OrdinalIgnoreCase)
                        ? "Conceptual"
                        : spec.CognitiveSkill.Contains("misconception", StringComparison.OrdinalIgnoreCase)
                            ? "Conceptual"
                            : "EvidenceInsufficient";
        }

        var outcomes = new JsonArray();
        foreach (var outcome in spec.LearningOutcomeKeys)
        {
            outcomes.Add(outcome);
        }
        question["learningOutcomeIds"] = outcomes;

        var sourceRefs = question["sourceRefs"] as JsonObject ?? new JsonObject();
        sourceRefs["assessmentItemId"] = spec.AssessmentItemId;
        sourceRefs["assessmentItemKey"] = spec.AssessmentItemKey;
        sourceRefs["conceptKey"] = spec.ConceptKey;
        sourceRefs["cognitiveSkill"] = spec.CognitiveSkill;
        sourceRefs["difficulty"] = spec.Difficulty;
        sourceRefs["misconceptionTarget"] = question["misconceptionTarget"]?.GetValue<string>() ?? "evidence_insufficient";
        sourceRefs["evidenceExpected"] = spec.EvidenceExpected;
        sourceRefs["scoringRule"] = spec.ScoringRule;
        question["sourceRefs"] = sourceRefs;

        NormalizeOptionDiagnostics(question, spec);
    }

    private static void NormalizeOptionDiagnostics(JsonObject question, AssessmentItemSpecDto spec)
    {
        if (question["options"] is not JsonArray options)
        {
            return;
        }

        string? correctText = null;
        foreach (var option in options.OfType<JsonObject>())
        {
            var isCorrect = option["isCorrect"]?.GetValue<bool?>() == true;
            var optionText = ReadJsonString(option, "text") ??
                             ReadJsonString(option, "label") ??
                             ReadJsonString(option, "value") ??
                             ReadJsonString(option, "id") ??
                             string.Empty;
            if (isCorrect && string.IsNullOrWhiteSpace(correctText))
            {
                correctText = optionText;
            }

            if (isCorrect)
            {
                if (string.IsNullOrWhiteSpace(ReadJsonString(option, "rationale")))
                {
                    option["rationale"] = $"Bu secenek {spec.ConceptLabel} icin beklenen kaniti karsilar.";
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(ReadJsonString(option, "misconceptionKey")))
            {
                option["misconceptionKey"] = string.IsNullOrWhiteSpace(spec.MisconceptionTarget)
                    ? $"{spec.ConceptKey}-gap"
                    : spec.MisconceptionTarget;
            }

            if (string.IsNullOrWhiteSpace(ReadJsonString(option, "rationale")))
            {
                option["rationale"] = $"Bu secim {spec.ConceptLabel} konusunda {question["misconceptionTarget"]?.GetValue<string>() ?? "evidence_insufficient"} sinyali verebilir.";
            }
        }

        if (string.IsNullOrWhiteSpace(ReadJsonString(question, "correctAnswer")) &&
            !string.IsNullOrWhiteSpace(correctText))
        {
            question["correctAnswer"] = correctText;
        }
    }

    private static string? ReadJsonString(JsonObject node, string propertyName)
    {
        if (!node.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return value.GetValue<string>();
        }
        catch
        {
            return value.ToJsonString(JsonOptions);
        }
    }

    private static string NormalizeDiagnosticQuizForDelivery(string quizJson)
    {
        var array = JsonNode.Parse(DiagnosticQuizQualityGate.ExtractJsonArray(quizJson)) as JsonArray;
        if (array == null)
        {
            return quizJson;
        }

        PublicTextNormalizer.RepairJsonStrings(array);
        foreach (var item in array.OfType<JsonObject>())
        {
            ShuffleOptionsDeterministically(item);
        }

        return array.ToJsonString(JsonOptions);
    }

    private static void ShuffleOptionsDeterministically(JsonObject question)
    {
        if (question["options"] is not JsonArray options || options.Count < 2)
        {
            return;
        }

        var seedText =
            question["assessmentItemId"]?.GetValue<string>() ??
            question["assessmentItemKey"]?.GetValue<string>() ??
            question["question"]?.GetValue<string>() ??
            Guid.NewGuid().ToString("N");
        var decorated = options
            .Select((option, index) => new
            {
                Option = option?.DeepClone(),
                Sort = StableOptionSort(seedText, option?.ToJsonString(JsonOptions) ?? string.Empty, index)
            })
            .OrderBy(x => x.Sort, StringComparer.Ordinal)
            .ToList();

        options.Clear();
        foreach (var entry in decorated)
        {
            options.Add(entry.Option);
        }
    }

    private static string StableOptionSort(string seedText, string optionText, int index)
    {
        var payload = $"{seedText}|{index}|{optionText}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    private async Task<KorteksResearchResultDto> BuildDirectLearningResearchAsync(
        string approvedResearchIntent,
        string topicTitle,
        string approvedMainTopic,
        string approvedFocusArea,
        Guid topicId,
        CancellationToken ct)
    {
        var retrievedAt = DateTimeOffset.UtcNow;
        var providerBlocks = new List<string>();
        var sources = new List<SourceEvidenceDto>();
        var failures = new List<string>();
        var warnings = new List<string>();

        var webQueries = BuildLearningResearchQueries(approvedResearchIntent, approvedMainTopic, approvedFocusArea);
        if (_webSearch != null)
        {
            var webResult = await TryProviderCallAsync(
                "WebSearch",
                () => _webSearch.SearchWebDeep(string.Join(", ", webQueries)),
                failures,
                ct);
            providerBlocks.Add(BuildProviderBlock("WebSearch", webResult));
            sources.AddRange(KorteksSourceEvidenceExtractor.Extract("WebSearch", "SearchWebDeep", webResult, retrievedAt));
            AddDegradedWarning("WebSearch", webResult, warnings);
        }
        else
        {
            warnings.Add("WebSearch plugin is not available in this runtime; learning research used internal curriculum synthesis.");
        }

        if (_wikipedia != null)
        {
            var wikiResult = await TryProviderCallAsync(
                "Wikipedia",
                () => _wikipedia.SearchWikipedia(approvedResearchIntent),
                failures,
                ct);
            providerBlocks.Add(BuildProviderBlock("Wikipedia", wikiResult));
            sources.AddRange(KorteksSourceEvidenceExtractor.Extract("Wikipedia", "SearchWikipedia", wikiResult, retrievedAt));
            AddDegradedWarning("Wikipedia", wikiResult, warnings);
        }
        else
        {
            warnings.Add("Wikipedia plugin is not available in this runtime.");
        }

        if (_youtube != null)
        {
            var youtubeResult = await TryProviderCallAsync(
                "YouTube",
                () => _youtube.SearchYouTubeVideos(approvedResearchIntent),
                failures,
                ct);
            providerBlocks.Add(BuildProviderBlock("YouTube", youtubeResult));
            sources.AddRange(KorteksSourceEvidenceExtractor.Extract("YouTube", "SearchYouTubeVideos", youtubeResult, retrievedAt));
            AddDegradedWarning("YouTube", youtubeResult, warnings);
        }
        else
        {
            warnings.Add("YouTube plugin is not available in this runtime.");
        }

        var deterministicBrief = BuildDeterministicLearningBrief(
            approvedResearchIntent,
            topicTitle,
            approvedMainTopic,
            approvedFocusArea);

        var systemPrompt = """
            You are Orka's Learning Research Synthesizer.
            You do not create the final quiz and you do not create the final study plan.
            Convert the approved study intent and available source snippets into a compact learning-research brief.

            Requirements:
            - Use the approved study intent, not the raw user sentence.
            - Prefer source-aware learning routes when source snippets are available.
            - If live sources are missing/degraded, be explicit and use conservative curriculum knowledge.
            - Extract prerequisites, sub-concepts, common mistakes, practice order, quiz scope, and recommended question count.
            - For programming topics, keep quiz material about language/concept knowledge; do not mention Orka IDE, sandbox, Visual Studio, or product UI inside quiz scope.
            - Do not invent citations. Do not claim current web grounding when providers are disabled.
            - Return a concise markdown brief with stable section headings.
            """;

        var userMessage = $$"""
            Approved study intent: {{approvedResearchIntent}}
            Main topic: {{approvedMainTopic}}
            Focus area: {{approvedFocusArea}}
            Display topic title: {{topicTitle}}

            Deterministic curriculum seed:
            {{deterministicBrief}}

            Provider notes:
            {{string.Join("\n\n", providerBlocks)}}

            Produce sections:
            [DIRECT LEARNING RESEARCH BRIEF]
            LearningRoute
            ReliableSources
            YouTubeLearningReferences
            Prerequisites
            SubConcepts
            CommonMistakes
            PracticeOrder
            QuizScope
            RecommendedQuestionCount
            PlanningNotes
            """;

        string report;
        try
        {
            report = await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, userMessage, ct);
            if (LooksLikeQuizJson(report) || !report.Contains("QuizScope", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Learning research synthesizer returned an invalid brief shape; deterministic curriculum brief was used.");
                report = deterministicBrief;
            }
        }
        catch (Exception ex)
        {
            warnings.Add("Learning research synthesizer failed; deterministic curriculum brief was used.");
            failures.Add($"LearningResearchSynthesizer: {ex.GetType().Name}");
            report = deterministicBrief;
        }

        sources = sources
            .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(12)
            .ToList();

        var groundingMode = sources.Count switch
        {
            > 2 => GroundingMode.SourceGrounded,
            > 0 => GroundingMode.PartialSourceGrounded,
            _ => GroundingMode.FallbackInternalKnowledge
        };

        return new KorteksResearchResultDto
        {
            Topic = approvedResearchIntent,
            TopicId = topicId,
            Report = report,
            GroundingMode = groundingMode,
            Sources = sources,
            ProviderFailures = failures,
            Warnings = warnings,
            IsFallback = sources.Count == 0,
            CreatedAt = retrievedAt
        };
    }

    private static LearningBlueprintDto BuildLegacyBlueprintFromConceptGraph(
        ConceptGraphDto graph,
        CompressedPlanResearchContextDto context)
    {
        var orderedConcepts = graph.Concepts.OrderBy(c => c.Order).ToList();
        var labels = orderedConcepts
            .Select(c => CleanLegacyText(c.Label, 140))
            .Where(IsUsefulLegacyText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var blueprint = new LearningBlueprintDto
        {
            Domain = string.IsNullOrWhiteSpace(graph.Domain) ? "concept-graph" : graph.Domain,
            ApprovedResearchIntent = CleanLegacyText(graph.ApprovedResearchIntent, 180),
            SourceConfidence = LegacySourceConfidence(context),
            SourceSignals = LegacySourceSignals(context),
            LearningRoute = labels.Take(10).ToList(),
            SubConcepts = labels.Take(12).ToList(),
            PracticeOrder = orderedConcepts
                .Select(c => $"{c.StableKey}|order={c.Order}|difficulty={c.DifficultyBand}")
                .Where(IsUsefulLegacyText)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .ToList(),
            PlanModules = labels
                .Chunk(4)
                .Select(chunk => new LearningBlueprintModuleDto
                {
                    Title = CleanLegacyText(chunk.FirstOrDefault() ?? graph.TopicTitle, 120),
                    Lessons = chunk.Where(IsUsefulLegacyText).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList()
                })
                .Where(m => m.Lessons.Count > 0)
                .ToList(),
            RecommendedQuestionCount = Math.Clamp(Math.Max(18, orderedConcepts.Count * 2), 15, 25)
        };

        blueprint.Prerequisites = orderedConcepts
            .SelectMany(c => c.PrerequisiteKeys)
            .Where(IsUsefulLegacyText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        blueprint.CommonMistakes = orderedConcepts
            .SelectMany(c => c.Misconceptions)
            .Concat(context.LikelyMisconceptions)
            .Where(IsUsefulLegacyText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        blueprint.AssessmentAxes = orderedConcepts
            .Select(c => c.StableKey)
            .Where(IsUsefulLegacyText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        blueprint.Concepts = orderedConcepts
            .Select(c => c.StableKey)
            .Where(IsUsefulLegacyText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
        blueprint.LearningOutcomes = orderedConcepts
            .SelectMany(c => c.LearningOutcomeKeys)
            .Where(IsUsefulLegacyText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
        blueprint.ConceptGraphKeys = orderedConcepts
            .Select(c => c.StableKey)
            .Where(IsUsefulLegacyText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
        blueprint.MisconceptionMap = orderedConcepts
            .SelectMany(c => c.Misconceptions.Select(m => $"{c.StableKey}:{StableLegacyKey(m)}"))
            .Where(IsUsefulLegacyText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
        blueprint.DiagnosticSkillMatrix = orderedConcepts
            .Select(c => $"{c.StableKey}|{c.DifficultyBand}|minEvidence=2|role={(c.PrerequisiteKeys.Count > 0 ? "target" : "prerequisite_or_foundation")}")
            .Where(IsUsefulLegacyText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();

        return blueprint;
    }

    private static string BuildLegacyBlueprintPromptBlock(LearningBlueprintDto blueprint)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[LEGACY BLUEPRINT ADAPTER]");
        sb.AppendLine($"BlueprintDomain: {blueprint.Domain}");
        sb.AppendLine($"BlueprintSourceConfidence: {blueprint.SourceConfidence}");
        AppendLegacyPromptLine(sb, "AdapterLearningRoute", blueprint.LearningRoute);
        AppendLegacyPromptLine(sb, "AdapterPrerequisites", blueprint.Prerequisites);
        AppendLegacyPromptLine(sb, "AdapterSubConcepts", blueprint.SubConcepts);
        AppendLegacyPromptLine(sb, "AdapterCommonMistakes", blueprint.CommonMistakes);
        AppendLegacyPromptLine(sb, "AdapterAssessmentAxes", blueprint.AssessmentAxes);
        AppendLegacyPromptLine(sb, "AdapterLearningOutcomes", blueprint.LearningOutcomes);
        AppendLegacyPromptLine(sb, "AdapterDiagnosticSkillMatrix", blueprint.DiagnosticSkillMatrix);
        AppendLegacyPromptLine(sb, "AdapterPlanModules", blueprint.PlanModules.Select(m => $"{m.Title}: {string.Join(" | ", m.Lessons.Take(5))}"));
        AppendLegacyPromptLine(sb, "AdapterSourceSignals", blueprint.SourceSignals);
        sb.AppendLine($"AdapterRecommendedQuestionCount: {blueprint.RecommendedQuestionCount}");
        sb.AppendLine("Instruction: This block is backward-compatible summary only. Concept graph, assessment grammar and diagnostic profile are canonical.");
        return sb.ToString();
    }

    private static string ComputeLegacyBlueprintHash(LearningBlueprintDto blueprint)
    {
        var json = JsonSerializer.Serialize(blueprint, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string LegacySourceConfidence(CompressedPlanResearchContextDto context)
    {
        if (context.GroundingMode == GroundingMode.SourceGrounded && context.SourceCount >= 3)
        {
            return "high";
        }

        if (context.GroundingMode is GroundingMode.SourceGrounded or GroundingMode.PartialSourceGrounded && context.SourceCount > 0)
        {
            return "medium";
        }

        return "low";
    }

    private static List<string> LegacySourceSignals(CompressedPlanResearchContextDto context) =>
        context.TopSources
            .Select(s => $"{CleanLegacyText(s.Provider, 40)}: {CleanLegacyText(s.Title, 120)}")
            .Concat(context.CurriculumMapHints.Take(3))
            .Where(IsUsefulLegacyText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    private static void AppendLegacyPromptLine(StringBuilder sb, string label, IEnumerable<string> values)
    {
        var list = values.Where(IsUsefulLegacyText).Take(12).ToList();
        if (list.Count > 0)
        {
            sb.AppendLine($"{label}: {string.Join(" | ", list)}");
        }
    }

    private static string StableLegacyKey(string value)
    {
        var normalized = NormalizeLegacyKey(value);
        var chars = normalized
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var key = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(key) ? "concept" : key[..Math.Min(48, key.Length)].Trim('-');
    }

    private static bool IsUsefulLegacyText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string CleanLegacyText(string? value, int max)
    {
        var cleaned = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length <= max ? cleaned : cleaned[..max];
    }

    private static string NormalizeLegacyKey(string? value) =>
        (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace('ç', 'c')
            .Replace('ğ', 'g')
            .Replace('ı', 'i')
            .Replace('ö', 'o')
            .Replace('ş', 's')
            .Replace('ü', 'u');

    private async Task EnrichAttemptRequestFromAssessmentAsync(
        PlanDiagnosticStateDto state,
        RecordQuizAttemptRequest request,
        CancellationToken ct)
    {
        request.AssessmentItemId ??= TryReadGuidMetadata(request.SourceRefsJson, "assessmentItemId");
        request.ConceptKey ??= TryReadStringMetadata(request.SourceRefsJson, "conceptKey");
        request.ConceptTag ??= TryReadStringMetadata(request.SourceRefsJson, "conceptTag");
        request.CognitiveSkill ??= TryReadStringMetadata(request.SourceRefsJson, "cognitiveSkill");
        request.MisconceptionTarget ??= TryReadStringMetadata(request.SourceRefsJson, "misconceptionTarget");
        request.EvidenceExpected ??= TryReadStringMetadata(request.SourceRefsJson, "evidenceExpected");
        request.ScoringRule ??= TryReadStringMetadata(request.SourceRefsJson, "scoringRule");
        request.LearningOutcomeIdsJson ??= TryReadArrayMetadata(request.SourceRefsJson, "learningOutcomeIds");

        if (!request.AssessmentItemId.HasValue)
        {
            request.AssessmentItemId = await ResolveAssessmentItemIdFromQuestionAsync(state, request, ct);
            if (!request.AssessmentItemId.HasValue)
            {
                return;
            }
        }

        var item = await _db.AssessmentItems.AsNoTracking()
            .FirstOrDefaultAsync(i =>
                i.Id == request.AssessmentItemId.Value &&
                i.UserId == state.UserId &&
                i.PlanRequestId == state.PlanRequestId &&
                i.QuizRunId == state.QuizRunId &&
                i.TopicId == state.TopicId,
                ct);
        if (item == null)
        {
            throw new InvalidOperationException("Plan diagnostic assessmentItemId does not belong to this plan request.");
        }

        request.ConceptKey = item.ConceptKey;
        request.ConceptTag = item.ConceptKey;
        request.SkillTag = item.ConceptKey;
        request.CognitiveSkill = item.CognitiveSkill;
        request.QuestionType = item.QuestionType;
        request.Difficulty = item.Difficulty;
        request.MisconceptionTarget = item.MisconceptionTarget;
        request.EvidenceExpected = item.EvidenceExpected;
        request.ScoringRule = item.ScoringRuleJson;
        request.LearningOutcomeIdsJson = item.LearningOutcomeKeysJson;
        request.LearningObjective = item.EvidenceExpected;
    }

    private async Task<Guid?> ResolveAssessmentItemIdFromQuestionAsync(
        PlanDiagnosticStateDto state,
        RecordQuizAttemptRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.QuestionId) && string.IsNullOrWhiteSpace(request.Question))
        {
            return null;
        }

        var items = await _db.AssessmentItems.AsNoTracking()
            .Where(i =>
                i.UserId == state.UserId &&
                i.PlanRequestId == state.PlanRequestId &&
                i.QuizRunId == state.QuizRunId &&
                i.TopicId == state.TopicId)
            .OrderBy(i => i.Order)
            .Take(80)
            .ToListAsync(ct);

        return items
            .FirstOrDefault(item => AssessmentItemMatchesRequest(item.GeneratedQuestionJson, request.QuestionId, request.Question))
            ?.Id;
    }

    private static bool AssessmentItemMatchesRequest(string? generatedQuestionJson, string? questionId, string? questionText)
    {
        if (string.IsNullOrWhiteSpace(generatedQuestionJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(generatedQuestionJson);
            var root = doc.RootElement;
            var itemIds = new[]
            {
                TryGetString(root, "assessmentItemId"),
                TryGetString(root, "assessmentItemKey"),
                TryGetString(root, "questionId"),
                TryGetString(root, "id")
            }.Where(value => !string.IsNullOrWhiteSpace(value));

            if (!string.IsNullOrWhiteSpace(questionId) &&
                itemIds.Any(id => string.Equals(id, questionId, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var stem = TryGetString(root, "question");
            return !string.IsNullOrWhiteSpace(questionText) &&
                   !string.IsNullOrWhiteSpace(stem) &&
                   string.Equals(NormalizeQuestionText(stem), NormalizeQuestionText(questionText), StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeQuestionText(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private async Task<List<QuizAttempt>> LoadDiagnosticAttemptsAsync(Guid userId, Guid quizRunId, CancellationToken ct) =>
        await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.QuizRunId == quizRunId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

    private async Task RefreshLearningSnapshotsAsync(Guid userId, PlanDiagnosticStateDto state, CancellationToken ct)
    {
        if (_learningSnapshots == null)
        {
            return;
        }

        try
        {
            await _learningSnapshots.BuildOrRefreshActiveLessonSnapshotAsync(userId, new ActiveLessonSnapshotRequestDto
            {
                TopicId = state.TopicId,
                SessionId = state.SessionId,
                PlanRequestId = state.PlanRequestId,
                QuizRunId = state.QuizRunId,
                ConceptGraphSnapshotId = state.ConceptGraphSnapshotId,
                SourceBundleHash = state.SourceBundleHash,
                ApprovedIntent = state.ApprovedResearchIntent,
                ApprovedMainTopic = state.ApprovedMainTopic,
                ApprovedFocusArea = state.ApprovedFocusArea,
                ApprovedStudyGoal = state.ApprovedStudyGoal,
                GroundingMode = state.GroundingMode.ToString()
            }, ct);

            await _learningSnapshots.BuildOrRefreshStudentContextSnapshotAsync(userId, new StudentContextSnapshotRequestDto
            {
                TopicId = state.TopicId,
                SessionId = state.SessionId
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[PlanDiagnostic] Learning snapshot refresh skipped. PlanRequestRef={PlanRequestRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(state.PlanRequestId, "plan"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    private async Task<PlanMaterializationSnapshot> ValidatePlanMaterializationAsync(Guid userId, Guid rootTopicId, CancellationToken ct)
    {
        var modules = await _db.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                        t.ParentTopicId == rootTopicId &&
                        t.PlanIntent == "Module" &&
                        !t.IsArchived)
            .OrderBy(t => t.Order)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);

        if (modules.Count == 0)
        {
            return new PlanMaterializationSnapshot([], [], false, "no_modules");
        }

        var moduleIds = modules.Select(m => m.Id).ToList();
        var lessons = await _db.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                        t.ParentTopicId.HasValue &&
                        moduleIds.Contains(t.ParentTopicId.Value) &&
                        !t.IsArchived)
            .OrderBy(t => t.Order)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);
        var moduleOrder = modules
            .Select((module, index) => new { module.Id, Index = index })
            .ToDictionary(x => x.Id, x => x.Index);
        lessons = lessons
            .OrderBy(t => moduleOrder.GetValueOrDefault(t.ParentTopicId!.Value, int.MaxValue))
            .ThenBy(t => t.Order)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        var hasEmptyModule = modules.Any(module => lessons.All(lesson => lesson.ParentTopicId != module.Id));
        var isValid = modules.Count >= MinimumPlanModules &&
                      lessons.Count >= MinimumPlanLessons &&
                      !hasEmptyModule;
        var reason = isValid
            ? "valid"
            : hasEmptyModule
                ? "empty_module"
                : $"below_minimum:{modules.Count}/{MinimumPlanModules}:{lessons.Count}/{MinimumPlanLessons}";

        return new PlanMaterializationSnapshot(modules, lessons, isValid, reason);
    }

    private async Task EnsureMinimumWikiMaterialAsync(
        Guid userId,
        Guid rootTopicId,
        string planTitle,
        PlanMaterializationSnapshot materialization,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var root = await _db.Topics.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == rootTopicId && t.UserId == userId, ct);
        if (root == null)
        {
            return;
        }

        var rootPage = await EnsureWikiPageAsync(
            userId,
            root.Id,
            $"topic:{root.Id:N}",
            "topic_root",
            root.Title,
            $"Master overview for {SafePlanLabel(planTitle)}. This page collects the module route, checkpoints, and learner repair notes for the plan.",
            0,
            now,
            ct);
        await EnsureWikiBlockAsync(rootPage.Id, WikiBlockType.Summary, "Master overview",
            $"Plan route for {SafePlanLabel(planTitle)}: {materialization.Modules.Count} modules and {materialization.Lessons.Count} lessons are ready. Use the lesson pages for summaries, checkpoints, tutor notes, and repair work.",
            "model_assisted", null, now, ct);

        foreach (var module in materialization.Modules)
        {
            var moduleLessons = materialization.Lessons
                .Where(lesson => lesson.ParentTopicId == module.Id)
                .OrderBy(lesson => lesson.Order)
                .ToList();
            var page = await EnsureWikiPageAsync(
                userId,
                module.Id,
                $"module:{module.Id:N}",
                "module",
                module.Title,
                $"Module overview for {SafePlanLabel(module.Title)}. It contains {moduleLessons.Count} lessons and a checkpoint path.",
                module.Order,
                now,
                ct);
            await EnsureWikiBlockAsync(page.Id, WikiBlockType.Summary, "Module overview",
                $"This module introduces {SafePlanLabel(module.Title)} through {moduleLessons.Count} sequenced lessons. Start from the first lesson, then complete the checkpoint before moving forward.",
                "model_assisted", null, now, ct);
            await EnsureWikiBlockAsync(page.Id, WikiBlockType.Checkpoint, "Module checkpoint",
                "Retrieval check: explain the main idea of this module, name one prerequisite, and solve or describe one representative example without looking at the notes.",
                "model_assisted", null, now, ct);
        }

        foreach (var lesson in materialization.Lessons)
        {
            var page = await EnsureWikiPageAsync(
                userId,
                lesson.Id,
                $"lesson:{lesson.Id:N}",
                "lesson",
                lesson.Title,
                $"Learning note for {SafePlanLabel(lesson.Title)}.",
                lesson.Order,
                now,
                ct);
            await EnsureWikiBlockAsync(page.Id, WikiBlockType.Summary, "Lesson summary",
                $"Study focus: {SafePlanLabel(lesson.Title)}. First understand the core idea, then do one worked example, then answer the checkpoint in your own words.",
                "model_assisted", StablePlanQualityKey(lesson.PlanIntent ?? lesson.Title), now, ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<WikiPage> EnsureWikiPageAsync(
        Guid userId,
        Guid topicId,
        string pageKey,
        string pageType,
        string title,
        string safeSummary,
        int orderIndex,
        DateTime now,
        CancellationToken ct)
    {
        var page = await _db.WikiPages
            .FirstOrDefaultAsync(p => p.UserId == userId && p.TopicId == topicId && p.PageKey == pageKey && !p.IsDeleted, ct);
        if (page == null)
        {
            page = new WikiPage
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                PageKey = pageKey,
                PageType = pageType,
                Title = SafePlanLabel(title, 240),
                SafeSummary = SafePlanLabel(safeSummary, 1200),
                SourceReadiness = "evidence_insufficient",
                EvidenceStatus = "evidence_insufficient",
                MetadataJson = "{}",
                Status = "ready",
                OrderIndex = orderIndex,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.WikiPages.Add(page);
            return page;
        }

        page.PageType = string.IsNullOrWhiteSpace(page.PageType) ? pageType : page.PageType;
        page.Title = SafePlanLabel(title, 240);
        page.SafeSummary = SafePlanLabel(safeSummary, 1200);
        page.SourceReadiness = string.IsNullOrWhiteSpace(page.SourceReadiness) ? "evidence_insufficient" : page.SourceReadiness;
        page.EvidenceStatus = string.IsNullOrWhiteSpace(page.EvidenceStatus) ? "evidence_insufficient" : page.EvidenceStatus;
        page.Status = "ready";
        page.OrderIndex = orderIndex;
        page.UpdatedAt = now;
        return page;
    }

    private async Task EnsureWikiBlockAsync(
        Guid wikiPageId,
        WikiBlockType blockType,
        string title,
        string content,
        string sourceBasis,
        string? conceptKey,
        DateTime now,
        CancellationToken ct)
    {
        var exists = await _db.WikiBlocks.AnyAsync(b =>
            b.WikiPageId == wikiPageId &&
            b.BlockType == blockType &&
            b.Title == title &&
            !b.IsDeleted, ct);
        if (exists)
        {
            return;
        }

        var maxOrder = await _db.WikiBlocks
            .Where(b => b.WikiPageId == wikiPageId && !b.IsDeleted)
            .MaxAsync(b => (int?)b.OrderIndex, ct) ?? 0;
        _db.WikiBlocks.Add(new WikiBlock
        {
            Id = Guid.NewGuid(),
            WikiPageId = wikiPageId,
            BlockType = blockType,
            Title = SafePlanLabel(title, 240),
            Content = SafePlanLabel(content, 2000),
            SourceBasis = sourceBasis,
            ConceptKey = conceptKey,
            Visibility = blockType == WikiBlockType.Checkpoint ? "highlighted" : "normal",
            SafetyWarningsJson = "[]",
            OrderIndex = maxOrder + 1,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static string SafePlanLabel(string? value, int maxLength = 500)
    {
        var cleaned = string.IsNullOrWhiteSpace(value)
            ? "Learning item"
            : System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private sealed record PlanMaterializationSnapshot(
        IReadOnlyList<Topic> Modules,
        IReadOnlyList<Topic> Lessons,
        bool IsValid,
        string Reason);

    private sealed record ProfessionalPlanGateResult(bool IsValid, string Message);

    private static ProfessionalPlanGateResult ValidateProfessionalPlanContract(
        PlanMaterializationSnapshot materialization,
        PlanDiagnosticStateDto state)
    {
        var steps = BuildPlanStepsFromGeneratedTopics(materialization.Lessons, state);
        if (steps.Count < MinimumPlanLessons)
        {
            return new(false, $"Generated plan contract has too few measurable lesson steps: {steps.Count}/{MinimumPlanLessons}.");
        }

        var missingCoreContract = steps.Count(step =>
            string.IsNullOrWhiteSpace(step.ConceptKey) ||
            string.IsNullOrWhiteSpace(step.Objective) ||
            string.IsNullOrWhiteSpace(step.SequenceReason) ||
            step.QuizHook == null ||
            string.IsNullOrWhiteSpace(step.QuizHook.ConceptKey) ||
            step.TutorHook == null ||
            string.IsNullOrWhiteSpace(step.TutorHook.ActiveConceptKey) ||
            step.SuccessCriteria.Count == 0);
        if (missingCoreContract > 0)
        {
            return new(false, $"Generated plan contract is missing professional lesson metadata for {missingCoreContract} lessons.");
        }

        var conceptDiversity = steps
            .Select(step => step.ConceptKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (conceptDiversity < 8)
        {
            return new(false, $"Generated plan concept diversity is too low: {conceptDiversity}/8.");
        }

        var genericModuleCount = materialization.Modules.Count(module => LooksGenericModuleTitle(module.Title, state.TopicTitle));
        if (genericModuleCount > Math.Max(1, materialization.Modules.Count / 4))
        {
            return new(false, $"Generated plan module titles are too generic: {genericModuleCount}/{materialization.Modules.Count}.");
        }

        var moduleTitleDiversity = materialization.Modules
            .Select(module => StablePlanQualityKey(module.Title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (moduleTitleDiversity < Math.Min(MinimumPlanModules, materialization.Modules.Count))
        {
            return new(false, "Generated plan module title diversity is too low.");
        }

        var diagnosticWeakConcepts = ExtractDiagnosticWeakConcepts(state);
        if (diagnosticWeakConcepts.Count > 0)
        {
            var earlySteps = steps.Take(Math.Min(12, steps.Count)).ToList();
            var hasRepair = earlySteps.Any(step =>
                step.RemediationNeed is "medium" or "high" ||
                step.TutorHook.TutorMove.Contains("repair", StringComparison.OrdinalIgnoreCase) ||
                step.TargetMisconceptions.Count > 0 ||
                diagnosticWeakConcepts.Any(weak =>
                    step.ConceptKey.Contains(weak, StringComparison.OrdinalIgnoreCase) ||
                    step.Title.Contains(weak, StringComparison.OrdinalIgnoreCase) ||
                    step.Objective.Contains(weak, StringComparison.OrdinalIgnoreCase)));
            if (!hasRepair)
            {
                return new(false, "Generated plan does not place diagnostic weak concepts into the early repair path.");
            }
        }

        return new(true, "professional_plan_contract_ready");
    }

    private static bool PlanQualityBlocksGeneration(PlanQualityEvaluationDto? quality)
    {
        if (quality == null)
        {
            return true;
        }

        return quality.QualityStatus is "insufficient" ||
               quality.BlockingIssues.Any(issue => HardPlanQualityBlockingIssueCodes.Contains(issue.Code)) ||
               quality.PlanContract.CoursePlanQuality.ReadinessStatus is "thin_plan";
    }

    private async Task<PlanQualityEvaluationDto?> EvaluatePlanQualityAsync(
        Guid userId,
        PlanDiagnosticStateDto state,
        IReadOnlyList<Topic> generatedTopics,
        CancellationToken ct)
    {
        if (_planSequencing == null)
        {
            return null;
        }

        try
        {
            var active = _learningSnapshots == null
                ? null
                : await _learningSnapshots.GetActiveLessonSnapshotAsync(userId, state.TopicId, state.SessionId, ct);
            var student = _learningSnapshots == null
                ? null
                : await _learningSnapshots.GetStudentContextSnapshotAsync(userId, state.TopicId, state.SessionId, ct);
            return await _planSequencing.EvaluatePlanSequenceAsync(userId, new PlanQualityEvaluationRequestDto
            {
                TopicId = state.TopicId,
                SessionId = state.SessionId,
                PlanRequestId = state.PlanRequestId,
                ActiveLessonSnapshotId = active?.Id,
                StudentContextSnapshotId = student?.Id,
                PlanTitle = state.TopicTitle,
                PlanSummary = $"Generated topic count: {generatedTopics.Count}",
                ProposedSteps = BuildPlanStepsFromGeneratedTopics(generatedTopics, state)
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PlanDiagnostic] Plan quality evaluation failed. PlanRequestRef={PlanRequestRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(state.PlanRequestId, "plan"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return null;
        }
    }

    private async Task<PlanQualityEvaluationDto?> GetLatestPlanQualityAsync(Guid userId, PlanDiagnosticStateDto state, CancellationToken ct)
    {
        if (_planSequencing == null)
        {
            return null;
        }

        try
        {
            return await _planSequencing.GetLatestPlanQualitySnapshotAsync(userId, state.TopicId, state.SessionId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PlanDiagnostic] Latest plan quality lookup failed. PlanRequestRef={PlanRequestRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(state.PlanRequestId, "plan"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return null;
        }
    }

    private static IReadOnlyList<PlanStepContractDto> BuildPlanStepsFromGeneratedTopics(
        IReadOnlyList<Topic> generatedTopics,
        PlanDiagnosticStateDto state)
    {
        return generatedTopics
            .Select((topic, index) =>
            {
                var metadata = LessonContractMetadata.FromJson(topic.MetadataJson);
                var conceptKey = StablePlanQualityKey(FirstNonEmpty(metadata.ConceptKey, metadata.SkillTag, topic.Title, topic.PlanIntent));
                var objective = FirstNonEmpty(
                    metadata.LearningObjective,
                    $"{topic.Title} adimini olculebilir kavram kontroluyle tamamlamak.")!;
                var sequenceReason = FirstNonEmpty(
                    metadata.SequenceReason,
                    $"Bu ders {FirstNonEmpty(metadata.ModuleTitle, "uretildigi modul")} icindeki konuya ozel ogrenme sirasina gore geldi.")!;
                var successCriteria = metadata.SuccessCriteria.Count > 0
                    ? metadata.SuccessCriteria
                    : [$"{topic.Title} kavramini aciklar.", $"{topic.Title} icin mikro kontrolu tamamlar."];
                return new PlanStepContractDto
                {
                    StepId = $"generated-{index + 1}-{topic.Id:N}"[..Math.Min(92, $"generated-{index + 1}-{topic.Id:N}".Length)],
                    Title = topic.Title,
                    Objective = objective,
                    ConceptKey = conceptKey,
                    ConceptLabel = topic.Title,
                    MasteryTarget = topic.PlanIntent == "Assessment" ? "micro_check_ready" : "understand_and_apply",
                    EstimatedMinutes = 20,
                    LearnerState = "diagnostic_profile_available",
                    RemediationNeed = topic.PlanIntent is "Remediation" or "DeepDive" ? "medium" : "none",
                    DifficultyBand = topic.PlanIntent == "DeepDive" ? "advanced" : "core",
                    SequenceReason = "Bu adim DeepPlan tarafindan diagnostic, concept graph ve adaptif baglamdan sonra olusan ders sirasina gore geldi.",
                    Evidence = new PlanStepEvidenceDto
                    {
                        EvidenceBasis = ["plan_diagnostic", "deep_plan", "concept_graph", "diagnostic_profile"],
                        SourceReadiness = string.IsNullOrWhiteSpace(state.SourceBundleHash) ? "evidence_insufficient" : "source_aware",
                        KorteksWorkflowId = state.KorteksResearchWorkflowId
                    },
                    QuizHook = new PlanStepAssessmentHookDto
                    {
                        HookType = FirstNonEmpty(metadata.QuizHookType, topic.PlanIntent == "Assessment" ? "micro_quiz" : "retrieval_practice")!,
                        ConceptKey = conceptKey,
                        DifficultyBand = FirstNonEmpty(metadata.DifficultyBand, topic.PlanIntent == "DeepDive" ? "advanced" : "core")!,
                        UserSafeReason = "Bu plan adimi kisa olcumle dogrulanabilir."
                    },
                    TutorHook = new PlanStepTutorHookDto
                    {
                        TutorMove = FirstNonEmpty(metadata.TutorMove, topic.PlanIntent switch
                        {
                            "Remediation" => "misconception_repair",
                            "PracticeLab" => "example",
                            "DeepDive" => "scaffold",
                            _ => "explain"
                        })!,
                        ActiveConceptKey = conceptKey,
                        UserSafeReason = "Tutor bu adimi aktif kavram ve mikro kontrolle isler."
                    },
                    WikiHook = new PlanStepWikiHookDto
                    {
                        SourceReadiness = string.IsNullOrWhiteSpace(state.SourceBundleHash) ? "evidence_insufficient" : "source_aware"
                    },
                    SuccessCriteria = successCriteria,
                    NextStepTrigger = "micro_check_passed",
                    FallbackIfEvidenceWeak = "Kaynak/ogrenci kaniti zayifsa Tutor scaffold ve diagnostic kontrol uygular."
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractDiagnosticWeakConcepts(PlanDiagnosticStateDto state)
    {
        return Array.Empty<string>();
    }

    private static void AddWeakConceptsFromText(List<string> concepts, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var part in value.Split(new[] { ' ', ',', ';', '/', ':' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Trim().Length >= 4)
            {
                concepts.Add(part.Trim());
            }
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? TryGetNestedString(JsonElement root, string objectName, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(objectName, out var nested)
            ? TryGetString(nested, propertyName)
            : null;

    private static IReadOnlyList<string> TryGetStringArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .Take(8)
            .ToArray();
    }

    private sealed class LessonContractMetadata
    {
        public string? ModuleTitle { get; private init; }
        public string? SkillTag { get; private init; }
        public string? ConceptKey { get; private init; }
        public string? LearningObjective { get; private init; }
        public string? SequenceReason { get; private init; }
        public string? QuizHookType { get; private init; }
        public string? TutorMove { get; private init; }
        public string? DifficultyBand { get; private init; }
        public IReadOnlyList<string> SuccessCriteria { get; private init; } = Array.Empty<string>();

        public static LessonContractMetadata FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new LessonContractMetadata();
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return new LessonContractMetadata
                {
                    ModuleTitle = TryGetString(root, "moduleTitle"),
                    SkillTag = TryGetString(root, "skillTag"),
                    ConceptKey = TryGetString(root, "conceptKey"),
                    LearningObjective = TryGetString(root, "learningObjective"),
                    SequenceReason = TryGetString(root, "sequenceReason"),
                    QuizHookType = TryGetNestedString(root, "quizHook", "hookType"),
                    TutorMove = TryGetNestedString(root, "tutorHook", "tutorMove"),
                    DifficultyBand = TryGetNestedString(root, "quizHook", "difficultyBand"),
                    SuccessCriteria = TryGetStringArray(root, "successCriteria")
                };
            }
            catch (JsonException)
            {
                return new LessonContractMetadata();
            }
        }
    }

    private static string StablePlanQualityKey(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "plan-step" : value.Trim().ToLowerInvariant();
        var chars = text.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var key = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(key) ? "plan-step" : key.Length <= 80 ? key : key[..80].Trim('-');
    }

    private static bool LooksGenericModuleTitle(string? moduleTitle, string topicTitle)
    {
        var text = StablePlanQualityKey(moduleTitle);
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var topicTokens = StablePlanQualityKey(topicTitle)
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var moduleTokens = text
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasTopicOverlap = topicTokens.Count > 0 && moduleTokens.Overlaps(topicTokens);
        var genericOnly = text is
            "fundamentals" or "core-concepts" or "practice" or "advanced" or
            "temel-bilgiler" or "ana-kavram-omurgasi" or "uygulama-ve-ornekleme" or
            "yanilgi-onarimi" or "karma-pratik" or "mastery-kontrolu-ve-sonraki-rota" or "onkosul-haritasi" ||
            text.StartsWith("module-", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("modul-", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("concept-graph-module", StringComparison.OrdinalIgnoreCase);

        return genericOnly && !hasTopicOverlap;
    }

    private static string BuildConceptGraphPromptBlock(ConceptGraphDto graph)
    {
        var lines = new List<string>
        {
            "[CONCEPT GRAPH]",
            $"ConceptGraphSnapshotId: {graph.SnapshotId?.ToString() ?? "pending"}",
            $"IntentHash: {graph.IntentHash}",
            $"Domain: {graph.Domain}",
            $"SourceConfidence: {graph.SourceConfidence}",
            $"SourceBundleHash: {graph.SourceBundleHash}",
            "Concepts:"
        };
        lines.AddRange(graph.Concepts.OrderBy(c => c.Order).Take(16).Select(c =>
            $"- {c.StableKey}: {c.Label}; difficulty={c.DifficultyBand}; prerequisites={string.Join(",", c.PrerequisiteKeys)}; misconceptions={string.Join(" | ", c.Misconceptions.Take(2))}; outcomes={string.Join(",", c.LearningOutcomeKeys)}"));
        lines.Add("Instruction: Use concept keys as the stable learning spine. Do not invent topic-specific templates outside this graph.");
        return string.Join("\n", lines);
    }

    private static string BuildLearningQualityPromptBlock(
        ConceptGraphBuildResultDto graphResult,
        AssessmentGrammarDraftDto assessmentDraft)
    {
        var graphStatus = string.IsNullOrWhiteSpace(graphResult.QualityStatus) ? "unknown" : graphResult.QualityStatus;
        var assessmentStatus = string.IsNullOrWhiteSpace(assessmentDraft.QualityStatus) ? "unknown" : assessmentDraft.QualityStatus;
        return string.Join("\n", new[]
        {
            "[LEARNING QUALITY]",
            $"ConceptGraphQualityStatus: {graphStatus}",
            $"ConceptGraphQualityRunId: {graphResult.QualityRunId?.ToString() ?? "none"}",
            $"AssessmentQualityStatus: {assessmentStatus}",
            $"AssessmentQualityRunId: {assessmentDraft.QualityRunId?.ToString() ?? "none"}",
            graphStatus == "degraded" || assessmentStatus == "degraded"
                ? "PlanningPolicy: Quality is degraded; produce a conservative plan, avoid over-confident mastery claims, and add extra check/remediation nodes."
                : "PlanningPolicy: Quality signals are acceptable; keep the plan adaptive and evidence-aware."
        });
    }

    private static Guid? TryReadGuidMetadata(string? json, string key)
    {
        var value = TryReadStringMetadata(json, key);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static string? TryReadStringMetadata(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(key, out var value))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? TryReadArrayMetadata(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.Array)
            {
                return value.GetRawText();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string[] BuildLearningResearchQueries(
        string approvedResearchIntent,
        string approvedMainTopic,
        string approvedFocusArea)
    {
        var baseIntent = string.Join(' ', new[] { approvedResearchIntent, approvedMainTopic, approvedFocusArea }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        var queries = new List<string>
        {
            $"{baseIntent} curriculum",
            $"{baseIntent} syllabus",
            $"{baseIntent} course outline",
            $"{baseIntent} learning path",
            $"{baseIntent} practice problems",
            $"{baseIntent} common mistakes",
            $"{baseIntent} prerequisites common mistakes practice exercises",
            $"{baseIntent} tutorial course roadmap"
        };

        if (ContainsTurkishStudySignal(approvedResearchIntent, approvedMainTopic, approvedFocusArea))
        {
            queries.Add($"{baseIntent} ders plani");
            queries.Add($"{baseIntent} konu anlatimi");
            queries.Add($"{baseIntent} mufredat");
        }

        return queries.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
    }

    private static bool ContainsTurkishStudySignal(params string[] values)
    {
        var text = string.Join(' ', values);
        return text.Contains('ç') || text.Contains('ğ') || text.Contains('ı') ||
               text.Contains('ö') || text.Contains('ş') || text.Contains('ü') ||
               text.Contains("calism", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ogren", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("tarih", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("mufredat", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> TryProviderCallAsync(
        string provider,
        Func<Task<string>> call,
        List<string> failures,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            return await call();
        }
        catch (Exception ex)
        {
            failures.Add($"{provider}: {ex.GetType().Name}");
            return $"[{provider}:degraded] {provider} unavailable during direct learning research.";
        }
    }

    private static string BuildProviderBlock(string provider, string? result)
    {
        var text = string.IsNullOrWhiteSpace(result)
            ? "[empty]"
            : result.Trim();
        if (text.Length > 2500)
        {
            text = text[..2500] + "...";
        }

        return $"[{provider}]\n{text}";
    }

    private static void AddDegradedWarning(string provider, string? result, List<string> warnings)
    {
        var marker = KorteksSourceEvidenceExtractor.FindDegradedMarker(result);
        if (!string.IsNullOrWhiteSpace(marker))
        {
            warnings.Add($"{provider} returned degraded marker: {marker}");
        }
    }

    private static bool LooksLikeQuizJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith("[", StringComparison.Ordinal) &&
               value.Contains("\"question\"", StringComparison.OrdinalIgnoreCase) &&
               value.Contains("\"options\"", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDeterministicLearningBrief(
        string approvedResearchIntent,
        string topicTitle,
        string approvedMainTopic,
        string approvedFocusArea)
    {
        if (LooksLikeJavaAlgorithmsIntent(approvedResearchIntent, topicTitle, approvedMainTopic, approvedFocusArea))
        {
            return $$"""
                [DIRECT LEARNING RESEARCH BRIEF]
                LearningRoute:
                1. Java array/list basics and iteration.
                2. Big-O intuition through simple loops, nested loops, and collection operations.
                3. Sorting and searching: Arrays.sort, Collections.sort, binary search prerequisites.
                4. Core data structures: ArrayList, LinkedList, Stack, Queue, HashSet, HashMap, PriorityQueue.
                5. Recursion, base cases, and call-stack reasoning.
                6. Graph basics: BFS with queue, DFS with recursion/stack.
                7. Practical patterns: two pointers, prefix sums, greedy checks, dynamic programming basics.

                ReliableSources:
                - Oracle Java Collections algorithms documentation is a stable reference for sorting/searching collection behavior.
                - Princeton Algorithms Part I is a strong Java-oriented algorithms/data structures curriculum reference.
                - OpenDSA/Open Data Structures are useful open learning references for practice and conceptual checks.

                YouTubeLearningReferences:
                - Use YouTube only as a teaching reference when configured. Prefer Java algorithms/data structures tutorials that show code traces and practice problems.

                Prerequisites:
                Java syntax, methods, loops, arrays, object basics, generics basics, and reading small code traces.

                SubConcepts:
                arrays, lists, sorting, searching, binary search precondition, Big-O, stack, queue, set, map, priority queue, recursion, BFS, DFS, greedy, dynamic programming.

                CommonMistakes:
                assuming binary search works on unsorted data; confusing HashMap with ordered maps; off-by-one loop bounds; ignoring base cases; treating every greedy idea as correct; mixing stack and queue behavior; memorizing Big-O without reading the code path.

                PracticeOrder:
                trace small arrays -> implement search -> compare linear/binary search -> sort custom objects with Comparator -> use stack/queue -> map/set frequency tasks -> recursion base cases -> BFS/DFS toy graph -> prefix sum/two pointer -> small DP table.

                QuizScope:
                Diagnostic questions must measure Java algorithm/data-structure understanding, not product UI usage. Include code reading, data-structure choice, complexity, and misconception probes.

                RecommendedQuestionCount:
                20

                PlanningNotes:
                Move already-known Java syntax quickly into practice. Spend more time on misconceptions found in quiz attempts, especially complexity, data-structure choice, and algorithm preconditions.
                """;
        }

        return $$"""
            [DIRECT LEARNING RESEARCH BRIEF]
            LearningRoute:
            Start from prerequisites, map the focus area into sub-concepts, then move from small examples to applied practice.

            ReliableSources:
            Live source grounding was unavailable or partial. Use conservative curriculum knowledge until provider-backed sources are available.

            YouTubeLearningReferences:
            Use as teaching references only when configured; do not treat video metadata as factual proof.

            Prerequisites:
            Identify vocabulary, prior skills, and basic examples needed before the learner starts the focus area.

            SubConcepts:
            Break "{{approvedResearchIntent}}" into measurable concept groups before quiz generation.

            CommonMistakes:
            Watch for memorized definitions, skipped prerequisites, confused terminology, and applying a rule outside its constraints.

            PracticeOrder:
            Concept check -> small worked example -> guided practice -> mixed practice -> error reflection.

            QuizScope:
            Questions should measure the approved intent directly and avoid internal product/system wording.

            RecommendedQuestionCount:
            20

            PlanningNotes:
            Use quiz results to fast-track known concepts and slow down on weak/misunderstood concepts.
            """;
    }

    private static bool LooksLikeJavaAlgorithmsIntent(params string[] values)
    {
        var text = string.Join(' ', values).ToLowerInvariant();
        return text.Contains("java", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("algoritma", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("algorithm", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("veri yapi", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("data structure", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<Guid>> GetGeneratedPlanTopicIdsAsync(Guid userId, Guid rootTopicId, CancellationToken ct)
    {
        var moduleIds = await _db.Topics.AsNoTracking()
            .Where(t => t.UserId == userId && t.ParentTopicId == rootTopicId && t.PlanIntent == "Module")
            .OrderBy(t => t.Order)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (moduleIds.Count == 0)
        {
            return [];
        }

        var lessonIds = await _db.Topics.AsNoTracking()
            .Where(t => t.UserId == userId && t.ParentTopicId.HasValue && moduleIds.Contains(t.ParentTopicId.Value))
            .OrderBy(t => t.Order)
            .Select(t => t.Id)
            .ToListAsync(ct);

        return lessonIds.Count > 0 ? lessonIds : moduleIds;
    }

    private async Task<string> BuildCurrentDiagnosticSummaryAsync(Guid userId, Guid quizRunId, CancellationToken ct)
    {
        var attempts = await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.QuizRunId == quizRunId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        if (attempts.Count == 0)
        {
            return "[PLAN DIAGNOSTIC QUIZ SUMMARY]\nNo answers were recorded.";
        }

        var correct = attempts.Where(a => a.IsCorrect).ToList();
        var wrong = attempts.Where(a => !a.IsCorrect).ToList();
        var knownSummary = correct
            .Select(ExtractWeakConcept)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();
        var conceptSummary = wrong
            .Select(ExtractWeakConcept)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();
        var fastTrack = knownSummary
            .Where(item => !conceptSummary.Any(weak => weak.StartsWith(item.Split(':')[0], StringComparison.OrdinalIgnoreCase)))
            .Take(8)
            .ToList();
        var mistakeSummary = wrong
            .Select(ExtractMistakePattern)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();
        var accuracyPercent = attempts.Count == 0
            ? 0
            : (int)Math.Round(attempts.Count(a => a.IsCorrect) * 100.0 / attempts.Count);
        var measuredLevel = accuracyPercent switch
        {
            >= 85 => "advanced",
            >= 65 => "intermediate",
            >= 40 => "developing",
            _ => "beginner"
        };

        return string.Join("\n", new[]
        {
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]",
            $"QuizRunId: {quizRunId}",
            $"Answered: {attempts.Count}",
            $"Correct: {attempts.Count(a => a.IsCorrect)}",
            $"Wrong: {wrong.Count}",
            $"AccuracyPercent: {accuracyPercent}",
            $"MeasuredLevel: {measuredLevel}",
            $"KnownConcepts: {(knownSummary.Count == 0 ? "none" : string.Join(" | ", knownSummary))}",
            $"FastTrackConcepts: {(fastTrack.Count == 0 ? "none" : string.Join(" | ", fastTrack))}",
            $"PracticeConcepts: {(knownSummary.Count == 0 ? "none" : string.Join(" | ", knownSummary.Take(8)))}",
            $"WeakConcepts: {(conceptSummary.Count == 0 ? "none" : string.Join(" | ", conceptSummary))}",
            $"MistakePatterns: {(mistakeSummary.Count == 0 ? "none" : string.Join(" | ", mistakeSummary))}",
            "Instruction: Move known concepts faster with short practice. Teach weak or mistaken concepts more slowly, logically, and with examples. Do not claim skipped or unanswered concepts are weaknesses."
        });
    }

    private static string? ExtractWeakConcept(QuizAttempt attempt)
    {
        var conceptTag = ExtractAttemptMetadata(attempt.SourceRefsJson, "conceptTag");
        if (!string.IsNullOrWhiteSpace(conceptTag))
        {
            return conceptTag.Trim();
        }

        var learningObjective = ExtractAttemptMetadata(attempt.SourceRefsJson, "learningObjective");
        if (!string.IsNullOrWhiteSpace(learningObjective))
        {
            return learningObjective.Trim();
        }

        if (!string.IsNullOrWhiteSpace(attempt.SkillTag))
        {
            return attempt.SkillTag.Trim();
        }

        if (!string.IsNullOrWhiteSpace(attempt.TopicPath))
        {
            var parts = attempt.TopicPath.Split(
                new[] { '/', '>', '|', '\\' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.LastOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(attempt.QuestionHash))
        {
            return attempt.QuestionHash.Trim();
        }

        return null;
    }

    private static string? ExtractAttemptMetadata(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string ExtractMistakePattern(QuizAttempt attempt)
    {
        var text = string.Join(" ",
            attempt.Explanation,
            attempt.CognitiveType,
            attempt.SourceRefsJson);

        foreach (var candidate in new[]
                 {
                     "Procedural",
                     "Reading",
                     "MisreadQuestion",
                     "Conceptual",
                     "Application",
                     "Careless"
                 })
        {
            if (text.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return string.IsNullOrWhiteSpace(attempt.CognitiveType)
            ? "IncorrectAnswer"
            : attempt.CognitiveType.Trim();
    }

    private async Task<PlanDiagnosticStateDto> RequireStateAsync(Guid userId, Guid planRequestId, CancellationToken ct)
    {
        var state = await _stateStore.GetAsync(planRequestId, ct);
        if (state == null || state.UserId != userId)
        {
            throw new InvalidOperationException("Plan diagnostic state was not found.");
        }

        return state;
    }

    private static string NormalizeApprovedIntent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 160 ? normalized : normalized[..160];
    }

    private static string CleanOrDefault(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string BuildApprovedTopicTitle(string? requestedTitle, string mainTopic, string focusArea, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(requestedTitle) && !LooksLikeRawStudyRequest(requestedTitle))
        {
            return CleanOrDefault(requestedTitle, fallback);
        }

        if (string.IsNullOrWhiteSpace(focusArea) || focusArea.Equals("genel kapsam", StringComparison.OrdinalIgnoreCase))
        {
            return CleanOrDefault(mainTopic, fallback);
        }

        return $"{mainTopic}: {focusArea}";
    }

    private static bool LooksLikeRawStudyRequest(string value)
    {
        var text = value.Trim().ToLowerInvariant();
        return text.Contains("calismak istiyorum", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ogrenmek istiyorum", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("istiyorum", StringComparison.OrdinalIgnoreCase);
    }

    private static int DetermineDiagnosticQuestionCount(string mainTopic, string focusArea, string researchIntent)
    {
        var text = $"{mainTopic} {focusArea} {researchIntent}".ToLowerInvariant();
        var narrowSignals = new[]
        {
            "temel",
            "intro",
            "giris",
            "syntax",
            "tek konu",
            "single topic"
        };

        if (narrowSignals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase)))
            return 15;

        var hasAlgorithm = text.Contains("algorithm", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("algoritma", StringComparison.OrdinalIgnoreCase);
        var hasDataStructures = text.Contains("data structure", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("veri yap", StringComparison.OrdinalIgnoreCase);
        if (hasAlgorithm && hasDataStructures)
            return 25;

        var hasSqlOptimization = text.Contains("sql", StringComparison.OrdinalIgnoreCase) &&
                                 (text.Contains("index", StringComparison.OrdinalIgnoreCase) ||
                                  text.Contains("indeks", StringComparison.OrdinalIgnoreCase) ||
                                  text.Contains("query", StringComparison.OrdinalIgnoreCase) ||
                                  text.Contains("sorgu", StringComparison.OrdinalIgnoreCase) ||
                                  text.Contains("optimization", StringComparison.OrdinalIgnoreCase) ||
                                  text.Contains("optimizasyon", StringComparison.OrdinalIgnoreCase));
        if (hasSqlOptimization)
            return 24;

        var mediumScopeSignals = new[]
        {
            "kpss",
            "yks",
            "ielts",
            "paragraf",
            "problem",
            "exam",
            "sinav",
            "practice",
            "pratik"
        };
        if (mediumScopeSignals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase)))
            return 20;

        if (hasAlgorithm ||
            text.Contains("curriculum", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("roadmap", StringComparison.OrdinalIgnoreCase))
            return 20;

        return 15;
    }

    private static int CountQuestions(string quizJson)
    {
        try
        {
            var count = DiagnosticQuizQualityGate.CountQuestions(quizJson);
            return count > 0 ? count : 20;
        }
        catch
        {
            // The model response will still be returned; default to the intended diagnostic size.
        }

        return 20;
    }

    private static string StripAnswerKeysForLearner(string quizJson)
    {
        try
        {
            var node = JsonNode.Parse(DiagnosticQuizQualityGate.ExtractJsonArray(quizJson)) as JsonArray;
            if (node == null)
            {
                return quizJson;
            }

            PublicTextNormalizer.RepairJsonStrings(node);
            foreach (var item in node.OfType<JsonObject>())
            {
                item.Remove("correctAnswer");
                item.Remove("correct_answer");
                item.Remove("answer");
                item.Remove("correctOption");
                item.Remove("correctOptionId");
                item.Remove("correct_option_id");
                item.Remove("explanation");
                item.Remove("rationale");
                item.Remove("reason");

                if (item["options"] is not JsonArray options)
                {
                    continue;
                }

                foreach (var option in options.OfType<JsonObject>())
                {
                    var id = option["id"]?.DeepClone();
                    var optionId = option["optionId"]?.DeepClone();
                    var value = option["value"]?.DeepClone();
                    var label = option["label"]?.DeepClone();
                    var text = option["text"]?.DeepClone();
                    option.Clear();
                    if (id != null) option["id"] = id;
                    if (optionId != null) option["optionId"] = optionId;
                    if (value != null) option["value"] = value;
                    if (label != null) option["label"] = label;
                    if (text != null) option["text"] = text;
                }
            }

            return node.ToJsonString(JsonOptions);
        }
        catch
        {
            return quizJson;
        }
    }
}

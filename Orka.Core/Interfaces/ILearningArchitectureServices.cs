using Orka.Core.DTOs;
using Orka.Core.DTOs.Korteks;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface IConceptGraphBuilder
{
    Task<ConceptGraphBuildResultDto> BuildOrLoadAsync(
        Guid userId,
        Guid? topicId,
        Guid planRequestId,
        string approvedResearchIntent,
        string topicTitle,
        string approvedMainTopic,
        string approvedFocusArea,
        CompressedPlanResearchContextDto compressedContext,
        CancellationToken ct = default);
}

public interface IConceptGraphQualityService
{
    Task<ConceptGraphQualityDto> EvaluateAndSaveAsync(
        Guid userId,
        Guid? topicId,
        Guid planRequestId,
        ConceptGraphDto graph,
        CancellationToken ct = default);

    Task<ConceptGraphQualityDto?> GetLatestAsync(
        Guid userId,
        Guid? topicId,
        Guid? snapshotId = null,
        CancellationToken ct = default);
}

public interface IAssessmentGrammarEngine
{
    Task<AssessmentGrammarDraftDto> BuildOrLoadDraftAsync(
        Guid userId,
        Guid? topicId,
        Guid planRequestId,
        Guid quizRunId,
        ConceptGraphDto graph,
        int requestedQuestionCount,
        CancellationToken ct = default);

    string BuildPromptBlock(AssessmentGrammarDto grammar);

    Task<string> AttachQuestionMetadataAsync(
        string quizJson,
        AssessmentGrammarDto grammar,
        CancellationToken ct = default);
}

public interface IAssessmentQualityService
{
    Task<AssessmentQualityDto> EvaluateAndSaveAsync(
        Guid userId,
        Guid? topicId,
        Guid? planRequestId,
        Guid? quizRunId,
        AssessmentGrammarDto grammar,
        ConceptGraphDto graph,
        CancellationToken ct = default);

    Task<AssessmentQualityDto?> GetLatestAsync(
        Guid userId,
        Guid? topicId,
        Guid? draftId = null,
        CancellationToken ct = default);

    Task<AssessmentItemStatDto?> UpdateItemStatsAsync(
        QuizAttempt attempt,
        CancellationToken ct = default);
}

public interface IDiagnosticProfileBuilder
{
    Task<DiagnosticProfileDto> BuildAndSaveAsync(
        PlanDiagnosticStateDto state,
        IReadOnlyList<QuizAttempt> attempts,
        CancellationToken ct = default);

    string BuildPromptBlock(DiagnosticProfileDto profile);
}

public interface IConceptMasteryService
{
    Task<IReadOnlyList<ConceptMasteryDto>> UpdateFromDiagnosticProfileAsync(
        DiagnosticProfileDto profile,
        CancellationToken ct = default);

    Task<IReadOnlyList<ConceptMasteryDto>> GetRecentMasteryAsync(
        Guid userId,
        Guid? topicId,
        int take = 12,
        CancellationToken ct = default);
}

public interface IKnowledgeTracingService
{
    Task<KnowledgeTracingStateDto?> UpdateFromAttemptAsync(
        QuizAttempt attempt,
        CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeTracingStateDto>> UpdateFromDiagnosticProfileAsync(
        DiagnosticProfileDto profile,
        CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeTracingStateDto>> GetRecentStatesAsync(
        Guid userId,
        Guid? topicId,
        int take = 12,
        CancellationToken ct = default);
}

public interface ILearningEventNormalizer
{
    Task<LearningEventDto?> RecordQuizAttemptEventAsync(
        QuizAttempt attempt,
        CancellationToken ct = default);

    Task<LearningEventDto?> RecordSignalEventAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string signalType,
        string? skillTag = null,
        string? topicPath = null,
        int? score = null,
        bool? isPositive = null,
        string? payloadJson = null,
        Guid? quizAttemptId = null,
        CancellationToken ct = default);
}

public interface ILearningEventSchemaService
{
    string NormalizeEventType(string rawEventType);

    Task<LearningEventSchemaValidationDto> ValidateAndLogAsync(
        LearningEvent learningEvent,
        CancellationToken ct = default);
}

public interface ITutorPolicyEngine
{
    Task<TutorPolicyContextDto> BuildAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        string notebookContext,
        string wikiContext,
        string learningSignalContext,
        CancellationToken ct = default);
}

public interface ITutorPolicyTraceService
{
    Task<TutorPolicyTraceDto> CreateTraceAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        TutorPolicyContextDto context,
        CancellationToken ct = default);

    Task<IReadOnlyList<TutorPolicyTraceDto>> GetRecentAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId = null,
        int take = 10,
        CancellationToken ct = default);
}

public interface ITutorTurnStateAssembler
{
    Task<TutorTurnStateDto> BuildAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        string conversationContext,
        string notebookContext,
        string wikiContext,
        string learningSignalContext,
        string ideContext,
        TutorPolicyContextDto policyContext,
        CancellationToken ct = default);
}

public interface ITutorWorkingMemoryService
{
    Task<TutorWorkingMemorySnapshot> SaveTurnSnapshotAsync(
        TutorTurnStateDto state,
        CancellationToken ct = default);

    Task<TutorMemoryPatchDto> ApplyPatchAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string patchType,
        object patch,
        CancellationToken ct = default);

    Task RecordStreamEventAsync(
        Guid sessionId,
        string eventType,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken ct = default);
}

public interface ILearnerProfileService
{
    Task<LearnerProfileDto> BuildOrUpdateAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        string learningSignalContext,
        string ideContext,
        CancellationToken ct = default);
}

public interface ILearningStyleSignalService
{
    Task<LearningStyleSignalDto> DetectAndRecordAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        CancellationToken ct = default);
}

public interface IAffectiveSignalService
{
    Task<AffectiveSignalDto> DetectAndRecordAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        CancellationToken ct = default);
}

public interface ICognitiveLoadService
{
    Task<CognitiveLoadSignalDto> DetectAndRecordAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        string learningSignalContext,
        string ideContext,
        CancellationToken ct = default);
}

public interface ITutorActionPlanner
{
    Task<TutorActionPlanDto> PlanAsync(
        TutorTurnStateDto turnState,
        CancellationToken ct = default);
}

public interface ITutorToolOrchestrator
{
    Task<IReadOnlyList<TutorToolCallDto>> RunAsync(
        TutorActionPlanDto actionPlan,
        TutorTurnStateDto turnState,
        CancellationToken ct = default);
}

public interface ITeachingArtifactService
{
    Task<IReadOnlyList<TeachingArtifactDto>> BuildArtifactsAsync(
        TutorActionPlanDto actionPlan,
        TutorTurnStateDto turnState,
        CancellationToken ct = default);

    Task MarkRenderedAsync(Guid artifactId, Guid userId, string? renderError = null, CancellationToken ct = default);
}

public interface ITutorReflectionService
{
    Task<TutorReflectionUpdateDto> ReflectAsync(
        TutorTurnStateDto turnState,
        TutorActionPlanDto actionPlan,
        string assistantAnswer,
        IReadOnlyList<TeachingArtifactDto> artifacts,
        CancellationToken ct = default);
}

public interface ITutorPedagogyRubricService
{
    IReadOnlyList<TutorPedagogyRubricScoreDto> EvaluateDeterministic(
        TutorPedagogyEvaluationRequestDto request);
}

public interface ITutorPedagogyQualityGate
{
    bool RequiresRepair(TutorPedagogyEvaluationRunDto evaluation);

    string BuildRepairPrompt(
        TutorPedagogyEvaluationRunDto evaluation,
        TutorTurnStateDto turnState,
        TutorActionPlanDto actionPlan,
        string assistantAnswer);
}

public interface ITutorPedagogyFeedbackService
{
    Task<TutorMemoryPatchDto?> WriteFeedbackPatchAsync(
        TutorPedagogyEvaluationRunDto evaluation,
        CancellationToken ct = default);
}

public interface ITutorPedagogyEvaluationService
{
    Task<TutorPedagogyEvaluationRunDto> EvaluateAsync(
        TutorPedagogyEvaluationRequestDto request,
        CancellationToken ct = default);

    Task<TutorPedagogyEvaluationRunDto?> GetRunAsync(
        Guid userId,
        Guid runId,
        CancellationToken ct = default);

    Task<TutorPedagogyTopicSummaryDto> GetTopicSummaryAsync(
        Guid userId,
        Guid? topicId,
        CancellationToken ct = default);

    Task<TutorPedagogyEvaluationRunDto?> EvaluateRecentAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId = null,
        CancellationToken ct = default);
}

public interface ITutorGoldenScenarioService
{
    IReadOnlyList<TutorGoldenScenarioDto> GetCanonicalScenarios();
}

public interface IResourceConceptAlignmentService
{
    Task<IReadOnlyList<ResourceConceptAlignmentDto>> AlignGraphSourcesAsync(
        Guid userId,
        Guid? topicId,
        ConceptGraphDto graph,
        CancellationToken ct = default);

    Task<IReadOnlyList<ResourceConceptAlignmentDto>> GetRecentAsync(
        Guid userId,
        Guid? topicId,
        Guid? snapshotId = null,
        int take = 20,
        CancellationToken ct = default);
}

public interface IRagEvaluationService
{
    Task<RagEvaluationRunDto> EvaluateTopicAsync(
        Guid userId,
        Guid? topicId,
        Guid? conceptGraphSnapshotId = null,
        CancellationToken ct = default);

    Task<RagEvaluationRunDto?> GetLatestAsync(
        Guid userId,
        Guid? topicId,
        CancellationToken ct = default);
}

public interface ITutorMemoryFragmentService
{
    Task<TutorMemoryFragmentDto> RecordAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string fragmentType,
        string conceptKey,
        string content,
        string source = "tutor",
        decimal importance = 0.50m,
        CancellationToken ct = default);

    Task<IReadOnlyList<TutorMemoryFragmentDto>> RetrieveAsync(
        Guid userId,
        Guid? topicId,
        string query,
        int take = 8,
        CancellationToken ct = default);
}

public interface ILearningQualityReportService
{
    Task<LearningQualityReportDto> BuildTopicReportAsync(
        Guid userId,
        Guid? topicId,
        Guid? planRequestId = null,
        CancellationToken ct = default);

    Task<LearningQualityReportDto> GetTopicReportAsync(
        Guid userId,
        Guid? topicId,
        CancellationToken ct = default);
}

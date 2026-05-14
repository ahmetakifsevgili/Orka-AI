using Orka.Core.DTOs;
using Orka.Core.DTOs.Chat;
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

public interface IAssessmentCalibrationService
{
    Task<AssessmentCalibrationRunDto> RunAsync(
        Guid userId,
        Guid? topicId,
        CancellationToken ct = default);

    Task<AssessmentCalibrationRunDto?> GetLatestAsync(
        Guid userId,
        Guid? topicId,
        CancellationToken ct = default);
}

public interface IAdaptiveAssessmentSelector
{
    Task<AdaptiveAssessmentDecisionDto?> SelectNextAsync(
        AdaptiveAssessmentSession session,
        CancellationToken ct = default);
}

public interface IAdaptiveAssessmentSessionService
{
    Task<AdaptiveAssessmentSessionDto> StartAsync(
        Guid userId,
        AdaptiveAssessmentStartRequest request,
        CancellationToken ct = default);

    Task<AdaptiveAssessmentNextItemDto> GetNextAsync(
        Guid userId,
        Guid adaptiveSessionId,
        CancellationToken ct = default);

    Task<AdaptiveAssessmentNextItemDto> RecordAnswerAsync(
        Guid userId,
        Guid adaptiveSessionId,
        AdaptiveAssessmentAnswerRequest request,
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

public interface ILearningMemoryService
{
    Task<LearningMemoryLiteDto> BuildAsync(
        Guid userId,
        IReadOnlyCollection<Guid> topicScopeIds,
        EvidenceQualityDto? evidenceQuality = null,
        CancellationToken ct = default);
}

public interface IAdaptiveStudyPlannerService
{
    Task<AdaptiveStudyPlanDto> BuildAsync(
        Guid userId,
        AdaptiveStudyPlanRequestDto? request,
        LearningMemoryLiteDto? learningMemory,
        IReadOnlyList<DashboardWeakConceptDto> weakConcepts,
        DashboardSourceHealthDto sourceHealth,
        DashboardActivePlanDto? activePlan,
        int dueReviewCount,
        IReadOnlyCollection<Guid> topicScopeIds,
        CancellationToken ct = default);
}

public interface IExamFrameworkService
{
    Task<IReadOnlyList<ExamDefinitionDto>> GetDefinitionsAsync(
        Guid userId,
        CancellationToken ct = default);

    Task<ExamDefinitionDto?> GetTreeAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        CancellationToken ct = default);

    Task<ExamDefinitionDto> ImportTreeAsync(
        Guid userId,
        ExamTreeImportDto request,
        CancellationToken ct = default);

    Task<ExamDefinitionDto> CreateSystemSkeletonAsync(
        CancellationToken ct = default);

    Task<ExamDefinitionDto> CreateSystemSkeletonAsync(
        string examCode,
        CancellationToken ct = default);
}

public interface IQuestionBankService
{
    Task<IReadOnlyList<QuestionItemDto>> GetQuestionsAsync(
        Guid userId,
        QuestionBankFilterDto filters,
        CancellationToken ct = default);

    Task<QuestionItemDto?> GetQuestionAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default);

    Task<QuestionItemDto> CreateQuestionAsync(
        Guid userId,
        CreateQuestionDto request,
        CancellationToken ct = default);

    Task<QuestionItemDto?> UpdateQuestionAsync(
        Guid userId,
        Guid questionId,
        UpdateQuestionDto request,
        CancellationToken ct = default);

    Task<QuestionItemDto?> SubmitForReviewAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default);

    Task<QuestionItemDto?> PublishQuestionAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default);

    Task<bool> SoftDeleteQuestionAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default);

    Task<QuestionAssetDto> CreateAssetAsync(
        Guid userId,
        CreateQuestionAssetDto request,
        CancellationToken ct = default);

    Task<QuestionAssetDto?> GetAssetAsync(
        Guid userId,
        Guid assetId,
        CancellationToken ct = default);

    Task<QuestionStimulusDto> CreateStimulusAsync(
        Guid userId,
        CreateQuestionStimulusDto request,
        CancellationToken ct = default);

    Task<QuestionItemDto?> AttachStimulusAsync(
        Guid userId,
        Guid questionId,
        QuestionStimulusLinkDto request,
        CancellationToken ct = default);

    Task<QuestionItemDto?> AddQuestionContentBlockAsync(
        Guid userId,
        Guid questionId,
        CreateQuestionContentBlockDto request,
        CancellationToken ct = default);

    Task<QuestionItemDto?> AddOptionContentBlockAsync(
        Guid userId,
        Guid optionId,
        CreateQuestionOptionContentBlockDto request,
        CancellationToken ct = default);
}

public interface IQuestionImportService
{
    Task<QuestionImportPreviewDto> PreviewImportAsync(
        Guid userId,
        QuestionImportRequestDto request,
        CancellationToken ct = default);

    Task<QuestionImportPreviewDto> PreviewPackageImportAsync(
        Guid userId,
        QuestionImportPackageDto request,
        CancellationToken ct = default);

    Task<QuestionImportPreviewDto> PreviewAikenImportAsync(
        Guid userId,
        QuestionImportTextAdapterRequestDto request,
        CancellationToken ct = default);

    Task<QuestionImportPreviewDto> PreviewGiftImportAsync(
        Guid userId,
        QuestionImportTextAdapterRequestDto request,
        CancellationToken ct = default);

    Task<QuestionImportPreviewDto> PreviewQtiImportAsync(
        Guid userId,
        QuestionImportTextAdapterRequestDto request,
        CancellationToken ct = default);

    Task<QuestionImportPreviewDto> PreviewMoodleImportAsync(
        Guid userId,
        QuestionImportTextAdapterRequestDto request,
        CancellationToken ct = default);

    Task<QuestionImportResultDto> ApproveImportAsync(
        Guid userId,
        QuestionImportApprovalDto request,
        CancellationToken ct = default);

    Task<QuestionImportPreviewDto?> GetImportPreviewAsync(
        Guid userId,
        Guid importPreviewId,
        CancellationToken ct = default);
}

public interface IQuestionDraftGenerationService
{
    Task<QuestionDraftPreviewDto> PreviewDraftGenerationAsync(
        Guid userId,
        QuestionDraftGenerationRequestDto request,
        CancellationToken ct = default);

    Task<QuestionDraftApprovalResultDto> ApproveDraftsToQuestionBankAsync(
        Guid userId,
        QuestionDraftApprovalDto request,
        CancellationToken ct = default);

    Task<QuestionDraftPreviewDto?> GetDraftGenerationPreviewAsync(
        Guid userId,
        Guid draftPreviewId,
        CancellationToken ct = default);
}

public interface IContentOperationsService
{
    Task<QuestionReviewWorkflowDto?> GetWorkflowAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default);

    Task<QuestionReviewWorkflowDto?> SubmitQuestionForReviewAsync(
        Guid userId,
        Guid questionId,
        SubmitQuestionReviewDto request,
        CancellationToken ct = default);

    Task<QuestionReviewWorkflowDto?> AssignReviewerAsync(
        Guid userId,
        Guid questionId,
        AssignQuestionReviewerDto request,
        CancellationToken ct = default);

    Task<QuestionReviewWorkflowDto?> AdvanceReviewStageAsync(
        Guid userId,
        Guid questionId,
        AdvanceQuestionReviewStageDto request,
        CancellationToken ct = default);

    Task<QuestionReviewWorkflowDto?> RejectQuestionAsync(
        Guid userId,
        Guid questionId,
        RejectQuestionReviewDto request,
        CancellationToken ct = default);

    Task<QuestionReviewWorkflowDto?> RetireQuestionAsync(
        Guid userId,
        Guid questionId,
        RetireQuestionDto request,
        CancellationToken ct = default);

    Task<QuestionPublishReadinessDto?> GetPublishReadinessAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default);

    Task<QuestionItemDto?> PublishApprovedQuestionAsync(
        Guid userId,
        Guid questionId,
        PublishQuestionContentDto request,
        CancellationToken ct = default);

    Task<IReadOnlyList<QuestionContentVersionDto>> GetQuestionVersionsAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default);
}

public interface ICurriculumSourceRegistryService
{
    Task<IReadOnlyList<SourceRegistryItemDto>> GetSourcesAsync(
        Guid userId,
        CancellationToken ct = default);

    Task<SourceRegistryItemDto?> GetSourceAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken ct = default);

    Task<SourceRegistryItemDto> RegisterSourceAsync(
        Guid userId,
        RegisterSourceRegistryItemDto request,
        CancellationToken ct = default);

    Task<SourceRegistryItemDto?> VerifySourceAsync(
        Guid userId,
        Guid sourceId,
        VerifySourceRegistryItemDto request,
        CancellationToken ct = default);

    Task<ContentLicenseReviewDto?> ReviewSourceLicenseAsync(
        Guid userId,
        Guid sourceId,
        ReviewSourceLicenseDto request,
        CancellationToken ct = default);

    Task<CurriculumVersionDto> CreateCurriculumVersionAsync(
        Guid userId,
        CreateCurriculumVersionDto request,
        CancellationToken ct = default);

    Task<CurriculumVersionDto?> GetCurriculumVersionAsync(
        Guid userId,
        Guid curriculumVersionId,
        CancellationToken ct = default);

    Task<IReadOnlyList<CurriculumVersionDto>> GetCurriculumVersionsForExamAsync(
        Guid userId,
        string examCode,
        CancellationToken ct = default);

    Task<CurriculumVersionDto?> DeprecateCurriculumVersionAsync(
        Guid userId,
        Guid curriculumVersionId,
        DeprecateCurriculumVersionDto request,
        CancellationToken ct = default);

    Task<CurriculumVersionDto?> SupersedeCurriculumVersionAsync(
        Guid userId,
        Guid curriculumVersionId,
        SupersedeCurriculumVersionDto request,
        CancellationToken ct = default);

    Task<CurriculumNodeDto?> AddCurriculumNodeAsync(
        Guid userId,
        Guid curriculumVersionId,
        CreateCurriculumNodeDto request,
        CancellationToken ct = default);

    Task<CurriculumOutcomeMappingDto?> MapOutcomeAsync(
        Guid userId,
        Guid curriculumVersionId,
        CreateCurriculumOutcomeMappingDto request,
        CancellationToken ct = default);

    Task<CurriculumOutcomeSourceDto> GetOutcomeSourcesAsync(
        Guid userId,
        Guid examOutcomeId,
        CancellationToken ct = default);
}

public interface ICentralExamStudyService
{
    Task<IReadOnlyList<CentralExamDto>> GetCentralExamsAsync(
        Guid userId,
        CancellationToken ct = default);

    Task<CentralExamStudyHomeDto> GetKpssStudyHomeAsync(
        Guid userId,
        string? variantCode = null,
        CancellationToken ct = default);

    Task<CentralExamStudyHomeDto?> GetStudyHomeAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        CancellationToken ct = default);

    Task<CentralExamCountdownDto> GetKpssCountdownAsync(
        Guid userId,
        string? variantCode = null,
        CancellationToken ct = default);

    Task<CentralExamPracticeEntryDto> GetKpssTurkceParagrafEntryAsync(
        Guid userId,
        string? variantCode = null,
        CancellationToken ct = default);

    Task<PracticeSessionDto> StartKpssTurkceParagrafPracticeAsync(
        Guid userId,
        PracticeStartRequestDto request,
        CancellationToken ct = default);

    Task<PracticeResultDto> SubmitKpssTurkceParagrafPracticeAsync(
        Guid userId,
        PracticeSubmitRequestDto request,
        CancellationToken ct = default);

    Task<PracticeResultDto?> GetPracticeAttemptAsync(
        Guid userId,
        Guid practiceAttemptId,
        CancellationToken ct = default);
}

public interface ICentralExamDenemeService
{
    Task<IReadOnlyList<CentralExamDenemeBlueprintDto>> GetDenemeBlueprintsAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        CancellationToken ct = default);

    Task<CentralExamDenemeBlueprintDto?> GetDenemeBlueprintAsync(
        Guid userId,
        string blueprintCode,
        string? variantCode = null,
        CancellationToken ct = default);

    Task<CentralExamDenemeSessionDto> StartDenemeAsync(
        Guid userId,
        string blueprintCode,
        CentralExamDenemeStartRequestDto request,
        CancellationToken ct = default);

    Task<CentralExamDenemeResultDto> SubmitDenemeAsync(
        Guid userId,
        CentralExamDenemeSubmitRequestDto request,
        CancellationToken ct = default);

    Task<CentralExamDenemeResultDto?> GetDenemeAttemptAsync(
        Guid userId,
        Guid attemptId,
        CancellationToken ct = default);
}

public interface IQuestionQualityAnalyticsService
{
    Task<RecalculateQuestionAnalyticsResultDto?> RecalculateQuestionAnalyticsAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default);

    Task<RecalculateExamAnalyticsResultDto> RecalculateCentralExamAnalyticsAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        CancellationToken ct = default);

    Task<QuestionItemAnalyticsDto?> GetQuestionAnalyticsAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default);

    Task<IReadOnlyList<QuestionQualityReviewSignalDto>> GetQuestionQualitySignalsAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default);

    Task<CentralExamQualityOverviewDto?> GetCentralExamQualityOverviewAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        CancellationToken ct = default);

    Task<CentralExamBlueprintCoverageDto?> GetBlueprintCoverageAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
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

public interface ITutorTraceProjectionService
{
    Task<TutorTraceTimelineDto> GetTimelineAsync(
        Guid userId,
        Guid sessionId,
        string afterId = "0-0",
        int take = 50,
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

public interface IStandardsAlignmentService
{
    Task<StandardsSummaryDto> GetSummaryAsync(Guid userId, Guid? topicId, CancellationToken ct = default);
}

public interface IStandardsValidationService
{
    Task<StandardsValidationRunDto> ValidateAsync(Guid userId, Guid? topicId, CancellationToken ct = default);
}

public interface IStandardsExportService
{
    Task<StandardsExportRunDto> ExportAsync(Guid userId, Guid? topicId, string exportType = "combined", CancellationToken ct = default);
}

public interface IProviderGovernanceService
{
    Task<ProviderGovernanceSummaryDto> GetSummaryAsync(Guid? userId = null, CancellationToken ct = default);
}

public interface IRetentionCleanupService
{
    Task<AudioRetentionSummaryDto> GetAudioRetentionSummaryAsync(CancellationToken ct = default);

    Task<AudioRetentionSummaryDto> PurgeExpiredAudioAsync(CancellationToken ct = default);
}

public interface IRedisStreamMaintenanceService
{
    Task<RedisStreamMaintenanceSummaryDto> GetSummaryAsync(CancellationToken ct = default);

    Task<RedisStreamMaintenanceSummaryDto> TrimTutorEventStreamsAsync(CancellationToken ct = default);
}

public interface IDbIndexAuditService
{
    Task<DbIndexAuditSummaryDto> AuditAsync(CancellationToken ct = default);
}

public interface IV1RegressionGateService
{
    Task<V1RegressionGateDto> EvaluateAsync(CancellationToken ct = default);
}

public interface IProductionReadinessService
{
    Task<ProductionReadinessDto> GetV1ReadinessAsync(Guid? userId = null, CancellationToken ct = default);
}

using Orka.Core.DTOs.Korteks;
using Orka.Core.Enums;
using System.Text.Json.Serialization;

namespace Orka.Core.DTOs.PlanDiagnostic;

public sealed class PlanDiagnosticStateDto
{
    public Guid PlanRequestId { get; set; }
    public Guid UserId { get; set; }
    public Guid TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public Guid? IntentRequestId { get; set; }
    public string RawStudyRequest { get; set; } = string.Empty;
    public string ApprovedMainTopic { get; set; } = string.Empty;
    public string ApprovedFocusArea { get; set; } = string.Empty;
    public string ApprovedStudyGoal { get; set; } = string.Empty;
    public string ApprovedResearchIntent { get; set; } = string.Empty;
    public string UserLevel { get; set; } = "Bilinmiyor";
    public PlanDiagnosticStatus Status { get; set; }
    public string CompressedResearchContextJson { get; set; } = string.Empty;
    public string CompressedResearchPromptBlock { get; set; } = string.Empty;
    public string LearningBlueprintJson { get; set; } = string.Empty;
    public string LearningBlueprintHash { get; set; } = string.Empty;
    public string LearningBlueprintDomain { get; set; } = string.Empty;
    public string LearningBlueprintSourceConfidence { get; set; } = string.Empty;
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string ConceptGraphJson { get; set; } = string.Empty;
    public string ConceptGraphHash { get; set; } = string.Empty;
    public string ConceptGraphCacheKey { get; set; } = string.Empty;
    public Guid? ConceptGraphQualityRunId { get; set; }
    public string ConceptGraphQualityStatus { get; set; } = "unknown";
    public string SourceBundleHash { get; set; } = string.Empty;
    public string SourceBundleCacheKey { get; set; } = string.Empty;
    public Guid? AssessmentDraftId { get; set; }
    public string AssessmentGrammarJson { get; set; } = string.Empty;
    public string AssessmentDraftCacheKey { get; set; } = string.Empty;
    public Guid? AssessmentQualityRunId { get; set; }
    public string AssessmentQualityStatus { get; set; } = "unknown";
    public Guid? DiagnosticProfileId { get; set; }
    public Guid? QualityReportId { get; set; }
    public GroundingMode GroundingMode { get; set; }
    public int SourceCount { get; set; }
    public Guid QuizRunId { get; set; }
    public int QuizQuestionCount { get; set; }
    public int AnsweredQuestionCount { get; set; }
    public DateTime? QuizCompletedAt { get; set; }
    public Guid? GeneratedPlanRootTopicId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class StartPlanDiagnosticRequest
{
    [JsonPropertyName("topicId")]
    public Guid TopicId { get; set; }
    [JsonPropertyName("sessionId")]
    public Guid? SessionId { get; set; }
    [JsonPropertyName("topicTitle")]
    public string? TopicTitle { get; set; }
    [JsonPropertyName("userLevel")]
    public string? UserLevel { get; set; }
    [JsonPropertyName("intentRequestId")]
    public Guid? IntentRequestId { get; set; }
    [JsonPropertyName("rawStudyRequest")]
    public string? RawStudyRequest { get; set; }
    [JsonPropertyName("approvedMainTopic")]
    public string? ApprovedMainTopic { get; set; }
    [JsonPropertyName("approvedFocusArea")]
    public string? ApprovedFocusArea { get; set; }
    [JsonPropertyName("approvedStudyGoal")]
    public string? ApprovedStudyGoal { get; set; }
    [JsonPropertyName("approvedResearchIntent")]
    public string? ApprovedResearchIntent { get; set; }
}

public sealed class StartPlanDiagnosticResponse
{
    public Guid PlanRequestId { get; set; }
    public Guid QuizRunId { get; set; }
    public Guid TopicId { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public PlanDiagnosticStatus Status { get; set; }
    public string QuestionsJson { get; set; } = "[]";
    public GroundingMode GroundingMode { get; set; }
    public int SourceCount { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public Guid? AssessmentDraftId { get; set; }
    public string ConceptGraphQualityStatus { get; set; } = "unknown";
    public string AssessmentQualityStatus { get; set; } = "unknown";
    public Guid? QualityReportId { get; set; }
    public string SourceBundleHash { get; set; } = string.Empty;
    public string SourceBundleCacheKey { get; set; } = string.Empty;
    public int QuizQuestionCount { get; set; }
    public Guid? IntentRequestId { get; set; }
    public string ApprovedMainTopic { get; set; } = string.Empty;
    public string ApprovedFocusArea { get; set; } = string.Empty;
    public string ApprovedStudyGoal { get; set; } = string.Empty;
    public string ApprovedResearchIntent { get; set; } = string.Empty;
}

public sealed class AnalyzeStudyIntentRequest
{
    public string RawRequest { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public string? ExistingTopicTitle { get; set; }
    public string? Correction { get; set; }
}

public sealed class StudyIntentPreviewResponse
{
    public Guid IntentRequestId { get; set; } = Guid.NewGuid();
    public string RawRequest { get; set; } = string.Empty;
    public string MainTopic { get; set; } = string.Empty;
    public string FocusArea { get; set; } = string.Empty;
    public string StudyGoal { get; set; } = string.Empty;
    public string ResearchIntent { get; set; } = string.Empty;
    public string ConfirmationText { get; set; } = string.Empty;
    public string Language { get; set; } = "tr";
    public List<string> ClarifyingNotes { get; set; } = [];
    public bool RequiresUserConfirmation { get; set; } = true;
}

public sealed class FinalizePlanDiagnosticRequest
{
    public Guid PlanRequestId { get; set; }
}

public sealed class FinalizePlanDiagnosticResponse
{
    public Guid PlanRequestId { get; set; }
    public PlanDiagnosticStatus Status { get; set; }
    public bool PlanGenerated { get; set; }
    public string? Message { get; set; }
    public Guid? GeneratedPlanRootTopicId { get; set; }
    public List<Guid> GeneratedTopicIds { get; set; } = [];
}

public sealed class PlanDiagnosticAnswerResponse
{
    public Guid PlanRequestId { get; set; }
    public Guid QuizRunId { get; set; }
    public PlanDiagnosticStatus Status { get; set; }
    public int AnsweredQuestionCount { get; set; }
    public int QuizQuestionCount { get; set; }
}

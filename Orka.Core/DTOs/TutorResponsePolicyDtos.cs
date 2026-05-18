namespace Orka.Core.DTOs;

public sealed class TutorResponsePolicyRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string? UserMessage { get; set; }
    public bool ActiveQuizUnsubmitted { get; set; }
}

public sealed class TutorResponseQualityEvaluationRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public TutorResponsePolicyDto? Policy { get; set; }
    public string AssistantAnswer { get; set; } = string.Empty;
    public bool ActiveQuizUnsubmitted { get; set; }
}

public sealed class TutorResponsePolicyDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public Guid? PlanQualitySnapshotId { get; set; }
    public string TeachingMove { get; set; } = "explain";
    public string ResponseDepth { get; set; } = "normal";
    public string GroundingPolicy { get; set; } = "model_assisted_unsourced";
    public string RemediationPolicy { get; set; } = "none";
    public string ToolPolicy { get; set; } = "no_tool";
    public string AnswerSafety { get; set; } = "safe";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string LatestAssessmentMode { get; set; } = "unknown";
    public string LatestMisconceptionConfidence { get; set; } = "none";
    public string QualityStatus { get; set; } = "usable";
    public IReadOnlyList<TutorContextUseDto> ContextUse { get; set; } = Array.Empty<TutorContextUseDto>();
    public IReadOnlyList<TutorNextLearningActionDto> NextActions { get; set; } = Array.Empty<TutorNextLearningActionDto>();
    public IReadOnlyList<TutorAnswerSafetyIssueDto> SafetyIssues { get; set; } = Array.Empty<TutorAnswerSafetyIssueDto>();
    public IReadOnlyList<TutorResponseQualityIssueDto> Warnings { get; set; } = Array.Empty<TutorResponseQualityIssueDto>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TutorContextUseDto
{
    public string ContextType { get; set; } = string.Empty;
    public string Status { get; set; } = "not_available";
    public string UserSafeSummary { get; set; } = string.Empty;
}

public sealed class TutorNextLearningActionDto
{
    public string ActionType { get; set; } = "continue_plan";
    public string UserSafeLabel { get; set; } = "Plana devam et";
    public string? TargetConceptKey { get; set; }
    public string Priority { get; set; } = "normal";
}

public sealed class TutorAnswerSafetyIssueDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public string UserSafeMessage { get; set; } = string.Empty;
}

public sealed class TutorResponseQualityEvaluationDto
{
    public string QualityStatus { get; set; } = "usable";
    public decimal ContextUseScore { get; set; }
    public decimal GroundingScore { get; set; }
    public decimal PedagogyScore { get; set; }
    public decimal RemediationScore { get; set; }
    public decimal SafetyScore { get; set; }
    public decimal ToolUseScore { get; set; }
    public IReadOnlyList<TutorResponseQualityIssueDto> BlockingIssues { get; set; } = Array.Empty<TutorResponseQualityIssueDto>();
    public IReadOnlyList<TutorResponseQualityIssueDto> WarningIssues { get; set; } = Array.Empty<TutorResponseQualityIssueDto>();
    public TutorResponsePolicyDto Policy { get; set; } = new();
    public DateTimeOffset EvaluatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TutorResponseQualityIssueDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public string UserSafeMessage { get; set; } = string.Empty;
}

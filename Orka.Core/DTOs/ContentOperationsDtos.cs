namespace Orka.Core.DTOs;

public sealed class QuestionReviewWorkflowDto
{
    public Guid Id { get; set; }
    public Guid QuestionItemId { get; set; }
    public string CurrentStage { get; set; } = "authoring";
    public string Status { get; set; } = "open";
    public bool HasAssignedReviewer { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<QuestionReviewEventDto> Events { get; set; } = [];
}

public sealed class QuestionReviewEventDto
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = "comment";
    public string? FromStage { get; set; }
    public string? ToStage { get; set; }
    public string? Reason { get; set; }
    public string? SafeNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class SubmitQuestionReviewDto
{
    public string? SafeNote { get; set; }
}

public sealed class AssignQuestionReviewerDto
{
    public Guid? AssignedReviewerUserId { get; set; }
    public string? SafeNote { get; set; }
}

public sealed class AdvanceQuestionReviewStageDto
{
    public string ToStage { get; set; } = string.Empty;
    public string? SafeNote { get; set; }
}

public sealed class RejectQuestionReviewDto
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class RetireQuestionDto
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class PublishQuestionContentDto
{
    public string? SafeNote { get; set; }
}

public sealed class QuestionPublishReadinessDto
{
    public Guid QuestionItemId { get; set; }
    public Guid? WorkflowId { get; set; }
    public string? WorkflowStage { get; set; }
    public string? WorkflowStatus { get; set; }
    public bool IsReadyToPublish { get; set; }
    public string RecommendedNextReviewStage { get; set; } = "authoring";
    public List<QuestionPublishIssueDto> BlockingIssues { get; set; } = [];
    public List<QuestionPublishIssueDto> WarningIssues { get; set; } = [];
}

public sealed class QuestionPublishIssueDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "blocking";
    public string Area { get; set; } = "question";
    public string Message { get; set; } = string.Empty;
}

public sealed class QuestionContentVersionDto
{
    public Guid Id { get; set; }
    public Guid QuestionItemId { get; set; }
    public int VersionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Reason { get; set; }
}

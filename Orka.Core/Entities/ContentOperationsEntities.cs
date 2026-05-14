namespace Orka.Core.Entities;

public sealed class QuestionReviewWorkflow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionItemId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string CurrentStage { get; set; } = "authoring";
    public string Status { get; set; } = "open";
    public Guid? AssignedReviewerUserId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public bool IsDeleted { get; set; }

    public QuestionItem QuestionItem { get; set; } = null!;
    public User? OwnerUser { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public User? UpdatedByUser { get; set; }
    public User? AssignedReviewerUser { get; set; }
    public List<QuestionReviewEvent> Events { get; set; } = [];
}

public sealed class QuestionReviewEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionReviewWorkflowId { get; set; }
    public Guid QuestionItemId { get; set; }
    public Guid ActorUserId { get; set; }
    public string EventType { get; set; } = "comment";
    public string? FromStage { get; set; }
    public string? ToStage { get; set; }
    public string? Reason { get; set; }
    public string? SafeNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public QuestionReviewWorkflow Workflow { get; set; } = null!;
    public QuestionItem QuestionItem { get; set; } = null!;
    public User ActorUser { get; set; } = null!;
}

public sealed class QuestionPublishReadinessSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionItemId { get; set; }
    public Guid? WorkflowId { get; set; }
    public bool IsReadyToPublish { get; set; }
    public string BlockingIssuesJson { get; set; } = "[]";
    public string WarningIssuesJson { get; set; } = "[]";
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public Guid CheckedByUserId { get; set; }
    public bool IsDeleted { get; set; }

    public QuestionItem QuestionItem { get; set; } = null!;
    public QuestionReviewWorkflow? Workflow { get; set; }
    public User CheckedByUser { get; set; } = null!;
}

public sealed class QuestionContentVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionItemId { get; set; }
    public int VersionNumber { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
    public bool IsDeleted { get; set; }

    public QuestionItem QuestionItem { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}

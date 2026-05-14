namespace Orka.Core.Entities;

public sealed class QuestionImportPreview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public User OwnerUser { get; set; } = null!;
    public string Status { get; set; } = "pending";
    public int TotalCount { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int WarningCount { get; set; }
    public string ImportFormat { get; set; } = "structured_json";
    public string? PackageTitle { get; set; }
    public string? PackageVersion { get; set; }
    public string? NormalizedPackageJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public DateTime? ApprovedAt { get; set; }
    public bool IsDeleted { get; set; }
    public List<QuestionImportPreviewItem> Items { get; set; } = [];
}

public sealed class QuestionImportPreviewItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionImportPreviewId { get; set; }
    public QuestionImportPreview Preview { get; set; } = null!;
    public int RowIndex { get; set; }
    public string? ExternalId { get; set; }
    public string Status { get; set; } = "rejected";
    public string IssuesJson { get; set; } = "[]";
    public string? NormalizedQuestionJson { get; set; }
    public Guid? DuplicateQuestionId { get; set; }
    public Guid? CreatedQuestionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}

namespace Orka.Core.Entities;

public sealed class LearningNotebookPack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? WikiPageId { get; set; }
    public string? WikiPageTitle { get; set; }
    public string? WikiPageKey { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public Guid? SourceEvidenceBundleId { get; set; }
    public Guid? WikiNotebookSnapshotId { get; set; }
    public Guid? PlanQualitySnapshotId { get; set; }
    public Guid? AssessmentQualitySnapshotId { get; set; }
    public string PackType { get; set; } = "milestone_review";
    public string PackStatus { get; set; } = "draft";
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string CompletedConceptKeysJson { get; set; } = "[]";
    public string WeakConceptKeysJson { get; set; } = "[]";
    public string MisconceptionKeysJson { get; set; } = "[]";
    public string ArtifactIdsJson { get; set; } = "[]";
    public string NextActionsJson { get; set; } = "[]";
    public string WarningsJson { get; set; } = "[]";
    public string SafeMetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}

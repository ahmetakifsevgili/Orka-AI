namespace Orka.Core.Entities;

public sealed class LearningArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public Guid? TeachingArtifactId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public Guid? PlanQualitySnapshotId { get; set; }
    public Guid? AssessmentQualitySnapshotId { get; set; }
    public Guid? SourceEvidenceBundleId { get; set; }
    public string? WikiNotebookSectionKey { get; set; }
    public string? ConceptKey { get; set; }
    public string? ConceptLabel { get; set; }
    public string ArtifactType { get; set; } = "concept_summary";
    public string ArtifactStatus { get; set; } = "draft";
    public string Origin { get; set; } = "manual";
    public string RenderFormat { get; set; } = "markdown";
    public string Title { get; set; } = string.Empty;
    public string SafeContent { get; set; } = string.Empty;
    public string? ContentJson { get; set; }
    public string SourceBasis { get; set; } = "evidence_insufficient";
    public string CitationIdsJson { get; set; } = "[]";
    public string ToolTraceIdsJson { get; set; } = "[]";
    public string AccessibilityJson { get; set; } = "{}";
    public string SafetyWarningsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsDeleted { get; set; }
}

namespace Orka.Core.DTOs;

public sealed class LearningArtifactDto
{
    public Guid Id { get; set; }
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
    public IReadOnlyList<string> CitationIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<Guid> ToolTraceIds { get; set; } = Array.Empty<Guid>();
    public LearningArtifactAccessibilityDto Accessibility { get; set; } = new();
    public LearningArtifactSafetyDto Safety { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LearningArtifactRequestDto
{
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
    public IReadOnlyList<string> CitationIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<Guid> ToolTraceIds { get; set; } = Array.Empty<Guid>();
    public LearningArtifactAccessibilityDto Accessibility { get; set; } = new();
}

public sealed class LearningArtifactRefreshRequestDto
{
    public string? Reason { get; set; }
}

public sealed class LearningArtifactListDto
{
    public IReadOnlyList<LearningArtifactDto> Items { get; set; } = Array.Empty<LearningArtifactDto>();
    public int Count { get; set; }
}

public sealed class LearningArtifactSafetyDto
{
    public string Status { get; set; } = "safe";
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> BlockingIssues { get; set; } = Array.Empty<string>();
}

public sealed class LearningArtifactAccessibilityDto
{
    public string Status { get; set; } = "unknown";
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public string? Summary { get; set; }
    public string? TextFallback { get; set; }
    public string? Language { get; set; }
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
}

public sealed class LearningArtifactSourceBasisDto
{
    public string SourceBasis { get; set; } = "evidence_insufficient";
    public Guid? SourceEvidenceBundleId { get; set; }
    public IReadOnlyList<string> CitationIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> UserSafeWarnings { get; set; } = Array.Empty<string>();
}

public sealed class LearningArtifactRenderDto
{
    public string RenderFormat { get; set; } = "markdown";
    public string SafeContent { get; set; } = string.Empty;
    public string? ContentJson { get; set; }
    public LearningArtifactAccessibilityDto Accessibility { get; set; } = new();
}

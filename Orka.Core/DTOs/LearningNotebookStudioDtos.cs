namespace Orka.Core.DTOs;

public static class NotebookStudioPhaseScope
{
    public static readonly IReadOnlyList<string> All =
    [
        "phase_1_contract",
        "phase_2_graph_metadata",
        "phase_3_text_notebook",
        "phase_4_slide_diagram",
        "phase_5_search_template_export",
        "phase_6_internal_connections",
        "phase_7_audio_classroom"
    ];
}

public sealed class LearningNotebookPackDto
{
    public Guid Id { get; set; }
    public Guid TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? WikiPageId { get; set; }
    public string? WikiPageTitle { get; set; }
    public string? WikiPageKey { get; set; }
    public string? SourceSurface { get; set; }
    public Guid? SourceId { get; set; }
    public string? SourceTitle { get; set; }
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
    public IReadOnlyList<string> CompletedConceptKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> WeakConceptKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MisconceptionKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> PhaseScope { get; set; } = Array.Empty<string>();
    public IReadOnlyList<Guid> ArtifactIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyList<LearningArtifactDto> Artifacts { get; set; } = Array.Empty<LearningArtifactDto>();
    public IReadOnlyList<NotebookStudioNextActionDto> NextActions { get; set; } = Array.Empty<NotebookStudioNextActionDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LearningNotebookPackListDto
{
    public int Count { get; set; }
    public IReadOnlyList<LearningNotebookPackDto> Items { get; set; } = Array.Empty<LearningNotebookPackDto>();
}

public sealed class LearningNotebookPackRequestDto
{
    public Guid? SessionId { get; set; }
    public Guid? WikiPageId { get; set; }
    public Guid? SourceId { get; set; }
    public string? SourceSurface { get; set; }
    public string PackType { get; set; } = "milestone_review";
    public string? FocusConceptKey { get; set; }
    public string? UserGoal { get; set; }
    public bool IncludeArtifacts { get; set; } = true;
}

public sealed class LearningNotebookArtifactRequestDto
{
    public string ArtifactType { get; set; } = "study_guide";
    public string? ConceptKey { get; set; }
    public Guid? WikiPageId { get; set; }
}

public sealed class NotebookExportRequestDto
{
    public string Format { get; set; } = "markdown";
    public Guid? SlideDeckArtifactId { get; set; }
}

public sealed class NotebookExportResultDto
{
    public Guid PackId { get; set; }
    public Guid? SlideDeckArtifactId { get; set; }
    public string Surface { get; set; } = "wiki";
    public string ContextType { get; set; } = "wiki_page";
    public Guid? WikiPageId { get; set; }
    public Guid? SourceId { get; set; }
    public string ExportScope { get; set; } = "wiki_export_scope";
    public bool SourceUploadAllowed { get; set; }
    public bool CrossSurfaceSync { get; set; }
    public string Format { get; set; } = "markdown";
    public string Status { get; set; } = "preview_ready";
    public string ExportReadiness { get; set; } = "preview_ready";
    public string Title { get; set; } = string.Empty;
    public string SourceBasis { get; set; } = "evidence_insufficient";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string Content { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/markdown";
    public string? FileName { get; set; }
    public bool BinaryExportAvailable { get; set; }
    public bool PptxLocalProofAvailable { get; set; }
    public NotebookSlideExportPreviewDto Preview { get; set; } = new();
    public NotebookExportSafetyDto Safety { get; set; } = new();
    public NotebookExportAccessibilityDto Accessibility { get; set; } = new();
    public IReadOnlyList<string> TemplateKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SearchFilterKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> InternalConnectionKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> PhaseScope { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NotebookSlideExportPreviewDto
{
    public Guid PackId { get; set; }
    public Guid? SlideDeckArtifactId { get; set; }
    public string Surface { get; set; } = "wiki";
    public string ContextType { get; set; } = "wiki_page";
    public Guid? WikiPageId { get; set; }
    public Guid? SourceId { get; set; }
    public string ExportScope { get; set; } = "wiki_export_scope";
    public bool SourceUploadAllowed { get; set; }
    public bool CrossSurfaceSync { get; set; }
    public string DeckTitle { get; set; } = string.Empty;
    public int SlideCount { get; set; }
    public string SourceBasis { get; set; } = "evidence_insufficient";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string ExportReadiness { get; set; } = "preview_ready";
    public IReadOnlyList<NotebookSlideExportItemDto> Slides { get; set; } = Array.Empty<NotebookSlideExportItemDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> TemplateKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SearchFilterKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> InternalConnectionKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> PhaseScope { get; set; } = Array.Empty<string>();
    public string AccessibilitySummary { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NotebookSlideExportItemDto
{
    public int Order { get; set; }
    public string SlideId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public IReadOnlyList<string> Bullets { get; set; } = Array.Empty<string>();
    public bool HasSpeakerNotes { get; set; }
    public string? SpeakerNotes { get; set; }
    public string? SourceLabel { get; set; }
    public string? VisualSuggestion { get; set; }
    public string? CheckpointQuestion { get; set; }
    public string? MisconceptionWarning { get; set; }
    public string AccessibilitySummary { get; set; } = string.Empty;
}

public sealed class NotebookExportSafetyDto
{
    public string Status { get; set; } = "safe";
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> BlockingIssues { get; set; } = Array.Empty<string>();
}

public sealed class NotebookExportAccessibilityDto
{
    public string Status { get; set; } = "usable";
    public string Summary { get; set; } = string.Empty;
    public bool HasSpeakerNotes { get; set; }
    public bool HasCheckpointQuestions { get; set; }
    public bool HasTextFallback { get; set; } = true;
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
}

public sealed class NotebookStudioNextActionDto
{
    public string ActionType { get; set; } = "continue_plan";
    public string UserSafeLabel { get; set; } = string.Empty;
    public string Priority { get; set; } = "normal";
}

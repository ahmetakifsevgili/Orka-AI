namespace Orka.Core.DTOs;

public sealed class WikiGraphDto
{
    public Guid TopicId { get; set; }
    public Guid? FocusPageId { get; set; }
    public string GraphStatus { get; set; } = "ready";
    public IReadOnlyList<WikiGraphPageDto> Pages { get; set; } = Array.Empty<WikiGraphPageDto>();
    public IReadOnlyList<WikiGraphLinkDto> Links { get; set; } = Array.Empty<WikiGraphLinkDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WikiGraphPageDto
{
    public Guid Id { get; set; }
    public Guid TopicId { get; set; }
    public Guid? ParentWikiPageId { get; set; }
    public string PageKey { get; set; } = string.Empty;
    public string PageType { get; set; } = "concept";
    public string? ConceptKey { get; set; }
    public string? ParentConceptKey { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string? SafeSummary { get; set; }
    public int OrderIndex { get; set; }
    public int BlockCount { get; set; }
    public WikiCurationSummaryDto Curation { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class WikiCurationSummaryDto
{
    public Guid? PageId { get; set; }
    public string? PageKey { get; set; }
    public string? ConceptKey { get; set; }
    public string CurationStatus { get; set; } = "clean";
    public int RetainedSignalCount { get; set; }
    public int MergedSignalCount { get; set; }
    public int SuppressedSignalCount { get; set; }
    public int StaleSignalCount { get; set; }
    public IReadOnlyList<string> RetainedSignals { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MergedSignals { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SuppressedSignals { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> StaleSignals { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public string StudentVisibleSummary { get; set; } = "Wiki sayfasi temiz ve okunabilir durumda.";
    public string NextAction { get; set; } = "continue_learning";
}

public sealed class WikiCopilotContextDto
{
    public Guid? PageId { get; set; }
    public string? PageKey { get; set; }
    public string? ConceptKey { get; set; }
    public string PageTitle { get; set; } = string.Empty;
    public string PageType { get; set; } = "concept";
    public string CurationStatus { get; set; } = "clean";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string MasteryStatus { get; set; } = "unknown";
    public IReadOnlyList<string> WeakConcepts { get; set; } = Array.Empty<string>();
    public string RepairState { get; set; } = "none";
    public int ArtifactCount { get; set; }
    public string NotebookPackStatus { get; set; } = "not_requested";
    public WikiCopilotActionDto? PrimaryAction { get; set; }
    public IReadOnlyList<WikiCopilotActionDto> SuggestedActions { get; set; } = Array.Empty<WikiCopilotActionDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public string StudentVisibleSummary { get; set; } = "Bu sayfa icin guvenli Wiki yardimi hazir.";
    public string NextAction { get; set; } = "continue_learning";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WikiCopilotActionDto
{
    public string ActionType { get; set; } = "no_action";
    public string UserSafeLabel { get; set; } = "Devam et";
    public string UserSafeDescription { get; set; } = string.Empty;
    public string TargetSurface { get; set; } = "wiki";
    public string Availability { get; set; } = "available";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SafetyWarnings { get; set; } = Array.Empty<string>();
}

public sealed class WikiGraphLinkDto
{
    public Guid Id { get; set; }
    public Guid SourcePageId { get; set; }
    public Guid? TargetPageId { get; set; }
    public string TargetPageKey { get; set; } = string.Empty;
    public string LinkType { get; set; } = "related";
    public decimal Strength { get; set; } = 1m;
    public string CreatedBy { get; set; } = "system";
    public string SafeLabel { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreateWikiLinkRequestDto
{
    public Guid SourcePageId { get; set; }
    public Guid? TargetPageId { get; set; }
    public string? TargetPageKey { get; set; }
    public string LinkType { get; set; } = "related";
    public decimal Strength { get; set; } = 1m;
    public string CreatedBy { get; set; } = "manual";
    public string? SafeLabel { get; set; }
}

public sealed class WikiGraphSyncRequestDto
{
    public Guid? ConceptGraphSnapshotId { get; set; }
    public bool IncludeTopicTreeFallback { get; set; } = true;
    public bool CreateSummaryBlocks { get; set; } = true;
}

public sealed class WikiGraphSyncResultDto
{
    public Guid TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string SyncStatus { get; set; } = "ready";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public int CreatedPageCount { get; set; }
    public int UpdatedPageCount { get; set; }
    public int CreatedLinkCount { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public WikiGraphDto Graph { get; set; } = new();
}

public sealed class CreateWikiBlockRequestDto
{
    public string BlockType { get; set; } = "manual_note";
    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public string SourceBasis { get; set; } = "model_assisted";
    public string? Source { get; set; }
    public string? ConceptKey { get; set; }
    public string? MisconceptionKey { get; set; }
    public Guid? QuizAttemptId { get; set; }
    public Guid? SourceEvidenceBundleId { get; set; }
    public Guid? LearningArtifactId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public string Visibility { get; set; } = "normal";
}

public sealed class WikiBlockDto
{
    public Guid Id { get; set; }
    public Guid WikiPageId { get; set; }
    public string BlockType { get; set; } = "manual_note";
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string SourceBasis { get; set; } = "model_assisted";
    public string? ConceptKey { get; set; }
    public string? MisconceptionKey { get; set; }
    public Guid? QuizAttemptId { get; set; }
    public Guid? SourceEvidenceBundleId { get; set; }
    public Guid? LearningArtifactId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public string Visibility { get; set; } = "normal";
    public IReadOnlyList<string> SafetyWarnings { get; set; } = Array.Empty<string>();
    public int OrderIndex { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

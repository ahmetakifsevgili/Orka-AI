using Orka.Core.Enums;

namespace Orka.Core.DTOs.Korteks;

public sealed class KorteksResearchSynthesisContextDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public string? ApprovedIntent { get; set; }
    public string? ApprovedMainTopic { get; set; }
    public string? ApprovedFocusArea { get; set; }
    public string? ApprovedStudyGoal { get; set; }
    public string? Purpose { get; set; }
}

public sealed class KorteksResearchWorkflowDto
{
    public Guid Id { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string? ApprovedIntent { get; set; }
    public string? ApprovedMainTopic { get; set; }
    public string? ApprovedFocusArea { get; set; }
    public string? ApprovedStudyGoal { get; set; }
    public string Status { get; set; } = "completed";
    public string WorkflowVersion { get; set; } = "korteks_synthesis_v1";
    public GroundingMode GroundingMode { get; set; }
    public string SourceConfidence { get; set; } = "low";
    public int SourceCount { get; set; }
    public int ToolCallCount { get; set; }
    public bool CanGroundTutorClaims { get; set; }
    public KorteksEvidenceSummaryDto EvidenceSummary { get; set; } = new();
    public KorteksResearchSynthesisDto Synthesis { get; set; } = new();
    public KorteksConsumerContextsDto ConsumerContexts { get; set; } = new();
    public IReadOnlyList<KorteksSynthesisIssueDto> SafetyIssues { get; set; } = Array.Empty<KorteksSynthesisIssueDto>();
    public string PromptBlock { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class KorteksResearchSynthesisDto
{
    public string Topic { get; set; } = string.Empty;
    public string SourceConfidence { get; set; } = "low";
    public IReadOnlyList<KorteksSynthesisItemDto> KeyFacts { get; set; } = Array.Empty<KorteksSynthesisItemDto>();
    public IReadOnlyList<KorteksSynthesisItemDto> LearningRoute { get; set; } = Array.Empty<KorteksSynthesisItemDto>();
    public IReadOnlyList<KorteksSynthesisItemDto> Prerequisites { get; set; } = Array.Empty<KorteksSynthesisItemDto>();
    public IReadOnlyList<KorteksSynthesisItemDto> Misconceptions { get; set; } = Array.Empty<KorteksSynthesisItemDto>();
    public IReadOnlyList<KorteksSynthesisItemDto> PracticeOrder { get; set; } = Array.Empty<KorteksSynthesisItemDto>();
    public IReadOnlyList<KorteksSynthesisItemDto> QuizScope { get; set; } = Array.Empty<KorteksSynthesisItemDto>();
    public IReadOnlyList<KorteksSynthesisItemDto> TutorTeachingHints { get; set; } = Array.Empty<KorteksSynthesisItemDto>();
    public IReadOnlyList<KorteksSynthesisItemDto> WikiNotebookSeeds { get; set; } = Array.Empty<KorteksSynthesisItemDto>();
    public IReadOnlyList<SourceEvidenceDto> Sources { get; set; } = Array.Empty<SourceEvidenceDto>();
    public IReadOnlyList<string> ProviderWarnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class KorteksSynthesisItemDto
{
    public string Kind { get; set; } = "note";
    public string Text { get; set; } = string.Empty;
    public string Confidence { get; set; } = "observed_only";
    public string EvidenceBasis { get; set; } = "korteks_synthesis";
}

public sealed class KorteksConsumerContextsDto
{
    public KorteksConsumerContextDto Plan { get; set; } = new();
    public KorteksConsumerContextDto Quiz { get; set; } = new();
    public KorteksConsumerContextDto Tutor { get; set; } = new();
    public KorteksConsumerContextDto Wiki { get; set; } = new();
}

public sealed class KorteksConsumerContextDto
{
    public string Consumer { get; set; } = string.Empty;
    public string UsagePolicy { get; set; } = "advisory_only";
    public string PromptBlock { get; set; } = string.Empty;
    public IReadOnlyList<string> MustUse { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MayUse { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MustNotUse { get; set; } = Array.Empty<string>();
}

public sealed class KorteksEvidenceSummaryDto
{
    public string GroundingStatus { get; set; } = "evidence_insufficient";
    public string SourceConfidence { get; set; } = "low";
    public int SourceCount { get; set; }
    public int SuccessfulToolCallCount { get; set; }
    public int FailedToolCallCount { get; set; }
    public bool HasUrlBackedEvidence { get; set; }
    public bool IsFallback { get; set; }
}

public sealed class KorteksSynthesisIssueDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string UserSafeMessage { get; set; } = string.Empty;
}

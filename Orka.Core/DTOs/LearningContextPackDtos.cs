namespace Orka.Core.DTOs;

public sealed class LearningContextPackDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string ScopeStatus { get; set; } = "unknown";
    public int EstimatedTokenCount { get; set; }
    public OrkaLearningStateDto? OrkaState { get; set; }
    public ActiveLessonSnapshotDto? ActiveLessonSnapshot { get; set; }
    public StudentContextSnapshotDto? StudentContextSnapshot { get; set; }
    public SourceEvidenceBundleDto? SourceEvidenceBundle { get; set; }
    public IReadOnlyList<LearningContextPackBlockDto> Blocks { get; set; } = Array.Empty<LearningContextPackBlockDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LearningContextPackBlockDto
{
    public string BlockType { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public string Summary { get; set; } = string.Empty;
    public int Priority { get; set; }
    public Guid? SnapshotId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}

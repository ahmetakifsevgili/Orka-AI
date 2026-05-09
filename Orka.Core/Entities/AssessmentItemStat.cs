namespace Orka.Core.Entities;

public sealed class AssessmentItemStat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentItemId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int Correct { get; set; }
    public int Incorrect { get; set; }
    public int Skipped { get; set; }
    public decimal CorrectRate { get; set; }
    public decimal DiscriminationProxy { get; set; }
    public decimal TotalTimeSeconds { get; set; }
    public decimal LastResponseTimeSeconds { get; set; }
    public decimal AverageTimeSeconds { get; set; }
    public decimal SkipRate { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public decimal DifficultyEstimate { get; set; } = 0.50m;
    public decimal DiscriminationEstimate { get; set; }
    public int ExposureCount { get; set; }
    public DateTime? LastSelectedAt { get; set; }
    public string CalibrationStatus { get; set; } = "uncalibrated";
    public DateTime? LastAttemptAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public AssessmentItem AssessmentItem { get; set; } = null!;
    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
}

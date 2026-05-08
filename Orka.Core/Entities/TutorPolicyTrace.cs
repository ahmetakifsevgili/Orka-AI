namespace Orka.Core.Entities;

public sealed class TutorPolicyTrace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string ActiveConceptKey { get; set; } = string.Empty;
    public string LearnerState { get; set; } = "unknown";
    public string RemediationNeed { get; set; } = "unknown";
    public string GroundingStatus { get; set; } = "model_only";
    public string SelectedPedagogicalMove { get; set; } = string.Empty;
    public int SourceEvidenceCount { get; set; }
    public bool DirectAnswerRisk { get; set; }
    public string PolicyViolationsJson { get; set; } = "[]";
    public string InputHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public Session? Session { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
}

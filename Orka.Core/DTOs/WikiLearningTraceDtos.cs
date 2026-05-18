namespace Orka.Core.DTOs;

public sealed class WikiLearningTraceRequestDto
{
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ActiveWikiPageId { get; set; }
    public string? ConceptKey { get; set; }
    public string? ParentConceptKey { get; set; }
    public Guid? PlanStepId { get; set; }
    public Guid? QuizAttemptId { get; set; }
    public Guid? LearningArtifactId { get; set; }
    public Guid? SourceEvidenceBundleId { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public string TraceType { get; set; } = "manual_note";
    public string? Title { get; set; }
    public string SafeContent { get; set; } = string.Empty;
    public string SourceBasis { get; set; } = "model_assisted";
    public string? MisconceptionKey { get; set; }
    public string? CorrelationId { get; set; }
    public string CreatedBy { get; set; } = "system";
    public string? Visibility { get; set; }
    public string? MetadataJson { get; set; }
}

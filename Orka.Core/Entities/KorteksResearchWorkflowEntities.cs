namespace Orka.Core.Entities;

public sealed class KorteksResearchWorkflow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
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
    public string GroundingMode { get; set; } = "FallbackInternalKnowledge";
    public string SourceConfidence { get; set; } = "low";
    public int SourceCount { get; set; }
    public int ToolCallCount { get; set; }
    public bool CanGroundTutorClaims { get; set; }
    public string EvidenceSummaryJson { get; set; } = "{}";
    public string SynthesisJson { get; set; } = "{}";
    public string PlanContextJson { get; set; } = "{}";
    public string QuizContextJson { get; set; } = "{}";
    public string TutorContextJson { get; set; } = "{}";
    public string WikiContextJson { get; set; } = "{}";
    public string SafetyIssuesJson { get; set; } = "[]";
    public string PromptBlock { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public bool IsDeleted { get; set; }
}

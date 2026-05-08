namespace Orka.Core.Entities;

public class AssessmentItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid? QuizRunId { get; set; }
    public QuizRun? QuizRun { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid ConceptGraphSnapshotId { get; set; }
    public ConceptGraphSnapshot ConceptGraphSnapshot { get; set; } = null!;
    public Guid? LearningConceptId { get; set; }
    public LearningConcept? LearningConcept { get; set; }
    public string AssessmentItemKey { get; set; } = string.Empty;
    public string ConceptKey { get; set; } = string.Empty;
    public string ConceptLabel { get; set; } = string.Empty;
    public string QuestionType { get; set; } = "conceptual";
    public string CognitiveSkill { get; set; } = "conceptual";
    public string Difficulty { get; set; } = "orta";
    public string MisconceptionTarget { get; set; } = string.Empty;
    public string EvidenceExpected { get; set; } = string.Empty;
    public string OptionQualityRulesJson { get; set; } = "[]";
    public string ScoringRuleJson { get; set; } = "{}";
    public string LearningOutcomeKeysJson { get; set; } = "[]";
    public string PromptSpecJson { get; set; } = "{}";
    public string? GeneratedQuestionJson { get; set; }
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

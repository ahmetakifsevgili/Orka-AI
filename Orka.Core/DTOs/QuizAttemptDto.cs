using System;

namespace Orka.Core.DTOs;

public class QuizAttemptDto
{
    public Guid Id { get; set; }
    public Guid? QuizRunId { get; set; }
    public string? QuestionId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string UserAnswer { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string? SkillTag { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public string? ConceptKey { get; set; }
    public string? ConceptTag { get; set; }
    public string? CognitiveSkill { get; set; }
    public string? MisconceptionTarget { get; set; }
    public string? EvidenceExpected { get; set; }
    public string? ScoringRule { get; set; }
    public string? LearningOutcomeIdsJson { get; set; }
    public Guid? KnowledgeTracingStateId { get; set; }
    public decimal? MasteryProbability { get; set; }
    public string? ItemQualityStatus { get; set; }
    public string? LearningObjective { get; set; }
    public string? QuestionType { get; set; }
    public string? MistakeCategory { get; set; }
    public string? AssessmentMode { get; set; }
    public string? SourceReadiness { get; set; }
    public string? WikiReviewHint { get; set; }
    public string? TopicPath { get; set; }
    public string? Difficulty { get; set; }
    public string? CognitiveType { get; set; }
    public string? QuestionHash { get; set; }
    public int? ResponseTimeMs { get; set; }
    public bool WasSkipped { get; set; }
    public decimal? ConfidenceSelfRating { get; set; }
    public DateTime CreatedAt { get; set; }
}

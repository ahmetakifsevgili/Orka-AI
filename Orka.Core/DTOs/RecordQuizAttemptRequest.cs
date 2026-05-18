using System;

namespace Orka.Core.DTOs;

public class RecordQuizAttemptRequest
{
    public Guid? QuizRunId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string? MessageId { get; set; }
    public string? QuestionId { get; set; }
    public string? Question { get; set; }
    public string? SelectedOptionId { get; set; }
    public bool? IsCorrect { get; set; }
    public string? Explanation { get; set; }
    public string? SkillTag { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public string? ConceptKey { get; set; }
    public string? ConceptTag { get; set; }
    public string? CognitiveSkill { get; set; }
    public string? MisconceptionTarget { get; set; }
    public string? EvidenceExpected { get; set; }
    public string? ScoringRule { get; set; }
    public string? LearningOutcomeIdsJson { get; set; }
    public string? LearningObjective { get; set; }
    public string? QuestionType { get; set; }
    public string? MistakeCategory { get; set; }
    public string? AssessmentMode { get; set; }
    public Guid? SourceEvidenceBundleId { get; set; }
    public string? WikiNotebookSectionKey { get; set; }
    public string? TopicPath { get; set; }
    public string? Difficulty { get; set; }
    public string? CognitiveType { get; set; }
    public string? QuestionHash { get; set; }
    public string? SourceRefsJson { get; set; }
    public int? ResponseTimeMs { get; set; }
    public bool? WasSkipped { get; set; }
    public decimal? ConfidenceSelfRating { get; set; }
}

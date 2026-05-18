using System;
using Orka.Core.Enums;

namespace Orka.Core.Entities;

public class WikiBlock
{
    public Guid Id { get; set; }
    public Guid WikiPageId { get; set; }
    public WikiPage WikiPage { get; set; } = null!;
    public WikiBlockType BlockType { get; set; }
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
    public string SafetyWarningsJson { get; set; } = "[]";
    public bool IsDeleted { get; set; }
    public int OrderIndex { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

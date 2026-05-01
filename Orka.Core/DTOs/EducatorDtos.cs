namespace Orka.Core.DTOs;

public sealed record TeacherContext(
    IReadOnlyList<SourceUsage> Sources,
    IReadOnlyList<TeachingReference> TeachingReferences,
    IReadOnlyList<MisconceptionSignal> Misconceptions,
    EducatorQualityScore QualityScore,
    string PromptBlock);

public sealed record TeachingReference(
    string SourceType,
    string SourceId,
    string Status,
    string TeachingFlow,
    IReadOnlyList<string> Examples,
    IReadOnlyList<string> Analogies,
    IReadOnlyList<string> CommonMistakes,
    IReadOnlyList<string> PracticeIdeas,
    DateTime GeneratedAt);

public sealed record SourceUsage(
    string Kind,
    string Label,
    string CitationRule,
    bool IsFactualSource,
    int Priority);

public sealed record MisconceptionSignal(
    string SkillTag,
    string TopicPath,
    string Evidence,
    string SuggestedTeachingMove);

public sealed record EducatorQualityScore(
    bool RequiresCitationGuard,
    bool HasNotebookContext,
    bool HasWikiContext,
    bool HasLearningSignals,
    bool HasTeachingReference,
    string Recommendation);

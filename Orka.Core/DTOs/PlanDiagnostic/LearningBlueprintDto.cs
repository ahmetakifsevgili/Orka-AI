namespace Orka.Core.DTOs.PlanDiagnostic;

public sealed class LearningBlueprintDto
{
    public string Domain { get; set; } = "general";
    public string ApprovedResearchIntent { get; set; } = string.Empty;
    public string SourceConfidence { get; set; } = "low";
    public List<string> SourceSignals { get; set; } = [];
    public List<string> LearningRoute { get; set; } = [];
    public List<string> Prerequisites { get; set; } = [];
    public List<string> SubConcepts { get; set; } = [];
    public List<string> CommonMistakes { get; set; } = [];
    public List<string> PracticeOrder { get; set; } = [];
    public List<string> AssessmentAxes { get; set; } = [];
    public List<string> LearningOutcomes { get; set; } = [];
    public List<string> ConceptGraphKeys { get; set; } = [];
    public List<string> MisconceptionMap { get; set; } = [];
    public List<string> DiagnosticSkillMatrix { get; set; } = [];
    public int RecommendedQuestionCount { get; set; } = 20;
    public List<LearningBlueprintModuleDto> PlanModules { get; set; } = [];

    public List<string> Timeline { get; set; } = [];
    public List<string> Actors { get; set; } = [];
    public List<string> Events { get; set; } = [];
    public List<string> Institutions { get; set; } = [];
    public List<string> CauseEffectPairs { get; set; } = [];

    public List<string> Concepts { get; set; } = [];
    public List<string> CodeReadingTargets { get; set; } = [];
    public List<string> DebuggingTargets { get; set; } = [];
    public List<string> PracticeLabs { get; set; } = [];

    public List<string> QuestionTypes { get; set; } = [];
    public List<string> TimingStrategies { get; set; } = [];
    public List<string> CommonTrapTypes { get; set; } = [];
}

public sealed class LearningBlueprintModuleDto
{
    public string Title { get; set; } = string.Empty;
    public List<string> Lessons { get; set; } = [];
}

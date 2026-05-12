using Orka.Core.Enums;

namespace Orka.Core.Constants;

public static class CanonicalAgentRoles
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TutorAgent"] = nameof(AgentRole.Tutor),
        ["QuizAgent"] = nameof(AgentRole.Quiz),
        ["EvaluatorAgent"] = nameof(AgentRole.Evaluator),
        ["SummarizerAgent"] = nameof(AgentRole.Summarizer),
        ["DeepPlanAgent"] = nameof(AgentRole.DeepPlan),
        ["KorteksAgent"] = nameof(AgentRole.Korteks),
        ["AnalyzerAgent"] = nameof(AgentRole.Analyzer),
        ["GraderAgent"] = nameof(AgentRole.Grader),
        ["RemedialAgent"] = nameof(AgentRole.Remedial),
        ["SupervisorAgent"] = nameof(AgentRole.Supervisor)
    };

    public static string NormalizeForCost(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return "Unknown";

        var trimmed = role.Trim();
        if (Aliases.TryGetValue(trimmed, out var canonical))
            return canonical;

        if (Enum.TryParse<AgentRole>(trimmed, ignoreCase: true, out var enumRole))
            return enumRole.ToString();

        return trimmed.Length <= 128 ? trimmed : trimmed[..128];
    }
}

using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class LearningModePlugin
{
    [KernelFunction, Description("Return a stable teaching-mode hint for Tutor planning.")]
    public Task<string> RecommendMode(string intent, string? weakness = null)
    {
        var normalized = (intent ?? string.Empty).Trim().ToLowerInvariant();
        var mode = normalized switch
        {
            var x when x.Contains("review") || x.Contains("tekrar") => "QuickReview",
            var x when x.Contains("mistake") || x.Contains("hata") || !string.IsNullOrWhiteSpace(weakness) => "Remediation",
            var x when x.Contains("quiz") || x.Contains("test") => "Assessment",
            var x when x.Contains("practice") || x.Contains("uygula") => "PracticeLab",
            _ => "Core"
        };

        return Task.FromResult($"learning_mode={mode}");
    }
}

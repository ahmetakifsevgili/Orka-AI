using System.ComponentModel;
using System.Net;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class VisualGeneratorPlugin
{
    [KernelFunction, Description("Generate a safe educational Pollinations image markdown link from a text prompt.")]
    public Task<string> GenerateVisual(string prompt, string altText)
    {
        var encodedPrompt = WebUtility.UrlEncode(prompt);
        var safeAlt = string.IsNullOrWhiteSpace(altText) ? "educational visual" : altText.Trim();
        var imageUrl = $"https://image.pollinations.ai/prompt/{encodedPrompt}?width=800&height=600&nologo=true";
        return Task.FromResult($"![{safeAlt}]({imageUrl})");
    }
}

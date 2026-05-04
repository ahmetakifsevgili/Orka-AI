using System.Diagnostics;
using Microsoft.SemanticKernel;
using Orka.Core.DTOs.Korteks;
using Orka.Infrastructure.Services;

namespace Orka.Infrastructure.SemanticKernel.Filters;

public sealed class KorteksToolCaptureFilter : IFunctionInvocationFilter
{
    private readonly List<ToolCallEvidenceDto> _calls = [];
    private readonly List<SourceEvidenceDto> _sources = [];
    private readonly List<string> _warnings = [];

    public IReadOnlyList<ToolCallEvidenceDto> Calls => _calls;
    public IReadOnlyList<SourceEvidenceDto> Sources => _sources;
    public IReadOnlyList<string> Warnings => _warnings;

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        var sw = Stopwatch.StartNew();
        var provider = context.Function.PluginName ?? "Unknown";
        var toolName = context.Function.Name ?? "Unknown";
        var success = false;
        string? failure = null;
        string? resultText = null;

        try
        {
            await next(context);
            success = true;
            resultText = context.Result?.GetValue<object>()?.ToString();
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            throw;
        }
        finally
        {
            sw.Stop();
            var retrievedAt = DateTimeOffset.UtcNow;
            var degraded = KorteksSourceEvidenceExtractor.FindDegradedMarker(resultText);
            var extracted = KorteksSourceEvidenceExtractor.Extract(provider, toolName, resultText, retrievedAt);
            _sources.AddRange(extracted);

            if (success && extracted.Count == 0 && KorteksGroundingClassifier.IsSourceProvider(provider))
            {
                _warnings.Add($"{provider}-{toolName} returned no URL-backed source evidence.");
            }

            if (!string.IsNullOrWhiteSpace(degraded))
            {
                _warnings.Add($"{provider}-{toolName} degraded: {degraded}");
            }

            _calls.Add(new ToolCallEvidenceDto(
                ToolName: toolName,
                Provider: provider,
                Invoked: true,
                Success: success && string.IsNullOrWhiteSpace(degraded),
                FailureReason: failure,
                ResultCount: extracted.Count,
                DurationMs: sw.ElapsedMilliseconds,
                DegradedMarker: degraded,
                Timestamp: retrievedAt));
        }
    }
}

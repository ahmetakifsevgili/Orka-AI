using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.SemanticKernel.Filters;

public sealed class PluginTelemetryFilter : IFunctionInvocationFilter
{
    private readonly IRuntimeTelemetryService _telemetry;
    private readonly ILogger<PluginTelemetryFilter> _logger;

    public PluginTelemetryFilter(IRuntimeTelemetryService telemetry, ILogger<PluginTelemetryFilter> logger)
    {
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        var sw = Stopwatch.StartNew();
        var success = false;
        try
        {
            await next(context);
            success = true;
        }
        finally
        {
            sw.Stop();
            var toolId = $"{context.Function.PluginName ?? "unknown"}.{context.Function.Name ?? "unknown"}";
            _logger.LogInformation(
                "[PluginTelemetry] Plugin={Plugin} Function={Function} Success={Success} DurationMs={DurationMs}",
                context.Function.PluginName ?? "unknown",
                context.Function.Name ?? "unknown",
                success,
                sw.ElapsedMilliseconds);

            await _telemetry.RecordToolEventAsync(new ToolTelemetryEventRequest(
                UserId: null,
                SessionId: null,
                TopicId: null,
                ToolId: toolId,
                CapabilityStatus: "invoked",
                Provider: null,
                Model: null,
                LatencyMs: sw.ElapsedMilliseconds,
                Success: success,
                ErrorCode: success ? null : "plugin_invocation_failed",
                FallbackUsed: !success,
                CorrelationId: null,
                MetadataJson: null));
        }
    }
}

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Filters;

public sealed class PluginTelemetryFilter : IFunctionInvocationFilter
{
    private readonly ILogger<PluginTelemetryFilter> _logger;

    public PluginTelemetryFilter(ILogger<PluginTelemetryFilter> logger)
    {
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
            _logger.LogInformation(
                "[PluginTelemetry] Plugin={Plugin} Function={Function} Success={Success} DurationMs={DurationMs}",
                context.Function.PluginName ?? "unknown",
                context.Function.Name ?? "unknown",
                success,
                sw.ElapsedMilliseconds);
        }
    }
}

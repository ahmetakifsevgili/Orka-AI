using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class IdeExecutionPlugin
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<IdeExecutionPlugin> _logger;

    public IdeExecutionPlugin(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<IdeExecutionPlugin> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    [KernelFunction, Description("Reports IDE/code execution tool availability. Direct Tutor auto-execution is disabled unless development/admin gate is enabled.")]
    public Task<string> RunCode(
        [Description("Source code requested by the model")] string code,
        [Description("Language name")] string language = "python",
        [Description("Optional stdin")] string? stdin = null)
    {
        var enabled = _environment.IsDevelopment() &&
                      bool.TryParse(_configuration["Tools:IdeExecution:Enabled"], out var value) &&
                      value;

        _logger.LogInformation("[IdeExecutionPlugin] Gate checked. Enabled={Enabled} Language={Language} Size={Size}",
            enabled, language, code?.Length ?? 0);

        return Task.FromResult(enabled
            ? "[ide:dev-only] Use the authenticated /api/code/run or /api/code/execute sandbox endpoint; SK auto-execution remains disabled."
            : "[ide:disabled] Direct Tutor IDE execution is disabled. Do not execute code automatically.");
    }
}

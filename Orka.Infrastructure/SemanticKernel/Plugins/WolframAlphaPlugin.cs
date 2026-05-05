using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class WolframAlphaPlugin
{
    private readonly string _appId;
    private readonly ILogger<WolframAlphaPlugin> _logger;

    public WolframAlphaPlugin(IConfiguration configuration, ILogger<WolframAlphaPlugin> logger)
    {
        _appId = configuration["AI:WolframAlpha:AppId"] ?? configuration["WolframAlpha:AppId"] ?? string.Empty;
        _logger = logger;
    }

    [KernelFunction, Description("Safely reports Wolfram Alpha availability. Exact computation is disabled unless provider configuration exists.")]
    public Task<string> QueryWolframAlphaAsync([Description("Exact computation query")] string query)
    {
        if (string.IsNullOrWhiteSpace(_appId))
        {
            _logger.LogInformation("[Wolfram] Disabled stub returned; AppId is not configured.");
            return Task.FromResult("[wolfram:disabled] Wolfram Alpha is not configured. Use general reasoning and do not claim a Wolfram result.");
        }

        return Task.FromResult("[wolfram:disabled] Wolfram provider is gated for backend hardening; no live call was made.");
    }
}

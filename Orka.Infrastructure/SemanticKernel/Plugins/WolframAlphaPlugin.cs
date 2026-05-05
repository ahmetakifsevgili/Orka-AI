using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class WolframAlphaPlugin
{
    private readonly IWolframProvider _provider;
    private readonly ILogger<WolframAlphaPlugin> _logger;

    public WolframAlphaPlugin(IWolframProvider provider, ILogger<WolframAlphaPlugin> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    [KernelFunction, Description("Uses Wolfram Alpha for exact math/physics/symbolic computation when configured. If unavailable, returns a safe fallback and never invents a computation result.")]
    public async Task<string> QueryWolframAlphaAsync([Description("Exact computation query")] string query)
    {
        var result = await _provider.QueryAsync(query);
        _logger.LogInformation("[Wolfram] Result Status={Status} Success={Success}", result.Status, result.Success);
        return ProviderResultFormatter.Format(result);
    }
}

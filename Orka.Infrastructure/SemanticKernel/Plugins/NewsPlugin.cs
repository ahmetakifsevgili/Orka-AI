using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class NewsPlugin
{
    private readonly INewsProvider _provider;
    private readonly ILogger<NewsPlugin> _logger;

    public NewsPlugin(INewsProvider provider, ILogger<NewsPlugin> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    [KernelFunction, Description("Searches current news with source/date metadata when configured. If unavailable, returns a clear no-current-source fallback; do not use model memory for current events.")]
    public async Task<string> SearchNews(string query, string language = "tr", int count = 5)
    {
        var result = await _provider.SearchAsync(query, language, count);
        _logger.LogInformation("[News] Result Status={Status} Success={Success}", result.Status, result.Success);
        return ProviderResultFormatter.Format(result);
    }
}

using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class NewsPlugin
{
    private readonly string _apiKey;
    private readonly ILogger<NewsPlugin> _logger;

    public NewsPlugin(IConfiguration configuration, ILogger<NewsPlugin> logger)
    {
        _apiKey = configuration["AI:NewsAPI:ApiKey"] ?? string.Empty;
        _logger = logger;
    }

    [KernelFunction, Description("Reports current-news tool availability. News must be sourced and dated; disabled without provider key.")]
    public Task<string> SearchNews(string query, string language = "tr", int count = 5)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogInformation("[News] Disabled stub returned for query.");
            return Task.FromResult("[news:disabled] News provider is not configured. Do not present current-news claims as sourced.");
        }

        return Task.FromResult("[news:disabled] News provider is gated for backend hardening; no live call was made.");
    }
}

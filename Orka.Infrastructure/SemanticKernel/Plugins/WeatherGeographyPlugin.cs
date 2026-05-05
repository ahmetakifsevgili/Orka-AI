using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class WeatherGeographyPlugin
{
    private readonly IWeatherProvider _provider;
    private readonly ILogger<WeatherGeographyPlugin> _logger;

    public WeatherGeographyPlugin(IWeatherProvider provider, ILogger<WeatherGeographyPlugin> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    [KernelFunction, Description("Gets current weather/geography context when configured. If unavailable, returns a safe fallback and does not invent live conditions.")]
    public async Task<string> GetWeatherAndGeography(double latitude, double longitude, string locationName = "")
    {
        var result = await _provider.GetWeatherAsync(latitude, longitude, locationName);
        _logger.LogInformation("[Weather] Result Status={Status} Success={Success}", result.Status, result.Success);
        return ProviderResultFormatter.Format(result);
    }
}

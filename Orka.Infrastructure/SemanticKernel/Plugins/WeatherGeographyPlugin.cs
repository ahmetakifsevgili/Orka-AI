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

    [KernelFunction, Description("Gets geography context for a place: coordinates, latitude band, climate cue, and map reasoning. Live weather is only a supporting signal.")]
    public async Task<string> GetWeatherAndGeography(double latitude, double longitude, string locationName = "")
    {
        var result = await _provider.GetGeographyContextAsync(latitude, longitude, locationName);
        _logger.LogInformation("[Geography] Result Status={Status} Success={Success}", result.Status, result.Success);
        return ProviderResultFormatter.Format(result);
    }
}

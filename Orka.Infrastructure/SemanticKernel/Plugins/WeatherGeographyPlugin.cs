using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class WeatherGeographyPlugin
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WeatherGeographyPlugin> _logger;

    public WeatherGeographyPlugin(IConfiguration configuration, ILogger<WeatherGeographyPlugin> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [KernelFunction, Description("Reports weather/geography tool availability. External live weather is beta-gated.")]
    public Task<string> GetWeatherAndGeography(double latitude, double longitude, string locationName = "")
    {
        var enabled = bool.TryParse(_configuration["Tools:Weather:Enabled"], out var value) && value;
        if (!enabled)
        {
            _logger.LogInformation("[Weather] Disabled stub returned for {Location}.", locationName);
            return Task.FromResult("[weather:disabled] Weather/geography live data is beta-gated and currently disabled.");
        }

        return Task.FromResult("[weather:disabled] Weather provider is gated for backend hardening; no live call was made.");
    }
}

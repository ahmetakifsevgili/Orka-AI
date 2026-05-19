using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

internal static class ProviderFailureCodes
{
    public const string ProviderMissing = "provider_missing";
    public const string ProviderDisabled = "provider_disabled";
    public const string ProviderTimeout = "provider_timeout";
    public const string ProviderError = "provider_error";
    public const string MalformedResponse = "malformed_response";
    public const string EmptyResult = "empty_result";
    public const string UnknownFailure = "unknown_failure";
}

public sealed class WolframProvider : IWolframProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IRuntimeTelemetryService _telemetry;
    private readonly ILogger<WolframProvider> _logger;

    public WolframProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IRuntimeTelemetryService telemetry,
        ILogger<WolframProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<ProviderToolResultDto> QueryAsync(string query, Guid? userId = null, Guid? sessionId = null, Guid? topicId = null, CancellationToken ct = default)
    {
        const string toolId = "wolfram_alpha";
        var sw = Stopwatch.StartNew();
        var appId = _configuration["AI:WolframAlpha:AppId"] ?? _configuration["WolframAlpha:AppId"];
        if (string.IsNullOrWhiteSpace(appId))
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "wolfram_alpha", "disabled", ProviderFailureCodes.ProviderMissing, "Wolfram Alpha is not configured; no external computation was performed."), userId, sessionId, topicId, ct);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var client = _httpClientFactory.CreateClient("WolframAlpha");
            var url = $"https://www.wolframalpha.com/api/v1/llm-api?input={Uri.EscapeDataString(query)}&appid={Uri.EscapeDataString(appId)}";
            var response = await client.GetAsync(url, timeout.Token);
            var body = (await response.Content.ReadAsStringAsync(timeout.Token)).Trim();
            if (!response.IsSuccessStatusCode)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "wolfram_alpha", "degraded", ProviderFailureCodes.ProviderError, $"Wolfram request failed with status {(int)response.StatusCode}.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
            if (string.IsNullOrWhiteSpace(body))
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "wolfram_alpha", "empty", ProviderFailureCodes.EmptyResult, "Wolfram returned no computation result.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            var result = new ProviderToolResultDto(
                true,
                toolId,
                "wolfram_alpha",
                "ready",
                new WolframComputationDto(query, body),
                [new ProviderCitationDto("Wolfram Alpha computation", null, "Wolfram Alpha", DateTime.UtcNow, 0.95)],
                DateTime.UtcNow,
                sw.ElapsedMilliseconds,
                false,
                null,
                "Wolfram Alpha LLM API computation result is available.",
                0.95,
                1);
            return await RecordAsync(result, userId, sessionId, topicId, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "wolfram_alpha", "timeout", ProviderFailureCodes.ProviderTimeout, "Wolfram request timed out; no external computation was used.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[Wolfram] Provider call failed. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "wolfram_alpha", "degraded", ProviderFailureCodes.UnknownFailure, "Wolfram provider failed safely; no external computation was used.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
    }

    private async Task<ProviderToolResultDto> RecordAsync(ProviderToolResultDto result, Guid? userId, Guid? sessionId, Guid? topicId, CancellationToken ct)
    {
        await ProviderTelemetry.RecordAsync(_telemetry, result, userId, sessionId, topicId, ct);
        return result;
    }
}

public sealed class NewsProvider : INewsProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IRuntimeTelemetryService _telemetry;
    private readonly ILogger<NewsProvider> _logger;

    public NewsProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration, IRuntimeTelemetryService telemetry, ILogger<NewsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<ProviderToolResultDto> SearchAsync(string query, string language = "tr", int count = 5, Guid? userId = null, Guid? sessionId = null, Guid? topicId = null, CancellationToken ct = default)
    {
        const string toolId = "news";
        var sw = Stopwatch.StartNew();
        var apiKey = _configuration["AI:NewsAPI:ApiKey"] ?? _configuration["NewsAPI:ApiKey"];

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var safeCount = Math.Clamp(count, 1, 10);
            var client = _httpClientFactory.CreateClient("News");
            var provider = string.IsNullOrWhiteSpace(apiKey) ? "gdelt" : "newsapi";
            var url = string.IsNullOrWhiteSpace(apiKey)
                ? $"https://api.gdeltproject.org/api/v2/doc/doc?query={Uri.EscapeDataString(query)}&mode=ArtList&format=json&maxrecords={safeCount}&sort=HybridRel"
                : $"v2/everything?q={Uri.EscapeDataString(query)}&language={Uri.EscapeDataString(language)}&pageSize={safeCount}&sortBy=publishedAt&apiKey={Uri.EscapeDataString(apiKey)}";
            var response = await client.GetAsync(url, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, provider, "degraded", ProviderFailureCodes.ProviderError, $"News provider failed with status {(int)response.StatusCode}.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("articles", out var articles) || articles.ValueKind != JsonValueKind.Array)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, provider, "malformed", ProviderFailureCodes.MalformedResponse, "News provider returned an unexpected response.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            var parsed = articles.EnumerateArray()
                .Select(a => provider == "gdelt" ? ParseGdeltArticle(a) : ParseNewsApiArticle(a))
                .Where(a => !string.IsNullOrWhiteSpace(a.Title))
                .Take(safeCount)
                .ToList();
            if (parsed.Count == 0)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, provider, "empty", ProviderFailureCodes.EmptyResult, "No sourced current news result was found.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            var citations = parsed
                .Select(a => new ProviderCitationDto(a.Title, a.Url, a.SourceName, a.PublishedAt, 0.85))
                .ToList();
            var result = new ProviderToolResultDto(true, toolId, provider, "ready", parsed, citations, DateTime.UtcNow, sw.ElapsedMilliseconds, false, null, "Sourced current news results are available. Use source/date metadata instead of model memory for current events.", 0.85, parsed.Count);
            return await RecordAsync(result, userId, sessionId, topicId, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "news", "timeout", ProviderFailureCodes.ProviderTimeout, "News provider timed out; do not invent current-news details.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                "[News] Malformed response. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "news", "malformed", ProviderFailureCodes.MalformedResponse, "News provider returned malformed data.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[News] Provider call failed. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "news", "degraded", ProviderFailureCodes.UnknownFailure, "News provider failed safely; do not infer current events.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
    }

    private static NewsArticleDto ParseNewsApiArticle(JsonElement article)
    {
        var title = article.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty;
        var url = article.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
        var summary = article.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? string.Empty : string.Empty;
        DateTime? publishedAt = null;
        if (article.TryGetProperty("publishedAt", out var publishedProp) && DateTime.TryParse(publishedProp.GetString(), out var parsed))
            publishedAt = parsed.ToUniversalTime();
        var sourceName = "news";
        if (article.TryGetProperty("source", out var source) && source.TryGetProperty("name", out var name))
            sourceName = name.GetString() ?? sourceName;
        return new NewsArticleDto(title, sourceName, url, publishedAt, summary);
    }

    private static NewsArticleDto ParseGdeltArticle(JsonElement article)
    {
        var title = article.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty;
        var url = article.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
        var summary = article.TryGetProperty("seendate", out var seenProp) ? $"Seen date: {seenProp.GetString()}" : string.Empty;
        DateTime? publishedAt = null;
        if (article.TryGetProperty("seendate", out var publishedProp) && DateTime.TryParse(publishedProp.GetString(), out var parsed))
            publishedAt = parsed.ToUniversalTime();
        var sourceName = article.TryGetProperty("domain", out var domain) ? domain.GetString() ?? "GDELT" : "GDELT";
        return new NewsArticleDto(title, sourceName, url, publishedAt, summary);
    }

    private async Task<ProviderToolResultDto> RecordAsync(ProviderToolResultDto result, Guid? userId, Guid? sessionId, Guid? topicId, CancellationToken ct)
    {
        await ProviderTelemetry.RecordAsync(_telemetry, result, userId, sessionId, topicId, ct);
        return result;
    }
}

public sealed class WeatherProvider : IWeatherProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IRuntimeTelemetryService _telemetry;
    private readonly ILogger<WeatherProvider> _logger;

    public WeatherProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration, IRuntimeTelemetryService telemetry, ILogger<WeatherProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _telemetry = telemetry;
        _logger = logger;
    }

    public Task<ProviderToolResultDto> GetWeatherAsync(double latitude, double longitude, string? locationName = null, Guid? userId = null, Guid? sessionId = null, Guid? topicId = null, CancellationToken ct = default) =>
        GetGeographyContextAsync(latitude, longitude, locationName, userId, sessionId, topicId, ct);

    public async Task<ProviderToolResultDto> GetGeographyContextAsync(double latitude, double longitude, string? locationName = null, Guid? userId = null, Guid? sessionId = null, Guid? topicId = null, CancellationToken ct = default)
    {
        const string toolId = "weather";
        var sw = Stopwatch.StartNew();
        var enabled = bool.TryParse(_configuration["Tools:Weather:Enabled"], out var enabledValue) && enabledValue;
        var apiKey = _configuration["Tools:Weather:ApiKey"] ?? _configuration["OpenWeatherMap:ApiKey"];
        if (double.IsNaN(latitude) || double.IsNaN(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "geography_context", "blocked", "malformed_location", "Geography location is invalid.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var client = _httpClientFactory.CreateClient("Weather");
            var provider = enabled && !string.IsNullOrWhiteSpace(apiKey) ? "openweathermap" : "open_meteo";
            var lat = latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var lon = longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var url = provider == "openweathermap"
                ? $"data/2.5/weather?lat={lat}&lon={lon}&appid={Uri.EscapeDataString(apiKey!)}&units=metric"
                : $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code&timezone=UTC";
            var response = await client.GetAsync(url, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, provider, "degraded", ProviderFailureCodes.ProviderError, $"Weather provider failed with status {(int)response.StatusCode}.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            using var doc = JsonDocument.Parse(body);
            var temp = provider == "openweathermap"
                ? doc.RootElement.TryGetProperty("main", out var main) && main.TryGetProperty("temp", out var tempProp) ? tempProp.GetDouble() : (double?)null
                : doc.RootElement.TryGetProperty("current", out var current) && current.TryGetProperty("temperature_2m", out var meteoTempProp) ? meteoTempProp.GetDouble() : (double?)null;
            var conditions = provider == "openweathermap"
                ? doc.RootElement.TryGetProperty("weather", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0 && arr[0].TryGetProperty("description", out var desc)
                    ? desc.GetString()
                    : null
                : doc.RootElement.TryGetProperty("current", out var curr) && curr.TryGetProperty("weather_code", out var code)
                    ? $"weather_code:{code.GetInt32()}"
                    : "current weather";
            var name = !string.IsNullOrWhiteSpace(locationName)
                ? locationName
                : doc.RootElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? $"{latitude},{longitude}" : $"{latitude},{longitude}";
            var source = provider == "openweathermap" ? "OpenWeatherMap" : "Open-Meteo";
            var dto = new WeatherContextDto(
                name,
                DateTime.UtcNow,
                temp,
                conditions,
                source,
                latitude,
                longitude,
                BuildClimateSummary(latitude, temp, conditions),
                BuildGeographySummary(name, latitude, longitude),
                "Use this as geography context: location, latitude band, hemisphere, climate cue, and map reasoning. Treat live weather as supporting evidence only.");
            var result = new ProviderToolResultDto(
                true,
                toolId,
                provider,
                "ready",
                dto,
                [new ProviderCitationDto($"{source} geography context", null, source, DateTime.UtcNow, 0.8)],
                DateTime.UtcNow,
                sw.ElapsedMilliseconds,
                false,
                null,
                "Geography context is available; live weather is only a supporting signal.",
                0.8,
                1);
            return await RecordAsync(result, userId, sessionId, topicId, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "geography_context", "timeout", ProviderFailureCodes.ProviderTimeout, "Geography context provider timed out.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            _logger.LogWarning(
                "[Weather] Provider call failed. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "geography_context", "degraded", ex is JsonException ? ProviderFailureCodes.MalformedResponse : ProviderFailureCodes.ProviderError, "Geography context provider failed safely.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
    }

    private static string BuildClimateSummary(double latitude, double? temperatureC, string? conditions)
    {
        var band = Math.Abs(latitude) switch
        {
            < 23.5 => "tropical latitude band",
            < 35 => "subtropical latitude band",
            < 55 => "mid-latitude band",
            < 66.5 => "subpolar latitude band",
            _ => "polar latitude band"
        };
        var tempText = temperatureC.HasValue ? $" Current temperature signal: {temperatureC.Value:0.#}C." : string.Empty;
        var conditionText = string.IsNullOrWhiteSpace(conditions) ? string.Empty : $" Current condition signal: {conditions}.";
        return $"{band}.{tempText}{conditionText}";
    }

    private static string BuildGeographySummary(string location, double latitude, double longitude)
    {
        var hemisphere = latitude >= 0 ? "Northern Hemisphere" : "Southern Hemisphere";
        var eastWest = longitude >= 0 ? "Eastern Hemisphere" : "Western Hemisphere";
        return $"{location} is at approximately {Math.Abs(latitude):0.##} degrees {(latitude >= 0 ? "N" : "S")}, {Math.Abs(longitude):0.##} degrees {(longitude >= 0 ? "E" : "W")} ({hemisphere}, {eastWest}). Use this for map position, latitude-climate reasoning, regional comparison, and place-based examples.";
    }

    private async Task<ProviderToolResultDto> RecordAsync(ProviderToolResultDto result, Guid? userId, Guid? sessionId, Guid? topicId, CancellationToken ct)
    {
        await ProviderTelemetry.RecordAsync(_telemetry, result, userId, sessionId, topicId, ct);
        return result;
    }
}

public sealed class GeocodingProvider : IGeocodingProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRuntimeTelemetryService _telemetry;
    private readonly ILogger<GeocodingProvider> _logger;

    public GeocodingProvider(
        IHttpClientFactory httpClientFactory,
        IRuntimeTelemetryService telemetry,
        ILogger<GeocodingProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<ProviderToolResultDto> GeocodeAsync(string location, Guid? userId = null, Guid? sessionId = null, Guid? topicId = null, CancellationToken ct = default)
    {
        const string toolId = "geocoding";
        var sw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(location) || location.Trim().Length < 2)
        {
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "open_meteo_geocoding", "needs_input", "missing_location", "Geography context needs a city, region, or location before provider data can be used."), userId, sessionId, topicId, ct);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var client = _httpClientFactory.CreateClient("Geocoding");
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(location.Trim())}&count=1&language=tr&format=json";
            var response = await client.GetAsync(url, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "open_meteo_geocoding", "degraded", ProviderFailureCodes.ProviderError, $"Geocoding failed with status {(int)response.StatusCode}.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "open_meteo_geocoding", "needs_input", ProviderFailureCodes.EmptyResult, "Location could not be resolved; ask the learner for a clearer city/location.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            var first = results[0];
            var name = first.TryGetProperty("name", out var n) ? n.GetString() ?? location.Trim() : location.Trim();
            var country = first.TryGetProperty("country", out var c) ? c.GetString() : null;
            var label = string.IsNullOrWhiteSpace(country) ? name : $"{name}, {country}";
            var lat = first.TryGetProperty("latitude", out var latProp) ? latProp.GetDouble() : double.NaN;
            var lon = first.TryGetProperty("longitude", out var lonProp) ? lonProp.GetDouble() : double.NaN;
            if (double.IsNaN(lat) || double.IsNaN(lon))
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "open_meteo_geocoding", "degraded", ProviderFailureCodes.MalformedResponse, "Location coordinates were missing.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            var dto = new GeocodingResultDto(label, lat, lon, "Open-Meteo Geocoding");
            var result = new ProviderToolResultDto(true, toolId, "open_meteo_geocoding", "ready", dto, [new ProviderCitationDto("Open-Meteo geocoding", null, "Open-Meteo", DateTime.UtcNow, 0.80)], DateTime.UtcNow, sw.ElapsedMilliseconds, false, null, "Location resolved for geography context.", 0.80, 1);
            return await RecordAsync(result, userId, sessionId, topicId, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "open_meteo_geocoding", "timeout", ProviderFailureCodes.ProviderTimeout, "Geocoding timed out; geography context should ask for location confirmation.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[Geocoding] Provider call failed. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "open_meteo_geocoding", "degraded", ProviderFailureCodes.UnknownFailure, "Geocoding failed safely; do not invent location data.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
    }

    private async Task<ProviderToolResultDto> RecordAsync(ProviderToolResultDto result, Guid? userId, Guid? sessionId, Guid? topicId, CancellationToken ct)
    {
        await ProviderTelemetry.RecordAsync(_telemetry, result, userId, sessionId, topicId, ct);
        return result;
    }
}

public sealed class VisualArtifactProvider : IVisualArtifactProvider
{
    private readonly IConfiguration _configuration;
    private readonly IRuntimeTelemetryService _telemetry;

    public VisualArtifactProvider(IConfiguration configuration, IRuntimeTelemetryService telemetry)
    {
        _configuration = configuration;
        _telemetry = telemetry;
    }

    public async Task<ProviderToolResultDto> CreateVisualAsync(string prompt, string artifactType = "image_prompt", Guid? userId = null, Guid? sessionId = null, Guid? topicId = null, CancellationToken ct = default)
    {
        const string toolId = "visual_generation";
        var enabled = bool.TryParse(_configuration["Tools:VisualGeneration:Enabled"] ?? _configuration["AI:VisualGeneration:Enabled"], out var value) && value;
        var safePrompt = string.IsNullOrWhiteSpace(prompt)
            ? "educational concept diagram, clean labels, high contrast"
            : prompt.Trim();

        ProviderToolResultDto result;
        if (!enabled)
        {
            result = ProviderToolResultDto.Fallback(
                toolId,
                "visual_fallback",
                "degraded",
                ProviderFailureCodes.ProviderDisabled,
                "Visual image generation is disabled; use Mermaid/table fallback instead.");
        }
        else
        {
            var url = $"https://image.pollinations.ai/prompt/{Uri.EscapeDataString(safePrompt)}";
            var dto = new VisualArtifactResultDto(safePrompt, url, "pollinations", "image_url", "Mermaid/table fallback remains available.");
            result = new ProviderToolResultDto(true, toolId, "pollinations", "ready", dto, [new ProviderCitationDto("Generated visual prompt", url, "Pollinations", DateTime.UtcNow, 0.60)], DateTime.UtcNow, 0, false, null, "Visual artifact URL is available; verify it before treating it as factual evidence.", 0.60, 1);
        }

        await ProviderTelemetry.RecordAsync(_telemetry, result, userId, sessionId, topicId, ct);
        return result;
    }
}

public sealed class MarketDataProvider : IMarketDataProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IRuntimeTelemetryService _telemetry;
    private readonly ILogger<MarketDataProvider> _logger;

    public MarketDataProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration, IRuntimeTelemetryService telemetry, ILogger<MarketDataProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<ProviderToolResultDto> GetMarketDataAsync(string assetIds, Guid? userId = null, Guid? sessionId = null, Guid? topicId = null, CancellationToken ct = default)
    {
        const string toolId = "crypto";
        var sw = Stopwatch.StartNew();
        var providerDisabled = bool.TryParse(_configuration["Tools:Crypto:Disabled"], out var disabled) && disabled;
        if (providerDisabled)
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "coingecko", "disabled", ProviderFailureCodes.ProviderDisabled, "Crypto market data is disabled; do not provide investment advice."), userId, sessionId, topicId, ct);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var ids = string.Join(",", (assetIds ?? "bitcoin,ethereum").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8));
            var client = _httpClientFactory.CreateClient("MarketData");
            var url = $"api/v3/simple/price?ids={Uri.EscapeDataString(ids)}&vs_currencies=usd&include_24hr_change=true&include_last_updated_at=true";
            var response = await client.GetAsync(url, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "coingecko", "degraded", ProviderFailureCodes.ProviderError, $"Market data provider failed with status {(int)response.StatusCode}.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            using var doc = JsonDocument.Parse(body);
            var data = new List<MarketDataDto>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var price = property.Value.TryGetProperty("usd", out var usd) && usd.TryGetDecimal(out var p) ? p : (decimal?)null;
                var change = property.Value.TryGetProperty("usd_24h_change", out var ch) && ch.TryGetDecimal(out var c) ? c : (decimal?)null;
                var timestamp = property.Value.TryGetProperty("last_updated_at", out var lu) && lu.TryGetInt64(out var seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                    : DateTime.UtcNow;
                data.Add(new MarketDataDto(property.Name, property.Name.ToUpperInvariant(), price, change, timestamp, "CoinGecko", "Educational market data only. This is not investment advice."));
            }

            if (data.Count == 0)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "coingecko", "empty", ProviderFailureCodes.EmptyResult, "No market data was returned. Do not provide investment advice.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            var result = new ProviderToolResultDto(true, toolId, "coingecko", "ready", data, [new ProviderCitationDto("CoinGecko market data", null, "CoinGecko", DateTime.UtcNow, 0.8)], DateTime.UtcNow, sw.ElapsedMilliseconds, false, null, "Market data is available for educational explanation only. This is not investment advice.", 0.8, data.Count);
            return await RecordAsync(result, userId, sessionId, topicId, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "coingecko", "timeout", ProviderFailureCodes.ProviderTimeout, "Market data provider timed out; do not provide investment advice.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            _logger.LogWarning(
                "[MarketData] Provider call failed. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "coingecko", "degraded", ex is JsonException ? ProviderFailureCodes.MalformedResponse : ProviderFailureCodes.ProviderError, "Market data provider failed safely. This is not investment advice.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
    }

    private async Task<ProviderToolResultDto> RecordAsync(ProviderToolResultDto result, Guid? userId, Guid? sessionId, Guid? topicId, CancellationToken ct)
    {
        await ProviderTelemetry.RecordAsync(_telemetry, result, userId, sessionId, topicId, ct);
        return result;
    }
}

internal static class ProviderTelemetry
{
    public static Task RecordAsync(IRuntimeTelemetryService telemetry, ProviderToolResultDto result, Guid? userId, Guid? sessionId, Guid? topicId, CancellationToken ct)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            result.Status,
            result.SafeMessage,
            result.Confidence,
            result.SourceCount,
            citations = result.Citations.Select(c => new { c.Label, c.Url, c.SourceName, c.PublishedAt })
        });
        return telemetry.RecordToolEventAsync(new ToolTelemetryEventRequest(
            userId,
            sessionId,
            topicId,
            result.ToolId,
            result.Status,
            result.Provider,
            null,
            result.LatencyMs,
            result.Success,
            result.ErrorCode,
            result.FallbackUsed,
            null,
            metadata), ct);
    }
}

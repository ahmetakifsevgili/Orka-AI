using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

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
            var url = $"v1/result?appid={Uri.EscapeDataString(appId)}&i={Uri.EscapeDataString(query)}";
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
                "Wolfram Alpha computation result is available.",
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
            _logger.LogWarning(ex, "[Wolfram] Provider call failed.");
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
        if (string.IsNullOrWhiteSpace(apiKey))
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "newsapi", "disabled", ProviderFailureCodes.ProviderMissing, "Current news provider is not configured; do not infer current events from model memory."), userId, sessionId, topicId, ct);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var safeCount = Math.Clamp(count, 1, 10);
            var client = _httpClientFactory.CreateClient("News");
            var url = $"v2/everything?q={Uri.EscapeDataString(query)}&language={Uri.EscapeDataString(language)}&pageSize={safeCount}&sortBy=publishedAt&apiKey={Uri.EscapeDataString(apiKey)}";
            var response = await client.GetAsync(url, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "newsapi", "degraded", ProviderFailureCodes.ProviderError, $"News provider failed with status {(int)response.StatusCode}.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("articles", out var articles) || articles.ValueKind != JsonValueKind.Array)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "newsapi", "malformed", ProviderFailureCodes.MalformedResponse, "News provider returned an unexpected response.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            var parsed = articles.EnumerateArray()
                .Select(ParseArticle)
                .Where(a => !string.IsNullOrWhiteSpace(a.Title))
                .Take(safeCount)
                .ToList();
            if (parsed.Count == 0)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "newsapi", "empty", ProviderFailureCodes.EmptyResult, "No sourced current news result was found.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            var citations = parsed
                .Select(a => new ProviderCitationDto(a.Title, a.Url, a.SourceName, a.PublishedAt, 0.85))
                .ToList();
            var result = new ProviderToolResultDto(true, toolId, "newsapi", "ready", parsed, citations, DateTime.UtcNow, sw.ElapsedMilliseconds, false, null, "Sourced current news results are available.", 0.85, parsed.Count);
            return await RecordAsync(result, userId, sessionId, topicId, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "newsapi", "timeout", ProviderFailureCodes.ProviderTimeout, "News provider timed out; do not invent current-news details.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[News] Malformed response.");
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "newsapi", "malformed", ProviderFailureCodes.MalformedResponse, "News provider returned malformed data.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[News] Provider call failed.");
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "newsapi", "degraded", ProviderFailureCodes.UnknownFailure, "News provider failed safely; do not infer current events.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
    }

    private static NewsArticleDto ParseArticle(JsonElement article)
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

    public async Task<ProviderToolResultDto> GetWeatherAsync(double latitude, double longitude, string? locationName = null, Guid? userId = null, Guid? sessionId = null, Guid? topicId = null, CancellationToken ct = default)
    {
        const string toolId = "weather";
        var sw = Stopwatch.StartNew();
        var enabled = bool.TryParse(_configuration["Tools:Weather:Enabled"], out var enabledValue) && enabledValue;
        var apiKey = _configuration["Tools:Weather:ApiKey"] ?? _configuration["OpenWeatherMap:ApiKey"];
        if (!enabled || string.IsNullOrWhiteSpace(apiKey))
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "openweathermap", "disabled", enabled ? ProviderFailureCodes.ProviderMissing : ProviderFailureCodes.ProviderDisabled, "Weather provider is not configured; no live weather data was used."), userId, sessionId, topicId, ct);
        if (double.IsNaN(latitude) || double.IsNaN(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "openweathermap", "blocked", "malformed_location", "Weather location is invalid.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var client = _httpClientFactory.CreateClient("Weather");
            var url = $"data/2.5/weather?lat={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&appid={Uri.EscapeDataString(apiKey)}&units=metric";
            var response = await client.GetAsync(url, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
                return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "openweathermap", "degraded", ProviderFailureCodes.ProviderError, $"Weather provider failed with status {(int)response.StatusCode}.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);

            using var doc = JsonDocument.Parse(body);
            var temp = doc.RootElement.TryGetProperty("main", out var main) && main.TryGetProperty("temp", out var tempProp) ? tempProp.GetDouble() : (double?)null;
            var conditions = doc.RootElement.TryGetProperty("weather", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0 && arr[0].TryGetProperty("description", out var desc)
                ? desc.GetString()
                : null;
            var name = !string.IsNullOrWhiteSpace(locationName)
                ? locationName
                : doc.RootElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? $"{latitude},{longitude}" : $"{latitude},{longitude}";
            var dto = new WeatherContextDto(name, DateTime.UtcNow, temp, conditions, "OpenWeatherMap");
            var result = new ProviderToolResultDto(true, toolId, "openweathermap", "ready", dto, [new ProviderCitationDto("OpenWeatherMap current weather", null, "OpenWeatherMap", DateTime.UtcNow, 0.8)], DateTime.UtcNow, sw.ElapsedMilliseconds, false, null, "Current weather context is available.", 0.8, 1);
            return await RecordAsync(result, userId, sessionId, topicId, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "openweathermap", "timeout", ProviderFailureCodes.ProviderTimeout, "Weather provider timed out.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            _logger.LogWarning(ex, "[Weather] Provider call failed.");
            return await RecordAsync(ProviderToolResultDto.Fallback(toolId, "openweathermap", "degraded", ex is JsonException ? ProviderFailureCodes.MalformedResponse : ProviderFailureCodes.ProviderError, "Weather provider failed safely.", sw.ElapsedMilliseconds), userId, sessionId, topicId, ct);
        }
    }

    private async Task<ProviderToolResultDto> RecordAsync(ProviderToolResultDto result, Guid? userId, Guid? sessionId, Guid? topicId, CancellationToken ct)
    {
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
        var enabled = bool.TryParse(_configuration["Tools:Crypto:Enabled"], out var value) && value;
        if (!enabled)
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
            _logger.LogWarning(ex, "[MarketData] Provider call failed.");
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

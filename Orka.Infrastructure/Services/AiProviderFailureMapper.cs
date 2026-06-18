using System.Net;
using System.Security.Cryptography;
using System.Text;
using Orka.Core.Enums;
using Orka.Core.Exceptions;

namespace Orka.Infrastructure.Services;

internal static class AiProviderFailureMapper
{
    public static AiProviderCallException FromResponse(
        string provider,
        string? model,
        HttpResponseMessage response,
        string? responseBody)
    {
        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiProviderFailureKind.Authentication,
            HttpStatusCode.PaymentRequired => AiProviderFailureKind.QuotaExceeded,
            HttpStatusCode.TooManyRequests => AiProviderFailureKind.RateLimited,
            HttpStatusCode.RequestEntityTooLarge => AiProviderFailureKind.RequestTooLarge,
            >= HttpStatusCode.InternalServerError => AiProviderFailureKind.ServerError,
            _ => AiProviderFailureKind.Unknown
        };

        var retryable = kind is AiProviderFailureKind.RateLimited or AiProviderFailureKind.ServerError;
        var fallbackable = retryable || kind is AiProviderFailureKind.QuotaExceeded or AiProviderFailureKind.RequestTooLarge;
        return new AiProviderCallException(
            provider,
            model,
            role: null,
            kind,
            "AI provider gecici olarak kullanilamiyor.",
            response.StatusCode,
            response.Headers.RetryAfter?.Delta,
            isRetryable: retryable,
            isFallbackable: fallbackable,
            redactedDiagnostic: BuildResponseDiagnostic(provider, model, response, responseBody, kind, fallbackable));
    }

    public static AiProviderCallException FromException(
        string provider,
        string? model,
        Exception exception,
        AiProviderFailureKind fallbackKind = AiProviderFailureKind.TransientNetwork)
    {
        if (exception is AiProviderCallException ai)
            return ai;

        var kind = exception switch
        {
            TaskCanceledException or TimeoutException => AiProviderFailureKind.Timeout,
            _ when IsTimeoutRejected(exception) => AiProviderFailureKind.Timeout,
            System.Text.Json.JsonException or InvalidOperationException => AiProviderFailureKind.InvalidResponse,
            HttpRequestException httpEx when httpEx.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiProviderFailureKind.Authentication,
            HttpRequestException httpEx when httpEx.StatusCode is HttpStatusCode.PaymentRequired => AiProviderFailureKind.QuotaExceeded,
            HttpRequestException httpEx when httpEx.StatusCode is HttpStatusCode.TooManyRequests => AiProviderFailureKind.RateLimited,
            HttpRequestException httpEx when httpEx.StatusCode is HttpStatusCode.RequestEntityTooLarge => AiProviderFailureKind.RequestTooLarge,
            HttpRequestException httpEx when httpEx.StatusCode >= HttpStatusCode.InternalServerError => AiProviderFailureKind.ServerError,
            HttpRequestException => fallbackKind,
            _ => AiProviderFailureKind.Unknown
        };

        var retryable = kind is AiProviderFailureKind.RateLimited
            or AiProviderFailureKind.ServerError
            or AiProviderFailureKind.Timeout
            or AiProviderFailureKind.TransientNetwork
            or AiProviderFailureKind.InvalidResponse;
        var fallbackable = retryable
            || kind is AiProviderFailureKind.QuotaExceeded
            || kind is AiProviderFailureKind.RequestTooLarge;

        return new AiProviderCallException(
            provider,
            model,
            role: null,
            kind,
            "AI provider gecici olarak kullanilamiyor.",
            statusCode: exception is HttpRequestException http ? http.StatusCode : null,
            isRetryable: retryable,
            isFallbackable: fallbackable,
            redactedDiagnostic: BuildExceptionDiagnostic(provider, model, exception, kind, fallbackable),
            innerException: exception);
    }

    private static string BuildResponseDiagnostic(
        string provider,
        string? model,
        HttpResponseMessage response,
        string? responseBody,
        AiProviderFailureKind kind,
        bool fallbackable)
    {
        var body = responseBody ?? string.Empty;
        var parts = new List<string>
        {
            $"provider={SafeToken(provider, "provider")}",
            $"status={(int)response.StatusCode}",
            $"category={kind.ToString().ToLowerInvariant()}",
            $"retryable={(kind is AiProviderFailureKind.RateLimited or AiProviderFailureKind.ServerError).ToString().ToLowerInvariant()}",
            $"fallbackable={fallbackable.ToString().ToLowerInvariant()}",
            $"bodyLength={body.Length}",
            $"bodyHash={Hash(body)}"
        };

        var safeModel = SafeToken(model, string.Empty);
        if (!string.IsNullOrWhiteSpace(safeModel))
            parts.Add($"model={safeModel}");

        return string.Join(' ', parts);
    }

    private static string BuildExceptionDiagnostic(
        string provider,
        string? model,
        Exception exception,
        AiProviderFailureKind kind,
        bool fallbackable)
    {
        var retryable = kind is AiProviderFailureKind.RateLimited
            or AiProviderFailureKind.ServerError
            or AiProviderFailureKind.Timeout
            or AiProviderFailureKind.TransientNetwork
            or AiProviderFailureKind.InvalidResponse;
        var parts = new List<string>
        {
            $"provider={SafeToken(provider, "provider")}",
            $"category={kind.ToString().ToLowerInvariant()}",
            $"exceptionType={SafeToken(exception.GetType().Name, "exception")}",
            $"retryable={retryable.ToString().ToLowerInvariant()}",
            $"fallbackable={fallbackable.ToString().ToLowerInvariant()}"
        };

        if (exception is HttpRequestException { StatusCode: { } statusCode })
            parts.Add($"status={(int)statusCode}");

        var safeModel = SafeToken(model, string.Empty);
        if (!string.IsNullOrWhiteSpace(safeModel))
            parts.Add($"model={safeModel}");

        return string.Join(' ', parts);
    }

    private static string SafeToken(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var chars = value
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '/' or ':')
            .ToArray();
        var safe = new string(chars).Trim('/', ':');

        return string.IsNullOrWhiteSpace(safe)
            ? fallback
            : safe.Length <= 120 ? safe : safe[..120];
    }

    private static bool IsTimeoutRejected(Exception exception) =>
        exception.GetType().Name.Contains("TimeoutRejected", StringComparison.OrdinalIgnoreCase);

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes)[..16].ToLowerInvariant()}";
    }
}

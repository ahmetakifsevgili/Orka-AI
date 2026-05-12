using System.Net;
using Orka.Core.Enums;
using Orka.Core.Exceptions;
using Orka.Infrastructure.Security;

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
            HttpStatusCode.TooManyRequests => AiProviderFailureKind.RateLimited,
            >= HttpStatusCode.InternalServerError => AiProviderFailureKind.ServerError,
            _ => AiProviderFailureKind.Unknown
        };

        var fallbackable = kind is AiProviderFailureKind.RateLimited or AiProviderFailureKind.ServerError;
        return new AiProviderCallException(
            provider,
            model,
            role: null,
            kind,
            "AI provider gecici olarak kullanilamiyor.",
            response.StatusCode,
            response.Headers.RetryAfter?.Delta,
            isRetryable: fallbackable,
            isFallbackable: fallbackable,
            redactedDiagnostic: SensitiveDataRedactor.Redact($"{(int)response.StatusCode} {Trim(responseBody)}"));
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
            System.Text.Json.JsonException or InvalidOperationException => AiProviderFailureKind.InvalidResponse,
            HttpRequestException httpEx when httpEx.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiProviderFailureKind.Authentication,
            HttpRequestException httpEx when httpEx.StatusCode is HttpStatusCode.TooManyRequests => AiProviderFailureKind.RateLimited,
            HttpRequestException httpEx when httpEx.StatusCode >= HttpStatusCode.InternalServerError => AiProviderFailureKind.ServerError,
            HttpRequestException => fallbackKind,
            _ => AiProviderFailureKind.Unknown
        };

        var fallbackable = kind is AiProviderFailureKind.RateLimited
            or AiProviderFailureKind.ServerError
            or AiProviderFailureKind.Timeout
            or AiProviderFailureKind.TransientNetwork
            or AiProviderFailureKind.InvalidResponse;

        return new AiProviderCallException(
            provider,
            model,
            role: null,
            kind,
            "AI provider gecici olarak kullanilamiyor.",
            statusCode: exception is HttpRequestException http ? http.StatusCode : null,
            isRetryable: fallbackable,
            isFallbackable: fallbackable,
            redactedDiagnostic: SensitiveDataRedactor.Redact(exception.Message),
            innerException: exception);
    }

    private static string Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length <= 300 ? value : value[..300];
    }
}

using System.Net;
using Orka.Core.Enums;

namespace Orka.Core.Exceptions;

public sealed class AiProviderCallException : Exception
{
    public AiProviderCallException(
        string provider,
        string? model,
        string? role,
        AiProviderFailureKind failureKind,
        string publicMessage,
        HttpStatusCode? statusCode = null,
        TimeSpan? retryAfter = null,
        bool isRetryable = false,
        bool isFallbackable = false,
        string? redactedDiagnostic = null,
        Exception? innerException = null)
        : base(publicMessage, innerException)
    {
        Provider = provider;
        Model = model;
        Role = role;
        FailureKind = failureKind;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
        IsRetryable = isRetryable;
        IsFallbackable = isFallbackable;
        RedactedDiagnostic = redactedDiagnostic ?? publicMessage;
    }

    public string Provider { get; }
    public string? Model { get; }
    public string? Role { get; }
    public AiProviderFailureKind FailureKind { get; }
    public HttpStatusCode? StatusCode { get; }
    public TimeSpan? RetryAfter { get; }
    public bool IsRetryable { get; }
    public bool IsFallbackable { get; }
    public string RedactedDiagnostic { get; }
}

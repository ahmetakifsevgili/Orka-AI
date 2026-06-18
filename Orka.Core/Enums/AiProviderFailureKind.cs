namespace Orka.Core.Enums;

public enum AiProviderFailureKind
{
    Configuration,
    Authentication,
    RateLimited,
    TransientNetwork,
    Timeout,
    RequestTooLarge,
    ServerError,
    InvalidResponse,
    QuotaExceeded,
    CircuitOpen,
    Unknown
}

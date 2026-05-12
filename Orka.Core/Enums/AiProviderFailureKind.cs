namespace Orka.Core.Enums;

public enum AiProviderFailureKind
{
    Configuration,
    Authentication,
    RateLimited,
    TransientNetwork,
    Timeout,
    ServerError,
    InvalidResponse,
    QuotaExceeded,
    CircuitOpen,
    Unknown
}

using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Request-scoped kaos bağlamı.
/// X-Chaos-Fail header'ından okunan değer burada saklanır.
/// </summary>
public class ChaosContext : IChaosContext
{
    private string? _failingProvider;

    public void SetFailingProvider(string providerName)
        => _failingProvider = providerName?.Trim();

    public bool IsProviderFailing(string providerName)
        => !string.IsNullOrEmpty(_failingProvider) &&
           string.Equals(_failingProvider, providerName, StringComparison.OrdinalIgnoreCase);
}

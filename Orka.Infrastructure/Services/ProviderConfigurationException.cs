namespace Orka.Infrastructure.Services;

public class ProviderConfigurationException : Exception
{
    public string Provider { get; }
    public string KeyPath { get; }

    public ProviderConfigurationException(string provider, string keyPath)
        : base($"Provider config missing: {provider} requires {keyPath}.")
    {
        Provider = provider;
        KeyPath = keyPath;
    }
}

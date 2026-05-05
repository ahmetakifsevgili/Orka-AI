using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class CryptoDataPlugin
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CryptoDataPlugin> _logger;

    public CryptoDataPlugin(IConfiguration configuration, ILogger<CryptoDataPlugin> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [KernelFunction, Description("Reports crypto market data tool availability. Educational market facts only; no financial advice.")]
    public Task<string> GetCryptoPrices(string coinIds = "bitcoin,ethereum,solana")
    {
        var enabled = bool.TryParse(_configuration["Tools:Crypto:Enabled"], out var value) && value;
        if (!enabled)
        {
            _logger.LogInformation("[Crypto] Disabled stub returned for {Coins}.", coinIds);
            return Task.FromResult("[crypto:disabled] Crypto data is beta-gated and currently disabled. Do not provide financial advice.");
        }

        return Task.FromResult("[crypto:disabled] Crypto provider is gated for backend hardening; no live call was made. No financial advice.");
    }
}

using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class CryptoDataPlugin
{
    private readonly IMarketDataProvider _provider;
    private readonly ILogger<CryptoDataPlugin> _logger;

    public CryptoDataPlugin(IMarketDataProvider provider, ILogger<CryptoDataPlugin> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    [KernelFunction, Description("Gets crypto/market data for educational explanation when configured. Never gives buy/sell/hold advice.")]
    public async Task<string> GetCryptoPrices(string coinIds = "bitcoin,ethereum,solana")
    {
        var result = await _provider.GetMarketDataAsync(coinIds);
        _logger.LogInformation("[Crypto] Result Status={Status} Success={Success}", result.Status, result.Success);
        return ProviderResultFormatter.Format(result);
    }
}

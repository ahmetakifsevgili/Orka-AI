using Microsoft.Extensions.Configuration;
using Orka.Infrastructure.Security;
using Xunit;

namespace Orka.API.Tests;

public sealed class JwtKeyResolverTests
{
    [Fact]
    public void EmptySecret_UsesDevelopmentFallbackOnlyInDevelopment()
    {
        var configuration = Config("");

        var key = JwtKeyResolver.Resolve(configuration, isDevelopment: true);

        Assert.False(string.IsNullOrWhiteSpace(key.Secret));
        Assert.NotNull(key.SigningKey);
    }

    [Fact]
    public void EmptySecret_ThrowsOutsideDevelopment()
    {
        var configuration = Config("");

        Assert.Throws<InvalidOperationException>(() =>
            JwtKeyResolver.Resolve(configuration, isDevelopment: false));
    }

    [Fact]
    public void ShortSecret_ThrowsInAllEnvironments()
    {
        var configuration = Config("short");

        Assert.Throws<InvalidOperationException>(() =>
            JwtKeyResolver.Resolve(configuration, isDevelopment: true));
    }

    [Fact]
    public void ValidSecret_ReturnsConfiguredSecret()
    {
        const string secret = "ORKA_VALID_TEST_SECRET_1234567890_ABCDEFGH";
        var configuration = Config(secret);

        var key = JwtKeyResolver.Resolve(configuration, isDevelopment: false);

        Assert.Equal(secret, key.Secret);
    }

    private static IConfiguration Config(string? secret) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT:Secret"] = secret
            })
            .Build();
}

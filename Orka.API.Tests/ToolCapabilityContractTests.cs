using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class ToolCapabilityContractTests
{
    [Fact]
    public void CapabilityMatrix_ClassifiesDirtyOrkaToolsWithoutSilentOmission()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Tavily:ApiKey"] = "",
                ["AI:WolframAlpha:AppId"] = "",
                ["AI:NewsAPI:ApiKey"] = "",
                ["Tools:IdeExecution:Enabled"] = "false",
                ["Tools:Weather:Enabled"] = "false",
                ["Tools:Crypto:Enabled"] = "false"
            })
            .Build();

        var service = new ToolCapabilityService(config, new TestEnvironment("Production"));
        var tools = service.GetCapabilities(includeInternal: true).ToDictionary(t => t.ToolId);

        string[] dirtyOrkaTools =
        [
            "wolfram_alpha",
            "ide_execution",
            "weather",
            "news",
            "crypto",
            "visual_generation",
            "youtube_pedagogy",
            "test_cleanup"
        ];

        foreach (var toolId in dirtyOrkaTools)
        {
            Assert.True(tools.ContainsKey(toolId), $"{toolId} must be explicitly classified.");
            Assert.NotEqual("unknown", tools[toolId].Decision, StringComparer.OrdinalIgnoreCase);
            Assert.True(tools[toolId].TelemetryEnabled);
        }

        Assert.Equal("Disabled", tools["wolfram_alpha"].Status);
        Assert.Equal("DISABLED_WITH_RUNTIME_STUB", tools["wolfram_alpha"].Decision);
        Assert.Equal("Enabled", tools["ide_execution"].Status);
        Assert.Equal("CORE_ENABLED_BEHIND_AUTH_AND_SANDBOX", tools["ide_execution"].Decision);
        Assert.Equal("High", tools["crypto"].RiskLevel);
        Assert.Equal("PRODUCTION_HARDENING", tools["cost_tracking"].Decision);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public TestEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Orka.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

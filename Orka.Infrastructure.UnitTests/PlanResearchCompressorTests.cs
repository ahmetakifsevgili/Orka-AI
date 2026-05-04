using Orka.Core.DTOs.Korteks;
using Orka.Core.Enums;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class PlanResearchCompressorTests
{
    [Fact]
    public void Compressor_TruncatesLongReport()
    {
        var compressor = new PlanResearchCompressor();
        var result = ResearchResult(report: string.Join('\n', Enumerable.Range(1, 50).Select(i => $"Fact {i}: current curriculum item is important for planning and practice sequencing.")));

        var context = compressor.Compress(result, new PlanResearchCompressionOptions { MaxKeyFacts = 3, MaxItemLength = 60 });

        Assert.True(context.KeyFacts.Count <= 3);
        Assert.All(context.KeyFacts, item => Assert.True(item.Length <= 60));
    }

    [Fact]
    public void Compressor_KeepsTopSourcesOnly()
    {
        var compressor = new PlanResearchCompressor();
        var result = ResearchResult(sources:
        [
            Source("WebSearch", "https://example.com/a", score: 0.2),
            Source("WebSearch", "https://example.com/b", score: 0.9),
            Source("Wikipedia", "https://example.com/b/", score: 0.1),
            Source("YouTube", "https://youtube.com/watch?v=1", score: 0.8),
            Source("Academic", "https://paper.example.org/1", score: 0.7)
        ]);

        var context = compressor.Compress(result, new PlanResearchCompressionOptions { MaxSources = 2 });

        Assert.Equal(2, context.TopSources.Count);
        Assert.Contains(context.TopSources, source => source.Url == "https://example.com/b");
        Assert.Contains(context.TopSources, source => source.Url == "https://youtube.com/watch?v=1");
    }

    [Fact]
    public void Compressor_DropsLongTranscript()
    {
        var compressor = new PlanResearchCompressor();
        var transcript = "transcript segment " + new string('x', 1500);
        var result = ResearchResult(report: $"Useful fact: learning paths should start with foundations.\n{transcript}");

        var context = compressor.Compress(result);
        var block = compressor.BuildPromptBlock(context);

        Assert.DoesNotContain("transcript segment", block, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("learning paths", block, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compressor_PreservesGroundingModeAndWarnings()
    {
        var compressor = new PlanResearchCompressor();
        var result = ResearchResult(
            mode: GroundingMode.PartialSourceGrounded,
            warnings: ["Tavily unavailable"],
            failures: ["Academic timeout"]);

        var context = compressor.Compress(result);

        Assert.Equal(GroundingMode.PartialSourceGrounded, context.GroundingMode);
        Assert.Contains("Tavily unavailable", context.ProviderWarnings);
        Assert.Contains("Academic timeout", context.ProviderWarnings);
        Assert.NotNull(context.FallbackWarning);
    }

    [Fact]
    public void Compressor_AddsFallbackWarning_WhenFallbackInternalKnowledge()
    {
        var compressor = new PlanResearchCompressor();
        var result = ResearchResult(mode: GroundingMode.FallbackInternalKnowledge, report: "Generic fact: start with prerequisites.");

        var context = compressor.Compress(result);

        Assert.Contains("fallback", context.FallbackWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.TopSources);
    }

    [Fact]
    public void Compressor_ReturnsBlockedDigest_WhenBlockedProvider()
    {
        var compressor = new PlanResearchCompressor();
        var result = ResearchResult(mode: GroundingMode.BlockedProvider, report: "", failures: ["provider blocked"]);

        var context = compressor.Compress(result);
        var block = compressor.BuildPromptBlock(context);

        Assert.Equal(GroundingMode.BlockedProvider, context.GroundingMode);
        Assert.Equal(0, context.SourceCount);
        Assert.Contains("provider blocked", block);
        Assert.Contains("GroundingMode: BlockedProvider", block);
    }

    [Fact]
    public void Compressor_EnforcesMaxTotalChars()
    {
        var compressor = new PlanResearchCompressor();
        var result = ResearchResult(
            report: string.Join('\n', Enumerable.Range(1, 100).Select(i => $"Fact {i}: current roadmap sequence requires bounded research evidence and practice.")),
            sources: Enumerable.Range(1, 10).Select(i => Source("WebSearch", $"https://example.com/{i}", score: i)).ToList());

        var block = compressor.BuildPromptBlock(
            compressor.Compress(result),
            new PlanResearchCompressionOptions { MaxTotalChars = 900, MaxItemLength = 120 });

        Assert.True(block.Length <= 900);
        Assert.Contains("GroundingMode", block);
        Assert.Contains("SourceCount", block);
    }

    private static KorteksResearchResultDto ResearchResult(
        GroundingMode mode = GroundingMode.SourceGrounded,
        string report = "Fact: current learning path should combine concepts, practice, and review.",
        List<SourceEvidenceDto>? sources = null,
        List<string>? warnings = null,
        List<string>? failures = null)
    {
        return new KorteksResearchResultDto
        {
            Topic = "Test topic",
            Report = report,
            GroundingMode = mode,
            Sources = sources ?? [Source("WebSearch", "https://example.com/source-1"), Source("Wikipedia", "https://example.org/source-2")],
            Warnings = warnings ?? [],
            ProviderFailures = failures ?? [],
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static SourceEvidenceDto Source(string provider, string url, double? score = null) =>
        new(
            Provider: provider,
            ToolName: $"{provider}Tool",
            Url: url,
            Title: $"{provider} source",
            Snippet: "Short source snippet for plan research.",
            PublishedAt: null,
            RetrievedAt: DateTimeOffset.UtcNow,
            RelevanceScore: score,
            SourceType: provider.Equals("YouTube", StringComparison.OrdinalIgnoreCase) ? "video" : "web",
            ExternalId: null,
            Warning: null);
}

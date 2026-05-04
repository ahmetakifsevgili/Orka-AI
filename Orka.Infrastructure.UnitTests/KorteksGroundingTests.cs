using Orka.Core.DTOs.Korteks;
using Orka.Core.Enums;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public class KorteksGroundingTests
{
    [Fact]
    public void GroundingMode_SourceGrounded_WhenReportTwoUrlsAndToolSuccess()
    {
        var mode = KorteksGroundingClassifier.Classify(
            "Grounded report",
            [
                Source("https://example.com/a"),
                Source("https://learn.microsoft.com/b")
            ],
            [Call(success: true)]);

        Assert.Equal(GroundingMode.SourceGrounded, mode);
    }

    [Fact]
    public void GroundingMode_Partial_WhenOneUrl()
    {
        var mode = KorteksGroundingClassifier.Classify(
            "Grounded report",
            [Source("https://example.com/a")],
            [Call(success: true)]);

        Assert.Equal(GroundingMode.PartialSourceGrounded, mode);
    }

    [Fact]
    public void GroundingMode_Fallback_WhenReportNoUrls()
    {
        var mode = KorteksGroundingClassifier.Classify(
            "A useful report without URL backed evidence.",
            [],
            []);

        Assert.Equal(GroundingMode.FallbackInternalKnowledge, mode);
    }

    [Fact]
    public void GroundingMode_Blocked_WhenEmptyOrOnlyDegraded()
    {
        var emptyMode = KorteksGroundingClassifier.Classify("", [], []);
        var degradedMode = KorteksGroundingClassifier.Classify("[web:degraded] provider unavailable", [], []);

        Assert.Equal(GroundingMode.BlockedProvider, emptyMode);
        Assert.Equal(GroundingMode.BlockedProvider, degradedMode);
    }

    [Fact]
    public void SourceEvidenceExtractor_DropsNoUrlResults()
    {
        var sources = KorteksSourceEvidenceExtractor.Extract(
            "WebSearch",
            "SearchWeb",
            "Title: result without URL\nSummary: no source link was provided",
            DateTimeOffset.UtcNow);

        Assert.Empty(sources);
    }

    [Fact]
    public void SourceEvidenceExtractor_TavilyWikipediaAcademicYouTube()
    {
        var text = """
        Web result
        URL: https://example.com/article
        Icerik: web snippet

        Wikipedia result
        https://en.wikipedia.org/wiki/Spaced_repetition

        Academic result
        https://doi.org/10.1000/example

        YouTube result
        https://www.youtube.com/watch?v=abc123
        Aciklama: video snippet
        """;

        var sources = KorteksSourceEvidenceExtractor.Extract("WebSearch", "SearchWeb", text, DateTimeOffset.UtcNow);

        Assert.Equal(4, sources.Count);
        Assert.All(sources, source => Assert.StartsWith("http", source.Url, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SourceEvidenceExtractor_DropsAssetLoginAndSitemapNoise()
    {
        var text = """
        Real result
        https://example.com/articles/korteks-nedir

        Login
        https://u.milliyet.com.tr/accounts/login

        Logo proxy
        https://kureansiklopedi.com/_next/image?url=https%3A%2F%2Fcdn.example.com%2Flogo.webp&w=1280&q=100

        Svg asset
        https://cdn.memorial.com.tr/files/images/hearth-white.svg

        Sitemap
        https://example.com/sitemap.xml
        """;

        var sources = KorteksSourceEvidenceExtractor.Extract("WebSearch", "SearchWeb", text, DateTimeOffset.UtcNow);

        var source = Assert.Single(sources);
        Assert.Equal("https://example.com/articles/korteks-nedir", source.Url);
    }

    [Fact]
    public void SourceEvidenceExtractor_WebSearchKeepsOnlyStructuredResultUrls()
    {
        var text = """
        [Kaynak 1] C# Async/Await Explained
        URL: https://blog.ndepend.com/c-async-await-explained/
        Icerik: navigation link https://blog.ndepend.com/ and svg namespace http://www.w3.org/2000/svg

        [Kaynak 2] Asynchronous programming - C#
        URL: https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/
        Icerik: download browser https://go.microsoft.com/fwlink/p/?LinkID=2092881
        """;

        var sources = KorteksSourceEvidenceExtractor.Extract("WebSearch", "SearchWebDeep", text, DateTimeOffset.UtcNow);

        Assert.Equal(
            [
                "https://blog.ndepend.com/c-async-await-explained/",
                "https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/"
            ],
            sources.Select(s => s.Url));
    }

    private static SourceEvidenceDto Source(string url) =>
        new(
            Provider: "WebSearch",
            ToolName: "SearchWeb",
            Url: url,
            Title: "Title",
            Snippet: null,
            PublishedAt: null,
            RetrievedAt: DateTimeOffset.UtcNow,
            RelevanceScore: null,
            SourceType: "web",
            ExternalId: null,
            Warning: null);

    private static ToolCallEvidenceDto Call(bool success) =>
        new(
            ToolName: "SearchWeb",
            Provider: "WebSearch",
            Invoked: true,
            Success: success,
            FailureReason: null,
            ResultCount: success ? 2 : 0,
            DurationMs: 1,
            DegradedMarker: null,
            Timestamp: DateTimeOffset.UtcNow);
}

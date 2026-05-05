using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class FinalCoreIntelligenceTests
{
    [Fact]
    public async Task WolframProvider_NoKeyReturnsSafeFallbackAndTelemetry()
    {
        var telemetry = new CapturingTelemetry();
        var provider = new WolframProvider(
            new FakeHttpClientFactory("{}"),
            Config([]),
            telemetry,
            NullLogger<WolframProvider>.Instance);

        var result = await provider.QueryAsync("integrate x^2");

        Assert.False(result.Success);
        Assert.Equal("provider_missing", result.ErrorCode);
        Assert.True(result.FallbackUsed);
        Assert.Equal("wolfram_alpha", telemetry.LastToolId);
        Assert.False(telemetry.LastSuccess);
    }

    [Fact]
    public async Task WolframProvider_FakeSuccessNormalizesComputation()
    {
        var telemetry = new CapturingTelemetry();
        var provider = new WolframProvider(
            new FakeHttpClientFactory("x^3/3"),
            Config([new("AI:WolframAlpha:AppId", "test-key")]),
            telemetry,
            NullLogger<WolframProvider>.Instance);

        var result = await provider.QueryAsync("integrate x^2");

        Assert.True(result.Success);
        Assert.Equal("ready", result.Status);
        Assert.Contains("LLM API", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("wolfram_alpha", telemetry.LastToolId);
        Assert.True(telemetry.LastSuccess);
    }

    [Fact]
    public async Task NewsProvider_FakeSuccessReturnsCitations()
    {
        var json = """
        {"articles":[{"title":"Physics education update","source":{"name":"Science Daily"},"url":"https://example.test/news","publishedAt":"2026-05-05T10:00:00Z","description":"A sourced education article."}]}
        """;
        var provider = new NewsProvider(
            new FakeHttpClientFactory(json),
            Config([new("AI:NewsAPI:ApiKey", "test-key")]),
            new CapturingTelemetry(),
            NullLogger<NewsProvider>.Instance);

        var result = await provider.SearchAsync("physics education");

        Assert.True(result.Success);
        Assert.Single(result.Citations);
        Assert.Equal("Science Daily", result.Citations[0].SourceName);
        Assert.NotNull(result.Citations[0].PublishedAt);
    }

    [Fact]
    public async Task NewsProvider_NoKeyUsesPublicGdeltFallback()
    {
        var json = """
        {"articles":[{"title":"Public climate education report","domain":"example.test","url":"https://example.test/gdelt","seendate":"20260505T120000Z"}]}
        """;
        var provider = new NewsProvider(
            new FakeHttpClientFactory(json),
            Config([]),
            new CapturingTelemetry(),
            NullLogger<NewsProvider>.Instance);

        var result = await provider.SearchAsync("climate education");

        Assert.True(result.Success);
        Assert.Equal("gdelt", result.Provider);
        Assert.Single(result.Citations);
        Assert.Equal("example.test", result.Citations[0].SourceName);
    }

    [Fact]
    public async Task WeatherProvider_MalformedLocationFailsSafelyBeforeProvider()
    {
        var provider = new WeatherProvider(
            new FakeHttpClientFactory("{}"),
            Config([new("Tools:Weather:Enabled", "true"), new("Tools:Weather:ApiKey", "test-key")]),
            new CapturingTelemetry(),
            NullLogger<WeatherProvider>.Instance);

        var result = await provider.GetWeatherAsync(200, 30, "bad");

        Assert.False(result.Success);
        Assert.Equal("malformed_location", result.ErrorCode);
    }

    [Fact]
    public async Task WeatherProvider_NoKeyUsesPublicOpenMeteoFallback()
    {
        var json = """
        {"current":{"temperature_2m":22.4,"weather_code":1}}
        """;
        var provider = new WeatherProvider(
            new FakeHttpClientFactory(json),
            Config([]),
            new CapturingTelemetry(),
            NullLogger<WeatherProvider>.Instance);

        var result = await provider.GetWeatherAsync(41.01, 28.97, "Istanbul");

        Assert.True(result.Success);
        Assert.Equal("open_meteo", result.Provider);
        Assert.Contains("Open-Meteo", result.SafeMessage + string.Join(",", result.Citations.Select(c => c.SourceName)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MarketDataProvider_FakeSuccessIncludesNoInvestmentAdviceGuard()
    {
        var json = """
        {"bitcoin":{"usd":65000.12,"usd_24h_change":1.5,"last_updated_at":1777980000}}
        """;
        var provider = new MarketDataProvider(
            new FakeHttpClientFactory(json),
            Config([]),
            new CapturingTelemetry(),
            NullLogger<MarketDataProvider>.Instance);

        var result = await provider.GetMarketDataAsync("bitcoin");

        Assert.True(result.Success);
        Assert.Contains("not investment advice", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("buy", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(result.Citations);
    }

    [Fact]
    public async Task MistakeClassifier_CodeSyntaxPersistsSignals()
    {
        var signals = new CapturingLearningSignalService();
        var classifier = new MistakeClassifierService(signals, NullLogger<MistakeClassifierService>.Instance);

        var result = await classifier.ClassifyAndRecordAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new MistakeClassificationRequest(
                "IDE",
                "compile",
                "Console.WriteLine(1)",
                "compile failed",
                Guid.NewGuid(),
                "csharp",
                "compile",
                CodePhase: "compile",
                CompileError: "CS1002 ; expected"));

        Assert.Equal("CodeSyntax", result.Category);
        Assert.Contains(LearningSignalTypes.MistakeClassified, signals.SignalTypes);
        Assert.Contains(LearningSignalTypes.MisconceptionDetected, signals.SignalTypes);
    }

    [Fact]
    public async Task YouTubeTeachingReference_FakeTranscriptRetrievesTeachingChunksAndSignal()
    {
        var signals = new CapturingLearningSignalService();
        var service = new YouTubeTeachingReferenceService(
            new FakeYouTubeTranscriptProvider(),
            signals,
            new CapturingTelemetry());

        var reference = await service.BuildReferenceAsync(
            new YouTubeTranscriptRequest("video123", Topic: "async deadlock example mistake"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.Equal("ready", reference.Status);
        Assert.NotEmpty(reference.EvidenceChunks);
        Assert.NotEmpty(reference.Examples);
        Assert.NotEmpty(reference.CommonMistakes);
        Assert.Contains(LearningSignalTypes.TeachingMoveApplied, signals.SignalTypes);
    }

    private static IConfiguration Config(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public FakeHttpClientFactory(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public HttpClient CreateClient(string name) =>
            new(new FakeHandler(_body, _status)) { BaseAddress = new Uri("https://provider.test/") };
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public FakeHandler(string body, HttpStatusCode status)
        {
            _body = body;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body)
            });
    }

    private sealed class CapturingTelemetry : IRuntimeTelemetryService
    {
        public string? LastToolId { get; private set; }
        public bool? LastSuccess { get; private set; }

        public Task RecordToolEventAsync(ToolTelemetryEventRequest request, CancellationToken ct = default)
        {
            LastToolId = request.ToolId;
            LastSuccess = request.Success;
            return Task.CompletedTask;
        }

        public Task RecordCostAsync(CostRecordRequest request, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CapturingLearningSignalService : ILearningSignalService
    {
        public List<string> SignalTypes { get; } = [];

        public Task RecordQuizAnsweredAsync(QuizAttempt attempt, CancellationToken ct = default) => Task.CompletedTask;

        public Task RecordSignalAsync(Guid userId, Guid? topicId, Guid? sessionId, string signalType, string? skillTag = null, string? topicPath = null, int? score = null, bool? isPositive = null, string? payloadJson = null, CancellationToken ct = default)
        {
            SignalTypes.Add(signalType);
            return Task.CompletedTask;
        }

        public Task<LearningTopicSummaryDto> GetTopicSummaryAsync(Guid userId, Guid topicId, CancellationToken ct = default) =>
            Task.FromResult(new LearningTopicSummaryDto(topicId, 0, 0, 0, [], []));

        public Task<IReadOnlyList<StudyRecommendationDto>> GetRecommendationsAsync(Guid userId, Guid topicId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StudyRecommendationDto>>([]);
    }

    private sealed class FakeYouTubeTranscriptProvider : IYouTubeTranscriptProvider
    {
        public Task<YouTubeTranscriptResult> GetTranscriptAsync(YouTubeTranscriptRequest request, CancellationToken ct = default)
        {
            var transcript = """
            First the teacher builds intuition for async deadlock with a queue example. For example, the UI thread waits while the continuation also needs the UI thread. A common mistake is blocking on Result or Wait. Practice by rewriting the blocking call with await.
            """;
            var chunks = YouTubeTranscriptProvider.ChunkTranscript(transcript, 120, 20);
            return Task.FromResult(new YouTubeTranscriptResult(true, "ready", request.VideoId, "Async deadlock", transcript, chunks, null, "ready"));
        }
    }
}

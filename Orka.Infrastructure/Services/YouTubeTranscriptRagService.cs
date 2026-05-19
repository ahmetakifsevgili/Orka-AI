using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed partial class YouTubeTranscriptProvider : IYouTubeTranscriptProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<YouTubeTranscriptProvider> _logger;

    public YouTubeTranscriptProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<YouTubeTranscriptProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<YouTubeTranscriptResult> GetTranscriptAsync(YouTubeTranscriptRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.VideoId))
            return Disabled(request.VideoId, "empty_video_id", "YouTube video id is required.");

        var apiKey = _configuration["AI:YouTube:ApiKey"] ?? _configuration["YouTube:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return Disabled(request.VideoId, "provider_missing", "YouTube transcript provider is not configured.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var client = _httpClientFactory.CreateClient("YouTubeTranscript");
            var response = await client.GetAsync($"transcripts/{Uri.EscapeDataString(request.VideoId)}?lang={Uri.EscapeDataString(request.Language ?? "tr")}&key={Uri.EscapeDataString(apiKey)}", timeout.Token);
            var transcript = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
                return Disabled(request.VideoId, "provider_error", $"YouTube transcript request failed with status {(int)response.StatusCode}.");
            if (string.IsNullOrWhiteSpace(transcript))
                return Disabled(request.VideoId, "empty_result", "YouTube transcript was not available.");

            var normalized = NormalizeTranscript(transcript);
            var chunks = ChunkTranscript(normalized);
            return new YouTubeTranscriptResult(true, "ready", request.VideoId, request.Topic, normalized, chunks, null, "YouTube transcript is available for pedagogy reference.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Disabled(request.VideoId, "provider_timeout", "YouTube transcript provider timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[YouTubeTranscriptRag] Transcript provider failed. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return Disabled(request.VideoId, "unknown_failure", "YouTube transcript provider failed safely.");
        }
    }

    public static IReadOnlyList<YouTubeTranscriptChunkDto> ChunkTranscript(string transcript, int chunkSize = 700, int overlap = 120)
    {
        var clean = NormalizeTranscript(transcript);
        if (string.IsNullOrWhiteSpace(clean)) return [];

        var chunks = new List<YouTubeTranscriptChunkDto>();
        var index = 0;
        var offset = 0;
        while (offset < clean.Length)
        {
            var length = Math.Min(chunkSize, clean.Length - offset);
            var text = clean.Substring(offset, length).Trim();
            if (!string.IsNullOrWhiteSpace(text))
                chunks.Add(new YouTubeTranscriptChunkDto(index++, text, offset, length));
            if (offset + length >= clean.Length) break;
            offset += Math.Max(1, chunkSize - overlap);
        }

        return chunks;
    }

    private static YouTubeTranscriptResult Disabled(string videoId, string errorCode, string message) =>
        new(false, errorCode == "provider_missing" ? "disabled" : "degraded", videoId, null, string.Empty, [], errorCode, message);

    private static string NormalizeTranscript(string transcript)
    {
        var clean = TagRegex().Replace(transcript ?? string.Empty, " ");
        clean = Regex.Replace(clean, @"\s+", " ");
        return clean.Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();
}

public sealed class YouTubeTeachingReferenceService : IYouTubeTeachingReferenceService
{
    private readonly IYouTubeTranscriptProvider _provider;
    private readonly ILearningSignalService _signals;
    private readonly IRuntimeTelemetryService _telemetry;

    public YouTubeTeachingReferenceService(
        IYouTubeTranscriptProvider provider,
        ILearningSignalService signals,
        IRuntimeTelemetryService telemetry)
    {
        _provider = provider;
        _signals = signals;
        _telemetry = telemetry;
    }

    public async Task<YouTubeTeachingReferenceDto> BuildReferenceAsync(
        YouTubeTranscriptRequest request,
        Guid? userId = null,
        Guid? topicId = null,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var transcript = await _provider.GetTranscriptAsync(request, ct);
        var status = transcript.Success ? "ready" : transcript.Status;
        var chunks = transcript.Chunks;
        var reference = transcript.Success
            ? BuildReadyReference(transcript, RetrieveRelevantChunks(chunks, request.Topic ?? request.VideoId, 5))
            : new YouTubeTeachingReferenceDto(status, request.VideoId, "No reliable transcript is available.", [], [], [], [], []);

        await _telemetry.RecordToolEventAsync(new ToolTelemetryEventRequest(
            userId,
            sessionId,
            topicId,
            "youtube_pedagogy",
            reference.Status,
            "youtube_transcript",
            null,
            0,
            transcript.Success,
            transcript.ErrorCode,
            !transcript.Success,
            null,
            System.Text.Json.JsonSerializer.Serialize(new { request.VideoId, request.Topic, chunkCount = chunks.Count, transcript.SafeMessage })), ct);

        if (userId.HasValue && topicId.HasValue && transcript.Success)
        {
            await _signals.RecordSignalAsync(
                userId.Value,
                topicId,
                sessionId,
                LearningSignalTypes.TeachingMoveApplied,
                skillTag: request.Topic,
                topicPath: "youtube-pedagogy",
                score: 100,
                isPositive: true,
                payloadJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    request.VideoId,
                    reference.TeachingFlow,
                    exampleCount = reference.Examples.Count,
                    commonMistakeCount = reference.CommonMistakes.Count
                }),
                ct: ct);
        }

        return reference;
    }

    public IReadOnlyList<YouTubeTranscriptChunkDto> RetrieveRelevantChunks(
        IReadOnlyList<YouTubeTranscriptChunkDto> chunks,
        string query,
        int take = 3)
    {
        if (chunks.Count == 0) return [];
        var terms = Regex.Split((query ?? string.Empty).ToLowerInvariant(), @"\W+")
            .Where(t => t.Length > 2)
            .Distinct()
            .ToArray();

        return chunks
            .Select(c => new
            {
                Chunk = c,
                Score = terms.Sum(t => c.Text.Contains(t, StringComparison.OrdinalIgnoreCase) ? 2 : 0)
                    + KeywordScore(c.Text)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.Index)
            .Take(Math.Max(1, take))
            .Select(x => x.Chunk)
            .ToList();
    }

    private static YouTubeTeachingReferenceDto BuildReadyReference(YouTubeTranscriptResult transcript, IReadOnlyList<YouTubeTranscriptChunkDto> evidence)
    {
        var text = string.Join(" ", evidence.Count > 0 ? evidence.Select(c => c.Text) : transcript.Chunks.Take(3).Select(c => c.Text));
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+")
            .Select(s => s.Trim())
            .Where(s => s.Length is > 20 and < 240)
            .ToList();

        var examples = Pick(sentences, "ornek", "örnek", "mesela", "example", "for instance");
        var analogies = Pick(sentences, "gibi", "benzet", "imagine", "düşün", "dusun", "like");
        var mistakes = Pick(sentences, "hata", "yanlış", "yanlis", "mistake", "confus", "dikkat");
        var practice = new List<string>
        {
            "Turn the clearest explanation into one micro exercise.",
            "Ask a misconception check before moving to the next step."
        };
        if (examples.Count > 0)
            practice.Insert(0, "Reuse the reference example with changed numbers or names.");

        var flow = string.Join(" -> ", sentences.Take(4));
        if (string.IsNullOrWhiteSpace(flow))
            flow = "Start with intuition, show one example, warn about a common mistake, then ask a practice question.";

        return new YouTubeTeachingReferenceDto("ready", transcript.VideoId, flow, examples, analogies, mistakes, practice.Take(4).ToList(), evidence);
    }

    private static IReadOnlyList<string> Pick(IReadOnlyList<string> sentences, params string[] needles) =>
        sentences
            .Where(s => needles.Any(n => s.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

    private static int KeywordScore(string text)
    {
        var score = 0;
        foreach (var keyword in new[] { "ornek", "örnek", "example", "hata", "mistake", "dikkat", "practice", "analog" })
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                score++;
        return score;
    }
}

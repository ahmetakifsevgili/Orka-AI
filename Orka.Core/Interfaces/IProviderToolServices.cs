using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface IWolframProvider
{
    Task<ProviderToolResultDto> QueryAsync(string query, Guid? userId = null, Guid? sessionId = null, Guid? topicId = null, CancellationToken ct = default);
}

public interface INewsProvider
{
    Task<ProviderToolResultDto> SearchAsync(string query, string language = "tr", int count = 5, Guid? userId = null, Guid? sessionId = null, Guid? topicId = null, CancellationToken ct = default);
}

public interface IWeatherProvider
{
    Task<ProviderToolResultDto> GetWeatherAsync(double latitude, double longitude, string? locationName = null, Guid? userId = null, Guid? sessionId = null, Guid? topicId = null, CancellationToken ct = default);
}

public interface IMarketDataProvider
{
    Task<ProviderToolResultDto> GetMarketDataAsync(string assetIds, Guid? userId = null, Guid? sessionId = null, Guid? topicId = null, CancellationToken ct = default);
}

public interface IMistakeClassifierService
{
    Task<MistakeClassificationResult> ClassifyAsync(MistakeClassificationRequest request, CancellationToken ct = default);

    Task<MistakeClassificationResult> ClassifyAndRecordAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        MistakeClassificationRequest request,
        CancellationToken ct = default);
}

public interface IYouTubeTranscriptProvider
{
    Task<YouTubeTranscriptResult> GetTranscriptAsync(YouTubeTranscriptRequest request, CancellationToken ct = default);
}

public interface IYouTubeTeachingReferenceService
{
    Task<YouTubeTeachingReferenceDto> BuildReferenceAsync(
        YouTubeTranscriptRequest request,
        Guid? userId = null,
        Guid? topicId = null,
        Guid? sessionId = null,
        CancellationToken ct = default);

    IReadOnlyList<YouTubeTranscriptChunkDto> RetrieveRelevantChunks(
        IReadOnlyList<YouTubeTranscriptChunkDto> chunks,
        string query,
        int take = 3);
}

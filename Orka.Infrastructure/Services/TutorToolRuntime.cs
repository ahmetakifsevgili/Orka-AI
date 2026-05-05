using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Chat;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class TutorToolRuntime : ITutorToolRuntime
{
    private readonly OrkaDbContext _db;
    private readonly IToolCapabilityService _capabilities;
    private readonly IRuntimeTelemetryService _telemetry;
    private readonly ILogger<TutorToolRuntime> _logger;

    public TutorToolRuntime(
        OrkaDbContext db,
        IToolCapabilityService capabilities,
        IRuntimeTelemetryService telemetry,
        ILogger<TutorToolRuntime> logger)
    {
        _db = db;
        _capabilities = capabilities;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UsedToolDto>> DetectToolUsageAsync(
        Guid userId,
        string userMessage,
        Session session,
        string assistantResponse,
        CancellationToken ct = default)
    {
        var text = $"{userMessage}\n{assistantResponse}".ToLowerInvariant();
        var tools = new List<UsedToolDto>();

        try
        {
            if (LooksLikeSourceQuery(text) && session.TopicId.HasValue)
            {
                var count = await _db.LearningSources.AsNoTracking()
                    .CountAsync(s => s.UserId == userId && s.TopicId == session.TopicId && !s.IsDeleted, ct);
                tools.Add(new UsedToolDto(
                    "sources_query",
                    count > 0 ? "ready" : "unavailable",
                    count > 0 ? $"{count} active source(s)" : "no active topic sources",
                    count > 0 ? null : "no_source_context",
                    ToolId: "sources_query",
                    Success: count > 0,
                    FallbackUsed: count <= 0,
                    ErrorCode: count > 0 ? null : "no_source_context",
                    SafeMessage: count > 0 ? "Topic source context is available." : "No active topic source context is available.",
                    GroundingMode: "document",
                    Timestamp: DateTime.UtcNow));
            }

            if (LooksLikeReviewQuery(text))
            {
                var dueCount = await _db.ReviewItems.AsNoTracking()
                    .CountAsync(r => r.UserId == userId &&
                                     r.Status == "active" &&
                                     r.DueAt <= DateTime.UtcNow &&
                                     (!session.TopicId.HasValue || r.TopicId == session.TopicId), ct);
                tools.Add(new UsedToolDto(
                    "review_query",
                    "ready",
                    $"{dueCount} due review item(s)",
                    null,
                    ToolId: "review_query",
                    Success: true,
                    FallbackUsed: false,
                    SafeMessage: "Durable review state is available.",
                    GroundingMode: "learning_state",
                    Timestamp: DateTime.UtcNow));
            }

            if (LooksLikeFlashcardQuery(text))
            {
                var cardCount = await _db.Flashcards.AsNoTracking()
                    .CountAsync(f => f.UserId == userId &&
                                     f.Status != "deleted" &&
                                     (!session.TopicId.HasValue || f.TopicId == session.TopicId), ct);
                tools.Add(new UsedToolDto(
                    "flashcards",
                    "ready",
                    $"{cardCount} flashcard(s)",
                    null,
                    ToolId: "flashcards",
                    Success: true,
                    FallbackUsed: false,
                    SafeMessage: "Durable flashcard state is available.",
                    GroundingMode: "learning_state",
                    Timestamp: DateTime.UtcNow));
            }

            if (LooksLikeDailyChallengeQuery(text))
            {
                var exists = await _db.DailyChallenges.AsNoTracking()
                    .AnyAsync(d => d.UserId == userId &&
                                   d.Date == DateTime.UtcNow.Date &&
                                   (!session.TopicId.HasValue || d.TopicId == session.TopicId), ct);
                tools.Add(new UsedToolDto(
                    "daily_challenge",
                    exists ? "ready" : "available",
                    exists ? "today challenge exists" : "lazy creation available",
                    null,
                    ToolId: "daily_challenge",
                    Success: true,
                    FallbackUsed: false,
                    SafeMessage: exists ? "Today's challenge exists." : "Daily challenge can be created lazily.",
                    GroundingMode: "learning_state",
                    Timestamp: DateTime.UtcNow));
            }

            if (LooksLikeBookmarkQuery(text))
            {
                tools.Add(new UsedToolDto(
                    "bookmarks",
                    "available",
                    "bookmark API/plugin available",
                    null,
                    ToolId: "bookmarks",
                    Success: true,
                    FallbackUsed: false,
                    SafeMessage: "Bookmark API and plugin are available.",
                    GroundingMode: "user_memory",
                    Timestamp: DateTime.UtcNow));
            }

            if (LooksLikeExternalToolQuery(text))
            {
                tools.Add(new UsedToolDto(
                    "semantic_kernel",
                    "ready",
                    "safe tutor tool runtime registered",
                    null));

                foreach (var toolId in DetectRequestedToolIds(text))
                {
                    var capability = _capabilities.GetCapability(toolId);
                    if (capability is null)
                        continue;

                    tools.Add(new UsedToolDto(
                        capability.ToolId,
                        capability.Status.ToLowerInvariant(),
                        capability.Decision,
                        capability.Status.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
                            ? capability.FallbackMode
                            : null,
                        ToolId: capability.ToolId,
                        Success: !capability.Status.Equals("Disabled", StringComparison.OrdinalIgnoreCase),
                        FallbackUsed: capability.Status.Equals("Disabled", StringComparison.OrdinalIgnoreCase),
                        Provider: capability.RequiresExternalProvider ? capability.DisplayName : null,
                        LatencyMs: 0,
                        ErrorCode: capability.Status.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
                            ? capability.FallbackMode
                            : null,
                        SafeMessage: capability.Notes,
                        GroundingMode: ToolGroundingMode(capability.Category),
                        Timestamp: DateTime.UtcNow));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[TutorToolRuntime] Metadata probe failed.");
            tools.Add(new UsedToolDto(
                "semantic_kernel",
                "degraded",
                "metadata probe failed",
                "tool_runtime_probe_failed",
                ToolId: "semantic_kernel",
                Success: false,
                FallbackUsed: true,
                ErrorCode: "tool_runtime_probe_failed",
                SafeMessage: "Tool metadata probe failed safely.",
                GroundingMode: "tool_runtime",
                Timestamp: DateTime.UtcNow));
        }

        var result = tools
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(12)
            .ToList();

        foreach (var tool in result)
        {
            await _telemetry.RecordToolEventAsync(new ToolTelemetryEventRequest(
                UserId: userId,
                SessionId: session.Id,
                TopicId: session.TopicId,
                ToolId: tool.Name,
                CapabilityStatus: tool.Status,
                Provider: null,
                Model: null,
                LatencyMs: 0,
                Success: !tool.Status.Contains("degraded", StringComparison.OrdinalIgnoreCase) &&
                         !tool.Status.Contains("disabled", StringComparison.OrdinalIgnoreCase),
                ErrorCode: tool.FallbackReason,
                FallbackUsed: !string.IsNullOrWhiteSpace(tool.FallbackReason),
                CorrelationId: null,
                MetadataJson: tool.Evidence));
        }

        return result;
    }

    private static bool LooksLikeSourceQuery(string text) =>
        ContainsAny(text, "kaynak", "source", "dokuman", "doküman", "pdf", "notebook", "[doc:");

    private static bool LooksLikeReviewQuery(string text) =>
        ContainsAny(text, "tekrar", "review", "srs", "pekiştir", "pekistir", "hangi konuyu calisayim", "hangi konuyu çalışayım");

    private static bool LooksLikeFlashcardQuery(string text) =>
        ContainsAny(text, "flashcard", "kart", "hafiza kart", "hafıza kart");

    private static bool LooksLikeDailyChallengeQuery(string text) =>
        ContainsAny(text, "daily challenge", "günlük görev", "gunluk gorev", "günlük challenge", "gunluk challenge");

    private static bool LooksLikeBookmarkQuery(string text) =>
        ContainsAny(text, "bookmark", "yer imi", "kaydet", "sakla");

    private static bool LooksLikeExternalToolQuery(string text) =>
        ContainsAny(text, "wolfram", "hesapla", "kod çalıştır", "kod calistir", "ide", "görsel", "gorsel", "youtube", "video", "weather", "hava durumu", "news", "haber", "crypto", "kripto");

    private static IEnumerable<string> DetectRequestedToolIds(string text)
    {
        if (ContainsAny(text, "wolfram", "integral", "denklem", "hesapla"))
            yield return "wolfram_alpha";
        if (ContainsAny(text, "kod çalıştır", "kod calistir", "ide", "run code", "execute code"))
            yield return "ide_execution";
        if (ContainsAny(text, "hava durumu", "weather", "iklim"))
            yield return "weather";
        if (ContainsAny(text, "haber", "news", "güncel", "guncel"))
            yield return "news";
        if (ContainsAny(text, "crypto", "kripto", "bitcoin", "ethereum"))
            yield return "crypto";
        if (ContainsAny(text, "görsel", "gorsel", "diagram", "çiz", "ciz"))
            yield return "visual_generation";
        if (ContainsAny(text, "youtube", "video", "hoca"))
            yield return "youtube_pedagogy";
    }

    private static string ToolGroundingMode(string category) => category.ToLowerInvariant() switch
    {
        "pedagogy_reference" => "youtube_pedagogy",
        "external_info" => "provider_grounded",
        "computation" => "computation",
        "visualization" => "generated_visualization",
        "code_execution" => "code_execution",
        "learning" => "learning_state",
        _ => "tool_runtime"
    };

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
}

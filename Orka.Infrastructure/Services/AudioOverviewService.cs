using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class AudioOverviewService : IAudioOverviewService
{
    private static readonly TimeSpan ScriptGenerationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TtsGenerationTimeout = TimeSpan.FromSeconds(20);

    private readonly OrkaDbContext _db;
    private readonly IAIAgentFactory _factory;
    private readonly IWikiService _wikiService;
    private readonly IEdgeTtsService _ttsService;
    private readonly ILogger<AudioOverviewService> _logger;

    public AudioOverviewService(
        OrkaDbContext db,
        IAIAgentFactory factory,
        IWikiService wikiService,
        IEdgeTtsService ttsService,
        ILogger<AudioOverviewService> logger)
    {
        _db = db;
        _factory = factory;
        _wikiService = wikiService;
        _ttsService = ttsService;
        _logger = logger;
    }

    public async Task<AudioOverviewJobDto> CreateOverviewAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        CancellationToken ct = default)
    {
        if (!topicId.HasValue && !sessionId.HasValue)
            throw new ArgumentException("Audio Overview icin topicId veya sessionId zorunlu.");

        if (topicId.HasValue)
        {
            var topicExists = await _db.Topics
                .AsNoTracking()
                .AnyAsync(t => t.Id == topicId.Value && t.UserId == userId, ct);
            if (!topicExists)
                throw new NotFoundException("Audio overview topic bulunamadi.");
        }

        if (sessionId.HasValue)
        {
            var sessionExists = await _db.Sessions
                .AsNoTracking()
                .AnyAsync(s => s.Id == sessionId.Value && s.UserId == userId, ct);
            if (!sessionExists)
                throw new NotFoundException("Audio overview session bulunamadi.");
        }

        var job = new AudioOverviewJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            Status = "generating",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.AudioOverviewJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        try
        {
            var context = await BuildOverviewContextAsync(userId, topicId, sessionId, ct);
            var systemPrompt = """
                Sen Orka AI'nin Sesli Sinif podcast yapimcisisin.
                Verilen ders/kaynak icerigini 2-3 dakikalik, uc konusmaciya kadar destekleyen kisa bir podcast metnine cevir.
                Format kesinlikle satir satir soyle olmali:
                [HOCA]: ...
                [ASISTAN]: ...
                [KONUK]: ...
                KONUK opsiyoneldir ama konuya dis bakis veya ogrenci sesi katacaksa kullan.
                Markdown, JSON veya aciklama ekleme.
                Turkce, sicak, ogretici ve akici yaz.
                """;

            using var scriptTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            scriptTimeout.CancelAfter(ScriptGenerationTimeout);
            var script = await _factory.CompleteChatAsync(
                AgentRole.Summarizer,
                systemPrompt,
                $"Kaynak materyal:\n\n{context}",
                scriptTimeout.Token);

            script = AudioDialogueFormatter.NormalizeScript(script);
            var speakers = AudioDialogueFormatter.ParseSpeakers(script);

            job.Script = script;
            job.SpeakersJson = JsonSerializer.Serialize(speakers);

            try
            {
                using var ttsTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                ttsTimeout.CancelAfter(TtsGenerationTimeout);
                job.AudioBytes = await _ttsService.SynthesizeDialogueAsync(script, ttsTimeout.Token);
                job.ContentType = "audio/mpeg";
                job.Status = "ready";
                _logger.LogInformation("[AudioOverview] Edge-TTS audio generated. Job={JobId} Bytes={Bytes}", job.Id, job.AudioBytes.Length);
            }
            catch (Exception ttsEx)
            {
                _logger.LogWarning(ttsEx, "[AudioOverview] Edge-TTS failed; switching to script-only. Job={JobId}", job.Id);
                job.Status = "script-only";
                job.ErrorMessage = "Edge-TTS uretilemedi; frontend browser TTS fallback kullanmali.";
            }

            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AudioOverview] Generation failed. Job={JobId}", job.Id);
            job.Status = "script-only";
            job.ErrorMessage = "Backend TTS uretilemedi; frontend browser TTS fallback kullanmali.";
            job.Script = string.IsNullOrWhiteSpace(job.Script)
                ? "[HOCA]: Sesli sinif su anda tam ses dosyasi uretemedi, ama bu metni tarayici sesiyle dinleyebilirsin."
                : job.Script;
            job.SpeakersJson = JsonSerializer.Serialize(AudioDialogueFormatter.ParseSpeakers(job.Script));
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        return ToDto(job);
    }

    public async Task<AudioOverviewJobDto?> GetOverviewAsync(
        Guid userId,
        Guid jobId,
        CancellationToken ct = default)
    {
        var job = await _db.AudioOverviewJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, ct);

        return job == null ? null : ToDto(job);
    }

    public async Task<(byte[] Bytes, string ContentType, string FileName)?> GetAudioAsync(
        Guid userId,
        Guid jobId,
        CancellationToken ct = default)
    {
        var job = await _db.AudioOverviewJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, ct);

        if (job?.AudioBytes == null || job.AudioBytes.Length == 0) return null;
        var contentType = NormalizeContentType(job.ContentType);
        return (job.AudioBytes, contentType, BuildFileName(job.Id, contentType));
    }

    private async Task<string> BuildOverviewContextAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
        var sb = new StringBuilder();

        if (topicId.HasValue)
        {
            var topic = await _db.Topics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId, ct);
            if (topic != null) sb.AppendLine($"Konu: {topic.Title}");

            var wiki = await _wikiService.GetWikiFullContentAsync(topicId.Value, userId);
            if (!string.IsNullOrWhiteSpace(wiki))
                sb.AppendLine(Trim(wiki, 5000));

            var sourceBits = await _db.SourceChunks
                .AsNoTracking()
                .Include(c => c.LearningSource)
                .Where(c => c.LearningSource.UserId == userId && c.LearningSource.TopicId == topicId)
                .OrderBy(c => c.ChunkIndex)
                .Take(5)
                .Select(c => $"[doc:{c.LearningSourceId}:p{c.PageNumber}] {c.Text}")
                .ToListAsync(ct);
            if (sourceBits.Count > 0)
                sb.AppendLine("\nKaynak notlari:\n" + string.Join("\n\n", sourceBits));
        }

        if (sessionId.HasValue)
        {
            var messages = await _db.Messages
                .AsNoTracking()
                .Where(m => m.SessionId == sessionId && m.UserId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(12)
                .OrderBy(m => m.CreatedAt)
                .Select(m => $"{m.Role}: {m.Content}")
                .ToListAsync(ct);
            if (messages.Count > 0)
                sb.AppendLine("\nSohbet ozeti:\n" + string.Join("\n", messages));
        }

        var text = sb.ToString();
        return string.IsNullOrWhiteSpace(text) ? "Henuz yeterli ders icerigi yok." : Trim(text, 8000);
    }

    private static string Trim(string value, int maxChars) =>
        value.Length > maxChars ? value[..maxChars] + "\n[...kirpildi]" : value;

    private static AudioOverviewJobDto ToDto(AudioOverviewJob job)
    {
        IReadOnlyList<string> speakers = [];
        try
        {
            speakers = JsonSerializer.Deserialize<List<string>>(job.SpeakersJson) ?? [];
        }
        catch (JsonException)
        {
            speakers = [];
        }

        var normalizedStatus = NormalizeStatus(job.Status);
        var contentType = job.AudioBytes is { Length: > 0 } ? NormalizeContentType(job.ContentType) : null;
        return new AudioOverviewJobDto(
            job.Id,
            normalizedStatus,
            job.Script,
            speakers,
            job.ErrorMessage,
            job.CreatedAt,
            contentType,
            contentType == null ? null : BuildFileName(job.Id, contentType),
            normalizedStatus == "ready" ? $"/api/audio/overview/{job.Id}/stream" : null,
            normalizedStatus == "script-only" ? job.ErrorMessage ?? "tts_unavailable_script_only" : null,
            job.UpdatedAt);
    }

    private static string NormalizeStatus(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? "pending"
            : status.Trim().ToLowerInvariant() switch
            {
                "processing" => "generating",
                "generated" => "ready",
                "script_only" => "script-only",
                "script-only" => "script-only",
                "ready" => "ready",
                "failed" => "failed",
                "generating" => "generating",
                _ => status.Trim().ToLowerInvariant()
            };

    private static string NormalizeContentType(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? "audio/mpeg" : contentType.Trim().ToLowerInvariant();

    private static string BuildFileName(Guid id, string contentType)
    {
        var ext = contentType.Contains("wav", StringComparison.OrdinalIgnoreCase) ? "wav" : "mp3";
        return $"orka-audio-overview-{id}.{ext}";
    }
}

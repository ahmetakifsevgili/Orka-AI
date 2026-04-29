using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private readonly OrkaDbContext _db;
    private readonly IAIAgentFactory _factory;
    private readonly IWikiService _wikiService;
    private readonly IEdgeTtsService _ttsService;
    private readonly ILogger<AudioOverviewService> _logger;

    private static readonly Regex SpeakerRegex =
        new(@"\[(HOCA|ASISTAN|KONUK)\]\s*:\s*(.+?)(?=\n\s*\[(?:HOCA|ASISTAN|KONUK)\]\s*:|\z)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

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
            throw new InvalidOperationException("Audio Overview için topicId veya sessionId zorunlu.");

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
                Sen Orka AI'nin Sesli Sınıf podcast yapımcısısın.
                Verilen ders/kaynak içeriğini 2-3 dakikalık, üç konuşmacıya kadar destekleyen kısa bir podcast metnine çevir.
                Format kesinlikle satır satır şöyle olmalı:
                [HOCA]: ...
                [ASISTAN]: ...
                [KONUK]: ...
                KONUK opsiyoneldir ama konuya dış bakış veya öğrenci sesi katacaksa kullan.
                Markdown, JSON veya açıklama ekleme.
                Türkçe, sıcak, öğretici ve akıcı yaz.
                """;

            var script = await _factory.CompleteChatAsync(
                AgentRole.Summarizer,
                systemPrompt,
                $"Kaynak materyal:\n\n{context}",
                ct);

            script = NormalizeScript(script);
            var speakers = ParseSpeakers(script);

            job.Script = script;
            job.SpeakersJson = JsonSerializer.Serialize(speakers);

            try
            {
                job.AudioBytes = await _ttsService.SynthesizeDialogueAsync(script, ct);
                job.ContentType = "audio/mpeg";
                job.Status = "ready";
                _logger.LogInformation("[AudioOverview] Edge-TTS ses üretimi tamamlandı. Job={JobId} Bytes={Bytes}", job.Id, job.AudioBytes.Length);
            }
            catch (Exception ttsEx)
            {
                _logger.LogWarning(ttsEx, "[AudioOverview] Edge-TTS başarısız, script-only moduna geçiliyor. Job={JobId}", job.Id);
                job.Status = "script-only";
                job.ErrorMessage = "Edge-TTS üretilemedi; frontend browser TTS fallback kullanmalı.";
            }

            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AudioOverview] Üretim başarısız. Job={JobId}", job.Id);
            job.Status = "script-only";
            job.ErrorMessage = "Backend TTS üretilemedi; frontend browser TTS fallback kullanmalı.";
            job.Script = string.IsNullOrWhiteSpace(job.Script)
                ? "[HOCA]: Sesli sınıf şu anda tam ses dosyası üretemedi, ama bu metni tarayıcı sesiyle dinleyebilirsin."
                : job.Script;
            job.SpeakersJson = JsonSerializer.Serialize(ParseSpeakers(job.Script));
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        return ToDto(job);
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
        return (job.AudioBytes, job.ContentType, $"orka-audio-overview-{job.Id}.wav");
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
                sb.AppendLine("\nKaynak notları:\n" + string.Join("\n\n", sourceBits));
        }

        if (sessionId.HasValue)
        {
            var messages = await _db.Messages
                .AsNoTracking()
                .Where(m => m.SessionId == sessionId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(12)
                .OrderBy(m => m.CreatedAt)
                .Select(m => $"{m.Role}: {m.Content}")
                .ToListAsync(ct);
            if (messages.Count > 0)
                sb.AppendLine("\nSohbet özeti:\n" + string.Join("\n", messages));
        }

        var text = sb.ToString();
        return string.IsNullOrWhiteSpace(text) ? "Henüz yeterli ders içeriği yok." : Trim(text, 8000);
    }

    private static string NormalizeScript(string raw)
    {
        var clean = raw.Replace("```", "").Trim();
        return SpeakerRegex.IsMatch(clean)
            ? clean
            : $"[HOCA]: {clean}";
    }

    private static IReadOnlyList<string> ParseSpeakers(string script)
    {
        var speakers = SpeakerRegex.Matches(script)
            .Select(m => m.Groups[1].Value.ToUpperInvariant())
            .Distinct()
            .ToList();
        return speakers.Count == 0 ? ["HOCA"] : speakers;
    }


    private static string Trim(string value, int maxChars) =>
        value.Length > maxChars ? value[..maxChars] + "\n[...kırpıldı]" : value;

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

        return new AudioOverviewJobDto(job.Id, job.Status, job.Script, speakers, job.ErrorMessage, job.CreatedAt);
    }
}

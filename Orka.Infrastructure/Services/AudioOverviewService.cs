using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public class AudioOverviewService : IAudioOverviewService
{
    private static readonly TimeSpan ScriptGenerationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TtsGenerationTimeout = TimeSpan.FromSeconds(20);

    private readonly OrkaDbContext _db;
    private readonly IAIAgentFactory _factory;
    private readonly IWikiService _wikiService;
    private readonly ISourceEvidenceLifecycleService _sourceLifecycle;
    private readonly IAgenticTrustPolicyService _trust;
    private readonly IEdgeTtsService _ttsService;
    private readonly ILogger<AudioOverviewService> _logger;

    public AudioOverviewService(
        OrkaDbContext db,
        IAIAgentFactory factory,
        IWikiService wikiService,
        ISourceEvidenceLifecycleService sourceLifecycle,
        IAgenticTrustPolicyService trust,
        IEdgeTtsService ttsService,
        ILogger<AudioOverviewService> logger)
    {
        _db = db;
        _factory = factory;
        _wikiService = wikiService;
        _sourceLifecycle = sourceLifecycle;
        _trust = trust;
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
                throw new NotFoundException("Audio overview topic bulunamadı.");
        }

        if (sessionId.HasValue)
        {
            var sessionExists = await _db.Sessions
                .AsNoTracking()
                .AnyAsync(s => s.Id == sessionId.Value && s.UserId == userId, ct);
            if (!sessionExists)
                throw new NotFoundException("Audio overview session bulunamadı.");
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
            var packContext = await BuildNotebookPackContextAsync(userId, topicId, sessionId, ct);
            if (packContext != null)
            {
                var packScript = BuildNotebookPackScript(packContext);
                var trust = await _trust.CheckPublicPayloadAsync(userId, new AgenticTrustCheckRequestDto
                {
                    TopicId = packContext.TopicId,
                    SessionId = packContext.SessionId,
                    Surface = "audio_overview_script",
                    Content = packScript
                }, ct);
                if (!trust.Allowed)
                {
                    packScript = "[HOCA]: Bu OrkaLM paketi icin sesli anlatim guvenlik kontrolunden gecemedi.\n[ASISTAN]: Kaynak veya not icerigini temizleyip paketi yeniledikten sonra tekrar deneyebilirsin.";
                }

                job.TopicId = packContext.TopicId;
                job.SessionId = packContext.SessionId;
                job.Script = AudioDialogueFormatter.NormalizeScript(packScript);
                job.SpeakersJson = JsonSerializer.Serialize(AudioDialogueFormatter.ParseSpeakers(job.Script));
                await TryGenerateTtsAsync(job, ct);
                job.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return ToDto(job);
            }

            var context = await BuildOverviewContextAsync(userId, topicId, sessionId, ct);
            if (IsInsufficientOverviewContext(context))
            {
                job.Status = "script-only";
                job.ErrorMessage = "Audio overview icin henuz kaynak, wiki veya ders sohbeti yok.";
                job.Script = "[HOCA]: Bu konu icin henuz sesli derse donusturulecek yeterli kaynak veya ders icerigi yok.\n[ASISTAN]: Once Tutor'da bir ders isleyebilir, Wiki'ye not ekleyebilir veya kaynak yukleyebilirsin. Sonra Orka bu icerigi AI sesli ders akisine cevirebilir.";
                job.SpeakersJson = JsonSerializer.Serialize(AudioDialogueFormatter.ParseSpeakers(job.Script));
                job.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return ToDto(job);
            }

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

            await TryGenerateTtsAsync(job, ct);

            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError("[AudioOverview] Generation failed. JobRef={JobRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(job.Id, "job"),
                LogPrivacyGuard.SafeExceptionType(ex));
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

    private async Task TryGenerateTtsAsync(AudioOverviewJob job, CancellationToken ct)
    {
        try
        {
            using var ttsTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ttsTimeout.CancelAfter(TtsGenerationTimeout);
            var audioBytes = await _ttsService.SynthesizeDialogueAsync(job.Script, ttsTimeout.Token);
            if (audioBytes.Length == 0)
            {
                throw new InvalidOperationException("Edge-TTS returned an empty audio payload.");
            }

            job.AudioBytes = audioBytes;
            job.AudioByteLength = audioBytes.LongLength;
            job.AudioExpiresAt = DateTime.UtcNow.AddDays(7);
            job.AudioPurgedAt = null;
            job.ContentType = "audio/mpeg";
            job.Status = "ready";
            job.ErrorMessage = null;
            _logger.LogInformation("[AudioOverview] Edge-TTS audio generated. JobRef={JobRef} Bytes={Bytes}",
                LogPrivacyGuard.SafeId(job.Id, "job"),
                job.AudioBytes.Length);
        }
        catch (Exception ttsEx)
        {
            _logger.LogWarning("[AudioOverview] Edge-TTS failed; switching to script-only. JobRef={JobRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(job.Id, "job"),
                LogPrivacyGuard.SafeExceptionType(ttsEx));
            job.Status = "script-only";
            job.ErrorMessage = "Edge-TTS uretilemedi; frontend browser TTS fallback kullanmali.";
        }
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

    private async Task<NotebookPackAudioContext?> BuildNotebookPackContextAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        CancellationToken ct)
    {
        var resolvedTopicId = topicId;
        if (!resolvedTopicId.HasValue && sessionId.HasValue)
        {
            resolvedTopicId = await _db.Sessions.AsNoTracking()
                .Where(s => s.Id == sessionId.Value && s.UserId == userId)
                .Select(s => (Guid?)s.TopicId)
                .FirstOrDefaultAsync(ct);
        }

        var query = _db.LearningNotebookPacks.AsNoTracking()
            .Where(p => p.UserId == userId && !p.IsDeleted);
        if (resolvedTopicId.HasValue) query = query.Where(p => p.TopicId == resolvedTopicId.Value);
        if (sessionId.HasValue) query = query.Where(p => p.SessionId == sessionId.Value || p.SessionId == null);

        var pack = await query
            .OrderByDescending(p => p.SessionId == sessionId)
            .ThenByDescending(p => p.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (pack == null) return null;

        var artifactIds = ParseGuids(pack.ArtifactIdsJson).Take(12).ToArray();
        var artifacts = artifactIds.Length == 0
            ? new List<NotebookPackAudioArtifact>()
            : await _db.LearningArtifacts.AsNoTracking()
                .Where(a => a.UserId == userId && artifactIds.Contains(a.Id) && !a.IsDeleted)
                .OrderByDescending(a => a.ArtifactType == "study_guide")
                .ThenByDescending(a => a.ArtifactType == "misconception_repair_pack")
                .ThenByDescending(a => a.UpdatedAt)
                .Take(8)
                .Select(a => new NotebookPackAudioArtifact(a.ArtifactType, a.Title, a.SourceBasis, Trim(a.SafeContent, 900)))
                .ToListAsync(ct);

        var sourceBundle = await _sourceLifecycle.GetLatestSourceEvidenceBundleAsync(userId, pack.TopicId, pack.SessionId, ct);
        var metadata = ParseWikiPageMetadata(pack.SafeMetadataJson);
        return new NotebookPackAudioContext(
            pack.Id,
            pack.TopicId,
            pack.SessionId,
            pack.Title,
            pack.Summary,
            pack.PackType,
            pack.PackStatus,
            pack.EvidenceStatus,
            sourceBundle?.EvidenceStatus ?? pack.SourceReadiness,
            metadata.WikiPageTitle,
            ParseStrings(pack.CompletedConceptKeysJson),
            ParseStrings(pack.WeakConceptKeysJson),
            ParseStrings(pack.MisconceptionKeysJson),
            artifacts);
    }

    private static string BuildNotebookPackScript(NotebookPackAudioContext context)
    {
        var completed = JoinOrFallback(context.CompletedConceptKeys, "tamamlanan kavram sinyali henuz zayif");
        var weak = JoinOrFallback(context.WeakConceptKeys, "zayif kavram sinyali henuz yok");
        var misconceptions = JoinOrFallback(context.MisconceptionKeys, "belirgin yanlis anlama sinyali yok");
        var artifactNotes = context.Artifacts.Count == 0
            ? "Bu pack icin henuz ek calisma artifact'i yok."
            : string.Join("\n", context.Artifacts.Take(4).Select(a =>
                $"- {a.ArtifactType} ({a.SourceBasis}): {Trim(a.SafeContent.ReplaceLineEndings(" "), 360)}"));
        var pageLine = string.IsNullOrWhiteSpace(context.WikiPageTitle)
            ? "Bu anlatim genel OrkaLM pack uzerinden hazirlandi."
            : $"Bu anlatim `{context.WikiPageTitle}` Wiki sayfasina bagli OrkaLM pack uzerinden hazirlandi.";

        return AudioDialogueFormatter.NormalizeScript($"""
            [HOCA]: {pageLine}
            [ASISTAN]: Kisa ozet: {Trim(context.Summary, 420)}
            [HOCA]: Tamamlanan kavramlar: {completed}.
            [ASISTAN]: Dikkat isteyen alanlar: {weak}. Yanlis anlama sinyali: {misconceptions}.
            [HOCA]: Kaynak durumu {context.SourceReadiness}. Kaynak zemini sinirliyse bunu kaynakli kesin anlatim gibi sunmayacagiz.
            [ASISTAN]: Pack icindeki calisma notlari sunlar: {artifactNotes}
            [HOCA]: Kapanista pasif dinleme yerine aktif hatirlama yap: once bir kavrami kendi cumlenle acikla, sonra kisa review quiz veya flashcard ile kontrol et.
            """);
    }

    private async Task<string> BuildOverviewContextAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var hasMeaningfulContext = false;

        if (topicId.HasValue)
        {
            var topic = await _db.Topics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId, ct);
            if (topic != null) sb.AppendLine($"Konu: {topic.Title}");

            var wiki = await _wikiService.GetWikiFullContentAsync(topicId.Value, userId);
            if (!string.IsNullOrWhiteSpace(wiki))
            {
                sb.AppendLine(Trim(wiki, 5000));
                hasMeaningfulContext = true;
            }

            var sourceBits = await _db.SourceChunks
                .AsNoTracking()
                .Include(c => c.LearningSource)
                .Where(c => c.LearningSource.UserId == userId && c.LearningSource.TopicId == topicId)
                .OrderBy(c => c.ChunkIndex)
                .Take(5)
                .Select(c => $"[doc:{c.LearningSourceId}:p{c.PageNumber}] {c.Text}")
                .ToListAsync(ct);
            if (sourceBits.Count > 0)
            {
                sb.AppendLine("\nKaynak notlari:\n" + string.Join("\n\n", sourceBits));
                hasMeaningfulContext = true;
            }
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
            {
                sb.AppendLine("\nSohbet ozeti:\n" + string.Join("\n", messages));
                hasMeaningfulContext = true;
            }
        }

        var text = sb.ToString();
        return string.IsNullOrWhiteSpace(text) || !hasMeaningfulContext ? "Henuz yeterli ders icerigi yok." : Trim(text, 8000);
    }

    private static bool IsInsufficientOverviewContext(string context) =>
        string.IsNullOrWhiteSpace(context) ||
        context.Trim().Equals("Henuz yeterli ders icerigi yok.", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ParseStrings(string? json) => Parse(json, Array.Empty<string>());
    private static IReadOnlyList<Guid> ParseGuids(string? json) => Parse(json, Array.Empty<Guid>());

    private static T Parse<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try { return JsonSerializer.Deserialize<T>(json) ?? fallback; }
        catch { return fallback; }
    }

    private static string JoinOrFallback(IReadOnlyList<string> values, string fallback) =>
        values.Count == 0 ? fallback : string.Join(", ", values.Take(8));

    private static PackWikiPageMetadata ParseWikiPageMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return new PackWikiPageMetadata(null);
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            var root = document.RootElement;
            var title = root.TryGetProperty("wikiPageTitle", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString()
                : null;
            return new PackWikiPageMetadata(title);
        }
        catch
        {
            return new PackWikiPageMetadata(null);
        }
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

    private sealed record NotebookPackAudioContext(
        Guid PackId,
        Guid TopicId,
        Guid? SessionId,
        string Title,
        string Summary,
        string PackType,
        string PackStatus,
        string EvidenceStatus,
        string SourceReadiness,
        string? WikiPageTitle,
        IReadOnlyList<string> CompletedConceptKeys,
        IReadOnlyList<string> WeakConceptKeys,
        IReadOnlyList<string> MisconceptionKeys,
        IReadOnlyList<NotebookPackAudioArtifact> Artifacts);

    private sealed record NotebookPackAudioArtifact(
        string ArtifactType,
        string Title,
        string SourceBasis,
        string SafeContent);

    private sealed record PackWikiPageMetadata(string? WikiPageTitle);
}

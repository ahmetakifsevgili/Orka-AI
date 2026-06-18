using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundTaskQueue _backgroundQueue;

    public AudioOverviewService(
        OrkaDbContext db,
        IAIAgentFactory factory,
        IWikiService wikiService,
        ISourceEvidenceLifecycleService sourceLifecycle,
        IAgenticTrustPolicyService trust,
        IEdgeTtsService ttsService,
        ILogger<AudioOverviewService> logger,
        IServiceScopeFactory scopeFactory,
        IBackgroundTaskQueue backgroundQueue)
    {
        _db = db;
        _factory = factory;
        _wikiService = wikiService;
        _sourceLifecycle = sourceLifecycle;
        _trust = trust;
        _ttsService = ttsService;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _backgroundQueue = backgroundQueue;
    }

    public async Task<AudioOverviewJobDto> CreateOverviewAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string? surface = null,
        Guid? wikiPageId = null,
        Guid? sourceId = null,
        string? audioMode = null,
        string? ttsQuality = null,
        CancellationToken ct = default)
    {
        var normalizedSurface = NormalizeSurface(surface, sourceId);
        if (wikiPageId.HasValue)
        {
            if (normalizedSurface == "orkalm")
                throw new ArgumentException("OrkaLM audio wikiPageId ile baslatilamaz.");

            var page = await _db.WikiPages.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == wikiPageId.Value && p.UserId == userId && !p.IsDeleted, ct);
            if (page == null)
                throw new NotFoundException("Audio overview wiki sayfasi bulunamadi.");

            EnsureTopicMatches(topicId, page.TopicId, "Audio overview topic wiki sayfasi ile eslesmiyor.");
            EnsureSessionMatches(sessionId, page.SessionId, "Audio overview session wiki sayfasi ile eslesmiyor.");
            topicId ??= page.TopicId;
            sessionId ??= page.SessionId;
            normalizedSurface = "wiki";
        }

        if (sourceId.HasValue)
        {
            if (normalizedSurface == "wiki")
                throw new ArgumentException("Wiki audio sourceId ile baslatilamaz.");

            var source = await _db.LearningSources.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sourceId.Value && s.UserId == userId && !s.IsDeleted, ct);
            if (source == null)
                throw new NotFoundException("Audio overview kaynagi bulunamadi.");

            EnsureTopicMatches(topicId, source.TopicId, "Audio overview topic kaynak ile eslesmiyor.");
            EnsureSessionMatches(sessionId, source.SessionId, "Audio overview session kaynak ile eslesmiyor.");
            topicId ??= source.TopicId;
            sessionId ??= source.SessionId;
            normalizedSurface = "orkalm";
        }

        if (!topicId.HasValue && !sessionId.HasValue && !wikiPageId.HasValue && !sourceId.HasValue)
            throw new ArgumentException("Audio Overview icin topicId, sessionId, wikiPageId veya sourceId zorunlu.");

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

        if (sessionId.HasValue)
        {
            var sessionTopicId = await _db.Sessions
                .AsNoTracking()
                .Where(s => s.Id == sessionId.Value && s.UserId == userId)
                .Select(s => s.TopicId)
                .FirstOrDefaultAsync(ct);
            EnsureTopicMatches(topicId, sessionTopicId, "Audio overview topic session ile eslesmiyor.");
            topicId ??= sessionTopicId;
        }

        var metadata = new AudioOverviewJobMetadata
        {
            Surface = normalizedSurface,
            ContextType = normalizedSurface == "orkalm" ? "source_notebook" : "wiki_page",
            WikiPageId = normalizedSurface == "wiki" ? wikiPageId : null,
            SourceId = normalizedSurface == "orkalm" ? sourceId : null,
            AudioMode = NormalizeAudioMode(audioMode),
            TtsQuality = NormalizeTtsQuality(ttsQuality),
            CrossSurfaceSync = false,
            Speakers = []
        };

        var job = new AudioOverviewJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            Status = "generating",
            SpeakersJson = SerializeAudioMetadata(metadata),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.AudioOverviewJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        try
        {
            await _backgroundQueue.QueueAsync(new BackgroundTaskItem(
                "audio-overview-generation",
                userId,
                job.Id.ToString(),
                async backgroundCt =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scopedService = scope.ServiceProvider.GetRequiredService<IAudioOverviewService>();
                    await scopedService.ProcessOverviewJobAsync(job.Id, backgroundCt);
                }
            ), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.Status = "failed";
            job.ErrorMessage = "Ses özeti kuyruğa alınamadı.";
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("[AudioOverview] Queue failed. JobRef={JobRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(job.Id, "audio_job"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }

        return ToDto(job);
    }

    public async Task ProcessOverviewJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.AudioOverviewJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null) return;

        try
        {
            var metadata = ReadAudioMetadata(job);
            var context = await BuildOverviewContextAsync(job.UserId, job.TopicId, job.SessionId, metadata, ct);
            if (IsInsufficientOverviewContext(context))
            {
                job.Status = "script-only";
                job.ErrorMessage = metadata.Surface == "orkalm"
                    ? "OrkaLM audio icin henuz secili kaynak/citation baglami yok."
                    : "Wiki audio icin henuz wiki, ders akisi veya tutor baglami yok.";
                job.Script = BuildFallbackScript(metadata, context, insufficient: true);
                metadata.Speakers = AudioDialogueFormatter.ParseSpeakers(job.Script);
                job.SpeakersJson = SerializeAudioMetadata(metadata);
                job.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return;
            }

            string script;
            try
            {
                using var scriptTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                scriptTimeout.CancelAfter(ScriptGenerationTimeout);
                script = await _factory.CompleteChatAsync(
                    AgentRole.Summarizer,
                    BuildAudioSystemPrompt(metadata),
                    $"Guvenli context:\n\n{context}",
                    scriptTimeout.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("[AudioOverview] Script provider fallback. JobRef={JobRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeId(job.Id, "job"),
                    LogPrivacyGuard.SafeExceptionType(ex));
                script = BuildFallbackScript(metadata, context, insufficient: false);
            }

            script = EnsureContextAnchor(AudioDialogueFormatter.NormalizeScript(script), metadata, context);
            var speakers = AudioDialogueFormatter.ParseSpeakers(script);

            job.Script = script;
            metadata.Speakers = speakers;
            job.SpeakersJson = SerializeAudioMetadata(metadata);

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
            var metadata = ReadAudioMetadata(job);
            metadata.Speakers = AudioDialogueFormatter.ParseSpeakers(job.Script);
            job.SpeakersJson = SerializeAudioMetadata(metadata);
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static void EnsureTopicMatches(Guid? requestedTopicId, Guid? contextTopicId, string message)
    {
        if (requestedTopicId.HasValue &&
            contextTopicId.HasValue &&
            requestedTopicId.Value != contextTopicId.Value)
        {
            throw new ArgumentException(message);
        }
    }

    private static void EnsureSessionMatches(Guid? requestedSessionId, Guid? contextSessionId, string message)
    {
        if (requestedSessionId.HasValue &&
            contextSessionId.HasValue &&
            requestedSessionId.Value != contextSessionId.Value)
        {
            throw new ArgumentException(message);
        }
    }

    private async Task TryGenerateTtsAsync(AudioOverviewJob job, CancellationToken ct)
    {
        try
        {
            using var ttsTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ttsTimeout.CancelAfter(TtsGenerationTimeout);
            var metadata = ReadAudioMetadata(job);
            var audioBytes = await _ttsService.SynthesizeDialogueAsync(job.Script, metadata.TtsQuality, ttsTimeout.Token);
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
        AudioOverviewJobMetadata metadata,
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
        query = metadata.Surface == "orkalm"
            ? query.Where(p => p.WikiPageId == null)
            : metadata.WikiPageId.HasValue
                ? query.Where(p => p.WikiPageId == metadata.WikiPageId.Value)
                : query.Where(p => p.WikiPageId != null);
        if (metadata.Surface == "orkalm" && metadata.SourceId.HasValue)
        {
            var sourceIdText = metadata.SourceId.Value.ToString();
            query = query.Where(p => p.SafeMetadataJson.Contains(sourceIdText));
        }

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
        var packMetadata = ParseWikiPageMetadata(pack.SafeMetadataJson);
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
            metadata.Surface,
            pack.WikiPageTitle ?? packMetadata.WikiPageTitle,
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
        var pageLine = context.Surface == "orkalm"
            ? "Bu anlatim OrkaLM source notebook pack uzerinden hazirlandi."
            : string.IsNullOrWhiteSpace(context.WikiPageTitle)
                ? "Bu anlatim Wiki ders pack uzerinden hazirlandi."
                : $"Bu anlatim `{context.WikiPageTitle}` Wiki ders pack uzerinden hazirlandi.";

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

    private async Task<string> BuildOverviewContextAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        AudioOverviewJobMetadata metadata,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var hasMeaningfulContext = false;
        sb.AppendLine($"[AUDIO_CONTEXT] surface={metadata.Surface}; contextType={metadata.ContextType}; mode={metadata.AudioMode}; crossSurfaceSync=false");

        var packContext = await BuildNotebookPackContextAsync(userId, topicId, sessionId, metadata, ct);
        if (packContext != null)
        {
            sb.AppendLine("\n[NOTEBOOK_PACK_CONTEXT]\n" + BuildNotebookPackScript(packContext));
            hasMeaningfulContext = true;
            if (IsEvidenceLimited(packContext.SourceReadiness) || IsEvidenceLimited(packContext.EvidenceStatus))
            {
                metadata.ContextWarnings = AddWarning(
                    metadata.ContextWarnings,
                    "source_evidence_limited_audio_uses_conservative_language");
            }
        }

        if (metadata.Surface == "orkalm")
        {
            if (metadata.SourceId.HasValue)
            {
                var source = await _db.LearningSources.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == metadata.SourceId.Value && s.UserId == userId && !s.IsDeleted, ct);
                if (source != null)
                {
                    sb.AppendLine($"[SOURCE_NOTEBOOK]\nTitle: {source.Title}\nStatus: {source.Status}\nChunks: {source.ChunkCount}");
                    hasMeaningfulContext = true;
                }

                var chunks = await _db.SourceChunks
                    .AsNoTracking()
                    .Where(c => c.LearningSourceId == metadata.SourceId.Value && c.LearningSource.UserId == userId && !c.LearningSource.IsDeleted)
                    .OrderBy(c => c.PageNumber)
                    .ThenBy(c => c.ChunkIndex)
                    .Take(6)
                    .Select(c => $"[citation:{c.LearningSourceId}:p{c.PageNumber}:c{c.ChunkIndex}] {c.HighlightHint ?? Trim(c.Text, 520)}")
                    .ToListAsync(ct);
                if (chunks.Count > 0)
                {
                    sb.AppendLine("\n[CITATIONS]\n" + string.Join("\n\n", chunks));
                    hasMeaningfulContext = true;
                }
            }
            else if (topicId.HasValue)
            {
                var sources = await _db.LearningSources.AsNoTracking()
                    .Where(s => s.UserId == userId && s.TopicId == topicId.Value && !s.IsDeleted)
                    .OrderByDescending(s => s.UpdatedAt)
                    .Take(4)
                    .Select(s => $"{s.Title} | status={s.Status} | chunks={s.ChunkCount}")
                    .ToListAsync(ct);
                if (sources.Count > 0)
                {
                    sb.AppendLine("\n[SOURCE_NOTEBOOK_COLLECTION]\n" + string.Join("\n", sources));
                    hasMeaningfulContext = true;
                }
            }

            var sourceQuestionThreads = await _db.LearningArtifacts
                .AsNoTracking()
                .Where(a => a.UserId == userId && !a.IsDeleted && a.ArtifactType == "source_question_thread")
                .Where(a => !metadata.SourceId.HasValue || (a.ContentJson ?? string.Empty).Contains(metadata.SourceId.Value.ToString()))
                .Where(a => !topicId.HasValue || a.TopicId == topicId.Value)
                .OrderByDescending(a => a.UpdatedAt)
                .Take(3)
                .Select(a => $"{a.Title}: {Trim(a.SafeContent, 520)}")
                .ToListAsync(ct);
            if (sourceQuestionThreads.Count > 0)
            {
                sb.AppendLine("\n[SOURCE_QA]\n" + string.Join("\n\n", sourceQuestionThreads));
                hasMeaningfulContext = true;
            }

            sb.AppendLine("\n[SCOPE_GUARD]\nWiki page blocks and normal Wiki graph are not read by OrkaLM audio in this phase.");
        }
        else
        {
            if (metadata.WikiPageId.HasValue)
            {
                var page = await _db.WikiPages.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == metadata.WikiPageId.Value && p.UserId == userId && !p.IsDeleted, ct);
                if (page != null)
                {
                    sb.AppendLine($"[WIKI_PAGE]\nTitle: {page.Title}\nConcept: {page.ConceptKey ?? "none"}\nSummary: {Trim(page.SafeSummary ?? string.Empty, 900)}");
                    hasMeaningfulContext = true;
                }

                var blocks = await _db.WikiBlocks.AsNoTracking()
                    .Where(b => b.WikiPageId == metadata.WikiPageId.Value && !b.IsDeleted)
                    .OrderBy(b => b.OrderIndex)
                    .Take(8)
                    .Select(b => $"{b.BlockType}: {b.Title} {b.Content} concept={b.ConceptKey ?? "none"} misconception={b.MisconceptionKey ?? "none"}")
                    .ToListAsync(ct);
                if (blocks.Count > 0)
                {
                    sb.AppendLine("\n[WIKI_BLOCKS]\n" + Trim(string.Join("\n\n", blocks), 4200));
                    hasMeaningfulContext = true;
                }
            }
            else if (topicId.HasValue)
            {
                var topic = await _db.Topics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId, ct);
                if (topic != null) sb.AppendLine($"Konu: {topic.Title}");

                var wiki = await _wikiService.GetWikiFullContentAsync(topicId.Value, userId);
                if (!string.IsNullOrWhiteSpace(wiki))
                {
                    sb.AppendLine(Trim(wiki, 5000));
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
                    sb.AppendLine("\n[TUTOR_SESSION]\n" + string.Join("\n", messages));
                    hasMeaningfulContext = true;
                }
            }

            sb.AppendLine("\n[SCOPE_GUARD]\nSource chunks/citations are not read by Wiki audio in this phase.");
        }

        var text = sb.ToString();
        return string.IsNullOrWhiteSpace(text) || !hasMeaningfulContext ? "Henuz yeterli ders icerigi yok." : Trim(text, 8000);
    }

    private static bool IsInsufficientOverviewContext(string context) =>
        string.IsNullOrWhiteSpace(context) ||
        context.Trim().Equals("Henuz yeterli ders icerigi yok.", StringComparison.OrdinalIgnoreCase);

    private static string EnsureContextAnchor(string script, AudioOverviewJobMetadata metadata, string context)
    {
        var anchor = ExtractContextAnchor(context);
        var highlights = ExtractContextHighlights(context);
        var needsAnchor = !string.IsNullOrWhiteSpace(anchor) &&
                          !script.Contains(anchor, StringComparison.OrdinalIgnoreCase);
        var missingHighlights = highlights
            .Where(highlight => !script.Contains(highlight, StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToArray();
        if (!needsAnchor && missingHighlights.Length == 0) return script;

        var surfaceLabel = metadata.Surface == "orkalm" ? "OrkaLM kaynak defteri" : "Wiki ders akisi";
        var anchorText = string.IsNullOrWhiteSpace(anchor) ? surfaceLabel : anchor;
        var highlightText = missingHighlights.Length == 0 ? string.Empty : $" Odak sinyalleri: {string.Join(", ", missingHighlights)}.";
        return AudioDialogueFormatter.NormalizeScript(
            $"[HOCA]: {anchorText} icin {metadata.AudioMode} sesli ders baglamini aciyorum. Surface {metadata.Surface}; cross-surface sync kapali.{highlightText}\n{script}");
    }

    private static string ExtractContextAnchor(string context)
    {
        var lines = context.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            if ((lines[i].Equals("[WIKI_PAGE]", StringComparison.OrdinalIgnoreCase) ||
                 lines[i].Equals("[SOURCE_NOTEBOOK]", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < lines.Length)
            {
                var value = lines[i + 1].StartsWith("Title:", StringComparison.OrdinalIgnoreCase)
                    ? lines[i + 1]["Title:".Length..].Trim()
                    : lines[i + 1].Trim();
                if (!string.IsNullOrWhiteSpace(value)) return Trim(value, 140);
            }

            if (lines[i].StartsWith("Konu:", StringComparison.OrdinalIgnoreCase))
                return Trim(lines[i]["Konu:".Length..].Trim(), 140);
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> ExtractContextHighlights(string context)
    {
        var candidates = new[] { "growth-rate-confusion", "big-o", "binary-search", "source_grounded", "source_notebook" };
        return candidates
            .Where(candidate => context.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildAudioSystemPrompt(AudioOverviewJobMetadata metadata)
    {
        var modeInstruction = metadata.AudioMode switch
        {
            "deep_dive" => "Derin inceleme yap: once ana fikri kur, sonra iki katmanli neden-sonuc, en sonda aktif hatirlama sorusu ver.",
            "critique" => "Elestirel ozet yap: guclu iddialari, zayif kanit alanlarini ve yanlis anlama risklerini ayir.",
            "debate" => "Tartisma formati kur: HOCA ana cerceveyi, ASISTAN itirazi, KONUK alternatif bakisi temsil etsin.",
            _ => "Kisa briefing yap: yogun ama net bir 90-120 saniyelik sesli tekrar metni uret."
        };
        var surfaceInstruction = metadata.Surface == "orkalm"
            ? "Yalniz OrkaLM source notebook/citation context'ini kullan. Wiki ders akisi veya Wiki graph baglami ekleme."
            : "Yalniz Wiki ders/kavram/tutor context'ini kullan. OrkaLM source chunk/citation baglami ekleme.";

        return $$"""
            Sen Orka'nin profesyonel Sesli Ders yapimcisisin.
            {{surfaceInstruction}}
            {{modeInstruction}}
            Format kesinlikle satir satir su speaker etiketleriyle olsun:
            [HOCA]: ...
            [ASISTAN]: ...
            [KONUK]: ...
            KONUK opsiyoneldir; debate modunda kullan.
            Ham kaynak, prompt, provider payload, debug trace veya gizli veri yazma.
            Cross-surface sync kapali; iki sistemi birbirine baglama.
            Turkce, sicak, ogretici, sinifta dinlenebilir bir akista yaz.
            """;
    }

    private static string BuildFallbackScript(AudioOverviewJobMetadata metadata, string context, bool insufficient)
    {
        var focus = metadata.Surface == "orkalm" ? "OrkaLM kaynak defteri" : "Wiki ders akisi";
        if (insufficient)
        {
            return AudioDialogueFormatter.NormalizeScript($"""
                [HOCA]: {focus} icin sesli ders baslatmak istedik ama bu yuzeyde yeterli baglam yok.
                [ASISTAN]: OrkaLM ve Wiki su an birbirini beslemiyor; bu yuzey icin once kendi notunu, kaynagini veya ders izini guclendirmek gerekiyor.
                [HOCA]: Hazir oldugunda brief, deep dive, critique veya debate modunda sesli ozet ve sesli calisma odasi akisini baslatabiliriz.
                """);
        }

        var clipped = Trim(context.ReplaceLineEndings(" "), 900);
        return metadata.AudioMode switch
        {
            "debate" => AudioDialogueFormatter.NormalizeScript($"""
                [HOCA]: {focus} icin tartisma modunu aciyorum. Ana baglam: {clipped}
                [ASISTAN]: Ben ogrencinin itirazini temsil ediyorum: burada hangi kanit guclu, hangi kisim sadece calisma notu?
                [KONUK]: Alternatif bakis su: once iddiayi kucult, sonra tek ornekle test et.
                [HOCA]: Kapanis sorusu: bu anlatimdaki en kritik kavrami kaynak veya Wiki baglamina sadik kalarak nasil aciklarsin?
                """),
            "critique" => AudioDialogueFormatter.NormalizeScript($"""
                [HOCA]: {focus} icin elestirel ozet basliyor. Ana baglam: {clipped}
                [ASISTAN]: Guclu taraf, baglamin acik verdigi kavramlari takip edebilmemiz. Zayif taraf, kanit sinirliyse kesin iddia kurmamamiz.
                [HOCA]: Yanlis anlama riski gordugun yerde dur, kavrami kendi cumlenle yeniden kur ve mini kontrol sorusu sor.
                """),
            "deep_dive" => AudioDialogueFormatter.NormalizeScript($"""
                [HOCA]: {focus} icin deep dive basliyor. Ana baglam: {clipped}
                [ASISTAN]: Once ana fikri tut, sonra neden-sonuc zincirini iki adimda ac.
                [HOCA]: Son adimda aktif hatirlama yap: bu konuyu bir ornekle anlat ve nerede takildigini isaretle.
                """),
            _ => AudioDialogueFormatter.NormalizeScript($"""
                [HOCA]: {focus} icin kisa sesli briefing hazir. Ana baglam: {clipped}
                [ASISTAN]: Simdi bunu pasif dinleme degil aktif tekrar gibi kullan: once ana fikri soyle, sonra tek kontrol sorusu cevapla.
                [HOCA]: Takildigin yerde sesli calisma odasini acip "burayi anlamadim" diyebilirsin.
                """)
        };
    }

    private static bool IsEvidenceLimited(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("insufficient", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("limited", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("degraded", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("no_sources", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> AddWarning(IReadOnlyList<string> warnings, string warning) =>
        warnings.Contains(warning, StringComparer.OrdinalIgnoreCase)
            ? warnings
            : warnings.Concat([warning]).ToArray();

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
        var metadata = ReadAudioMetadata(job);
        var speakers = metadata.Speakers.Count > 0
            ? metadata.Speakers
            : AudioDialogueFormatter.ParseSpeakers(job.Script);

        var normalizedStatus = NormalizeStatus(job.Status);
        var contentType = job.AudioBytes is { Length: > 0 } ? NormalizeContentType(job.ContentType) : null;
        var captions = BuildCaptionCues(job.Script);
        var captionTrack = BuildCaptionTrack(captions);
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
            job.UpdatedAt,
            metadata.Surface,
            metadata.ContextType,
            metadata.WikiPageId,
            metadata.SourceId,
            metadata.AudioMode,
            "hoca_asistan_konuk",
            metadata.TtsQuality,
            job.Script,
            captionTrack,
            captions,
            true,
            false,
            job.AudioExpiresAt,
            job.AudioPurgedAt,
            job.AudioByteLength,
            new[]
            {
                "audio_bytes_retention_days_7",
                "script_transcript_retained_with_learning_record",
                "purge_removes_binary_audio_only"
            }.Concat(metadata.ContextWarnings).ToArray());
    }

    private static AudioOverviewJobMetadata ReadAudioMetadata(AudioOverviewJob job)
    {
        var fallback = new AudioOverviewJobMetadata
        {
            Surface = "wiki",
            ContextType = "wiki_page",
            AudioMode = "brief",
            TtsQuality = "standard",
            CrossSurfaceSync = false,
            Speakers = AudioDialogueFormatter.ParseSpeakers(job.Script)
        };

        if (string.IsNullOrWhiteSpace(job.SpeakersJson)) return fallback;
        try
        {
            using var document = JsonDocument.Parse(job.SpeakersJson);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                fallback.Speakers = JsonSerializer.Deserialize<List<string>>(job.SpeakersJson) ?? [];
                return fallback;
            }

            var parsed = JsonSerializer.Deserialize<AudioOverviewJobMetadata>(job.SpeakersJson) ?? fallback;
            parsed.Surface = NormalizeSurface(parsed.Surface, parsed.SourceId);
            parsed.ContextType = parsed.Surface == "orkalm" ? "source_notebook" : "wiki_page";
            parsed.AudioMode = NormalizeAudioMode(parsed.AudioMode);
            parsed.TtsQuality = NormalizeTtsQuality(parsed.TtsQuality);
            parsed.CrossSurfaceSync = false;
            if (parsed.Speakers.Count == 0)
                parsed.Speakers = AudioDialogueFormatter.ParseSpeakers(job.Script);
            return parsed;
        }
        catch
        {
            return fallback;
        }
    }

    private static string SerializeAudioMetadata(AudioOverviewJobMetadata metadata)
    {
        metadata.Surface = NormalizeSurface(metadata.Surface, metadata.SourceId);
        metadata.ContextType = metadata.Surface == "orkalm" ? "source_notebook" : "wiki_page";
        metadata.AudioMode = NormalizeAudioMode(metadata.AudioMode);
        metadata.TtsQuality = NormalizeTtsQuality(metadata.TtsQuality);
        metadata.CrossSurfaceSync = false;
        return JsonSerializer.Serialize(metadata);
    }

    private static IReadOnlyList<AudioCaptionCueDto> BuildCaptionCues(string script)
    {
        var segments = AudioDialogueFormatter.ParseSegments(script).Take(24).ToArray();
        return segments.Select((segment, index) =>
        {
            var start = TimeSpan.FromSeconds(index * 12);
            var end = TimeSpan.FromSeconds((index + 1) * 12);
            return new AudioCaptionCueDto(
                index + 1,
                segment.Speaker,
                Trim(segment.Text, 240),
                FormatTimestamp(start),
                FormatTimestamp(end));
        }).ToArray();
    }

    private static string BuildCaptionTrack(IReadOnlyList<AudioCaptionCueDto> captions)
    {
        if (captions.Count == 0) return "WEBVTT\n";
        var builder = new StringBuilder();
        builder.AppendLine("WEBVTT");
        builder.AppendLine();
        foreach (var cue in captions)
        {
            builder.AppendLine(cue.CueId.ToString());
            builder.AppendLine($"{cue.Start} --> {cue.End}");
            builder.AppendLine($"{cue.Speaker}: {cue.Text}");
            builder.AppendLine();
        }
        return builder.ToString().TrimEnd();
    }

    private static string FormatTimestamp(TimeSpan value) =>
        $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.000";

    private static string NormalizeSurface(string? value, Guid? sourceId = null)
    {
        var key = string.IsNullOrWhiteSpace(value) ? (sourceId.HasValue ? "orkalm" : "wiki") : value.Trim().ToLowerInvariant();
        return key is "orkalm" or "source" or "source_notebook" ? "orkalm" : "wiki";
    }

    private static string NormalizeAudioMode(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "brief" : value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return key is "deep_dive" or "critique" or "debate" ? key : "brief";
    }

    private static string NormalizeTtsQuality(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "standard" : value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return key is "draft" or "standard" or "studio" ? key : "standard";
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
        string Surface,
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

    private sealed class AudioOverviewJobMetadata
    {
        public IReadOnlyList<string> Speakers { get; set; } = Array.Empty<string>();
        public string Surface { get; set; } = "wiki";
        public string ContextType { get; set; } = "wiki_page";
        public Guid? WikiPageId { get; set; }
        public Guid? SourceId { get; set; }
        public string AudioMode { get; set; } = "brief";
        public string TtsQuality { get; set; } = "standard";
        public bool CrossSurfaceSync { get; set; }
        public IReadOnlyList<string> ContextWarnings { get; set; } = Array.Empty<string>();
    }

    private sealed record PackWikiPageMetadata(string? WikiPageTitle);
}

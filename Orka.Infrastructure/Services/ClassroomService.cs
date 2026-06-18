using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public class ClassroomService : IClassroomService
{
    private static readonly TimeSpan AiAnswerTimeout = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan TtsTimeout = TimeSpan.FromSeconds(8);

    private readonly OrkaDbContext _db;
    private readonly IAIAgentFactory _factory;
    private readonly ILearningSignalService _signals;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly IWikiService _wikiService;
    private readonly ILogger<ClassroomService> _logger;

    public ClassroomService(
        OrkaDbContext db,
        IAIAgentFactory factory,
        ILearningSignalService signals,
        IServiceScopeFactory scopeFactory,
        IBackgroundTaskQueue backgroundQueue,
        IWikiService wikiService,
        ILogger<ClassroomService> logger)
    {
        _db = db;
        _factory = factory;
        _signals = signals;
        _scopeFactory = scopeFactory;
        _backgroundQueue = backgroundQueue;
        _wikiService = wikiService;
        _logger = logger;
    }

    public async Task<ClassroomSessionDto> StartSessionAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        Guid? audioOverviewJobId,
        string transcript,
        string? surface = null,
        Guid? wikiPageId = null,
        Guid? sourceId = null,
        string? audioMode = null,
        CancellationToken ct = default)
    {
        var context = NormalizeClassroomContext(surface, wikiPageId, sourceId, audioMode);
        if (!topicId.HasValue && !sessionId.HasValue && !audioOverviewJobId.HasValue && !context.WikiPageId.HasValue && !context.SourceId.HasValue && string.IsNullOrWhiteSpace(transcript))
            throw new ArgumentException("Classroom baslatmak icin topicId, sessionId, audioOverviewJobId, wikiPageId, sourceId veya transcript gerekli.");

        AudioOverviewJob? audioOverviewJob = null;
        if (audioOverviewJobId.HasValue)
        {
            audioOverviewJob = await _db.AudioOverviewJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == audioOverviewJobId.Value && j.UserId == userId, ct);
            if (audioOverviewJob == null)
                throw new NotFoundException("Classroom audio overview job bulunamadi.");

            var jobContext = TryParseAudioOverviewContext(audioOverviewJob);
            if (jobContext != null)
            {
                var canInferFromJob = string.IsNullOrWhiteSpace(surface) && !wikiPageId.HasValue && !sourceId.HasValue;
                if (!canInferFromJob && !IsCompatibleClassroomContext(context, jobContext))
                    throw new ArgumentException("Classroom surface/context audio overview job ile eslesmiyor.");

                context = MergeClassroomContext(context, jobContext);
            }

            EnsureTopicMatches(topicId, audioOverviewJob.TopicId, "Classroom topic audio overview job ile eslesmiyor.");
            EnsureSessionMatches(sessionId, audioOverviewJob.SessionId, "Classroom session audio overview job ile eslesmiyor.");
            topicId ??= audioOverviewJob.TopicId;
            sessionId ??= audioOverviewJob.SessionId;
        }

        if (context.WikiPageId.HasValue)
        {
            var page = await _db.WikiPages.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == context.WikiPageId.Value && p.UserId == userId && !p.IsDeleted, ct);
            if (page == null)
                throw new NotFoundException("Classroom wiki sayfasi bulunamadi.");
            EnsureTopicMatches(topicId, page.TopicId, "Classroom topic wiki sayfasi ile eslesmiyor.");
            EnsureSessionMatches(sessionId, page.SessionId, "Classroom session wiki sayfasi ile eslesmiyor.");
            topicId ??= page.TopicId;
            sessionId ??= page.SessionId;
        }

        if (context.SourceId.HasValue)
        {
            var source = await _db.LearningSources.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == context.SourceId.Value && s.UserId == userId && !s.IsDeleted, ct);
            if (source == null)
                throw new NotFoundException("Classroom kaynagi bulunamadi.");
            EnsureTopicMatches(topicId, source.TopicId, "Classroom topic kaynak ile eslesmiyor.");
            EnsureSessionMatches(sessionId, source.SessionId, "Classroom session kaynak ile eslesmiyor.");
            topicId ??= source.TopicId;
            sessionId ??= source.SessionId;
        }

        if (topicId.HasValue)
        {
            var topicExists = await _db.Topics
                .AsNoTracking()
                .AnyAsync(t => t.Id == topicId.Value && t.UserId == userId, ct);
            if (!topicExists)
                throw new NotFoundException("Classroom topic bulunamadı.");
        }

        if (sessionId.HasValue)
        {
            var sessionExists = await _db.Sessions
                .AsNoTracking()
                .AnyAsync(s => s.Id == sessionId.Value && s.UserId == userId, ct);
            if (!sessionExists)
                throw new NotFoundException("Classroom session bulunamadı.");
        }

        if (audioOverviewJobId.HasValue && audioOverviewJob == null)
        {
            var jobExists = await _db.AudioOverviewJobs
                .AsNoTracking()
                .AnyAsync(j => j.Id == audioOverviewJobId.Value && j.UserId == userId, ct);
            if (!jobExists)
                throw new NotFoundException("Classroom audio overview job bulunamadı.");
        }

        if (sessionId.HasValue)
        {
            var sessionTopicId = await _db.Sessions
                .AsNoTracking()
                .Where(s => s.Id == sessionId.Value && s.UserId == userId)
                .Select(s => s.TopicId)
                .FirstOrDefaultAsync(ct);
            EnsureTopicMatches(topicId, sessionTopicId, "Classroom topic session ile eslesmiyor.");
            topicId ??= sessionTopicId;
        }

        var preparedTranscript = await BuildClassroomContextAsync(
            userId,
            topicId,
            sessionId,
            audioOverviewJob,
            transcript,
            context,
            ct);

        var classroom = new ClassroomSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            AudioOverviewJobId = audioOverviewJobId,
            Transcript = preparedTranscript,
            LastSegment = ExtractLastSegment(preparedTranscript),
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ClassroomSessions.Add(classroom);
        await _db.SaveChangesAsync(ct);

        await _signals.RecordSignalAsync(
            userId,
            topicId,
            sessionId,
            LearningSignalTypes.ClassroomStarted,
            payloadJson: JsonSerializer.Serialize(new { audioOverviewJobId, context.Surface, context.ContextType, context.WikiPageId, context.SourceId, context.AudioMode, crossSurfaceSync = false }),
            ct: ct);

        return ToDto(classroom);
    }

    public async Task<ClassroomAskResultDto> AskAsync(
        Guid userId,
        Guid classroomSessionId,
        string question,
        string? activeSegment,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Classroom sorusu bos olamaz.");

        var classroom = await _db.ClassroomSessions
            .FirstOrDefaultAsync(c => c.Id == classroomSessionId && c.UserId == userId, ct);
        if (classroom == null) throw new NotFoundException("Classroom session bulunamadı.");

        var context = ParseClassroomContext(classroom.Transcript);
        var segment = string.IsNullOrWhiteSpace(activeSegment) ? classroom.LastSegment : activeSegment.Trim();
        var systemPrompt = """
            Sen Orka Sesli Sinif ajanisin. Ogrenci ders dinlerken soru sordu.
            Cevabi ayni podcast/sinif formatinda ver:
            [HOCA]: ana aciklama, sakin ve net.
            [ASISTAN]: ogrencinin kafasindaki itirazi/soruyu temsil eder.
            [KONUK]: sadece faydaliysa uzman/ogrenci sesi ekler.
            Ogrencinin anlamadigi bolumu tekrar anlat; tum dersi bastan anlatma.
            Gerekirse mini diagrami sozle tarif et veya Mermaid kodunu kisa ver.
            """;

        var userPrompt = $$"""
            [AKTIF TRANSCRIPT]
            {{Trim(classroom.Transcript, 5000)}}

            [OGRencinin takildigi aktif bolum]
            {{segment}}

            [SORU]
            {{question}}
            """;

        var answer = await GenerateClassroomAnswerOrFallbackAsync(systemPrompt, userPrompt, question, segment);
        var interaction = new ClassroomInteraction
        {
            Id = Guid.NewGuid(),
            ClassroomSessionId = classroom.Id,
            Question = question,
            AnswerScript = answer,
            CreatedAt = DateTime.UtcNow
        };

        _db.ClassroomInteractions.Add(interaction);

        classroom.LastSegment = segment;
        classroom.Transcript = $"{classroom.Transcript.Trim()}\n\n[STUDENT]: {question}\n{answer}".Trim();
        classroom.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        QueueAudioGeneration(interaction.Id, answer);

        await _signals.RecordSignalAsync(
            userId,
            classroom.TopicId,
            classroom.SessionId,
            LearningSignalTypes.ClassroomQuestionAsked,
            score: null,
            isPositive: false,
            payloadJson: JsonSerializer.Serialize(new { question, segment, context.Surface, context.ContextType, context.WikiPageId, context.SourceId, context.AudioMode, crossSurfaceSync = false }),
            ct: ct);

        _logger.LogInformation("[Classroom] Student question answered. ClassroomRef={ClassroomRef}",
            LogPrivacyGuard.SafeId(classroom.Id, "classroom"));
        return new ClassroomAskResultDto(
            classroom.Id,
            interaction.Id,
            answer,
            ParseSpeakers(answer),
            context.Surface,
            context.ContextType,
            context.WikiPageId,
            context.SourceId,
            context.AudioMode,
            AudioQueued: true,
            BrowserTtsFallback: true);
    }

    private async Task<string> GenerateClassroomAnswerOrFallbackAsync(
        string systemPrompt,
        string userPrompt,
        string question,
        string? segment)
    {
        try
        {
            using var timeout = new CancellationTokenSource(AiAnswerTimeout);
            var answer = await _factory.CompleteChatAsync(AgentRole.Classroom, systemPrompt, userPrompt, timeout.Token);
            return NormalizeDialogue(answer);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[Classroom] AI answer unavailable within {TimeoutSeconds}s. Returning structured fallback. ErrorType={ErrorType}",
                AiAnswerTimeout.TotalSeconds,
                LogPrivacyGuard.SafeExceptionType(ex));
            return BuildProviderFallbackDialogue(question, segment);
        }
    }

    private static string BuildProviderFallbackDialogue(string question, string? segment)
    {
        var safeQuestion = Trim(question ?? string.Empty, 220);
        var safeSegment = Trim(segment ?? string.Empty, 280);
        return NormalizeDialogue($"""
            [HOCA]: Canlı AI sağlayıcısı şu an yavaş yanıt verdi. Sınıf akışı bozulmasın diye güvenli kısa tekrar moduna geçiyorum.
            [ASISTAN]: Ogrencinin sorusu: {safeQuestion}
            [HOCA]: Takildigin aktif bolum su kisimla ilgili: {safeSegment}
            [KONUK]: Devam etmek icin once bu bolumu kendi cumlenle tekrar et, sonra hocadan tek bir adimi orneklemesini iste.
            """);
    }

    private void QueueAudioGeneration(Guid interactionId, string answer)
    {
        _ = _backgroundQueue.QueueAsync(new BackgroundTaskItem(
            "classroom-interaction-tts",
            null,
            null,
            async ct =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
                    var tts = scope.ServiceProvider.GetRequiredService<IEdgeTtsService>();

                    var audioBytes = await tts.SynthesizeDialogueAsync(answer, "standard", ct);
                    if (audioBytes.Length == 0) return;

                    var interaction = await db.ClassroomInteractions
                        .FirstOrDefaultAsync(i => i.Id == interactionId, ct);
                    if (interaction == null) return;

                    interaction.AudioBytes = audioBytes;
                    interaction.AudioByteLength = audioBytes.LongLength;
                    interaction.AudioExpiresAt = DateTime.UtcNow.AddDays(7);
                    interaction.AudioPurgedAt = null;
                    interaction.ContentType = "audio/mpeg";
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation("[Classroom] Edge-TTS audio attached. InteractionRef={InteractionRef} Bytes={Bytes}",
                        LogPrivacyGuard.SafeId(interactionId, "interaction"), audioBytes.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[Classroom] Interaction TTS failed; keeping script-only interaction. InteractionRef={InteractionRef} ErrorType={ErrorType}",
                        LogPrivacyGuard.SafeId(interactionId, "interaction"),
                        LogPrivacyGuard.SafeExceptionType(ex));
                }
            },
            MaxAttempts: 1,
            Timeout: TtsTimeout));
    }

    public async Task<(byte[] Bytes, string ContentType)?> GetInteractionAudioAsync(
        Guid userId,
        Guid interactionId,
        CancellationToken ct = default)
    {
        var interaction = await _db.ClassroomInteractions
            .AsNoTracking()
            .Include(i => i.ClassroomSession)
            .FirstOrDefaultAsync(i => i.Id == interactionId && i.ClassroomSession.UserId == userId, ct);

        if (interaction?.AudioBytes == null || interaction.AudioBytes.Length == 0) return null;
        return (interaction.AudioBytes, interaction.ContentType);
    }

    private static ClassroomSessionDto ToDto(ClassroomSession session)
    {
        var context = ParseClassroomContext(session.Transcript);
        var publicTranscript = BuildPublicTranscript(session.Transcript);
        return new ClassroomSessionDto(
            session.Id,
            session.TopicId,
            session.SessionId,
            publicTranscript,
            ExtractLastSegment(publicTranscript),
            session.Status,
            session.CreatedAt,
            context.Surface,
            context.ContextType,
            context.WikiPageId,
            context.SourceId,
            session.AudioOverviewJobId,
            context.AudioMode,
            CrossSurfaceSync: false,
            InternalConnections: BuildInternalConnectionKeys(context.Surface));
    }

    private async Task<string> BuildClassroomContextAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        AudioOverviewJob? audioOverviewJob,
        string? transcript,
        ClassroomContext context,
        CancellationToken ct)
    {
        var parts = new List<string>
        {
            BuildClassroomContextHeader(context)
        };

        if (!string.IsNullOrWhiteSpace(transcript))
            parts.Add($"[TRANSCRIPT]\n{Trim(transcript.Trim(), 4000)}");

        if (context.Surface == "orkalm")
        {
            if (context.SourceId.HasValue)
            {
                var source = await _db.LearningSources.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == context.SourceId.Value && s.UserId == userId && !s.IsDeleted, ct);
                if (source != null)
                    parts.Add($"[SOURCE_NOTEBOOK]\n{source.Title}\nStatus: {source.Status}\nChunks: {source.ChunkCount}");

                var sourceBits = await _db.SourceChunks
                    .AsNoTracking()
                    .Where(c => c.LearningSourceId == context.SourceId.Value && c.LearningSource.UserId == userId && !c.LearningSource.IsDeleted)
                    .OrderBy(c => c.PageNumber)
                    .ThenBy(c => c.ChunkIndex)
                    .Take(4)
                    .Select(c => $"[citation:{c.LearningSourceId}:p{c.PageNumber}:c{c.ChunkIndex}] {c.HighlightHint ?? Trim(c.Text, 420)}")
                    .ToListAsync(ct);
                if (sourceBits.Count > 0)
                    parts.Add("[CITATIONS]\n" + Trim(string.Join("\n\n", sourceBits), 2500));
            }
            else if (topicId.HasValue)
            {
                var sourceSummaries = await _db.LearningSources.AsNoTracking()
                    .Where(s => s.UserId == userId && s.TopicId == topicId.Value && !s.IsDeleted)
                    .OrderByDescending(s => s.UpdatedAt)
                    .Take(4)
                    .Select(s => $"{s.Title} | status={s.Status} | chunks={s.ChunkCount}")
                    .ToListAsync(ct);
                if (sourceSummaries.Count > 0)
                    parts.Add("[SOURCE_COLLECTION]\n" + string.Join("\n", sourceSummaries));
            }

            parts.Add("[SCOPE_GUARD]\nOrkaLM classroom only reads source notebook/citation/source Q&A context in this phase.");
        }
        else
        {
            if (context.WikiPageId.HasValue)
            {
                var page = await _db.WikiPages.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == context.WikiPageId.Value && p.UserId == userId && !p.IsDeleted, ct);
                if (page != null)
                    parts.Add($"[WIKI_PAGE]\n{page.Title}\nConcept: {page.ConceptKey ?? "none"}\nSummary: {Trim(page.SafeSummary ?? string.Empty, 900)}");

                var blocks = await _db.WikiBlocks.AsNoTracking()
                    .Where(b => b.WikiPageId == context.WikiPageId.Value && !b.IsDeleted)
                    .OrderBy(b => b.OrderIndex)
                    .Take(6)
                    .Select(b => $"{b.BlockType}: {b.Title} {b.Content} concept={b.ConceptKey ?? "none"} misconception={b.MisconceptionKey ?? "none"}")
                    .ToListAsync(ct);
                if (blocks.Count > 0)
                    parts.Add("[WIKI_BLOCKS]\n" + Trim(string.Join("\n\n", blocks), 2500));
            }
            else if (topicId.HasValue)
            {
                var topic = await _db.Topics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == topicId.Value && t.UserId == userId, ct);
                if (topic != null)
                    parts.Add($"[TOPIC]\n{topic.Title}");

                var wiki = await _wikiService.GetWikiFullContentAsync(topicId.Value, userId);
                if (!string.IsNullOrWhiteSpace(wiki))
                    parts.Add($"[WIKI]\n{Trim(wiki, 2500)}");
            }

            if (sessionId.HasValue)
            {
                var messages = await _db.Messages
                    .AsNoTracking()
                    .Where(m => m.SessionId == sessionId.Value && m.UserId == userId)
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(8)
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => $"{m.Role}: {m.Content}")
                    .ToListAsync(ct);
                if (messages.Count > 0)
                    parts.Add("[SESSION]\n" + Trim(string.Join("\n", messages), 2500));
            }

            parts.Add("[SCOPE_GUARD]\nWiki classroom only reads Wiki lesson/tutor/question context in this phase.");
        }

        if (!string.IsNullOrWhiteSpace(audioOverviewJob?.Script))
            parts.Add("[AUDIO_OVERVIEW]\n" + Trim(audioOverviewJob.Script, 2500));

        return parts.Count == 0
            ? "Henuz classroom icin yeterli baglam yok."
            : Trim(string.Join("\n\n", parts), 9000);
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

    private static ClassroomContext NormalizeClassroomContext(string? surface, Guid? wikiPageId, Guid? sourceId, string? audioMode)
    {
        var normalizedSurface = NormalizeSurface(surface, sourceId);
        return new ClassroomContext(
            normalizedSurface,
            normalizedSurface == "orkalm" ? "source_notebook" : "wiki_page",
            normalizedSurface == "wiki" ? wikiPageId : null,
            normalizedSurface == "orkalm" ? sourceId : null,
            NormalizeAudioMode(audioMode));
    }

    private static ClassroomContext ParseClassroomContext(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return NormalizeClassroomContext(null, null, null, null);

        var firstLine = transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (firstLine == null || !firstLine.StartsWith("[ORKA_CLASSROOM_CONTEXT]", StringComparison.OrdinalIgnoreCase))
            return NormalizeClassroomContext(null, null, null, null);

        var values = firstLine.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);

        Guid? TryGuid(string key) =>
            values.TryGetValue(key, out var value) && Guid.TryParse(value, out var parsed) ? parsed : null;

        values.TryGetValue("surface", out var surface);
        values.TryGetValue("audioMode", out var audioMode);
        return NormalizeClassroomContext(surface, TryGuid("wikiPageId"), TryGuid("sourceId"), audioMode);
    }

    private static string BuildClassroomContextHeader(ClassroomContext context) =>
        $"[ORKA_CLASSROOM_CONTEXT]; surface={context.Surface}; contextType={context.ContextType}; wikiPageId={context.WikiPageId?.ToString() ?? "none"}; sourceId={context.SourceId?.ToString() ?? "none"}; audioMode={context.AudioMode}; crossSurfaceSync=false";

    private static ClassroomContext? TryParseAudioOverviewContext(AudioOverviewJob job)
    {
        if (string.IsNullOrWhiteSpace(job.SpeakersJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(job.SpeakersJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            var root = document.RootElement;
            var surface = TryGetPropertyString(root, "surface");
            var audioMode = TryGetPropertyString(root, "audioMode");
            var wikiPageId = TryGetPropertyGuid(root, "wikiPageId");
            var sourceId = TryGetPropertyGuid(root, "sourceId");
            return NormalizeClassroomContext(surface, wikiPageId, sourceId, audioMode);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsCompatibleClassroomContext(ClassroomContext requested, ClassroomContext audioJob)
    {
        if (!string.Equals(requested.Surface, audioJob.Surface, StringComparison.OrdinalIgnoreCase))
            return false;
        if (requested.WikiPageId.HasValue && audioJob.WikiPageId.HasValue && requested.WikiPageId != audioJob.WikiPageId)
            return false;
        if (requested.SourceId.HasValue && audioJob.SourceId.HasValue && requested.SourceId != audioJob.SourceId)
            return false;
        return true;
    }

    private static ClassroomContext MergeClassroomContext(ClassroomContext requested, ClassroomContext audioJob) =>
        NormalizeClassroomContext(
            audioJob.Surface,
            requested.WikiPageId ?? audioJob.WikiPageId,
            requested.SourceId ?? audioJob.SourceId,
            audioJob.AudioMode);

    private static string? TryGetPropertyString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Guid? TryGetPropertyGuid(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;

    private static string BuildPublicTranscript(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return string.Empty;
        var context = ParseClassroomContext(transcript);
        var publicLines = new List<string>
        {
            BuildClassroomContextHeader(context)
        };

        foreach (var line in transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("[HOCA]:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("[ASISTAN]:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("[KONUK]:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("[STUDENT]:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("[NARRATOR]:", StringComparison.OrdinalIgnoreCase))
            {
                publicLines.Add(Trim(line, 900));
            }
        }

        return string.Join("\n", publicLines);
    }

    private static IReadOnlyList<string> BuildInternalConnectionKeys(string surface) =>
        surface == "orkalm"
            ? ["source_notebook", "citation", "source_qa", "source_practice"]
            : ["wiki_page", "plan_step", "tutor_trace", "question_bank_trace", "wiki_learning_trace"];

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

    private static string NormalizeDialogue(string raw)
    {
        var normalized = AudioDialogueFormatter.NormalizeScript(raw);
        var speakers = AudioDialogueFormatter.ParseSpeakers(normalized);

        if (!speakers.Contains("HOCA"))
        {
            normalized = $"[HOCA]: Kisa bir cerceve kuralim; ogrencinin takildigi kismi adim adim acacagiz.\n{normalized}";
            speakers = ParseSpeakers(normalized);
        }

        if (!speakers.Contains("ASISTAN"))
        {
            normalized = $"{normalized.Trim()}\n[ASISTAN]: Burada kritik nokta su: hocanin anlattigi adimi kendi cumlenle tekrar edip kucuk bir ornekle denemelisin.";
        }

        return normalized.Trim();
    }

    private static IReadOnlyList<string> ParseSpeakers(string script) =>
        AudioDialogueFormatter.ParseSpeakers(script);

    private static string ExtractLastSegment(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return string.Empty;
        var lines = transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\n", lines.TakeLast(3));
    }

    private static string Trim(string value, int maxChars) =>
        value.Length > maxChars ? value[^maxChars..] : value;

    private sealed record ClassroomContext(
        string Surface,
        string ContextType,
        Guid? WikiPageId,
        Guid? SourceId,
        string AudioMode);
}

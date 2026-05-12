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
        CancellationToken ct = default)
    {
        if (!topicId.HasValue && !sessionId.HasValue && !audioOverviewJobId.HasValue && string.IsNullOrWhiteSpace(transcript))
            throw new ArgumentException("Classroom baslatmak icin topicId, sessionId, audioOverviewJobId veya transcript gerekli.");

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

        if (audioOverviewJobId.HasValue)
        {
            var jobExists = await _db.AudioOverviewJobs
                .AsNoTracking()
                .AnyAsync(j => j.Id == audioOverviewJobId.Value && j.UserId == userId, ct);
            if (!jobExists)
                throw new NotFoundException("Classroom audio overview job bulunamadı.");
        }

        var preparedTranscript = await BuildClassroomContextAsync(
            userId,
            topicId,
            sessionId,
            audioOverviewJobId,
            transcript,
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
            payloadJson: JsonSerializer.Serialize(new { audioOverviewJobId }),
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
            payloadJson: JsonSerializer.Serialize(new { question, segment }),
            ct: ct);

        _logger.LogInformation("[Classroom] Student question answered. Classroom={ClassroomId}", classroom.Id);
        return new ClassroomAskResultDto(classroom.Id, interaction.Id, answer, ParseSpeakers(answer));
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
            _logger.LogWarning(ex,
                "[Classroom] AI answer unavailable within {TimeoutSeconds}s. Returning structured fallback.",
                AiAnswerTimeout.TotalSeconds);
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

                    var audioBytes = await tts.SynthesizeDialogueAsync(answer, ct);
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
                    _logger.LogInformation("[Classroom] Edge-TTS audio attached. Interaction={InteractionId} Bytes={Bytes}",
                        interactionId, audioBytes.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Classroom] Interaction TTS failed; keeping script-only interaction. Interaction={InteractionId}", interactionId);
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

    private static ClassroomSessionDto ToDto(ClassroomSession session) =>
        new(session.Id, session.TopicId, session.SessionId, session.Transcript, session.LastSegment, session.Status, session.CreatedAt);

    private async Task<string> BuildClassroomContextAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        Guid? audioOverviewJobId,
        string? transcript,
        CancellationToken ct)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(transcript))
            parts.Add($"[TRANSCRIPT]\n{Trim(transcript.Trim(), 4000)}");

        if (topicId.HasValue)
        {
            var topic = await _db.Topics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == topicId.Value && t.UserId == userId, ct);
            if (topic != null)
                parts.Add($"[TOPIC]\n{topic.Title}");

            var wiki = await _wikiService.GetWikiFullContentAsync(topicId.Value, userId);
            if (!string.IsNullOrWhiteSpace(wiki))
                parts.Add($"[WIKI]\n{Trim(wiki, 2500)}");

            var sourceBits = await _db.SourceChunks
                .AsNoTracking()
                .Include(c => c.LearningSource)
                .Where(c => c.LearningSource.UserId == userId && c.LearningSource.TopicId == topicId)
                .OrderBy(c => c.ChunkIndex)
                .Take(4)
                .Select(c => $"[doc:{c.LearningSourceId}:p{c.PageNumber}] {c.Text}")
                .ToListAsync(ct);
            if (sourceBits.Count > 0)
                parts.Add("[SOURCES]\n" + Trim(string.Join("\n\n", sourceBits), 2500));
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

        if (audioOverviewJobId.HasValue)
        {
            var audioJob = await _db.AudioOverviewJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == audioOverviewJobId.Value && j.UserId == userId, ct);
            if (!string.IsNullOrWhiteSpace(audioJob?.Script))
                parts.Add("[AUDIO_OVERVIEW]\n" + Trim(audioJob.Script, 2500));
        }

        return parts.Count == 0
            ? "Henuz classroom icin yeterli baglam yok."
            : Trim(string.Join("\n\n", parts), 9000);
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
}

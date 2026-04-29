using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static readonly TimeSpan TtsTimeout = TimeSpan.FromSeconds(8);

    private readonly OrkaDbContext _db;
    private readonly IAIAgentFactory _factory;
    private readonly ILearningSignalService _signals;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly ILogger<ClassroomService> _logger;

    private static readonly Regex SpeakerRegex =
        new(@"\[(HOCA|ASISTAN|KONUK|OGRETMEN|TEACHER|GUEST|ASSISTANT)\]\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ClassroomService(
        OrkaDbContext db,
        IAIAgentFactory factory,
        ILearningSignalService signals,
        IServiceScopeFactory scopeFactory,
        IBackgroundTaskQueue backgroundQueue,
        ILogger<ClassroomService> logger)
    {
        _db = db;
        _factory = factory;
        _signals = signals;
        _scopeFactory = scopeFactory;
        _backgroundQueue = backgroundQueue;
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
        var classroom = new ClassroomSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            AudioOverviewJobId = audioOverviewJobId,
            Transcript = transcript ?? string.Empty,
            LastSegment = ExtractLastSegment(transcript),
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
        var classroom = await _db.ClassroomSessions
            .FirstOrDefaultAsync(c => c.Id == classroomSessionId && c.UserId == userId, ct);
        if (classroom == null) throw new InvalidOperationException("Classroom session bulunamadi.");

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

        var answer = await _factory.CompleteChatAsync(AgentRole.Classroom, systemPrompt, userPrompt, ct);
        answer = NormalizeDialogue(answer);
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
    private void QueueAudioGeneration(Guid interactionId, string answer)
    {
        _ = _backgroundQueue.QueueAsync(new BackgroundTaskItem(
            "classroom-interaction-tts",
            null,
            null,
            async ct =>
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
                interaction.ContentType = "audio/mpeg";
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("[Classroom] Edge-TTS audio attached. Interaction={InteractionId} Bytes={Bytes}",
                    interactionId, audioBytes.Length);
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

    private static string NormalizeDialogue(string raw)
    {
        var clean = (raw ?? string.Empty).Replace("```", "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = "Bu bolumu daha sade bir ornekle tekrar anlatalim.";
        }

        if (!SpeakerRegex.IsMatch(clean))
        {
            clean = $"[HOCA]: {clean}";
        }

        var normalized = SpeakerRegex.Replace(clean, match => $"[{NormalizeSpeaker(match.Groups[1].Value)}]:");
        var speakers = ParseSpeakers(normalized);

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
        SpeakerRegex.Matches(script)
            .Select(m => NormalizeSpeaker(m.Groups[1].Value))
            .Distinct()
            .DefaultIfEmpty("HOCA")
            .ToList();

    private static string NormalizeSpeaker(string raw)
    {
        var label = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return label switch
        {
            "ASSISTANT" => "ASISTAN",
            "OGRETMEN" or "TEACHER" => "HOCA",
            "GUEST" => "KONUK",
            _ => label is "HOCA" or "ASISTAN" or "KONUK" ? label : "HOCA"
        };
    }

    private static string ExtractLastSegment(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return string.Empty;
        var lines = transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\n", lines.TakeLast(3));
    }

    private static string Trim(string value, int maxChars) =>
        value.Length > maxChars ? value[^maxChars..] : value;
}

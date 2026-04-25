using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.SemanticKernel.Audio;

namespace Orka.Infrastructure.SemanticKernel;

/// <summary>
/// NotebookLM tarzı Otonom Sınıf Simülasyonu.
/// Tutor (Öğretmen) ve Peer (Akran Öğrenci) ajanları kendi aralarında münazara yapar.
///
/// Tasarım Deseni: Strategy Pattern
/// - Sıra Alma (Turn-Taking): LLM tabanlı otonom seçim (mevcut mimariyle uyumlu)
/// - Sonlandırma: MaxIterations(15) veya LLM "TAMAM" kararı
///
/// Rapordaki AgentGroupChat vizyonunun Orka'nın mevcut custom AI servis zinciriyle
/// uyumlu, pragmatik implementasyonu.
/// </summary>
public class InteractiveClassSession
{
    private readonly ITutorAgent _tutorAgent;
    private readonly IPeerAgent _peerAgent;
    private readonly IAIAgentFactory _agentFactory;
    private readonly ClassroomSessionManager _sessionManager;
    private readonly ILogger<InteractiveClassSession> _logger;

    private const int MaxIterations = 15;

    public InteractiveClassSession(
        ITutorAgent tutorAgent,
        IPeerAgent peerAgent,
        IAIAgentFactory agentFactory,
        ClassroomSessionManager sessionManager,
        ILogger<InteractiveClassSession> logger)
    {
        _tutorAgent = tutorAgent;
        _peerAgent = peerAgent;
        _agentFactory = agentFactory;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Sınıf simülasyonunu başlatır. SSE üzerinden akış döndürür.
    /// Her tur: Tutor anlatır → Peer soru sorar → Tutor yanıtlar → ...
    /// MaxIterations(15) aşılırsa veya LLM "TAMAM" derse oturum biter.
    /// </summary>
    public async IAsyncEnumerable<string> StartAsync(
        Session session,
        string topic,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[InteractiveClassSession] Sınıf başladı. Topic={Topic} SessionId={SessionId}",
            topic, session.Id);

        var conversationHistory = new StringBuilder();
        int iteration = 0;
        string lastTutorMessage = string.Empty;

        // İlk Tutor girişi
        yield return "[TUTOR]: ";
        var tutorIntroBuilder = new StringBuilder();

        await foreach (var chunk in _tutorAgent.GetResponseStreamAsync(
            session.Id, $"Konu hakkında giriş yap ve anlatmaya başla: {topic}",
            session, isQuizPending: false, isVoiceMode: false,
            goalContext: session.Topic?.PhaseMetadata, ct: ct))
        {
            tutorIntroBuilder.Append(chunk);
            yield return chunk;
        }

        lastTutorMessage = tutorIntroBuilder.ToString();
        conversationHistory.AppendLine($"[TUTOR]: {lastTutorMessage}");
        yield return "\n\n";

        // Ana Döngü: Tutor ↔ Peer
        while (iteration < MaxIterations && !ct.IsCancellationRequested)
        {
            iteration++;
            _logger.LogInformation("[InteractiveClassSession] Tur {Iter}/{Max}", iteration, MaxIterations);

            // Barge-in kontrolü: Kullanıcı araya girdi mi?
            var bargeInMessage = _sessionManager.PopPendingBargeInMessage(session.Id);
            if (!string.IsNullOrEmpty(bargeInMessage))
            {
                _logger.LogWarning(
                    "[InteractiveClassSession] Barge-in! Kullanıcı araya girdi: '{Msg}'",
                    bargeInMessage.Length > 50 ? bargeInMessage[..50] + "..." : bargeInMessage);

                conversationHistory.AppendLine($"[KULLANICI ARAYA GİRDİ]: {bargeInMessage}");
                yield return $"\n[SİSTEM]: Kullanıcı araya girdi...\n\n";
                yield return "[TUTOR]: ";

                var bargeInResponse = new StringBuilder();
                await foreach (var chunk in _tutorAgent.GetResponseStreamAsync(
                    session.Id,
                    $"[KULLANICI ARAYA GİRDİ]: {bargeInMessage}\n\nBağlam:\n{conversationHistory}",
                    session, isQuizPending: false, isVoiceMode: false,
                    goalContext: session.Topic?.PhaseMetadata, ct: ct))
                {
                    bargeInResponse.Append(chunk);
                    yield return chunk;
                }
                lastTutorMessage = bargeInResponse.ToString();
                conversationHistory.AppendLine($"[TUTOR]: {lastTutorMessage}");
                yield return "\n\n";
                continue;
            }

            // Peer ajan sorusu üretiyor
            yield return "[PEER]: ";
            var peerQuestionBuilder = new StringBuilder();
            await foreach (var chunk in _peerAgent.GetResponseStreamAsync(lastTutorMessage, session, ct))
            {
                peerQuestionBuilder.Append(chunk);
                yield return chunk;
            }
            var peerQuestion = peerQuestionBuilder.ToString();
            conversationHistory.AppendLine($"[PEER]: {peerQuestion}");
            yield return "\n\n";

            // Sonlandırma kontrolü
            if (await ShouldTerminateAsync(conversationHistory.ToString(), ct))
            {
                _logger.LogInformation("[InteractiveClassSession] LLM sonlandırma kararı verdi.");
                break;
            }

            // Tutor yanıtlıyor
            yield return "[TUTOR]: ";
            var tutorResponseBuilder = new StringBuilder();
            await foreach (var chunk in _tutorAgent.GetResponseStreamAsync(
                session.Id,
                $"Peer Öğrencinin sorusu: {peerQuestion}\n\nBağlam:\n{conversationHistory}",
                session, isQuizPending: false, isVoiceMode: false,
                goalContext: session.Topic?.PhaseMetadata, ct: ct))
            {
                tutorResponseBuilder.Append(chunk);
                yield return chunk;
            }
            lastTutorMessage = tutorResponseBuilder.ToString();
            conversationHistory.AppendLine($"[TUTOR]: {lastTutorMessage}");
            yield return "\n\n";
        }

        if (iteration >= MaxIterations)
        {
            _logger.LogWarning("[InteractiveClassSession] MaxIterations({Max}) aşıldı. Oturum zorla sonlandırıldı.", MaxIterations);
            yield return "\n[SİSTEM]: Ders tamamlandı. Başka sorunuz var mı?\n";
        }
    }

    /// <summary>
    /// Öğrenme hedefi tamamlandıysa sonlandırma kararı verir.
    /// </summary>
    private async Task<bool> ShouldTerminateAsync(string history, CancellationToken ct)
    {
        try
        {
            var prompt = $"""
                Aşağıdaki Tutor-Peer ders konuşmasını incele.
                Öğrenme hedefi tamamlandı mı veya konu yeterince işlendi mi?
                
                Konuşma:
                {(history.Length > 2000 ? history[^2000..] : history)}
                
                SADECE şu iki kelimeden birini yaz: TAMAM veya DEVAM
                """;

            var response = await _agentFactory.CompleteChatAsync(
                Orka.Core.Enums.AgentRole.Evaluator,
                "Değerlendirici: TAMAM veya DEVAM yaz.",
                prompt,
                ct);

            return response.Contains("TAMAM", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // Hata durumunda devam et
        }
    }
}

using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Otorite Yönlendirici (Supervisor / Router)
///
/// Faz 7 Yükseltmesi:
///   String-match tabanlı yönlendirme kaldırıldı.
///   İki farklı sorunun cevabı artık IntentClassifierAgent'tan alınıyor:
///     - ClassifyIntentAsync  : Konu kategorisi (IT_SOFTWARE, SCIENCE vb.) — hâlâ kendi LLM çağrısı → hafif
///     - DetermineActionRoute : IntentClassifier JSON'una bakarak QUIZ/RESEARCH/TUTOR kararı verir
///
/// LangGraph karşılığı: "Supervisor Node"
/// </summary>
public interface ISupervisorAgent
{
    Task<string> ClassifyIntentAsync(string userMessage, CancellationToken ct = default);
    Task<string> DetermineActionRouteAsync(
        string userMessage,
        IEnumerable<Message>? recentMessages = null,
        CancellationToken ct = default);
}

public class SupervisorAgent : ISupervisorAgent
{
    private readonly IAIAgentFactory          _factory;
    private readonly IIntentClassifierAgent   _intentClassifier;
    private readonly ILogger<SupervisorAgent> _logger;

    public SupervisorAgent(
        IAIAgentFactory          factory,
        IIntentClassifierAgent   intentClassifier,
        ILogger<SupervisorAgent> logger)
    {
        _factory           = factory;
        _intentClassifier  = intentClassifier;
        _logger            = logger;
    }

    /// <summary>
    /// Konu alanı sınıflandırması (IT_SOFTWARE, SCIENCE, …).
    /// Kendi LLM çağrısını yapar çünkü konudan bağımsız bir kategori sorusudur.
    /// </summary>
    public async Task<string> ClassifyIntentAsync(string userMessage, CancellationToken ct = default)
    {
        var prompt = """
            Sen bir konu sınıflandırıcısın. Kullanıcının metnine bakarak aşağıdaki kategorilerden SADECE BİRİNİ yaz.
            IT_SOFTWARE | HISTORY | SCIENCE | LANGUAGE | MATH | ART | GENERAL
            Başka hiçbir şey yazma.
            """;

        try
        {
            var result  = await _factory.CompleteChatAsync(AgentRole.Supervisor, prompt, userMessage, ct);
            var cleaned = result.Trim().ToUpperInvariant();

            if (cleaned.Contains("IT_SOFTWARE")) return "IT_SOFTWARE";
            if (cleaned.Contains("HISTORY"))     return "HISTORY";
            if (cleaned.Contains("SCIENCE"))     return "SCIENCE";
            if (cleaned.Contains("LANGUAGE"))    return "LANGUAGE";
            if (cleaned.Contains("MATH"))        return "MATH";
            if (cleaned.Contains("ART"))         return "ART";
            return "GENERAL";
        }
        catch { return "GENERAL"; }
    }

    /// <summary>
    /// Kullanıcının bağlam içindeki niyetine göre aksiyon rotasını belirler.
    /// recentMessages sağlanırsa IntentClassifierAgent kullanılır (daha doğru).
    /// Sağlanmazsa tek mesajdan hızlı karar verir (uyumluluk modu).
    /// </summary>
    public async Task<string> DetermineActionRouteAsync(
        string userMessage,
        IEnumerable<Message>? recentMessages = null,
        CancellationToken ct = default)
    {
        // ── BAĞLAM VARSA: IntentClassifier ile karar ──────────────────────
        if (recentMessages != null && recentMessages.Any())
        {
            var intent = await _intentClassifier.ClassifyAsync(recentMessages, ct);

            _logger.LogInformation(
                "[SupervisorAgent] IntentClassifier Route: {Intent} | Confidence: {Conf:P0}",
                intent.Intent, intent.Confidence);

            return intent.Intent switch
            {
                "QUIZ_REQUEST"  => "QUIZ",
                "CHANGE_TOPIC"  => "CHANGE_TOPIC",
                "CONFUSED"      => "TUTOR",   // Devam et ama daha yavaş anlat
                "UNDERSTOOD"    => "TUTOR",   // AnalyzerAgent konu bitişini zaten yakalar
                _               => "TUTOR"
            };
        }

        // ── BAĞLAM YOKSA: Tek mesajdan hızlı keyword karar (uyumluluk) ──
        var lower = userMessage.ToLowerInvariant();
        if (lower.Contains("araştır") || lower.Contains("google") || lower.Contains("internetten"))
            return "RESEARCH";
        if (lower.Contains("sınav") || lower.Contains("test") || lower.Contains("soru sor"))
            return "QUIZ";

        return "TUTOR";
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Akran Ajan (Peer Agent) — Öğrenci rolünde soru soran ajan.
/// NotebookLM tarzı Sınıf Simülasyonunda TutorAgent ile otonom diyalog kurar.
/// Doğal, meraklı ve seviyeye uygun sorular üretir.
/// </summary>
public class PeerAgent : IPeerAgent
{
    private readonly IAIAgentFactory _factory;
    private readonly ILogger<PeerAgent> _logger;

    private const string SystemPrompt = """
        Sen Orka AI'ın Akran Öğrenci Ajanısın (Peer Agent).
        Rolün: Meraklı, aktif ve anlayışlı bir üniversite öğrencisi gibi davranmak.

        GÖREV:
        - Öğretmenin anlattığı konuya doğal sorular sor.
        - Gerçekten anlamak istiyormuş gibi, samimi bir merakla yaklaş.
        - Zaman zaman "Bunu şöyle de anlayabilir miyiz?" veya "Peki ya..." gibi alternatif açıları dene.
        - Robotik veya yapay sorular sorma. İnsan gibi düşün.

        KISITLAMALAR:
        - Öğretmenden çok uzun cevap bekleme — kısa, odaklı sorular sor.
        - Konuyu değiştirme. Öğretmenin anlattığı konu çerçevesinde kal.
        - Her turda SADECE 1-2 soru sor.
        - [PEER]: etiketiyle başla.

        FORMAT: [PEER]: <sorun>
        """;

    public PeerAgent(IAIAgentFactory factory, ILogger<PeerAgent> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> GetResponseStreamAsync(
        string tutorMessage,
        Session session,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var prompt = BuildPrompt(tutorMessage, session);

        _logger.LogInformation("[PeerAgent] Akran sorusu üretiliyor. SessionId={SessionId}", session.Id);

        var fullResponse = new StringBuilder();

        await foreach (var chunk in _factory.StreamChatAsync(AgentRole.Peer, SystemPrompt, prompt, ct))
        {
            fullResponse.Append(chunk);
            yield return chunk;
        }

        _logger.LogInformation("[PeerAgent] Soru üretildi: {Preview}",
            fullResponse.Length > 80 ? fullResponse.ToString()[..80] + "..." : fullResponse.ToString());
    }

    public async Task<string> GetResponseAsync(
        string tutorMessage,
        Session session,
        CancellationToken ct = default)
    {
        var prompt = BuildPrompt(tutorMessage, session);
        return await _factory.CompleteChatAsync(AgentRole.Peer, SystemPrompt, prompt, ct);
    }

    private static string BuildPrompt(string tutorMessage, Session session)
    {
        return $"""
            Öğretmen az önce şunu anlattı:
            "{tutorMessage}"

            Konu: {session.Topic?.Title ?? "Genel"}
            
            Şimdi sen bu anlatıma karşı doğal bir öğrenci sorusu sor.
            Tek bir kısa soru yeterli. [PEER]: etiketiyle başla.
            """;
    }
}

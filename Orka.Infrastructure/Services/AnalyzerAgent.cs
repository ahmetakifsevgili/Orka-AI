using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public class AnalyzerAgent : IAnalyzerAgent
{
    private readonly IAIAgentFactory _factory;
    private readonly ILogger<AnalyzerAgent> _logger;

    public AnalyzerAgent(IAIAgentFactory factory, ILogger<AnalyzerAgent> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    public async Task<bool> AnalyzeCompletionAsync(IEnumerable<Message> messages)
    {
        var lastMessages = messages.TakeLast(6).Select(m => $"{m.Role}: {m.Content}");
        var context = string.Join("\n", lastMessages);

        var systemPrompt = @"Aşağıdaki sohbet geçmişine bakarak, kullanıcının o anki alt başlığı veya genel konuyu bitirip bitirmediğini analiz et.
Eğer kullanıcı 'anladım', 'tamam', 'başka sorum yok', 'test yapalım' gibi ifadeler kullanıyorsa veya eğitmen konuyu tamamen bitirdiğine dair bir onay aldıysa sonucun 'TRUE' olsun.
Hala anlatım devam ediyorsa veya kullanıcı soru sormaya devam ediyorsa sonucun 'FALSE' olsun.

SADECE 'TRUE' veya 'FALSE' yaz. Başka hiçbir açıklama ekleme.";

        var userMessage = $"Sohbet Geçmişi:\n{context}";

        try
        {
            // gpt-4o-mini — hafif, hızlı completion analizi
            var response = await _factory.CompleteChatAsync(AgentRole.Analyzer, systemPrompt, userMessage);
            return response.Contains("TRUE", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AnalyzerAgent] Tüm sağlayıcılar başarısız, fail-safe FALSE döndürülüyor.");
            return false;
        }
    }
}

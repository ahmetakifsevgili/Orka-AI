using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orka.Core.Entities;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public class AnalyzerAgent : IAnalyzerAgent
{
    private readonly IGroqService _groqService;

    public AnalyzerAgent(IGroqService groqService)
    {
        _groqService = groqService;
    }

    public async Task<bool> AnalyzeCompletionAsync(IEnumerable<Message> messages)
    {
        var lastMessages = messages.TakeLast(6).Select(m => $"{m.Role}: {m.Content}");
        var context = string.Join("\n", lastMessages);

        var prompt = $@"Aşağıdaki sohbet geçmişine bakarak, kullanıcının o anki alt başlığı veya genel konuyu bitirip bitirmediğini analiz et.
Eğer kullanıcı 'anladım', 'tamam', 'başka sorum yok', 'test yapalım' gibi ifadeler kullanıyorsa veya eğitmen konuyu tamamen bitirdiğine dair bir onay aldıysa sonucun 'TRUE' olsun.
Hala anlatım devam ediyorsa veya kullanıcı soru sormaya devam ediyorsa sonucun 'FALSE' olsun.

SADECE 'TRUE' veya 'FALSE' yaz. Başka hiçbir açıklama ekleme.

Sohbet Geçmişi:
{context}

Sonuç:";

        var response = await _groqService.GetResponseAsync(new List<Message> { new Message { Role = "user", Content = prompt } }, "Hızlı Analizci");
        return response.Contains("TRUE", StringComparison.OrdinalIgnoreCase);
    }
}

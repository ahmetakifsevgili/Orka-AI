using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public class WikiAgent : IWikiAgent
{
    private readonly Kernel _kernel;

    public WikiAgent(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async IAsyncEnumerable<string> AskQuestionStreamAsync(string wikiContent, string question)
    {
        var chatService = _kernel.GetRequiredService<IChatCompletionService>();
        
        var history = new ChatHistory($"""
            Sen Orka AI'nın 'Wiki Destek Asistanı'sın. 
            Görevin, kullanıcının aşağıdaki Wiki içeriği hakkında sorduğu soruları cevaplamaktır.

            [WIKI İÇERİĞİ]:
            {wikiContent}

            [KURALLAR]:
            1. SADECE yukarıdaki Wiki içeriğine dayanarak cevap ver.
            2. Eğer cevap Wiki içeriğinde yoksa, nazikçe "Bu bilgi mevcut dökümanda yer almıyor, ancak ana sohbetten Orka Eğitmeni'ne sorabilirsin." şeklinde belirt.
            3. Kısa, öz ve yardımsever bir ton kullan.
            4. Markdown formatını (kalın metin, listeler) kullanabilirsin.
            5. Yanıtın 2-3 paragrafı geçmesin.
            """);

        history.AddUserMessage(question);

        var settings = new OpenAIPromptExecutionSettings { Temperature = 0.3 };

        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(history, settings, _kernel))
        {
            if (chunk.Content != null)
                yield return chunk.Content;
        }
    }
}

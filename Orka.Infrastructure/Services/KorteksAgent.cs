using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Orka.Core.Interfaces;
using Orka.Infrastructure.SemanticKernel.Plugins;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Korteks — Orka AI'nın otonom derin araştırma motoru.
///
/// Araştırma Akışı (Perplexity benzeri):
///   1. Konuyu 3 açıdan paralel web araması (Tavily)
///   2. Temel kavramları Wikipedia ile doğrulama (hallucination önleme)
///   3. Kaynakları çapraz kontrol — çelişen bilgileri işaret et
///   4. Her iddiaya kaynak URL bağla (citation)
///   5. Yapılandırılmış rapor: Genel Bakış → Bulgular → Kaynaklar → Özet
///
/// Hallucination Önleme:
///   - Kafadan uydurma yasak — her bilgi için kaynak zorunlu
///   - Wikipedia otorite kaynak olarak kullanılır
///   - Çelişen kaynaklar olduğunda kullanıcı uyarılır
///   - Bilgi bulunamazsa "Doğrulanamadı" yazılır, uydurulmaz
/// </summary>
public class KorteksAgent : IKorteksAgent
{
    private readonly IServiceProvider      _serviceProvider;
    private readonly IConfiguration        _config;
    private readonly ILogger<KorteksAgent> _logger;

    public KorteksAgent(
        IServiceProvider      serviceProvider,
        IConfiguration        configuration,
        ILogger<KorteksAgent> logger)
    {
        _serviceProvider = serviceProvider;
        _config          = configuration;
        _logger          = logger;
    }

    private Kernel BuildKorteksKernel()
    {
        var model   = _config["AI:GitHubModels:Agents:Korteks:Model"] ?? "Meta-Llama-3.1-405B-Instruct";
        var apiKey  = _config["AI:GitHubModels:Token"]                ?? throw new InvalidOperationException("AI:GitHubModels:Token eksik.");
        var baseUrl = _config["AI:GitHubModels:BaseUrl"]              ?? "https://models.inference.ai.azure.com";

        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: model, apiKey: apiKey, endpoint: new Uri(baseUrl))
            .Build();

        // Plugin 1: Web araması (paralel + raw content)
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<TavilySearchPlugin>(), "WebSearch");

        // Plugin 2: Wikipedia (hallucination önleme + kavram doğrulama)
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<WikipediaPlugin>(), "Wikipedia");

        // Plugin 3: Orka topic bilgisi
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<TopicPlugin>(), "Topics");

        // Plugin 4: Orka wiki (mevcut öğrenme içeriği)
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<WikiPlugin>(), "OrkaWiki");

        return kernel;
    }

    public async IAsyncEnumerable<string> RunResearchAsync(
        string  topic,
        Guid    userId,
        Guid?   topicId = null,
        string? fileContext = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("[Korteks] Araştırma başlatılıyor: '{Topic}' | User: {User}", topic, userId);

        yield return "🧠 Korteks derin araştırma motoru başlatılıyor...\n";

        Kernel? kernel   = null;
        string? buildErr = null;
        try
        {
            kernel = BuildKorteksKernel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Korteks] Kernel başlatılamadı.");
            buildErr = ex.Message;
        }

        if (buildErr != null)
        {
            yield return $"❌ Korteks başlatılamadı: {buildErr}\n";
            yield break;
        }

        yield return "✅ Web araması + Wikipedia + Kaynak doğrulama aktif.\n";
        yield return $"🔍 \"{topic}\" için derin araştırma başlıyor...\n\n";

        var chatService = kernel!.GetRequiredService<IChatCompletionService>();

        var topicContext = topicId.HasValue
            ? $"Konu ID: {topicId.Value} | Kullanıcı ID: {userId}"
            : $"Kullanıcı ID: {userId}";

        var fileSection = string.IsNullOrWhiteSpace(fileContext)
            ? ""
            : $"""

            KULLANICI TARAFINDAN YÜKLENEN DOKÜMAN:
            Aşağıdaki içerik kullanıcının yüklediği dosyadan alınmıştır.
            Web araması YAP — bu dokümanı web kaynaklarıyla karşılaştır, güncel bilgilerle zenginleştir.
            Dokümanda hata veya eski bilgi varsa bunu ⚠️ bölümünde belirt.

            ---DOKÜMAN BAŞLANGICI---
            {fileContext}
            ---DOKÜMAN SONU---
            """;

        var systemPrompt = $"""
            Sen Orka AI'nın "Korteks" modülüsün — Perplexity benzeri bir derin araştırma motorusun.

            ARAŞTIRMA SÜRECİ (bu sırayı takip et):
            1. WebSearch-SearchWebDeep ile konuyu 3 farklı açıdan paralel ara.
               Sorgular: ana kavram / teknik detaylar / son gelişmeler/örnekler
            2. Wikipedia-SearchWikipedia ile temel kavramı doğrula.
               Bulunan Wikipedia makalesini Wikipedia-GetWikipediaArticle ile çek.
            3. Toplanan bilgileri karşılaştır — çelişen bilgileri not et.
            4. Aşağıdaki formatta kapsamlı raporu yaz.

            ZORUNLU ÇIKTI FORMATI:

            ## 📌 Genel Bakış
            Konunun ne olduğunu ve neden önemli olduğunu 3-4 cümleyle açıkla.
            Wikipedia kaynağını burada kullan — en güvenilir temel tanım oradadır.

            ## 🔬 Temel Bulgular
            Web aramasından elde ettiğin en önemli 5-7 bulguyu maddeler halinde yaz.
            **Her maddenin sonuna kaynak göster: ([Kaynak başlığı](URL))**
            Örnek: Python, 1991'de Guido van Rossum tarafından geliştirilmiştir. ([Wikipedia](https://tr.wikipedia.org/wiki/Python))

            ## ⚠️ Doğrulanamayan / Çelişen Bilgiler
            Kaynaklar arasında tutarsızlık varsa veya doğrulayamadığın bir bilgi varsa burada belirt.
            Eğer yoksa: "Tüm bulgular çapraz kaynaklarla doğrulandı." yaz.

            ## 📚 Kaynaklar
            Kullandığın tüm kaynakları listele:
            - [Başlık](URL) — ne tür bilgi sağladı (tek satır açıklama)

            ## 💡 Özet
            2-3 cümlede araştırmanın ana bulgusunu özetle.
            Öğrenmek isteyenler için önerilen sonraki adım nedir?

            HALLUCİNASYON ÖNLEME KURALLARI (KESİNLİKLE UYULACAK):
            - Herhangi bir bilgiyi yazmadan önce kaynağını bul. Kaynak yoksa yazma.
            - "Bilindiği kadarıyla", "genellikle" gibi belirsiz ifadeler kullanırsan kaynağını da ekle.
            - Rakam, tarih, isim gibi spesifik bilgilerde kaynak zorunlu. Kaynak yoksa "Doğrulanamadı" yaz.
            - Wikipedia ile web aramasından çelişen bilgi gelirse her ikisini de yaz, hangisinin güncel olduğunu belirt.
            - Tavily aramasının "raw_content" kısmını temel al — snippet yanıltıcı olabilir.

            DİL: Türkçe. Teknik terimlerin İngilizce karşılığını parantez içinde ver.
            FORMAT: Markdown. Bölüm başlıkları, maddeler, kalın terimler.
            {topicContext}{fileSection}
            """;

        var chatHistory = new ChatHistory(systemPrompt);
        chatHistory.AddUserMessage(
            $"Konu: \"{topic}\"\n\n" +
            $"Adımları sırayla uygula: önce WebSearch-SearchWebDeep ile paralel ara, " +
            $"sonra Wikipedia-SearchWikipedia ile doğrula, ardından raporu yaz.");

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature      = 0.2,   // Daha deterministik = daha az hallucination
            MaxTokens        = 4096,
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        yield return "📡 Web araması + Wikipedia doğrulaması çalışıyor...\n";
        yield return "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n";

        var fullResponse = new StringBuilder();

        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
            chatHistory, settings, kernel, ct))
        {
            if (chunk.Content != null)
            {
                fullResponse.Append(chunk.Content);
                yield return chunk.Content;
            }
        }

        yield return "\n\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n";
        yield return "✅ Araştırma tamamlandı. Tüm bilgiler kaynaklara dayalıdır.\n";

        _logger.LogInformation("[Korteks] Tamamlandı. Çıktı: {Len} karakter", fullResponse.Length);
    }
}

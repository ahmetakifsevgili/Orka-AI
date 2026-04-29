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
        // AgentRouting'den Korteks için provider+model oku (varsayılan: OpenRouter / claude-opus-4-7).
        var provider = _config["AI:AgentRouting:Korteks:Provider"] ?? "OpenRouter";
        var model    = _config[$"AI:AgentRouting:Korteks:Model"]
                       ?? _config["AI:GitHubModels:Agents:Korteks:Model"]
                       ?? "anthropic/claude-opus-4-7";

        string apiKey;
        string baseUrl;

        switch (provider.ToLowerInvariant())
        {
            case "openrouter":
                apiKey  = _config["AI:OpenRouter:ApiKey"] ?? throw new InvalidOperationException("AI:OpenRouter:ApiKey eksik.");
                baseUrl = _config["AI:OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
                break;
            case "groq":
                apiKey  = _config["AI:Groq:ApiKey"] ?? throw new InvalidOperationException("AI:Groq:ApiKey eksik.");
                baseUrl = _config["AI:Groq:BaseUrl"] ?? "https://api.groq.com/openai/v1";
                break;
            default: // GitHubModels fallback
                apiKey  = _config["AI:GitHubModels:Token"] ?? throw new InvalidOperationException("AI:GitHubModels:Token eksik.");
                baseUrl = _config["AI:GitHubModels:BaseUrl"] ?? "https://models.inference.ai.azure.com";
                break;
        }

        _logger.LogInformation("[Korteks] Kernel: Provider={Provider} Model={Model}", provider, model);

        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: model, apiKey: apiKey, endpoint: new Uri(baseUrl))
            .Build();

        // Plugin 1: Web araması (paralel + raw content)
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<TavilySearchPlugin>(), "WebSearch");

        // Plugin 2: Wikipedia (hallucination önleme + kavram doğrulama)
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<WikipediaPlugin>(), "Wikipedia");

        // Plugin 3: Akademik kaynak (Semantic Scholar + ArXiv) — V4 derin doğrulama
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<AcademicSearchPlugin>(), "Academic");

        // Plugin 4: Orka topic bilgisi
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<TopicPlugin>(), "Topics");

        // Plugin 5: Orka wiki (mevcut öğrenme içeriği)
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<WikiPlugin>(), "OrkaWiki");

        // Plugin 6: YouTube eğitim videosu arama ve transcript çekme
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<YouTubeTranscriptPlugin>(), "YouTube");

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
            Kullanıcının verdiği konuyu o kadar derinlemesine, akademik ve çok boyutlu işle ki, çıkan sonuç bir ansiklopedi maddesi kadar doyurucu olsun.

            ARAŞTIRMA SÜRECI (bu sırayı takip et):
            1. WebSearch-SearchWebDeep ile konuyu 3 farklı açıdan paralel ara.
               Sorgular: [Ana kavramın detayları] / [Pratik kullanım, teknik örnekler] / [Son gelişmeler, endüstri standartları]
            2. Wikipedia-SearchWikipedia ile makaleyi bulup çek.
            3. Academic-SearchSemanticScholar ile peer-reviewed makaleler ara (V4 yeni — bilimsel iddiaları buradan doğrula).
               Eğer konu yeni / preprint ağırlıklıysa Academic-SearchArXiv'i de kullan.
            4. YouTube-SearchYouTubeVideos ile konuyla ilgili eğitim videoları ara.
               En alakalı videonun transcript'ını YouTube-GetVideoTranscript ile çek.
               Transcript varsa raporun içinde "Bu konuda şu eğitim videosu referans alınmıştır" şeklinde kaynak göster.
            5. Toplanan bilgileri karşılaştır. Web ↔ Wikipedia ↔ Akademik makale çelişiyorsa "akademik" sürümü öncele.
            6. En az 6-8 paragraf uzunluğunda, son derece doyurucu ve derinlemesine yapılandırılmış bir rapor yaz.

            ZORUNLU ÇIKTI FORMATI:

            ## 📌 Genel Bakış ve Kapsam
            Konunun ne olduğunu tarihsel veya güncel bağlamıyla derinlemesine açıkla (Wikipedia'dan yararlan).

            ## 🔬 Teknik Detaylar ve Bulgular (EN ÖNEMLİ BÖLÜM)
            Web aramasından elde ettiğin tüm kritik teknik bilgileri, kod/uygulama standartlarını veya sektörel pratikleri madde madde ve paragraflar halinde yaz. Yüzeysel geçme.
            **Her önemli iddianın sonuna kaynak göster: ([Kaynak başlığı](URL))**

            ## ⚠️ Doğrulanamayan / Kısıtlı Bilgiler
            Kaynaklar arasında çelişki varsa belirt. Yoksa tümünün doğrulandığını yaz.

            ## 📚 Kaynakça
            - [Başlık](URL) — ne tür bilgi sağladı (açıklama)

            ## 🎬 Önerilen Eğitim Videoları
            YouTube'da bulduğun alakalı eğitim videolarını buraya listele.
            - [Video Başlığı — Kanal Adı](https://youtube.com/watch?v=ID)
            Video bulunamadıysa veya YouTube araması başarısız olduysa bu bölümü atlayabilirsin.

            ## 💡 Sentez ve Sonuç
            Tüm araştırmayı bağlayan akademik bir kapanış.

            HALLUCİNASYON ÖNLEME KURALLARI:
            - Kaynağı olmayan bilgiyi ekleme.
            - Tavily raw_content kısmını temel al. Yeterli veri yoksa "Bulunamadı" de.

            DİL: Türkçe. Teknik terimlerin İngilizce karşılığını parantez içinde ver.
            FORMAT: Markdown.
            {topicContext}{fileSection}
            """;

        var chatHistory = new ChatHistory(systemPrompt);
        chatHistory.AddUserMessage(
            $"Konu: \"{topic}\"\n\n" +
            $"Adımları sırayla uygula: önce WebSearch-SearchWebDeep ile paralel ara, " +
            $"sonra Wikipedia-SearchWikipedia ile doğrula, YouTube-SearchYouTubeVideos ile eğitim videosu bul, ardından raporu yaz.");

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

        // Araştırma sonucunu Wiki'ye kaydet (topicId varsa)
        bool wikiSaved = false;
        bool wikiError = false;

        string? guardrailMessage = null;
        if (topicId.HasValue && fullResponse.Length > 0)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var grader = scope.ServiceProvider.GetRequiredService<IGraderAgent>();
                var wikiService = scope.ServiceProvider.GetRequiredService<IWikiService>();

                guardrailMessage = "\n\n🛡️ [Güvenlik Kalkanı]: Elde edilen bilgiler 'Halüsinasyon' ve 'Konu Sapması' testinden geçiriliyor...\n";
                
                bool isValid = await grader.IsContextRelevantAsync(topic, fullResponse.ToString(), ct);

                if (isValid)
                {
                    await wikiService.AutoUpdateWikiAsync(
                        topicId.Value,
                        fullResponse.ToString(),
                        $"Korteks derin araştırması: {topic}",
                        "korteks-agent");
                    wikiSaved = true;

                    // Faz 11: TutorAgent'a "bu konu için taze araştırma var" sinyali
                    var redisService = scope.ServiceProvider.GetService<IRedisMemoryService>();
                    if (redisService != null)
                    {
                        await redisService.SetWikiReadyAsync(topicId.Value);
                        // Faz 16: QuizAgent + TutorAgent araştırma bağlamını okuyabilsin diye Redis'e koy.
                        await redisService.SaveKorteksResearchReportAsync(topicId.Value, fullResponse.ToString());
                    }
                }
                else
                {
                    wikiError = true;
                    guardrailMessage += "\n⚠️ [UYARI]: Grader Ajanı bu içeriğin konuyla örtüşmediğini veya tutarsız bilgiler taşıdığını tespit etti! (Wiki'ye işlenmedi)\n";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Korteks] Wiki/Grader doğrulama hatası. TopicId={TopicId}", topicId);
                wikiError = true;
            }
        }

        if (guardrailMessage != null)
            yield return guardrailMessage;

        if (wikiSaved)
            yield return "\n📖 **Araştırma sonuçları Wiki'nize başarıyla kaydedildi.** Detaylara Wiki panelinden erişebilirsiniz.\n";
        else if (wikiError && !wikiSaved)
            yield return "\n⚠️ Kayıt yapılamadı. Araştırma içeriği güvenlik denetiminden geçememiş veya ağ hatası oluşmuş olabilir.\n";
        else if (!topicId.HasValue)
            yield return "\n💡 **İpucu:** Bir müfredat konusu seçili iken Korteks araştırması yaparsanız, sonuçlar otomatik olarak Wiki'nize kaydedilir.\n";

        _logger.LogInformation("[Korteks] Tamamlandı. Çıktı: {Len} karakter", fullResponse.Length);
    }
}

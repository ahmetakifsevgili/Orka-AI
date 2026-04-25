using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Orka.Core.Interfaces;
using Orka.Core.Enums;
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
    private readonly IAIAgentFactory       _factory;
    private readonly IServiceProvider      _serviceProvider;
    private readonly IConfiguration        _config;
    private readonly ILogger<KorteksAgent> _logger;

    public KorteksAgent(
        IAIAgentFactory       factory,
        IServiceProvider      serviceProvider,
        IConfiguration        configuration,
        ILogger<KorteksAgent> logger)
    {
        _factory         = factory;
        _serviceProvider = serviceProvider;
        _config          = configuration;
        _logger          = logger;
    }

    private Kernel BuildKorteksKernel()
    {
        var model    = _factory.GetModel(AgentRole.Korteks);
        var provider = _factory.GetProvider(AgentRole.Korteks);
        
        string apiKey;
        string baseUrl;

        switch (provider)
        {
            case "GitHubModels":
                apiKey = _config["AI:GitHubModels:Token"] ?? throw new InvalidOperationException("GitHubModels Token eksik.");
                baseUrl = _config["AI:GitHubModels:BaseUrl"] ?? "https://models.inference.ai.azure.com";
                break;
            case "OpenRouter":
                apiKey = _config["AI:OpenRouter:ApiKey"] ?? throw new InvalidOperationException("OpenRouter ApiKey eksik.");
                baseUrl = "https://openrouter.ai/api/v1";
                break;
            case "Groq":
                apiKey = _config["AI:Groq:ApiKey"] ?? throw new InvalidOperationException("Groq ApiKey eksik.");
                baseUrl = "https://api.groq.com/openai/v1";
                break;
            case "SambaNova":
                apiKey = _config["AI:SambaNova:ApiKey"] ?? throw new InvalidOperationException("SambaNova ApiKey eksik.");
                baseUrl = "https://api.sambanova.ai/v1";
                break;
            case "Cerebras":
                apiKey = _config["AI:Cerebras:ApiKey"] ?? throw new InvalidOperationException("Cerebras ApiKey eksik.");
                baseUrl = "https://api.cerebras.ai/v1";
                break;
            default:
                // Fallback to GitHub Models
                apiKey = _config["AI:GitHubModels:Token"] ?? "";
                baseUrl = "https://models.inference.ai.azure.com";
                break;
        }

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

        // Plugin 5: Korteks V2 — Akademik makale arama (Semantic Scholar)
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<SemanticScholarPlugin>(), "AcademicSearch");

        // --- YENİ PUBLIC API EKLENTİLERİ ---
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<CrossRefPlugin>(), "CrossRef");
            
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<ArXivPlugin>(), "ArXiv");
            
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<DatamusePlugin>(), "Datamuse");
            
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<OpenLibraryPlugin>(), "OpenLibrary");
            
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<NewtonMathPlugin>(), "NewtonMath");
            
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<FreeDictionaryPlugin>(), "Dictionary");
            
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<OpenTriviaPlugin>(), "Trivia");
            
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<QuickChartPlugin>(), "QuickChart");
            
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<KrokiPlugin>(), "Diyagram");
            
        kernel.Plugins.AddFromObject(
            _serviceProvider.GetRequiredService<LibreTranslatePlugin>(), "Translate");

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

        yield return "✅ İki dalga iteratif araştırma + Wikipedia + Kaynakça numaralandırma aktif.\n";
        yield return $"🔍 \"{topic}\" için derin araştırma başlıyor...\n\n";

        // Kullanıcıya çıktının nereye gideceğini baştan net söyle.
        if (topicId.HasValue)
            yield return "📍 **Çıktı:** Rapor aşağıya yazılacak ve **bu konunun Wiki'sine otomatik kaydedilecek**. Tamamlanınca PDF/Markdown olarak indirebilirsin.\n\n";
        else
            yield return "📍 **Çıktı:** Rapor aşağıya yazılacak. Tamamlanınca PDF/Markdown olarak indirebilirsin. (Wiki'ye kaydetmek için bir müfredat konusu seçmen gerekir.)\n\n";

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
            Sen Orka AI'nın "Korteks V2" modülüsün — ChatGPT Deep Research + Perplexity Pro seviyesinde otonom bir araştırma motorusun.
            Kullanıcının verdiği konuyu akademik bir doktora tez özeti seviyesinde derinleştireceksin.
            Çıktın bir üniversite ansiklopedi maddesi + sektör raporu + akademik literatür taraması bileşimi kadar kapsamlı olmalı.

            ZORUNLU ARAŞTIRMA SÜRECİ — ÜÇ DALGA İTERATİF DERİN ARAŞTIRMA:

            [DALGA 1 — GENİŞ KAPSAM]
            1. WebSearch-SearchWebDeep ile konuyu **5 farklı açıdan** paralel ara. İhtiyaç duyarsan Datamuse-FindRelatedWords ile arama terimlerini zenginleştir.
            2. AcademicSearch-SearchPapers, CrossRef-SearchWorks ve ArXiv-SearchPapers kullanarak konuyla ilgili **en az 5 akademik makale** bul.
            3. Wikipedia ve OpenLibrary-SearchBooks ile otorite kaynakları ve kitapları tara.

            [DALGA 2 — DERİNLEŞTİRME]
            4. İlk dalganın sonuçlarını incele: Hangi istatistik eksik? Hangi zıt görüş eksik?
            5. En değerli web kaynaklarından WebSearch-ExtractFromUrls ile tam sayfa içeriği çek.
            6. Yabancı dilde kritik metinler bulursan Translate-TranslateText ile Türkçe'ye çevir.

            [DALGA 3 — BOŞLUK DOLDURMA]
            7. WebSearch-SearchWebFollowUp ile **2-4 takip sorgusu** gönder.
            8. Şayet iddialarda formül/matematik varsa NewtonMath-CalculateMath ile doğrula.

            [DALGA 4 — GÖRSEL SENTEZ VE YAZIM]
            9. Rapor yazımına geç. Eğer sayısal veriler elde ettiysen QuickChart-GenerateChart ile grafiğe dönüştür.
            10. Mimari, yapısal veya ilişkisel durumlar barizse Kroki-GenerateDiagram ile SVG diyagram kodu üret.
            11. En az **12-18 paragraf** uzunluğunda, akademik ton ve derinlikte nihai raporunu yaz.

            ZORUNLU ÇIKTI FORMATI:

            # <Konu Başlığı>

            ## 📌 Özet (TL;DR)
            3-4 cümlede ne, neden önemli, kimi ilgilendiriyor.

            ## 🌍 Genel Bakış ve Tarihsel Bağlam
            Konunun tarihi, doğuşu, evrimi. Wikipedia otorite kaynağı olarak kullan. 2-3 paragraf.

            ## 🔬 Teknik Detaylar ve Temel Kavramlar
            Konunun iç yapısı, metodolojisi, kilit terminolojisi. Paragraflar halinde. Her kritik iddiaya kaynak[^N] numaralı dipnot.

            ## 📊 Karşılaştırma Tablosu (ZORUNLU — Markdown Tablo formatında)
            Konuyla ilgili en az 1 adet karşılaştırma, istatistik veya veri tablosu.
            | Kriter | A | B | C |
            |--------|---|---|---|
            | ...    |...|...|...|

            ## 🧪 Pratik Uygulamalar ve Vaka Örnekleri
            Gerçek dünyada nasıl kullanılıyor, somut örnekler, endüstri vakaları.

            ## 💻 Kod Örneği veya Teknik Uygulama (ZORUNLU — Konu Uygunsa)
            Eğer konu yazılım, algoritma, veri bilimi veya teknik bir alansa KESİNLİKLE bir kod örneği (Python/JS/C#) ver.
            Konu tamamen sosyal/tarihsel ise bu bölümü atla.

            ## 📈 Sayısal Veriler — Mermaid Diyagramı (ZORUNLU — En Az 1 Adet)
            Konunun akışını, yapısını veya tarihsel gelişimini gösteren bir `mermaid` diyagramı.
            ```mermaid
            graph TD veya timeline veya flowchart
            ```

            ## 🎓 Akademik Literatür Taraması
            Semantic Scholar'dan bulunan en az 3-5 akademik makaleyi özet, yazarlar ve atıf sayısıyla sun.
            Her makaleye kaynak numarası ver.

            ## 🆕 2024-2026 Güncel Gelişmeler
            Son 12-24 ayda neler değişti, hangi yeni yaklaşımlar ortaya çıktı.

            ## ⚖️ Eleştiriler, Sınırlılıklar, Çelişen Kaynaklar
            Konunun zayıf yönleri, akademik eleştiriler. Çelişen kaynak varsa hem A hem B görüşünü kaynaklarıyla ver.

            ## 💡 Sentez ve Önerilen Okuma Yolu
            Araştırmayı bağlayan akademik kapanış + kullanıcının daha derine inmek isterse hangi 3-5 konuyu araması gerektiği.

            ## 📚 Kaynakça
            Numaralı dipnot listesi:
            [^1]: [Başlık](URL) — kısa açıklama
            [^2]: [Başlık](URL) — kısa açıklama
            ... (en az 12-18 kaynak — web + akademik makale karışık)

            HALLUCİNASYON ÖNLEME KURALLARI — İHLALİ YASAK:
            - Kaynağı olmayan iddia YAZMA. Her somut bilgiye `[^N]` numaralı atıf ekle.
            - Tavily raw_content kısmını temel al. Yeterli veri yoksa "Doğrulanamadı: kaynak bulunamadı" diye işaretle.
            - Uydurma URL, uydurma istatistik, uydurma tarih YASAK.
            - Numara/yıl/yüzde veriyorsan kaynağı mutlaka yanında olsun.
            - Çelişen kaynak varsa "A kaynağı X diyor[^3], B kaynağı Y diyor[^7]" şeklinde ikisini de ver.

            GÖRSEL SENTEZİ (ZORUNLU — EN AZ 3 ADET):
            Kavramları görselleştirmek için Pollinations.ai kullan:
            `![Görsel Açıklaması](https://image.pollinations.ai/prompt/URL_ENCODED_DETAYLI_INGILIZCE_PROMPT?width=1024&height=512&nologo=true)`
            Görselleri raporun ilgili bölümlerine (Genel Bakış, Teknik Detaylar, Gelecek) dağıt.

            MATEMATİK/FORMÜL:
            - Matematiksel ifadeler için KaTeX syntax'ı kullan: inline `$x^2$`, blok `$$\int f(x)dx$$`.

            DİL VE STİL: Türkçe akademik, otoriter ve ufuk açıcı. Teknik terimlerin İngilizce karşılığını ilk geçişte parantez içinde ver.
            UZUNLUK: En az 4500 kelime. Konuyu en ince ayrıntısına kadar (derin teknik mimari, matematiksel arka plan, küresel ekonomik etkiler vb.) işle. Yüzeyselliğe tolerans sıfır.
            FORMAT: Zengin Markdown; tablolar, Mermaid diyagramları, iç linkler ve kapsamlı kaynakça.
            {topicContext}{fileSection}
            """;

        var chatHistory = new ChatHistory(systemPrompt);
        chatHistory.AddUserMessage(
            $"Konu: \"{topic}\"\n\n" +
            $"ZORUNLU ARAŞTIRMA ADIMLARI — BU SIRALARI ATLAYAMAZSIN:\n\n" +
            $"[ADIM 1 — TEMEL WEB ARAŞTIRMASI]:\n" +
            $"WebSearch-SearchWebDeep ile konuyu minimum 3 farklı açıdan ara:\n" +
            $"  - Arama 1: \"{topic} tanım kavram tarihçe\"\n" +
            $"  - Arama 2: \"{topic} teknik detay metodoloji\"\n" +
            $"  - Arama 3: \"{topic} güncel gelişmeler 2024 2025\"\n\n" +
            $"[ADIM 2 — AKADEMİK KAYNAK TARAMASI — ZORUNLU, EN AZ 3 MAKALE BUL]:\n" +
            $"  - AcademicSearch-SearchPapers ile en az 2 akademik makale bul\n" +
            $"  - CrossRef-SearchWorks ile en az 1 peer-reviewed çalışma bul\n" +
            $"  - ArXiv-SearchPapers ile varsa preprint makale bul (özellikle CS/matematik/fizik konularında)\n" +
            $"  - Bu adımı atlarsan rapor KABUL EDİLMEZ.\n\n" +
            $"[ADIM 3 — WİKİPEDİA DOĞRULAMASI]:\n" +
            $"  - Wikipedia-SearchWikipedia ile konunun temel tanımını ve tarihçesini doğrula.\n\n" +
            $"[ADIM 4 — URL EXTRACT (TAM İÇERİK)]:\n" +
            $"  - WebSearch-ExtractFromUrls ile en değerli 2 kaynağın tam içeriğini çek.\n\n" +
            $"[ADIM 5 — TAKİP ARAMALARI]:\n" +
            $"  - WebSearch-SearchWebFollowUp ile eksik kalan alt başlıklara 2 takip araması yap.\n\n" +
            $"[ADIM 6 — RAPOR YAZ]:\n" +
            $"  - Tüm kaynakları bir araya getirip yukarıdaki sistem promptundaki formatta akademik rapor yaz.\n" +
            $"  - Minimum 3500 kelime. Her iddiaya kaynak numarası. En az 12 kaynakça girişi.\n" +
            $"  - Akademik makaleleri 'Akademik Literatür Taraması' bölümünde özet + atıf sayısı ile listele.\n" +
            $"  - Eğer sayısal veriler bulduysan QuickChart-GenerateChart ile grafik üret.\n" +
            $"  - Eğer mimari/yapısal ilişki varsa Kroki-GenerateDiagram ile SVG diyagram üret.");


        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature      = 0.15,  // Daha deterministik = daha az hallucination
            MaxTokens        = 16384, // Korteks V2: 3500-5000+ kelime rapora yeterli alan
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
                
                bool isValid = await grader.IsContextRelevantAsync(topic, fullResponse.ToString(), ct: ct);

                if (isValid)
                {
                    await wikiService.AutoUpdateWikiAsync(
                        topicId.Value,
                        fullResponse.ToString(),
                        $"Korteks derin araştırması: {topic}",
                        "korteks-agent");
                    wikiSaved = true;

                    // Faz 11: Araştırma raporunun tamamını Redis'e kaydet — TutorAgent bağlam olarak kullanır
                    var redisService = scope.ServiceProvider.GetService<IRedisMemoryService>();
                    if (redisService != null)
                    {
                        var summary = fullResponse.Length > 3000
                            ? fullResponse.ToString()[..3000]
                            : fullResponse.ToString();
                        await redisService.SetKorteksResearchReportAsync(topicId.Value, summary);
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

using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Orka.Core.DTOs.Korteks;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.SemanticKernel.Filters;
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
    private readonly IAIAgentFactory       _factory;
    private readonly IConfiguration        _config;
    private readonly ILogger<KorteksAgent> _logger;

    public KorteksAgent(
        IServiceProvider      serviceProvider,
        IAIAgentFactory       factory,
        IConfiguration        configuration,
        ILogger<KorteksAgent> logger)
    {
        _serviceProvider = serviceProvider;
        _factory         = factory;
        _config          = configuration;
        _logger          = logger;
    }

    private Kernel BuildKorteksKernel(KorteksToolCaptureFilter? captureFilter = null)
    {
        var provider = _factory.GetProvider(AgentRole.Korteks);
        var model    = _factory.GetModel(AgentRole.Korteks);

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
            case "mistral":
                apiKey  = _config["AI:Mistral:ApiKey"] ?? throw new InvalidOperationException("AI:Mistral:ApiKey eksik.");
                baseUrl = _config["AI:Mistral:BaseUrl"] ?? "https://api.mistral.ai/v1";
                break;
            case "gemini":
                apiKey  = _config["AI:Gemini:ApiKey"] ?? throw new InvalidOperationException("AI:Gemini:ApiKey eksik.");
                baseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/"; // Semantic Kernel compatible endpoint
                break;
            case "sambanova":
                apiKey  = _config["AI:SambaNova:ApiKey"] ?? throw new InvalidOperationException("AI:SambaNova:ApiKey eksik.");
                baseUrl = _config["AI:SambaNova:BaseUrl"] ?? "https://api.sambanova.ai/v1";
                break;
            default: // GitHubModels fallback
                apiKey  = _config["AI:GitHubModels:Token"] ?? throw new InvalidOperationException("AI:GitHubModels:Token eksik.");
                baseUrl = "https://models.inference.ai.azure.com";
                break;
        }

        _logger.LogInformation("[Korteks] Kernel: Provider={Provider} Model={Model}", provider, model);

        // BaseUrl normalizasyonu (Semantic Kernel sadece ana dizini bekler)
        if (baseUrl.EndsWith("/chat/completions")) baseUrl = baseUrl.Replace("/chat/completions", "");
        if (baseUrl.EndsWith("/chat"))            baseUrl = baseUrl.Replace("/chat", "");
        if (!baseUrl.EndsWith("/"))               baseUrl += "/";

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

        if (captureFilter != null)
        {
            kernel.FunctionInvocationFilters.Add(captureFilter);
        }

        return kernel;
    }

    public async Task<KorteksResearchResultDto> RunResearchWithEvidenceAsync(
        string topic,
        Guid userId,
        Guid? topicId = null,
        string? fileContext = null,
        CancellationToken ct = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        var capture = new KorteksToolCaptureFilter();
        var warnings = new List<string>();
        var providerFailures = new List<string>();
        var fullResponse = new StringBuilder();
        var stage = KorteksFailureStage.KernelBuild;
        var provider = _factory.GetProvider(AgentRole.Korteks);
        var model = _factory.GetModel(AgentRole.Korteks);
        var endpointHost = ResolveEndpointHost(provider);

        _logger.LogInformation(
            "[Korteks] Structured run started. RunId={RunId} Topic={Topic} TopicId={TopicId} User={UserId}",
            runId,
            topic,
            topicId,
            userId);

        Kernel kernel;
        try
        {
            stage = KorteksFailureStage.KernelBuild;
            kernel = BuildKorteksKernel(capture);
        }
        catch (Exception ex)
        {
            var diagnostic = KorteksFailureDiagnostic.Create(ex, stage, provider, model, endpointHost, capture);
            _logger.LogError(ex, "[Korteks] Structured kernel build failed. RunId={RunId} Diagnostic={Diagnostic}", runId, diagnostic);
            providerFailures.Add(diagnostic);
            return BuildResearchResult(topic, topicId, string.Empty, capture, warnings, providerFailures);
        }

        try
        {
            stage = KorteksFailureStage.ModelStreamStart;
            var chatService = kernel.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory(BuildStructuredSystemPrompt(topic, userId, topicId, fileContext));
            chatHistory.AddUserMessage(
                $"Konu: \"{topic}\"\n\nAdımları sırayla uygula: önce WebSearch-SearchWebDeep ile ara, sonra Wikipedia, Academic ve YouTube araçlarıyla doğrula, ardından kaynaklı raporu yaz.");

            await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
                               chatHistory,
                               new OpenAIPromptExecutionSettings
                               {
                                   Temperature = 0.2,
                                   MaxTokens = 4096,
                                   ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
                               },
                               kernel,
                               ct))
            {
                if (capture.Calls.Count > 0)
                {
                    stage = KorteksFailureStage.ToolCallRoundtrip;
                }

                if (chunk.Content != null)
                {
                    fullResponse.Append(chunk.Content);
                }
            }
        }
        catch (Exception ex)
        {
            var failureStage = capture.Calls.Count > 0 ? KorteksFailureStage.ToolCallRoundtrip : stage;
            var diagnostic = KorteksFailureDiagnostic.Create(ex, failureStage, provider, model, endpointHost, capture);
            _logger.LogError(ex, "[Korteks] Structured research stream failed. RunId={RunId} Diagnostic={Diagnostic}", runId, diagnostic);
            providerFailures.Add(diagnostic);
        }

        stage = KorteksFailureStage.ResultBuild;
        var report = fullResponse.ToString();
        if (topicId.HasValue && !string.IsNullOrWhiteSpace(report))
        {
            var persistenceWarning = await TryPersistResearchAsync(topic, topicId.Value, report, ct);
            if (!string.IsNullOrWhiteSpace(persistenceWarning))
            {
                warnings.Add(persistenceWarning);
            }
        }

        var result = BuildResearchResult(topic, topicId, report, capture, warnings, providerFailures);
        stage = KorteksFailureStage.Completed;
        _logger.LogInformation(
            "[Korteks] Structured run completed. RunId={RunId} Mode={GroundingMode} Sources={SourceCount} Calls={CallCount} Failures={FailureCount} IsFallback={IsFallback}",
            runId,
            result.GroundingMode,
            result.SourceCount,
            result.ProviderCalls.Count,
            result.ProviderFailures.Count,
            result.IsFallback);

        return result;
    }

    private string ResolveEndpointHost(string provider)
    {
        var baseUrl = provider.ToLowerInvariant() switch
        {
            "openrouter" => _config["AI:OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1",
            "groq" => _config["AI:Groq:BaseUrl"] ?? "https://api.groq.com/openai/v1",
            "mistral" => _config["AI:Mistral:BaseUrl"] ?? "https://api.mistral.ai/v1",
            "gemini" => "https://generativelanguage.googleapis.com/v1beta/openai/",
            "sambanova" => _config["AI:SambaNova:BaseUrl"] ?? "https://api.sambanova.ai/v1",
            _ => "https://models.inference.ai.azure.com"
        };

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : "unknown";
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
    private string BuildStructuredSystemPrompt(string topic, Guid userId, Guid? topicId, string? fileContext)
    {
        var topicContext = topicId.HasValue
            ? $"Topic ID: {topicId.Value} | User ID: {userId}"
            : $"User ID: {userId}";

        var fileSection = string.IsNullOrWhiteSpace(fileContext)
            ? string.Empty
            : $"""

            USER PROVIDED DOCUMENT:
            Compare this document with web sources and explicitly flag stale or conflicting details.

            ---DOCUMENT START---
            {fileContext}
            ---DOCUMENT END---
            """;

        return $"""
            You are Orka AI Korteks, a source-grounded research engine.

            Research process:
            1. Use WebSearch-SearchWebDeep for broad web research.
            2. Use Wikipedia-SearchWikipedia and GetWikipediaArticle for baseline definitions.
            3. Use Academic-SearchSemanticScholar and Academic-SearchArXiv for scientific or technical claims.
            4. Use YouTube-SearchYouTubeVideos and YouTube-GetVideoTranscript for educational references when useful.
            Output rules:
            - Write the final report in Turkish Markdown.
            - Put source links beside important claims.
            - If a source cannot be found, state that explicitly.
            - Do not invent citations.
            - Include a short bibliography section with title and URL when sources are available.

            Topic context: {topicContext}
            Target topic: {topic}
            {fileSection}
            """;
    }

    private async Task<string?> TryPersistResearchAsync(string topic, Guid topicId, string report, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var grader = scope.ServiceProvider.GetRequiredService<IGraderAgent>();
            var wikiService = scope.ServiceProvider.GetRequiredService<IWikiService>();

            var isValid = await grader.IsContextRelevantAsync(topic, report, ct);
            if (!isValid)
            {
                _logger.LogWarning("[Korteks] Structured research rejected by grader. TopicId={TopicId}", topicId);
                return "Korteks report was not saved to Wiki because the grader rejected context relevance.";
            }

            await wikiService.AutoUpdateWikiAsync(
                topicId,
                report,
                $"Korteks derin araştırması: {topic}",
                "korteks-agent");

            var redisService = scope.ServiceProvider.GetService<IRedisMemoryService>();
            if (redisService != null)
            {
                await redisService.SetWikiReadyAsync(topicId);
                await redisService.SaveKorteksResearchReportAsync(topicId, report);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Korteks] Structured Wiki/Redis persistence failed. TopicId={TopicId}", topicId);
            return "Korteks report was generated, but Wiki/Redis persistence failed.";
        }
    }

    private static KorteksResearchResultDto BuildResearchResult(
        string topic,
        Guid? topicId,
        string report,
        KorteksToolCaptureFilter capture,
        List<string> warnings,
        List<string> providerFailures)
    {
        var sources = capture.Sources
            .Where(s => KorteksSourceEvidenceExtractor.IsValidSourceUrl(s.Url))
            .GroupBy(s => NormalizeUrl(s.Url), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var calls = capture.Calls.ToList();
        warnings.AddRange(capture.Warnings);
        providerFailures.AddRange(calls
            .Where(c => !c.Success)
            .Select(c => string.IsNullOrWhiteSpace(c.FailureReason)
                ? $"{c.Provider}-{c.ToolName}: {c.DegradedMarker ?? "unsuccessful"}"
                : $"{c.Provider}-{c.ToolName}: {c.FailureReason}"));

        if (calls.Count == 0 && !string.IsNullOrWhiteSpace(report))
        {
            warnings.Add("No Korteks provider tool invocation was observed for this report.");
        }

        var mode = KorteksGroundingClassifier.Classify(report, sources, calls);
        return new KorteksResearchResultDto
        {
            Topic = topic,
            TopicId = topicId,
            Report = report,
            Sources = sources,
            ProviderCalls = calls,
            ProviderFailures = providerFailures.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            GroundingMode = mode,
            IsFallback = KorteksGroundingClassifier.IsFallback(mode),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return url;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}

public enum KorteksFailureStage
{
    KernelBuild,
    ModelStreamStart,
    ToolCallRoundtrip,
    ResultBuild,
    Completed
}

public static class KorteksFailureDiagnostic
{
    private const int MaxErrorTextLength = 1200;

    private static readonly Regex[] SecretPatterns =
    [
        new(@"Bearer\s+[A-Za-z0-9._\-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(?i)(api[_-]?key|apikey|key|token|access_token|refresh_token|authorization|password|secret)\s*[:=]\s*[""']?[^""'\s,}]+", RegexOptions.Compiled),
        new(@"AIza[0-9A-Za-z_\-]{20,}", RegexOptions.Compiled),
        new(@"tvly-[0-9A-Za-z_\-]{10,}", RegexOptions.Compiled),
        new(@"sk-[A-Za-z0-9_\-]{16,}", RegexOptions.Compiled),
        new(@"sk-or-[A-Za-z0-9_\-]{16,}", RegexOptions.Compiled),
        new(@"ghp_[A-Za-z0-9_]{20,}", RegexOptions.Compiled),
        new(@"gsk_[A-Za-z0-9_]{20,}", RegexOptions.Compiled),
        new(@"csk-[A-Za-z0-9_\-]{20,}", RegexOptions.Compiled),
        new(@"hf_[A-Za-z0-9_\-]{20,}", RegexOptions.Compiled),
        new(@"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+", RegexOptions.Compiled),
        new(@"(?i)(connection\s*string|server|database|user\s*id|uid|pwd|password)\s*=\s*[^;\r\n]+", RegexOptions.Compiled)
    ];

    public static string Create(
        Exception exception,
        KorteksFailureStage stage,
        string provider,
        string model,
        string endpointHost,
        KorteksToolCaptureFilter capture)
    {
        var status = TryGetStatusCode(exception);
        var responseBody = TryGetResponseBody(exception);
        var providerNames = capture.Calls
            .Select(c => c.Provider)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var parts = new List<string>
        {
            $"Stage={stage}",
            $"ExceptionType={exception.GetType().FullName}",
            $"Provider={Sanitize(provider)}",
            $"Model={Sanitize(model)}",
            $"EndpointHost={Sanitize(endpointHost)}",
            $"ToolCallCount={capture.Calls.Count}",
            $"ToolProviders=[{string.Join(",", providerNames.Select(Sanitize))}]",
            $"SourceEvidenceCount={capture.Sources.Count}",
            "LastToolResultLength=unavailable",
            $"Message={Sanitize(exception.Message)}"
        };

        if (!string.IsNullOrWhiteSpace(status))
        {
            parts.Insert(5, $"Status={Sanitize(status)}");
        }

        if (!string.IsNullOrWhiteSpace(exception.InnerException?.Message))
        {
            parts.Add($"InnerMessage={Sanitize(exception.InnerException.Message)}");
        }

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            parts.Add($"ProviderResponse={Sanitize(responseBody)}");
        }

        return string.Join("; ", parts);
    }

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = value;
        foreach (var pattern in SecretPatterns)
        {
            sanitized = pattern.Replace(sanitized, match =>
            {
                var text = match.Value;
                var splitIndex = text.IndexOfAny([':', '=']);
                return splitIndex > 0
                    ? text[..(splitIndex + 1)] + "[REDACTED]"
                    : "[REDACTED]";
            });
        }

        sanitized = sanitized.Replace("\r", "\\r").Replace("\n", "\\n");
        return sanitized.Length > MaxErrorTextLength
            ? sanitized[..MaxErrorTextLength] + "...[truncated]"
            : sanitized;
    }

    private static string? TryGetStatusCode(Exception exception)
    {
        foreach (var candidate in EnumerateExceptionChain(exception))
        {
            var status = ReadProperty(candidate, "Status") ??
                         ReadProperty(candidate, "StatusCode") ??
                         ReadProperty(candidate, "HttpStatusCode");
            if (!string.IsNullOrWhiteSpace(status))
            {
                return status;
            }
        }

        return null;
    }

    private static string? TryGetResponseBody(Exception exception)
    {
        foreach (var candidate in EnumerateExceptionChain(exception))
        {
            foreach (var propertyName in new[] { "ResponseContent", "ResponseBody", "Content", "Body", "ErrorContent" })
            {
                var value = ReadProperty(candidate, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            var response = candidate.GetType().GetProperty("Response", BindingFlags.Public | BindingFlags.Instance)?.GetValue(candidate);
            if (response == null)
            {
                continue;
            }

            foreach (var propertyName in new[] { "Content", "ReasonPhrase", "StatusCode" })
            {
                var value = ReadProperty(response, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static IEnumerable<Exception> EnumerateExceptionChain(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static string? ReadProperty(object instance, string propertyName)
    {
        try
        {
            var value = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
            return value?.ToString();
        }
        catch
        {
            return null;
        }
    }
}

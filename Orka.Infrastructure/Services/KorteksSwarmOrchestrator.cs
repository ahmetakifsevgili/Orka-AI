using System;
using System.Text;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.SemanticKernel.Plugins;

namespace Orka.Infrastructure.Services;

public class KorteksSwarmOrchestrator : IKorteksSwarmOrchestrator
{
    private readonly OrkaDbContext _db;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KorteksSwarmOrchestrator> _logger;

    public KorteksSwarmOrchestrator(
        OrkaDbContext db,
        IBackgroundJobClient backgroundJobClient,
        IServiceProvider serviceProvider,
        ILogger<KorteksSwarmOrchestrator> logger)
    {
        _db = db;
        _backgroundJobClient = backgroundJobClient;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<Guid> EnqueueResearchJobAsync(
        Guid userId,
        string query,
        Guid? topicId = null,
        string? documentContext = null,
        bool requiresWebSearch = true)
    {
        var job = new ResearchJob
        {
            UserId = userId,
            Query = query,
            TopicId = topicId,
            Phase = ResearchPhase.Queued,
            CreatedAt = DateTime.UtcNow,
            DocumentContext = documentContext,
            RequiresWebSearch = documentContext == null || requiresWebSearch, // Belge yoksa her zaman web'e çık
        };

        _db.ResearchJobs.Add(job);
        await _db.SaveChangesAsync();

        _backgroundJobClient.Enqueue(() => ExecuteResearchJobAsync(job.Id));

        return job.Id;
    }

    public async Task<ResearchJob?> GetJobStatusAsync(Guid jobId)
    {
        return await _db.ResearchJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId);
    }

    public async Task<IEnumerable<ResearchJob>> GetUserLibraryAsync(Guid userId, int take = 20)
    {
        return await _db.ResearchJobs
            .AsNoTracking()
            .Where(j => j.UserId == userId && j.Phase == ResearchPhase.Completed)
            .OrderByDescending(j => j.CompletedAt)
            .Take(take)
            .ToListAsync();
    }

    [AutomaticRetry(Attempts = 0)] // Don't retry automatically during development
    public async Task ExecuteResearchJobAsync(Guid jobId)
    {
        // Scope must be created so we don't hold the DB Context captive
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IAIAgentFactory>();
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var job = await db.ResearchJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        if (job == null) return;

        try
        {
            await UpdatePhaseAsync(db, job, ResearchPhase.ManagerPlanning, "Araştırma stratejisi planlanıyor (V2 — Çok Dalgalı)...");

            // 1) Manager Agent V2: Konuyu 5-7 alt-soruya ayrıştır + arama terimleri
            var planPrompt = $"""
                Kullanıcı isteği: '{job.Query}'.
                Bu konuyu kapsamlı araştırmak için 5-7 adet alt-soru ve her biri için İngilizce arama terimi üret.
                Format (her satır): ALT_SORU | ARAMA_TERİMİ
                Sadece listeyi yaz, açıklama yapma.
                """;
            var searchTermsRaw = await factory.CompleteChatAsync(Core.Enums.AgentRole.Supervisor, "Sen bir araştırma stratejistisin. Konuyu derinlemesine araştırmak için onu alt-sorulara bölersin.", planPrompt);
            await UpdatePhaseAsync(db, job, ResearchPhase.DataFetching, $"Dalga 1: Geniş kapsam taraması başlıyor...\n{searchTermsRaw}");

            // 2) Data Fetcher: Seçim job'ın konfigürasyonuna göre yapılır
            string rawData;
            if (!string.IsNullOrWhiteSpace(job.DocumentContext) && !job.RequiresWebSearch)
            {
                // RAG modu: sadece belge analizi, internete çıkılmaz
                await UpdatePhaseAsync(db, job, ResearchPhase.DataFetching, "Belge analiz ediliyor (RAG modu)...");
                rawData = "## Kullanıcı Belgesi\n\n"
                        + job.DocumentContext
                        + "\n\nKullanıcı İsteği: " + job.Query;
            }
            else if (!string.IsNullOrWhiteSpace(job.DocumentContext) && job.RequiresWebSearch)
            {
                // Hibrit mod: Web araştırması YAP + belgeyle karşılaştır
                await UpdatePhaseAsync(db, job, ResearchPhase.DataFetching, $"Dalga 1: Web taraması + belge ön analizi yapılıyor...");
                var hSearchHistory = new ChatHistory("Sen bir Veri Toplayıcı ajansın. Belirtilen konuda Web, Wikipedia ve Akademik Makale araçlarını kullanarak ham veriyi bul.");
                hSearchHistory.AddUserMessage($"Aşağıdaki başlıklarda detaylı araştırma yap: {searchTermsRaw}");

                var hFetchSettings = new OpenAIPromptExecutionSettings { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions };
                var hFetchResult = await chatService.GetChatMessageContentAsync(hSearchHistory, hFetchSettings, kernel);
                var webData = hFetchResult.Content ?? "Arama sonuçları boş döndü.";

                rawData = "## Kullanıcı Belgesi\n\n"
                        + job.DocumentContext
                        + "\n\n## İnternet Araştırması (Teyit ve Zenginleştirme)\n\n"
                        + webData
                        + "\n\nKullanıcı İsteği: " + job.Query;
            }
            else
            {
                // Standart web modu: belge yok — V2 Çok Dalgalı Araştırma
                // Dalga 1: Geniş kapsam
                await UpdatePhaseAsync(db, job, ResearchPhase.DataFetching, "Dalga 1: Web + Akademik arama yapılıyor...");
                var sSearchHistory = new ChatHistory(
                    "Sen bir Veri Toplayıcı ajansın. Belirtilen konuda Web araması, Wikipedia ve Akademik Makale (Semantic Scholar) araçlarını kullanarak ham veriyi bul. " +
                    "Özetleme, tüm bulguları aktar. ExtractFromUrls ile en değerli kaynakların tam içeriğini çek.");
                sSearchHistory.AddUserMessage($"Aşağıdaki başlıklarda detaylı araştırma yap: {searchTermsRaw}");

                var sFetchSettings = new OpenAIPromptExecutionSettings { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions, MaxTokens = 8192 };
                var sFetchResult = await chatService.GetChatMessageContentAsync(sSearchHistory, sFetchSettings, kernel);
                var wave1Data = sFetchResult.Content ?? "Bütün arama sonuçları boş döndü.";

                // Dalga 2: Refleksiyon — Eksik tespiti
                await UpdatePhaseAsync(db, job, ResearchPhase.DataFetching, "Dalga 2: Refleksiyon — Eksik ve boşluklar tespit ediliyor...");
                var reflectionPrompt = $"""
                    Aşağıdaki ham araştırma verilerini oku. Kullanıcının sorusu: "{job.Query}"

                    HAM VERİLER:
                    {(wave1Data.Length > 8000 ? wave1Data[..8000] : wave1Data)}

                    Şimdi şu soruları cevapla:
                    1. Hangi alt-konular hâlâ yüzeysel veya cevapsız?
                    2. Hangi iddialarda çelişen bilgi var?
                    3. Hangi sayısal veri, istatistik veya tablo eksik?
                    4. Bu eksikleri doldurmak için 3-4 spesifik takip araması öner (terimi yaz).

                    Kısa ve öz yanıt ver.
                    """;
                var reflectionResult = await factory.CompleteChatAsync(Core.Enums.AgentRole.Supervisor, "Sen bir araştırma kalite denetçisisin.", reflectionPrompt);

                // Dalga 3: Boşluk doldurma aramaları
                await UpdatePhaseAsync(db, job, ResearchPhase.DataFetching, $"Dalga 3: Boşluk doldurma aramaları yapılıyor...\n{reflectionResult}");
                var followUpHistory = new ChatHistory("Sen bir takip araştırmacısın. Eksik kalan konularda ek araştırma yap. Web araması ve ExtractFromUrls kullan.");
                followUpHistory.AddUserMessage($"Eksik tespiti:\n{reflectionResult}\n\nBu eksikleri doldurmak için gerekli araştırmaları yap.");

                var followUpSettings = new OpenAIPromptExecutionSettings { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions, MaxTokens = 4096 };
                var followUpResult = await chatService.GetChatMessageContentAsync(followUpHistory, followUpSettings, kernel);

                rawData = $"## DALGA 1 — Geniş Kapsam Verileri\n\n{wave1Data}\n\n## DALGA 2 — Refleksiyon Notları\n\n{reflectionResult}\n\n## DALGA 3 — Boşluk Doldurma Verileri\n\n{(followUpResult.Content ?? "")}";
            }

            // 2.5) Analyst Agent: Veri Doğrulama ve Yapılandırma
            await UpdatePhaseAsync(db, job, ResearchPhase.Synthesizing, "Ham veriler Analist ajan tarafından inceleniyor, çelişkiler giderilip yapılandırılıyor...");
            var analystPrompt = $"""
                Aşağıdaki ham araştırma verilerini incele. Görevin, bunu "Akademik Editör" ajanına hazırlamaktır.
                1. Bilgi kirliliğini ve tekrarları temizle.
                2. Çelişen iddialar varsa, her iki tarafı da belirten akademik bir tablo formatında özetle.
                3. Çıkarılan verileri mantıksal başlıklara ayır.
                4. Kaynakça taslağını oluştur.
                
                HAM VERİLER:
                {rawData}
                """;
            var structuredData = await factory.CompleteChatAsync(Core.Enums.AgentRole.Grader, "Sen titiz bir Akademik Analist ve Doğrulayıcısın.", analystPrompt);

            // 3) Editor Agent V3 (Thesis Level): Write Comprehensive Report
            var isDocBased = !string.IsNullOrWhiteSpace(job.DocumentContext);
            await UpdatePhaseAsync(db, job, ResearchPhase.Synthesizing,
                isDocBased ? "Belge ve veriler tez seviyesinde araştırma raporuna dönüştürülüyor..." : "Tüm dalgalar ve analist yapılandırması sentezleniyor — Çok boyutlu devasa akademik rapor yazılıyor...");

            var editorSystemPrompt = """
                Sen 'Orka Deep Research Editor V3 (Tez Seviyesi)' ajanısın. 
                Görevin: Analist ajandan gelen yapılandırılmış verilerden yüksek lisans/tez kalitesinde, devasa bir araştırma raporu (Markdown) sentezlemek.

                ÖNEMLİ ZORUNLULUKLAR:
                1) KAYNAKÇA VE ATIF: Verilen bilgilerin nereden elde edildiğine atıf yapmalısın ([^1] gibi) ve en sona "Kaynakça" bölümü eklemelisin.
                2) GÖRSEL (ZORUNLU): Kavramları pekiştirmek için KESİNLİKLE en az 2 adet detaylı görsel üret. (Format: `![Alt Metin](https://image.pollinations.ai/prompt/{INGILIZCE_DETAYLI_PROMPT}?width=800&height=400&nologo=true)`)
                3) MERMAID DİYAGRAMLARI (ZORUNLU): En az 1 adet markdown `mermaid` bloğu ile akış diyagramı, mimari veya kavram haritası oluştur.
                4) KOD VEYA FORMÜL (ZORUNLU): Teknik/Bilimsel ise kod parçacığı (` ```python `), Sosyal/Sözel ise matematiksel veya mantıksal bir modelleme KESİNLİKLE ekle.
                5) KARŞILAŞTIRMA TABLOSU (ZORUNLU): En az 1 adet markdown tablo formatında detaylı veri tablosu oluştur.
                6) UZUNLUK VE DERİNLİK: Rapor 4000-5000 kelime arası olmalı. Yüzeyselliğe tolerans sıfırdır. Kısa raporlar KABUL EDİLMEZ.

                ZORUNLU BÖLÜMLER:
                - 📌 Özet (TL;DR)
                - 🌍 Genel Bakış ve Tarihsel Bağlam
                - 🔬 Teknik Detaylar ve Temel Kavramlar
                - 📊 Karşılaştırma Tablosu (Markdown)
                - 💻 Kod / Formül Örnekleri (Bağlama uygun şekilde)
                - 📈 Akış / Kavram Diyagramı (Mermaid bloğu)
                - 🎓 Akademik Literatür Taraması (Sentez)
                - 🧪 Pratik Uygulamalar ve Gelecek Projeksiyonları
                - ⚖️ Eleştiriler ve Sınırlılıklar
                - 💡 Sonuç ve Sentez
                - 📚 Kaynakça

                TÜRKÇE ve otoriter bir akademik ton kullan. Uydurma bilgi YASAK. Tüm bilgiler ham verilere dayanmalı.
                """;

            var editorPrompt = $"Yapılandırılmış Veriler (Analistten Gelen):\n{structuredData}\n\nBu verileri kullanarak profesyonel, kod, şema, tablo, görsel ve akademik atıflarla desteklenmiş kapsamlı tez seviyesi araştırma raporunu yaz.";
            var finalThesis = await factory.CompleteChatAsync(Core.Enums.AgentRole.Analyzer, editorSystemPrompt, editorPrompt);

            await UpdatePhaseAsync(db, job, ResearchPhase.Completed, "Araştırma makalesi tamamlandı.", finalThesis);

            // Araştırma raporu ResearchJob.FinalReport'ta saklanıyor — Korteks Kütüphanesi'nden erişilir.
            // Müfredat Wiki'sine YAZILMAZ: bu farklı bir bilgi yapısıdır.
            if (job.TopicId.HasValue)
            {
                var redisService = scope.ServiceProvider.GetService<IRedisMemoryService>();
                if (redisService != null)
                    await redisService.SetKorteksResearchReportAsync(job.TopicId.Value, finalThesis ?? "Araştırma başarıyla tamamlandı.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Korteks Swarm Job Error {JobId}", jobId);
            await UpdatePhaseAsync(db, job, ResearchPhase.Failed, $"Hata: {ex.Message}");
        }
    }

    private async Task UpdatePhaseAsync(OrkaDbContext db, ResearchJob job, ResearchPhase phase, string logMsg, string? finalReport = null)
    {
        job.Phase = phase;
        job.Logs += $"[{DateTime.UtcNow:HH:mm:ss}] {logMsg}\n";
        
        if (finalReport != null)
        {
            job.FinalReport = finalReport;
            job.CompletedAt = DateTime.UtcNow;
        }

        db.Update(job);
        await db.SaveChangesAsync();

        // Anlık SignalR Bildirimi (Fire and Forget mantığıyla)
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var notifier = scope.ServiceProvider.GetService<INotificationService>();
            if (notifier != null)
            {
                await notifier.NotifyJobPhaseUpdatedAsync(job.UserId, job.Id, job.Phase.ToString(), job.Logs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send real-time notification for job {JobId}", job.Id);
        }
    }
}

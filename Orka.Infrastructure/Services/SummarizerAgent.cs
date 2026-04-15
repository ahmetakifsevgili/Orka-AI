using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class SummarizerAgent : ISummarizerAgent
{
    // Aynı (sessionId:topicId) çifti için eş-zamanlı çift tetiklenmeyi önler
    private static readonly ConcurrentDictionary<string, byte> _inProgress
        = new ConcurrentDictionary<string, byte>();

    private readonly IAIAgentFactory _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SummarizerAgent> _logger;

    public SummarizerAgent(
        IAIAgentFactory factory,
        IServiceScopeFactory scopeFactory,
        ILogger<SummarizerAgent> logger)
    {
        _factory      = factory;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task SummarizeAndSaveWikiAsync(Guid sessionId, Guid topicId, Guid userId)
    {
        // ── Idempotency Guard: aynı çift için tek işlem çalışır ─────────────
        var lockKey = $"{sessionId}:{topicId}";
        if (!_inProgress.TryAdd(lockKey, 0))
        {
            _logger.LogInformation("[WikiCurator] Zaten işlemde — atlandı. Key={Key}", lockKey);
            return;
        }

        try
        {
            await SummarizeAndSaveWikiInternalAsync(sessionId, topicId, userId);
        }
        finally
        {
            _inProgress.TryRemove(lockKey, out _);
        }
    }

    private async Task SummarizeAndSaveWikiInternalAsync(Guid sessionId, Guid topicId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var wikiService = scope.ServiceProvider.GetRequiredService<IWikiService>();

        // ── DB-level idempotency: wiki zaten üretildiyse yeniden üretme ─────
        var alreadyGenerated = await db.WikiBlocks
            .AnyAsync(b => b.WikiPage.TopicId == topicId
                        && (b.Source == "Qwen-Wiki-Architect" || b.Source == "Groq-Curator"));
        if (alreadyGenerated)
        {
            _logger.LogInformation("[WikiCurator] Wiki zaten mevcut — yeniden üretim atlandı. TopicId={TopicId}", topicId);
            return;
        }

        var session = await db.Sessions.Include(s => s.Messages).FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null || !session.Messages.Any()) return;

        var topic = await db.Topics.FindAsync(topicId);
        var topicTitle = topic?.Title ?? "Bilinmeyen Konu";

        var history = string.Join("\n", session.Messages.OrderBy(m => m.CreatedAt).Select(m => $"{m.Role}: {m.Content}"));

        var systemPrompt = $$"""
            Sen Orka AI'nın en üst düzey 'Bilgi Mimarı ve Müfredat Küratörü (Deep Knowledge Architect)' botusun.
            Görevin: Verilen ham sohbet geçmişini, sadece bir özet olarak değil, "Master Content" niteliğinde profesyonel bir Wiki dokümanına dönüştürmek.

            İşleyeceğin Konu: {{topicTitle}}

            KALİTE VE FORMAT STANDARTLARI:
            - **Derinlik**: Kavramları sadece listeleme; her birinin 'neden' ve 'nasıl'ını açıkla. Teknik terimleri profesyonelce tanımla.
            - **Görsel Düzen (Markdown)**: 
                - Önemli karşılaştırmalar için mutlaka **Tablo** kullan.
                - Kritik uyarılar için blok alıntılar (Blockquotes) veya listeler kullan.
                - Kod örneklerini açıklayıcı yorum satırlarıyla zenginleştir.
                - Önemli terimleri **kalın** veya *italik* yaparak vurgula.
            - **Dil**: Akademik bir ciddiyetle, ancak öğretici (pedagogical) bir tonda yaz.

            WIKI SAYFA YAPISI:
            # 📚 {{topicTitle}}: Teknik İnceleme ve Uygulama Rehberi

            ## 1. 🔍 Kavramsal Temeller
            (Konuya derinlemesine giriş, tarihsel veya teknik bağlam, ana hedefler.)

            ---

            ## 2. 🧠 Mimari ve Teknik Detaylar
            (Seansta geçen teorik bilgilerin profesyonel sentezi. Bu bölümün en az 2-3 paragraf detaylı olmasını ve teknik tablolar içermesini sağla.)

            ---

            ## 3. 🛠️ Uygulama Örnekleri ve Analiz
            (Eğer kod veya pratik akış varsa: Kod bloğu + Satır bazlı teknik analiz.)

            ---

            ## 4. 💡 Kritik Stratejiler ve 'Best Practices'
            (Kaçınılması gereken hatalar, performans ipuçları ve endüstri standartları.)

            ---

            ## 5. 🏁 Sentez ve İleri Adımlar
            (Konu bütünlüğünü sağlayan güçlü bir kapanış paragrafı.)

            [UYARI]: Sadece Markdown formatında yanıt ver. Gereksiz giriş/çıkış cümleleri kullanma.
            """;

        var userPrompt = $"Sohbet Geçmişi:\n\n{history}";

        try
        {
            _logger.LogInformation("[WikiCurator] AIAgentFactory (Summarizer/{Model}) özetleme başladı. SessionId={SessionId}",
                _factory.GetModel(Orka.Core.Enums.AgentRole.Summarizer), sessionId);

            var summary = await _factory.CompleteChatAsync(Orka.Core.Enums.AgentRole.Summarizer, systemPrompt, userPrompt);

            session.Summary = summary;
            await wikiService.AutoUpdateWikiAsync(topicId, summary, "Otomatik Oluşturulan Kapsamlı Wiki", "GitHubModels-WikiArchitect");
            await db.SaveChangesAsync();

            _logger.LogInformation("[WikiCurator] Başarıyla Wiki'ye aktarıldı. Topic={Topic}", topicTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WikiCurator] Tüm sağlayıcılar başarısız. TopicId={TopicId}", topicId);
        }
    }
}

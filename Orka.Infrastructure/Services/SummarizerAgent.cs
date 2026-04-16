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
    private readonly IGraderAgent _grader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SummarizerAgent> _logger;

    public SummarizerAgent(
        IAIAgentFactory factory,
        IGraderAgent grader,
        IServiceScopeFactory scopeFactory,
        ILogger<SummarizerAgent> logger)
    {
        _factory      = factory;
        _grader       = grader;
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
            Sen Orka AI'nın 'Bilgi Özetleyici ve İçerik Hazırlayıcı'sın.
            Görev: Sohbet geçmişini, İLKOKUL'dan LİSE'ye kadar HER kesimden öğrencinin anlayabileceği,
            sade Türkçeyle ve görsel açıdan zengin bir Wiki sayfasına dönüştür.

            Konu: {{topicTitle}}

            HEDEF KİTLE & DİL:
            - Akademik değil, öğretici ve samimi bir dil kullan.
            - Teknik terimleri mutlaka Türkçe açıklamayla ver: "Algoritma (adım adım çözüm yolu)"
            - Karmaşık cümleler kurma; kısa, net ve akıcı paragraflar yaz.
            - Yaş grubuna göre anlayışlı örnekler ver (günlük hayattan benzetmeler iyi olur).

            WIKI SAYFA YAPISI:
            # {{topicTitle}} 📖

            ## 🎯 Bu Konuda Ne Öğrendik?
            (Konuyu hiç bilmeyen birine 3-4 cümleyle, sade bir dille anlat.)

            ---

            ## 🧠 Temel Kavramlar
            (Konunun önemli kavramlarını her biri için 2-3 cümle, basit dil, mümkünse 🔹 gibi emojilerle listele.)

            ---

            ## 💡 Gerçek Hayattan Örnekler
            (Soyut kavramları somutlaştır. "Örneğin..." ile başla, günlük hayattan benzetmeler kullan.)

            ---

            ## 🚀 Pratik Uygulama
            (Eğer konuda kod, formül veya adım adım süreç varsa burada ver. Kod bloğu kullan ama tek satırlık açıklama ekle.)

            ---

            ## ✅ Kısaca Hatırla
            (3-5 maddelik sade özet. Öğrenci bunu okuyunca konunun özünü anlasın.)

            ---

            UYARI:
            - Sadece Markdown formatında yaz.
            - Gereksiz giriş/kapanış cümleleri yok.
            - Emoji kullan ama aşırıya kaçma.
            - Basit dil her şeyin önünde.
            """;


        var userPrompt = $"Sohbet Geçmişi:\n\n{history}";

        try
        {
            _logger.LogInformation("[WikiCurator] AIAgentFactory (Summarizer/{Model}) özetleme başladı. SessionId={SessionId}",
                _factory.GetModel(Orka.Core.Enums.AgentRole.Summarizer), sessionId);

            var summary = await _factory.CompleteChatAsync(Orka.Core.Enums.AgentRole.Summarizer, systemPrompt, userPrompt);

            // ── PEER REVIEW: Grader Öğrenci Dostu Dil Denetimi ──────────────────
            _logger.LogInformation("[WikiCurator] Grader dil kalite denetimi başlatıldı.");
            bool isApproved = await _grader.IsContextRelevantAsync(
                $"{topicTitle} konusu için öğrenci dostu wiki sayfası",
                summary);

            if (!isApproved)
            {
                _logger.LogWarning("[WikiCurator] Grader wiki'yi reddetti. Daha sade versiyon üretiliyor (Self-Refining).");

                var fallbackPrompt = $"""
                    Konu: {topicTitle}
                    Bu konuyu 7. sınıf düzeyinde, çok basit ve kısa Markdown formatında anlat.
                    5-7 maddeden oluşan kısa bir "Konu Özeti" yaz.
                    Sıradan Markdown kullan, tablo vs. gerekmez.
                    """;
                summary = await _factory.CompleteChatAsync(Orka.Core.Enums.AgentRole.Summarizer, fallbackPrompt, userPrompt);
                _logger.LogInformation("[WikiCurator] Sade yedek wiki üretildi.");
            }

            session.Summary = summary;
            await wikiService.AutoUpdateWikiAsync(topicId, summary, "Otomatik Oluşturulan Öğrenci Dostu Wiki", "GitHubModels-WikiArchitect");
            await db.SaveChangesAsync();

            _logger.LogInformation("[WikiCurator] Başarıyla Wiki'ye aktarıldı. Topic={Topic}", topicTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WikiCurator] Tüm sağlayıcılar başarısız. TopicId={TopicId}", topicId);
        }
    }
}

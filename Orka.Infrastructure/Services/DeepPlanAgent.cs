using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Yeni konu için 4 alt başlık üretir ve Topics tablosuna ParentTopicId ile kaydeder.
///
/// Model seçimi: Cerebras (llama3.1-8b) — hız optimizasyonu için.
/// Fallback: IGroqService (Cerebras başarısız olursa).
/// </summary>
public class DeepPlanAgent : IDeepPlanAgent
{
    // CIRCULAR DEPENDENCY GUARD: yalnızca leaf servisler enjekte ediliyor
    private readonly ICerebrasService   _cerebras;
    private readonly IGroqService       _groq;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeepPlanAgent> _logger;

    public DeepPlanAgent(
        ICerebrasService cerebras,
        IGroqService groq,
        IServiceScopeFactory scopeFactory,
        ILogger<DeepPlanAgent> logger)
    {
        _cerebras     = cerebras;
        _groq         = groq;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task<List<Topic>> GenerateAndSaveDeepPlanAsync(
        Guid parentTopicId, string topicTitle, Guid userId)
    {
        var subTitles = await GenerateSubTitlesAsync(topicTitle);
        return await SaveSubTopicsAsync(parentTopicId, subTitles, userId);
    }

    // ── Özel metotlar ──────────────────────────────────────────────────────────

    private async Task<List<string>> GenerateSubTitlesAsync(string topicTitle)
    {
        var systemPrompt = """
            Sen bir eğitim müfredatı planlayıcısısın.
            Verilen konunun pedagojik yapısına göre alt başlıkları özgürce belirle.
            Başlık sayısı konunun doğasına bağlıdır: 2 ile 10 arasında olabilir.
            Basit konular için az başlık, karmaşık konular için daha fazla başlık üret.
            SADECE şu JSON formatında yanıt ver, açıklama ekleme:
            ["Alt Başlık 1","Alt Başlık 2","Alt Başlık 3"]
            """;

        string raw;
        try
        {
            raw = await _cerebras.GenerateResponseAsync(systemPrompt,
                $"Konu: \"{topicTitle}\"");
            _logger.LogInformation("[DeepPlan] Cerebras ile alt başlıklar üretildi.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DeepPlan] Cerebras başarısız, Groq'a fallback.");
            raw = await _groq.GenerateResponseAsync(systemPrompt,
                $"Konu: \"{topicTitle}\"");
        }

        return ParseJsonArray(raw, topicTitle);
    }

    private static List<string> ParseJsonArray(string raw, string topicTitle)
    {
        try
        {
            var cleaned = raw.Trim();
            if (cleaned.StartsWith("```"))
                cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();

            var s = cleaned.IndexOf('[');
            var e = cleaned.LastIndexOf(']');
            if (s >= 0 && e > s) cleaned = cleaned[s..(e + 1)];

            using var doc   = JsonDocument.Parse(cleaned);
            var titles = doc.RootElement.EnumerateArray()
                            .Select(el => el.GetString())
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                            .Select(t => t!)
                            .Take(10)        // Maksimum 10 başlık
                            .ToList();

            if (titles.Count >= 2) return titles;  // En az 2 başlık yeterli
        }
        catch { /* fallback'e düş */ }

        // Fallback: konu adından 4 genel başlık üret
        return new List<string>
        {
            $"{topicTitle} — Temel Kavramlar",
            $"{topicTitle} — Kurulum ve Ortam",
            $"{topicTitle} — Pratik Uygulamalar",
            $"{topicTitle} — İleri Konular ve Best Practices"
        };
    }

    private async Task<List<Topic>> SaveSubTopicsAsync(
        Guid parentTopicId, List<string> titles, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var parent = await db.Topics.FindAsync(parentTopicId);
        if (parent == null)
        {
            _logger.LogError("[DeepPlan] Parent topic bulunamadı: {Id}", parentTopicId);
            return new List<Topic>();
        }

        var subTopics = titles.Select((title, i) => new Topic
        {
            Id             = Guid.NewGuid(),
            UserId         = userId,
            ParentTopicId  = parentTopicId,
            Title          = title,
            Emoji          = parent.Emoji ?? "📖",
            Category       = parent.Category ?? "Diğer",
            CurrentPhase   = TopicPhase.Discovery,
            Order          = i,              // Deterministik sıralama (0-tabanlı)
            CreatedAt      = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow,
            TotalSections  = 1
        }).ToList();

        // Her alt konu için bir WikiPage oluştur
        var wikiPages = subTopics.Select((t, i) => new WikiPage
        {
            Id         = Guid.NewGuid(),
            TopicId    = t.Id,
            UserId     = userId,
            Title      = t.Title,
            OrderIndex = i + 1,
            Status     = "pending",
            CreatedAt  = DateTime.UtcNow,
            UpdatedAt  = DateTime.UtcNow
        }).ToList();

        db.Topics.AddRange(subTopics);
        db.WikiPages.AddRange(wikiPages);

        // Ana konunun TotalSections'ını güncelle
        parent.TotalSections = subTopics.Count;
        parent.CurrentPhase  = TopicPhase.Planning;

        await db.SaveChangesAsync();

        _logger.LogInformation("[DeepPlan] {Count} alt konu oluşturuldu. ParentId: {Id}", subTopics.Count, parentTopicId);
        return subTopics;
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Yeni konu için müfredat planı oluşturur ve Topics tablosuna kaydeder.
///
/// Model seçimi: GitHub Models (Meta-Llama-3.1-405B-Instruct) — Yüksek akıl yürütme.
/// Failover: AIAgentFactory → Groq → Gemini.
/// </summary>
public class DeepPlanAgent : IDeepPlanAgent
{
    private readonly IAIAgentFactory _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeepPlanAgent> _logger;

    public DeepPlanAgent(
        IAIAgentFactory factory,
        IServiceScopeFactory scopeFactory,
        ILogger<DeepPlanAgent> logger)
    {
        _factory      = factory;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task<List<Topic>> GenerateAndSaveDeepPlanAsync(
        Guid parentTopicId, string topicTitle, Guid userId, string userLevel = "Bilinmiyor", string? researchContext = null)
    {
        var subTitles = await GenerateSubTitlesAsync(topicTitle, userLevel, researchContext);
        return await SaveSubTopicsAsync(parentTopicId, subTitles, userId);
    }

    private async Task<List<string>> GenerateSubTitlesAsync(string topicTitle, string userLevel, string? researchContext = null)
    {
        var contextInfo = string.IsNullOrWhiteSpace(researchContext) 
            ? "" 
            : $"\n\n[ARAŞTIRMA VERİLERİ (GÜNCEL BİLGİ KAYNAĞI)]:\n{researchContext}\n\nLütfen yukarıdaki güncel verileri kullanarak konuyu mantıksal ve pedagojik bölümlere ayır.";

        var systemPrompt = $"""
            Sen akademik seviyede bir 'Bilgi Mimarisi (Deep Research)' botusun.
            Görev: Verilen konuyu en güncel pedagojik yaklaşımla alt bölümlere ayırmak.
            Mevcut kullanıcının bilgi seviyesi (Baseline Quiz Sonucu): {userLevel}
            {contextInfo}

            Kullanıcı seviyesi 'İleri' ise temel kavramları atla, ileri düzey detaylara gir.
            Kullanıcı seviyesi 'Temel' ise sıfırdan başlayan detaylı mantıksal bir yapı kur.
            
            Başlık sayısı: 2 ile 10 arasında.
            SADECE şu JSON array formatında yanıt ver, markdown tırnakları dahil OLMASIN:
            ["Bölüm 1","Bölüm 2","Bölüm 3"]
            """;

        _logger.LogInformation("[DeepPlan] AIAgentFactory tetikleniyor. Model: {Model}, Seviye: {Level}",
            _factory.GetModel(AgentRole.DeepPlan), userLevel);

        var raw = await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, $"Konu: \"{topicTitle}\"");

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

            if (titles.Count >= 2) return titles;
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

    private async Task<List<Topic>> SaveSubTopicsAsync(Guid parentTopicId, List<string> titles, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var parent = await db.Topics.FindAsync(parentTopicId);
        if (parent == null) return new List<Topic>();

        var subTopics = titles.Select((title, i) => new Topic
        {
            Id             = Guid.NewGuid(),
            UserId         = userId,
            ParentTopicId  = parentTopicId,
            Title          = title,
            Emoji          = parent.Emoji ?? "📖",
            Category       = (parent.Category == "Genel" || string.IsNullOrEmpty(parent.Category)) ? "Plan" : parent.Category,
            CurrentPhase   = TopicPhase.Discovery,
            Order          = i,
            CreatedAt      = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow,
            TotalSections  = 1
        }).ToList();

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

        parent.TotalSections = subTopics.Count;
        parent.CurrentPhase  = TopicPhase.Planning;

        await db.SaveChangesAsync();

        _logger.LogInformation("[DeepPlan] {Count} alt konu oluşturuldu.", subTopics.Count);
        return subTopics;
    }

    public async Task<string> GenerateBaselineQuizAsync(string topicTitle)
    {
        var systemPrompt = $$"""
            Sen bir 'Eğitim Tanılama Uzmanı (Educational Diagnostician)' botusun.
            Görevin: Kullanıcının '{{topicTitle}}' konusundaki GENEL kavramsal seviyesini ve vizyonunu ölçmek.

            SORU TİPİ KURALI (KESİNLİKLE UYULACAK):
            - Kullanıcının NEREDE durduğunu anlamak için GENİŞ AÇILI, temel kavramları kapsayan bir tanı sorusu sor.
            - AŞIRI SPESİFİK alt detaylara, ezber gerektiren API isimlerine veya versiyon numaralarına ASLA girme.
            - Doğru cevap, konuya giriş yapmış biri tarafından MANTIKLA çıkarılabilir olmalı.
            - Konu hakkında hiç bilgisi olmayan biri "C" veya "D" seçeneğini tahmin edebilir olmalı.

            ÇIKTI KURALI (KESİNLİKLE UYULACAK):
            - SADECE aşağıdaki JSON nesnesini döndür. Giriş metni, açıklama veya markdown EKLEME.
            - "text" alanlarına A), B), C) gibi ön ek EKLEME — sadece seçenek metnini yaz.

            {
              "question": "Konuyu kavramsal düzeyde ölçen, geniş açılı soru metni",
              "options": [
                { "text": "Seçenek metni (çeldirici)", "isCorrect": false },
                { "text": "Seçenek metni (doğru)", "isCorrect": true },
                { "text": "Seçenek metni (çeldirici)", "isCorrect": false },
                { "text": "Seçenek metni (çeldirici)", "isCorrect": false }
              ],
              "explanation": "Neden bu cevabın doğru olduğunun kısa ve net açıklaması."
            }

            DİL: Türkçe.
            """;

        return await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, $"Konu: \"{topicTitle}\"");
    }
}

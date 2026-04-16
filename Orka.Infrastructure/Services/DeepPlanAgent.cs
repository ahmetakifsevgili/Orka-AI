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
    private readonly ISupervisorAgent _supervisor;
    private readonly IGraderAgent _grader;
    private readonly ILogger<DeepPlanAgent> _logger;

    public DeepPlanAgent(
        IAIAgentFactory factory,
        IServiceScopeFactory scopeFactory,
        ISupervisorAgent supervisor,
        IGraderAgent grader,
        ILogger<DeepPlanAgent> logger)
    {
        _factory      = factory;
        _scopeFactory = scopeFactory;
        _supervisor   = supervisor;
        _grader       = grader;
        _logger       = logger;
    }

    public async Task<List<Topic>> GenerateAndSaveDeepPlanAsync(
        Guid parentTopicId, string topicTitle, Guid userId, string userLevel = "Bilinmiyor", string? researchContext = null)
    {
        var modules = await GenerateModulesAsync(topicTitle, userLevel, researchContext);
        return await SaveModularSubTopicsAsync(parentTopicId, modules, userId);
    }

    private async Task<List<ModuleDefinition>> GenerateModulesAsync(string topicTitle, string userLevel, string? researchContext = null)
    {
        _logger.LogInformation("[DeepPlan] Multi-Agent RAG döngüsü başlıyor. Konu: {Topic}", topicTitle);

        // 1. Durum Sınıflandırması (Supervisor Node)
        var intentCategory = await _supervisor.ClassifyIntentAsync(topicTitle);
        _logger.LogInformation("[DeepPlan] Katman: Supervisor -> Kategori: {Category}", intentCategory);

        // 2. RAG Kalite Kontrolü (Grader Node)
        var contextInfo = "";
        if (!string.IsNullOrWhiteSpace(researchContext))
        {
            _logger.LogInformation("[DeepPlan] Katman: Grader -> Research Context denetleniyor...");
            var isRelevant = await _grader.IsContextRelevantAsync(topicTitle, researchContext);
            if (isRelevant)
            {
                contextInfo = $"\n\n[ARAŞTIRMA VERİLERİ (GÜNCEL BİLGİ KAYNAĞI)]:\n{researchContext}\n\nLütfen yukarıdaki güncel verileri kullanarak konuyu mantıksal ve pedagojik bölümlere ayır.";
            }
            else
            {
                _logger.LogWarning("[DeepPlan] Grader REDDETTİ. Hallucination veya alakasız context engellendi. (Sıfır bilgi ile devam ediliyor)");
            }
        }

        var systemPrompt = $$"""
            Sen akademik seviyede bir 'Müfredat Mimarı (Curriculum Architect)' botusun.
            Görev: Verilen konuyu profesyonel, kapsamlı ve konunun doğasına uygun bir müfredata dönüştürmek.
            Mevcut kullanıcının bilgi seviyesi: {{userLevel}}
            Konunun Alanı / Kategorisi: {{intentCategory}}
            {{contextInfo}}

            ORGANİZASYON KURALI (KRİTİK — KONUYA GÖRE AKILLI YAPILANDIR):
            - Programlama/teknoloji → "Temel Yapı Taşları → Uygulama Becerileri → İleri Düzey" yaklaşımı
            - Tarih/toplum → "Dönemsel sıralama" veya "Tematik gruplama"
            - Bilim/matematik → "Teorik temeller → Uygulamalı konular → İleri araştırma"
            - Sanat/dil → "Giriş → Teknik beceriler → Yaratıcı uygulama"

            MODÜL VE DERS İSİMLENDİRME KURALI (ŞİDDETLİ UYARI):
            - "Bölüm 1", "Modül 1", "Giriş", "Genel Bakış", "Temel Kavramlar", "Sonuç", "Uygulama" gibi JENERİK, İÇİ BOŞ BAŞLIKLAR KESİNLİKLE YASAKTIR!
            - Modül başlıkları temanın özeti olmalı (Örn: JS için "Veri Tipleri ve Fonksiyonel Yaklaşım", Tarih için "Osmanlı Yükseliş Dönemi Etkenleri").
            - Ders (Topic) başlıkları tamamen spesifik olmalı (Örn: "Primitive vs Reference Type Farkları", "Fatih Sultan Mehmet'in Yönetsel Stratejisi").
            - Her modülde 2 ile 4 arası DERS konusu olsun. Toplam 3 ile 5 MODÜL üret.
            - Kullanıcının seviyesi '{{userLevel}}' olduğu için içerik yoğunluğunu buna göre ayarla.

            ÇIKTI KURALI (KESİNLİKLE UYULACAK):
            SADECE aşağıdaki JSON formatını döndür. Markdown, açıklama veya başka metin EKLEME. ` ```json ` bloklarını bile kullanmadan direkt { ile başlayan JSON ver.
            {
              "modules": [
                {
                  "title": "Spesifik Modül Başlığı",
                  "emoji": "🧱",
                  "topics": ["Spesifik Ders 1", "Spesifik Ders 2"]
                }
              ]
            }
            DİL: Türkçe.
            """;

        _logger.LogInformation("[DeepPlan] AIAgentFactory tetikleniyor. Model: {Model}, Seviye: {Level}",
            _factory.GetModel(AgentRole.DeepPlan), userLevel);

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var raw = await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, $"Konu: \"{topicTitle}\"");
            var parsedModules = ParseModuleStructure(raw);
            if (parsedModules != null)
            {
                return parsedModules;
            }
            _logger.LogWarning("[DeepPlan] Parse hatası (Deneme {Attempt}/2). Çıktı: {Raw}", attempt, raw.Length > 200 ? raw[..200] + "..." : raw);
        }

        _logger.LogError("[DeepPlan] JSON parse tüm denemelerde başarısız. Kapsamlı Fallback uygulanıyor.");
        // Gelişmiş Dinamik Fallback (Sıradan olmaktan arındırılmış)
        return new List<ModuleDefinition>
        {
            new($"{topicTitle} Sistem Yapısı ve Metodoloji", "🧱", new List<string> { $"{topicTitle} Çekirdek Mimarisi", $"{topicTitle} Kurulum ve Parametre Yönetimi" }),
            new($"{topicTitle} Süreç İşletimi ve Entegrasyon", "⚙️", new List<string> { $"{topicTitle} Veri İşleme Metotları", $"{topicTitle} Endüstriyel Senaryo Analizi" }),
            new($"{topicTitle} Uzmanlık ve Performans Ayarları", "🚀", new List<string> { $"{topicTitle} Kaynak Optimizasyon Teknikleri", $"{topicTitle} İleri Düzey Problem Çözme" })
        };
    }

    /// <summary>LLM çıktısını modül/ders yapısına parse eder. Başarısız olursa null döndürür.</summary>
    private static List<ModuleDefinition>? ParseModuleStructure(string raw)
    {
        try
        {
            var s = raw.IndexOf('{');
            var e = raw.LastIndexOf('}');
            if (s >= 0 && e > s) 
            {
                var cleaned = raw[s..(e + 1)];
                using var doc = JsonDocument.Parse(cleaned);
                var modulesArray = doc.RootElement.GetProperty("modules").EnumerateArray();

                var modules = new List<ModuleDefinition>();
                foreach (var mod in modulesArray)
                {
                    var title = mod.GetProperty("title").GetString() ?? "Modül";
                    var emoji = mod.TryGetProperty("emoji", out var emojiProp) ? emojiProp.GetString() ?? "📖" : "📖";
                    var topics = mod.GetProperty("topics").EnumerateArray()
                        .Select(t => t.GetString())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t!)
                        .ToList();

                    if (topics.Count > 0)
                        modules.Add(new ModuleDefinition(title, emoji, topics));
                }

                if (modules.Count >= 2) return modules;
            }
        }
        catch { /* yoksay, null dönecek */ }
        
        return null;
    }

    /// <summary>3 seviyeli Topic hiyerarşisi: Ana Konu → Modül → Ders. Her ders için WikiPage oluşturur.</summary>
    private async Task<List<Topic>> SaveModularSubTopicsAsync(
        Guid parentTopicId, List<ModuleDefinition> modules, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var parent = await db.Topics.FindAsync(parentTopicId);
        if (parent == null) return new List<Topic>();

        var allLessonTopics = new List<Topic>();
        var allWikiPages = new List<WikiPage>();
        int totalLessons = 0;

        for (int mi = 0; mi < modules.Count; mi++)
        {
            var mod = modules[mi];

            // Modül topic'i (2. seviye)
            var moduleTopic = new Topic
            {
                Id             = Guid.NewGuid(),
                UserId         = userId,
                ParentTopicId  = parentTopicId,
                Title          = mod.Title,
                Emoji          = mod.Emoji,
                Category       = "Plan",
                CurrentPhase   = TopicPhase.Planning,
                Order          = mi,
                CreatedAt      = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                TotalSections  = mod.Topics.Count
            };
            db.Topics.Add(moduleTopic);

            // Ders topic'leri (3. seviye)
            for (int li = 0; li < mod.Topics.Count; li++)
            {
                var lessonTopic = new Topic
                {
                    Id             = Guid.NewGuid(),
                    UserId         = userId,
                    ParentTopicId  = moduleTopic.Id,
                    Title          = mod.Topics[li],
                    Emoji          = mod.Emoji,
                    Category       = "Plan",
                    CurrentPhase   = TopicPhase.Discovery,
                    Order          = li,
                    CreatedAt      = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow,
                    TotalSections  = 1
                };
                db.Topics.Add(lessonTopic);
                allLessonTopics.Add(lessonTopic);
                totalLessons++;

                // Her ders için WikiPage
                allWikiPages.Add(new WikiPage
                {
                    Id         = Guid.NewGuid(),
                    TopicId    = lessonTopic.Id,
                    UserId     = userId,
                    Title      = lessonTopic.Title,
                    OrderIndex = totalLessons,
                    Status     = "pending",
                    CreatedAt  = DateTime.UtcNow,
                    UpdatedAt  = DateTime.UtcNow
                });
            }
        }

        db.WikiPages.AddRange(allWikiPages);

        parent.TotalSections = totalLessons;
        parent.CurrentPhase  = TopicPhase.Planning;

        await db.SaveChangesAsync();

        _logger.LogInformation("[DeepPlan] {ModuleCount} modül, {LessonCount} ders oluşturuldu.",
            modules.Count, totalLessons);
        return allLessonTopics;
    }

    /// <summary>Modül tanımı: başlık, emoji ve altındaki dersler.</summary>
    private record ModuleDefinition(string Title, string Emoji, List<string> Topics);

    public async Task<string> GenerateBaselineQuizAsync(string topicTitle)
    {
        var systemPrompt = $$"""
            Sen bir 'Eğitim Tanılama Uzmanı (Educational Diagnostician)' botusun.
            Görevin: Kullanıcının '{{topicTitle}}' konusundaki bilgi seviyesini TAM OLARAK tespit etmek için 5 soru hazırlamak.

            SORU DAĞILIMI (her biri FARKLI bir boyutu ölçer):
            1. KAVRAMSAL — Konunun temel kavramlarını anlıyor mu? (Orta zorluk)
            2. UYGULAMA — Bilgiyi gerçek bir senaryoda kullanabiliyor mu? (Orta-üst zorluk)
            3. ANALİZ — İki yaklaşımı karşılaştırıp doğru olanı seçebiliyor mu? (Üst zorluk)
            4. PROBLEM ÇÖZME — Verilen bir soruna çözüm üretebiliyor mu? (Üst zorluk)
            5. İLERİ SEVİYE — İleri düzey bir kavramı biliyor mu? (Zor)

            SORU KALİTESİ KURALI:
            - "X nedir?" veya "X'in amacı nedir?" gibi Google'lanabilir tanımlama soruları YASAK.
            - Her soru gerçek dünya senaryosu, kod parçacığı veya somut bir durum içermeli.
            - Seçenekler birbirine yakın ve mantıklı olmalı — düşünme gerektirmeli.
            - Her soru bağımsız; öncekine dayalı olmamalı.

            ÇIKTI KURALI (KESİNLİKLE UYULACAK):
            - SADECE aşağıdaki JSON dizisini döndür. Başka hiçbir metin, markdown tırnağı veya açıklama EKLEME.
            - "text" alanlarına A), B), C) gibi ön ek EKLEME.

            [
              {
                "question": "1. sorunun metni",
                "options": [
                  { "text": "Seçenek", "isCorrect": false },
                  { "text": "Doğru cevap", "isCorrect": true },
                  { "text": "Çeldirici", "isCorrect": false },
                  { "text": "Çeldirici", "isCorrect": false }
                ],
                "explanation": "Kısa açıklama"
              },
              ... (toplam 5 soru)
            ]

            DİL: Türkçe.
            """;

        return await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, $"Konu: \"{topicTitle}\"");
    }
}

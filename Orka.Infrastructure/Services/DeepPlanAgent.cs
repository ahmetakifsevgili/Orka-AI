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
    private readonly IStudentProfileService _profileService;
    private readonly ILogger<DeepPlanAgent> _logger;

    public DeepPlanAgent(
        IAIAgentFactory factory,
        IServiceScopeFactory scopeFactory,
        ISupervisorAgent supervisor,
        IGraderAgent grader,
        IStudentProfileService profileService,
        ILogger<DeepPlanAgent> logger)
    {
        _factory        = factory;
        _scopeFactory   = scopeFactory;
        _supervisor     = supervisor;
        _grader         = grader;
        _profileService = profileService;
        _logger         = logger;
    }

    public async Task<List<Topic>> GenerateAndSaveDeepPlanAsync(
        Guid parentTopicId, string topicTitle, Guid userId, string userLevel = "Bilinmiyor", string? goalContext = null, string? researchContext = null, string? failedTopics = null)
    {
        var modules = await GenerateModulesAsync(topicTitle, userLevel, userId, goalContext, researchContext, failedTopics);
        return await SaveModularSubTopicsAsync(parentTopicId, modules, userId);
    }

    private async Task<List<ModuleDefinition>> GenerateModulesAsync(string topicTitle, string userLevel, Guid userId, string? goalContext = null, string? researchContext = null, string? failedTopics = null)
    {
        _logger.LogInformation("[DeepPlan] Multi-Agent RAG döngüsü başlıyor. Konu: {Topic}", topicTitle);

        // 1. Durum Sınıflandırması (Supervisor Node)
        var intentCategory = await _supervisor.ClassifyIntentAsync(topicTitle);
        _logger.LogInformation("[DeepPlan] Katman: Supervisor -> Kategori: {Category}", intentCategory);

        // Öğrenci profili (Faz B) — yaş/eğitim/hedef bazlı müfredat adaptasyonu
        string profileBlock = string.Empty;
        string examScaffolding = string.Empty;
        (int lessonMin, int lessonMax) = (8, 20);
        try
        {
            using var pscope = _scopeFactory.CreateScope();
            var pdb = pscope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var user = await pdb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user is not null)
            {
                profileBlock = _profileService.BuildProfileBlock(user);
                (lessonMin, lessonMax) = _profileService.SuggestLessonCountRange(user, intentCategory);
                examScaffolding = _profileService.BuildExamScaffolding(user, topicTitle);
                _logger.LogInformation("[DeepPlan] Profil adaptasyonu: {Min}-{Max} ders aralığı (Edu={Edu}, Goal={Goal}), Exam={HasExam}",
                    lessonMin, lessonMax, user.EducationLevel, user.LearningGoal, !string.IsNullOrEmpty(examScaffolding));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DeepPlan] Öğrenci profili yüklenemedi — varsayılan aralıkla devam.");
        }

        // Eğer sınav profili 80/150 aralığındaysa, klasik limitlerin dışına çık ve Tiered (Kademeli) Ağı oluştur
        if (lessonMax >= 80)
        {
            _logger.LogInformation("[DeepPlan] MACRO EXAM DETECTED (Maks {Max} ders). Tiered Curriculum Engine devrede.", lessonMax);
            return await GenerateTieredModulesAsync(topicTitle, userLevel, userId, intentCategory, profileBlock, examScaffolding, goalContext, researchContext, failedTopics);
        }

        // Modül sayısını ders aralığına göre türet: modül başına ~4 ders
        int modMin = Math.Max(2, (int)Math.Ceiling(lessonMin / 5.0));
        int modMax = Math.Max(modMin, (int)Math.Ceiling(lessonMax / 3.0));

        // 2. RAG Kalite Kontrolü (Grader Node)
        var contextInfo = "";
        if (!string.IsNullOrWhiteSpace(researchContext))
        {
            _logger.LogInformation("[DeepPlan] Katman: Grader -> Research Context denetleniyor...");
            var isRelevant = await _grader.IsContextRelevantAsync(topicTitle, researchContext, goalContext: goalContext);
            if (isRelevant)
            {
                contextInfo = $"\n\n[ARAŞTIRMA VERİLERİ (GÜNCEL BİLGİ KAYNAĞI)]:\n{researchContext}\n\nLütfen yukarıdaki güncel verileri kullanarak konuyu mantıksal ve pedagojik bölümlere ayır.";
            }
            else
            {
                _logger.LogWarning("[DeepPlan] Grader REDDETTİ. Hallucination veya alakasız context engellendi. (Sıfır bilgi ile devam ediliyor)");
            }
        }

        string failedTopicsDiagnostic = "";
        if (!string.IsNullOrWhiteSpace(failedTopics))
        {
            failedTopicsDiagnostic = $"\n\n[DİKKAT - MİKRO TEŞHİS (ZAYIFLIK Analizi)]:\nÖğrenci şu konularda HATA YAPMIŞ veya zorlanmış: {failedTopics}.\nMüfredatı eksiksiz ve kapsamlı çıkar, ANCAK öğrencinin eksik olduğu bu konulara matkapla (drill) in! Bu kavramlar geçtiğinde müfredata ekstra 'Uygulamalı Örnekler', 'Derinlemesine Analiz' ve 'Pratik Lab' alt modülleri ekle. Diğer bildiği konularda standart anlatımla geç.";
        }

        var systemPrompt = $$"""
            Sen akademik seviyede bir 'Müfredat Mimarı (Curriculum Architect)' botusun.
            Görev: Verilen konuyu profesyonel, kapsamlı ve konunun doğasına uygun bir müfredata dönüştürmek.
            Mevcut kullanıcının bilgi seviyesi: {{userLevel}}
            Kullanıcının Özel Hedefi / Amacı: {{(string.IsNullOrWhiteSpace(goalContext) ? "Genel Öğrenim" : goalContext)}}
            Konunun Alanı / Kategorisi: {{intentCategory}}
            {{profileBlock}}
            {{examScaffolding}}
            {{contextInfo}}
            {{failedTopicsDiagnostic}}

            HEDEF ODAKLI MÜFREDAT KURALI (ÇOK KRİTİK):
            - Kullanıcının "Özel Hedefi / Amacı" neyse, ders başlıkları, kullanılacak teknik dil ve kapsam tamamen o hedefe göre optimize edilmelidir (Örn: Hedef KPSS ise, KPSS müfredatında yer alan spesifik detaylar olmalı, Hobi ise daha pratik ve eğlenceli başlıklar seçilmelidir).
            
            ORGANİZASYON KURALI (KRİTİK — KONUYA GÖRE AKILLI YAPILANDIR):
            - Kullanıcı cümlesinden (Örn: "C# çalışmak istiyorum") ASIL KONUYU çıkar ("C# Programlama"). Kesinlikle "Selamlaşma", "İstek", "Giriş" gibi konulardan bahsetme!
            - Programlama/teknoloji → "Temel Yapı Taşları → Uygulama Becerileri → İleri Düzey" yaklaşımı
            - Tarih/toplum → "Dönemsel sıralama" veya "Tematik gruplama"
            - Bilim/matematik → "Teorik temeller → Uygulamalı konular → İleri araştırma"
            - Sanat/dil → "Temel İfadeler → Dil Bilgisi → İleri Seviye Konuşma"

            MODÜL VE DERS İSİMLENDİRME KURALI (ŞİDDETLİ UYARI):
            - "Bölüm 1", "Modül 1", "Giriş", "Genel Bakış", "Temel Kavramlar", "Sonuç", "Uygulama", "Selamlaşma" gibi JENERİK, İÇİ BOŞ BAŞLIKLAR KESİNLİKLE YASAKTIR!
            - Modül başlıkları temanın teknik ve mantıksal özeti olmalı (Örn: JS için "Veri Tipleri ve Fonksiyonel Yaklaşım").
            - Ders (Topic) başlıkları tamamen spesifik olmalı (Örn: "Primitive vs Reference Type Farkları").
            - Müfredat SADECE seçilen uzmanlık alanının profesyonel teknik içeriğiyle dolu olmalıdır.

            MÜFREDAT DERİNLİĞİ (PROFİL ADAPTİF — ZORUNLU):
            - TOPLAM DERS SAYISI: EN AZ {{lessonMin}}, EN FAZLA {{lessonMax}} DERS olacak.
            - MODÜL SAYISI: {{modMin}}-{{modMax}} arası. Her modülde 3-6 ders olsun.
            - Eğer yukarıdaki profil bloğunda sınav hedefi, profesyonel veya akademik amaç varsa: müfredatı YUKARI sınıra yaklaştır (kapsamlı ve derin).
            - Profilde çocuk yaş, ilkokul/ortaokul veya hobi hedefi varsa: müfredatı AŞAĞI sınıra yaklaştır (hafif ve eğlenceli).
            - Derin müfredatlarda (≥25 ders): her modülde en az 1 uygulamalı lab/pratik dersi, her modülün son dersi mini-proje veya konu değerlendirme olsun.
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

    private async Task<List<ModuleDefinition>> GenerateTieredModulesAsync(
        string macroTopic, string userLevel, Guid userId, string intentCategory,
        string profileBlock, string examScaffolding, string? goalContext, string? contextInfo, string? failedTopicsDiagnostic)
    {
        _logger.LogInformation("[TieredEngine] Aşama 1: Sınavın Ana Sütunları Üretiliyor...");

        // Aşama 1: Sadece Subject (Dersler) ve boş Modüller (örneğin KPSS -> Türkçe, Matematik -> Modüller) listesini iste.
        var skeletonPrompt = $$"""
            Sen akademik seviyede 'Makro Sınav Müfredat Analisti' botusun.
            Öğrencinin Özel Hedefi: {{(string.IsNullOrWhiteSpace(goalContext) ? "Genel Kapsamlı Ulusal Sınav" : goalContext)}}
            Görev: '{{macroTopic}}' hedefine ve öğrencinin özel hedefine yönelik TÜM ANA DERSLERİNİ (Örn: Matematik, Tarih, Türkçe) ve her dersin altındaki MODÜL BAŞLIKLARINI JSON olarak ver.
            İÇERİK ASLA YÜZEYSEL OLMAYACAK. Bu profesyonel ve devasa bir hedef odaklı sınav müfredatıdır.

            ÇIKTI KURALI: 
            SADECE aşağıdaki JSON formatını döndür:
            {
              "subjects": [
                {
                  "subjectName": "Tarih",
                  "moduleTitles": ["İslam Öncesi Türk Tarihi", "İlk Türk İslam Devletleri", "Osmanlı Kuruluş", "İnkılap Tarihi"]
                }
              ]
            }
            """;

        var rawSkeleton = await _factory.CompleteChatAsync(AgentRole.TieredPlanner, skeletonPrompt, $"Hedef: \"{macroTopic}\"");
        var subjects = ParseSkeleton(rawSkeleton);

        if (subjects == null || subjects.Count == 0)
        {
            _logger.LogWarning("[TieredEngine] Skeleton parse başarısız. Fallback modüle geçiliyor.");
            return new List<ModuleDefinition> { new("Hata Modülü", "⚠️", new List<string> { "Müfredat üretilemedi" }) };
        }

        _logger.LogInformation("[TieredEngine] {SubjectCount} adet Ana Dal tespit edildi. Paralel detaylandırma başlıyor...", subjects.Count);

        var finalModules = new List<ModuleDefinition>();
        var tasks = new List<Task<List<ModuleDefinition>?>>();

        foreach (var sub in subjects)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var modNames = string.Join(", ", sub.ModuleTitles);
                    var detailPrompt = $$"""
                        Sen "{{macroTopic}}" müfredatının "{{sub.SubjectName}}" branş uzmanısın.
                        Görev: Verilen modüllerin İÇİNE, her modül için DERS BAŞLIKLARINI detaylıca üret.
                        Modüller şunlar: {{modNames}}
                        
                        KULLANICI PROFİLİ: {{userLevel}}
                        Öğrenci Özel Hedefi: {{(string.IsNullOrWhiteSpace(goalContext) ? "Sınav Başarısı" : goalContext)}}
                        {{profileBlock}}
                        {{examScaffolding}}
                        {{failedTopicsDiagnostic}}

                        KURAL: Her modül için kesinlikle en az 4, en fazla 8 spesifik, derinlemesine ders (topic) başlığı ver! Ders isimleri öğrencinin hedefine özel olmalıdır!
                        Jenerik "Giriş", "Özet" isimleri YASAK!

                        ÇIKTI JSON (Sadece bunu ver):
                        {
                          "modules": [
                            { "title": "(Modül Adı)", "emoji": "📚", "topics": ["(Spesifik Ders 1)", "(Spesifik Ders 2)"] }
                          ]
                        }
                        """;

                    var rawMods = await _factory.CompleteChatAsync(AgentRole.TieredPlanner, detailPrompt, $"Branş: {sub.SubjectName}");
                    return ParseModuleStructure(rawMods);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TieredEngine] {Subject} branşı detaylandırılırken hata oluştu. Es geçiliyor.", sub.SubjectName);
                    return null;
                }
            }));
        }

        var results = await Task.WhenAll(tasks);
        foreach (var res in results)
        {
            if (res != null) finalModules.AddRange(res);
        }

        if (finalModules.Count == 0)
        {
            _logger.LogError("[TieredEngine] Paralel üretimlerin tamamı çöktü.");
            return new List<ModuleDefinition> { new("Hata Modülü", "⚠️", new List<string> { "Müfredat üretilemedi" }) };
        }

        return finalModules;
    }

    private record SkeletonSubject(string SubjectName, List<string> ModuleTitles);
    
    private static List<SkeletonSubject>? ParseSkeleton(string raw)
    {
        try
        {
            var s = raw.IndexOf('{');
            var e = raw.LastIndexOf('}');
            if (s >= 0 && e > s) 
            {
                var cleaned = raw[s..(e + 1)];
                using var doc = JsonDocument.Parse(cleaned);
                
                if (!doc.RootElement.TryGetProperty("subjects", out var subjectsElement) || subjectsElement.ValueKind != JsonValueKind.Array)
                    return null;

                var arr = subjectsElement.EnumerateArray();
                var list = new List<SkeletonSubject>();
                
                foreach(var item in arr)
                {
                    var name = item.TryGetProperty("subjectName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String 
                        ? nameProp.GetString() ?? "Genel" : "Genel";
                    
                    var mods = new List<string>();
                    if (item.TryGetProperty("moduleTitles", out var modsProp) && modsProp.ValueKind == JsonValueKind.Array)
                    {
                        mods = modsProp.EnumerateArray()
                            .Where(x => x.ValueKind == JsonValueKind.String)
                            .Select(x => x.GetString()!)
                            .ToList();
                    }
                    
                    list.Add(new SkeletonSubject(name, mods));
                }
                return list;
            }
        }
        catch { }
        return null;
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
                
                if (!doc.RootElement.TryGetProperty("modules", out var modulesElement) || modulesElement.ValueKind != JsonValueKind.Array)
                    return null;

                var modulesArray = modulesElement.EnumerateArray();
                var modules = new List<ModuleDefinition>();
                
                foreach (var mod in modulesArray)
                {
                    var title = mod.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String 
                        ? titleProp.GetString() ?? "Modül" : "Modül";
                        
                    var emoji = mod.TryGetProperty("emoji", out var emojiProp) && emojiProp.ValueKind == JsonValueKind.String 
                        ? emojiProp.GetString() ?? "📖" : "📖";
                        
                    var topics = new List<string>();
                    if (mod.TryGetProperty("topics", out var topicsProp) && topicsProp.ValueKind == JsonValueKind.Array)
                    {
                        topics = topicsProp.EnumerateArray()
                            .Where(t => t.ValueKind == JsonValueKind.String)
                            .Select(t => t.GetString())
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                            .Select(t => t!)
                            .ToList();
                    }

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

    public async Task<string> GenerateBaselineQuizAsync(string topicTitle, string? contextGoal = null)
    {
        // Konuyu araştırarak quiz soruları üret — önce Wikipedia + web'e bak, sonra soru çıkar
        _logger.LogInformation("[DeepPlan] Baseline quiz için '{Topic}' araştırılıyor (Wikipedia + Web)...", topicTitle);

        string researchSummary = "";
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(12) };

            // Wikipedia özeti çek
            var wikiEncoded = Uri.EscapeDataString(topicTitle);
            var wikiResp = await httpClient.GetAsync($"https://en.wikipedia.org/api/rest_v1/page/summary/{wikiEncoded}");
            if (wikiResp.IsSuccessStatusCode)
            {
                var wikiJson = await wikiResp.Content.ReadAsStringAsync();
                var wikiDoc = System.Text.Json.JsonDocument.Parse(wikiJson);
                if (wikiDoc.RootElement.TryGetProperty("extract", out var extract))
                {
                    researchSummary = $"[Wikipedia Özeti]: {extract.GetString()?.Substring(0, Math.Min(extract.GetString()!.Length, 1500))}";
                    _logger.LogInformation("[DeepPlan] Wikipedia özeti alındı ({Chars} karakter).", researchSummary.Length);
                }
            }

            // Türkçe Wikipedia da dene
            if (string.IsNullOrEmpty(researchSummary))
            {
                var trWikiResp = await httpClient.GetAsync($"https://tr.wikipedia.org/api/rest_v1/page/summary/{wikiEncoded}");
                if (trWikiResp.IsSuccessStatusCode)
                {
                    var wikiJson = await trWikiResp.Content.ReadAsStringAsync();
                    var wikiDoc = System.Text.Json.JsonDocument.Parse(wikiJson);
                    if (wikiDoc.RootElement.TryGetProperty("extract", out var extract))
                    {
                        researchSummary = $"[Wikipedia (TR) Özeti]: {extract.GetString()?.Substring(0, Math.Min(extract.GetString()!.Length, 1500))}";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DeepPlan] Baseline quiz araştırması başarısız, ham prompt ile devam ediliyor.");
        }

        var researchBlock = string.IsNullOrWhiteSpace(researchSummary)
            ? ""
            : $"""

            [ARAŞTIRMA VERİLERİ — SORULARI BURADAN ÇIKARMALISıN]:
            {researchSummary}
            """;

        var goalBlock = string.IsNullOrWhiteSpace(contextGoal)
            ? "Genel kapsamlı bir seviye tespiti yap."
            : $"[HEDEF KİTLE / AMAÇ]: Öğrencinin özel amacı/sınavı şudur: '{contextGoal}'. SADECE BU AMACA YÖNELİK (örn. bu bir KPSS, YKS veya Hobi hedefi olabilir) özel ve o formatta sorular üret!";

        var systemPrompt = $$"""
            Sen uzman bir 'Eğitim Tanılama ve Ölçme Değerlendirme (Diagnostik)' asistanısın.
            Görevin: Kullanıcının '{{topicTitle}}' konusundaki BİLGİ SEVİYESİNİ EN İNCE AYRINTISINA kadar ölçmek için 20 soruluk, zorluk derecesi giderek artan KAPSAMLI bir seviye tespit sınavı hazırlamaktır.
            {{researchBlock}}
            {{goalBlock}}

            KONU AGNOSTİK KURAL (EN KRİTİK — İHLAL EDİLEMEZ):
            - Konu: '{{topicTitle}}'. Buna uygun sorular üret.
            - Eğer konu Matematik ise: sayısal hesaplama, formül uygulama, ispat soruları.
            - Eğer konu Tarih veya Sosyal Bilimler ise: kronolojik analiz, neden-sonuç, tarihsel karşılaştırma.
            - Eğer konu Biyoloji/Kimya/Fizik ise: bilimsel kavram, deney yorumu, formül/denge.
            - Eğer konu Hukuk/İktisat ise: senaryo, vaka analizi, kavram ayırt etme.
            - Eğer konu gerçekten Yazılım/CS ise: kod okuma, algoritma analizi, hata bulma.
            - ASLA konu gerektirmediği halde yazılım/kod sorusu EKLEME.
            
            DİKKAT EDİLECEK ÇOK KRİTİK KURALLAR:
            1. Birbirinin kopyası veya aşırı basit sorular YASAK ("5 ile 7 arasında hangi sayı var" düzeyinde sorular KABUL EDİLMEZ).
            2. Sorular ezber bilgiden ziyade analitik düşünme, kavrama, çıkarım ve uygulama becerisini ölçmelidir.
            3. Her soru kendi içinde zorluk ve bağlam olarak benzersiz olmalıdır.
            4. Seçenekler (çeldiriciler) mantıklı ve iyi tasarlanmış olmalı — doğru cevap bariz olmamalı.

            ZORLUK VE KAPSAM DAĞILIMI (Toplam 20 Soru — Giderek Zorlaşan Akış):
            - 1-5. Sorular (Temel Seviye): Kavramsal bilgiler, tanımlar, konuya giriş. Öğrenci konuyu hiç bilmiyor mu?
            - 6-10. Sorular (Orta Seviye): Formül, temel kural veya kavramların doğrudan uygulaması.
            - 11-15. Sorular (İleri Seviye): Birden fazla kuralın aynı anda kullanıldığı, mantık yürütme ve analiz gerektiren sorular.
            - 16-20. Sorular (Uzman Seviye): Uç durumlar (edge case), çok karmaşık senaryolar, istisnalara dayalı zorlayıcı eleyici sorular.

            ÇIKTI FORMATI (SADECE JSON DİZİSİ DÖNDÜR — BAŞKA METİN YAZMA):
            [
              {
                "type": "multiple_choice",
                "question": "Soru metnini detaylı ve senaryoya dayalı şekilde buraya yazın...",
                "options": [
                  { "text": "A Şıkkı", "isCorrect": false },
                  { "text": "B Şıkkı", "isCorrect": true },
                  { "text": "C Şıkkı", "isCorrect": false },
                  { "text": "D Şıkkı", "isCorrect": false }
                ],
                "explanation": "Bu sorunun doğru cevabı B'dir çünkü...",
                "topic": "{{topicTitle}}"
              }
            ]

            DİL: Tamamen Türkçe.
            """;

        return await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, $"Konu: \"{topicTitle}\"");
    }
}


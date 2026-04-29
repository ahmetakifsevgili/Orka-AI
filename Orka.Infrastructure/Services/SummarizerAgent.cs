using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Enums;
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
    private readonly IRedisMemoryService _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SummarizerAgent> _logger;

    public SummarizerAgent(
        IAIAgentFactory factory,
        IGraderAgent grader,
        IRedisMemoryService redis,
        IServiceScopeFactory scopeFactory,
        ILogger<SummarizerAgent> logger)
    {
        _factory      = factory;
        _grader       = grader;
        _redis        = redis;
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

        // ── DB-level idempotency: Son üretilmiş wiki block'undan bu yana
        // yeni mesaj gelmişse yeniden üretebilir ─────
        var lastWikiBlock = await db.WikiBlocks
            .Where(b => b.WikiPage.TopicId == topicId
                        && (b.Source == "Qwen-Wiki-Architect" || b.Source == "GitHubModels-WikiArchitect" || b.Source == "Groq-Curator"))
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync();

        if (lastWikiBlock != null)
        {
            // Son wiki üretiminden sonra yeni mesaj geldi mi kontrol et
            var newMsgsCount = await db.Messages
                .Where(m => m.SessionId == sessionId && m.CreatedAt > lastWikiBlock.CreatedAt)
                .CountAsync();

            if (newMsgsCount < 5)
            {
                _logger.LogInformation("[WikiCurator] Son wiki üretiminden sonra yeterli yeni mesaj yok ({Count}). Atlanıyor.", newMsgsCount);
                return;
            }
        }

        var session = await db.Sessions.Include(s => s.Messages).FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null || !session.Messages.Any()) return;

        var topic = await db.Topics.FindAsync(topicId);
        var topicTitle = topic?.Title ?? "Bilinmeyen Konu";

        var history = string.Join("\n", session.Messages.OrderBy(m => m.CreatedAt).Select(m => $"{m.Role}: {m.Content}"));

        // ── Faz 16: Modül seviyesi (parent topic) wiki'de zayıf alt konuları işaretle ──
        // Modül topic'i (üst), altındaki dersleri tarar; understandingScore < 5 olanlar
        // "⚠️ Bu konuda daha fazla pratik gerekiyor" notu ile vurgulanır.
        string moduleWeaknessNotice = "";
        try
        {
            // Topic alt konuları varsa = bu bir modül/parent
            var subTopicIds = await db.Topics
                .Where(t => t.ParentTopicId == topicId)
                .Select(t => new { t.Id, t.Title })
                .ToListAsync();

            if (subTopicIds.Count > 0)
            {
                var weakLessons = new List<string>();
                foreach (var st in subTopicIds)
                {
                    var profile = await _redis.GetStudentProfileAsync(st.Id);
                    if (profile.HasValue && profile.Value.score < 5)
                    {
                        weakLessons.Add($"- **{st.Title}** (Anlama: {profile.Value.score}/10) → ⚠️ Bu konuda daha fazla pratik gerekiyor");
                    }
                }

                if (weakLessons.Count > 0)
                {
                    moduleWeaknessNotice = $"\n\n[MODÜL ZAYIFLIK RAPORU — Bu modülün özetinde aşağıdaki başlıklara özel vurgu yap]:\n{string.Join("\n", weakLessons)}\n";
                    _logger.LogInformation("[WikiCurator] Modül seviyesi zayıflık raporu eklendi. TopicId={TopicId} WeakCount={Count}",
                        topicId, weakLessons.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WikiCurator] Modül zayıflık raporu üretilemedi.");
        }

        // ── KİŞİSELLEŞTİRME: Öğrenci zayıf noktaları + yanlış quiz cevapları ──
        string personalizationBlock = "";
        try
        {
            var profile = await _redis.GetStudentProfileAsync(topicId);

            var failedAttempts = await db.QuizAttempts
                .Where(q => q.TopicId == topicId && q.UserId == userId && !q.IsCorrect)
                .OrderByDescending(q => q.CreatedAt)
                .Take(5)
                .Select(q => new
                {
                    q.Question,
                    q.SkillTag,
                    q.TopicPath,
                    q.Difficulty,
                    q.CognitiveType
                })
                .ToListAsync();

            var parts = new List<string>();
            if (profile.HasValue)
            {
                parts.Add($"- Kavrama Seviyesi: {profile.Value.score}/10");
                if (!string.IsNullOrEmpty(profile.Value.weaknesses))
                    parts.Add($"- Zayıf Noktalar: {profile.Value.weaknesses}");
            }
            if (failedAttempts.Any())
            {
                parts.Add($"- Yanlış Cevaplanan Sorular ({failedAttempts.Count}):");
                parts.AddRange(failedAttempts.Select(q =>
                {
                    var question = q.Question.Length > 150 ? q.Question[..150] + "..." : q.Question;
                    var skill = !string.IsNullOrWhiteSpace(q.SkillTag) ? q.SkillTag : q.TopicPath ?? "unknown skill";
                    var meta = string.Join(", ", new[] { skill, q.Difficulty, q.CognitiveType }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    return $"  - [{meta}] {question}";
                }));
            }

            if (parts.Any())
            {
                personalizationBlock = $"""

                    [KİŞİSELLEŞTİRME — ÖĞRENCİ PROFİLİ]:
                    {string.Join("\n", parts)}

                    WIKI'Yİ BU PROFİLE GÖRE ŞEKİLLENDİR:
                    - Zayıf noktalar/yanlış cevaplar varsa o kavramlara daha fazla yer ver, ek örnekle açıkla.
                    - Kavrama seviyesi düşükse dili daha basitleştir, benzetmeleri artır.
                    - Yanlış cevaplanan soruların ardındaki temel kavramı Wiki'de açıkça anlat.
                    """;
                _logger.LogInformation("[WikiCurator] Kişiselleştirme uygulandı. TopicId={TopicId} Failed={Count}",
                    topicId, failedAttempts.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WikiCurator] Kişiselleştirme verisi okunamadı, jenerik wiki üretiliyor.");
        }

        var systemPrompt = $$"""
            Sen Orka AI'nın 'Kişisel Not Defteri ve Eğitim Özetleyici'sisin.
            Görev: Sohbet geçmişini analiz ederek öğrenci için kalıcı bir 'Ders Not Defteri' (Wiki) oluştur.
            Bu not defteri sadece yüzeysel bir özet değil; öğrencinin tekrar dönüp baktığında konuyu tam olarak kavrayacağı, nerede zorlandığını göreceği ve kendini test edebileceği kapsamlı bir rehber olmalıdır.

            Konu: {{topicTitle}}
            {{personalizationBlock}}
            {{moduleWeaknessNotice}}

            HEDEF KİTLE & DİL:
            - Akademik değil, öğretici ve samimi bir dil kullan.
            - Teknik terimleri mutlaka Türkçe açıklamayla ver: "Algoritma (adım adım çözüm yolu)"
            - Karmaşık cümleler kurma; net, akıcı ve yapılandırılmış paragraflar yaz.

            WIKI SAYFA YAPISI:
            # {{topicTitle}} 📖

            ## 🎯 Ders Özeti
            (Chat sırasında anlatılan konunun doyurucu ama net bir özeti.)

            ---

            ## 🧠 Temel Kavramlar
            (Konunun önemli kavramlarını her biri için 2-3 cümle, basit dil, mümkünse 🔹 gibi emojilerle listele.)

            ---

            ## 💡 Dikkat Edilmesi Gerekenler & Anlaşılmayanlar
            (Sohbet boyunca öğrencinin zorlandığı, yanlış cevap verdiği veya tekrar edilen kavramları burada vurgula. "Şuraya dikkat edelim:" formatında toparla.)

            ---

            ## 🚀 Pratik Uygulama & Örnekler
            (Eğer konuda kod, formül veya adım adım süreç varsa burada detaylandır. Kod bloğu kullan ve açıkla.)

            ---

            ## ❓ Pekiştirme Soruları
            (Öğrencinin konuyu kendi kendine test edebilmesi için chat dışında çözebileceği 3-4 ufuk açıcı, pratik soru veya senaryo yaz.)

            ---

            ## ✅ Kısaca Hatırla
            (3-5 maddelik hap bilgi. Öğrenci sadece burayı okuyarak konunun en temel özünü hatırlasın.)

            ---

            GÖRSELLEŞTİRME KURALLARI (ZORUNLU):
            1. Süreç, mimari veya algoritma anlatıyorsan MUTLAKA bir adet Mermaid diyagramı ekle.
            2. Soyut kavramlar için görsel ekle: ![kısa açıklama](https://image.pollinations.ai/prompt/<URL_ENCODED_PROMPT>?width=512&height=512&nologo=true)

            UYARI:
            - Sadece Markdown formatında yaz.
            - Gereksiz giriş/kapanış cümleleri (Örn: "İşte notların:") kullanma.
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
                _logger.LogWarning("[WikiCurator] Grader wiki'yi reddetti. Aynı yapıda ama daha sade dille yeniden üretiliyor (Self-Refining).");

                var fallbackPrompt = $"""
                    Konu: {topicTitle}
                    Aşağıdaki sohbet geçmişinden bu konu için öğrenci dostu bir Wiki sayfası oluştur.
                    Dili çok sade tut (ortaokul düzeyinde anlaşılır olsun), ama YAPI'yı koru:

                    # {topicTitle} 📖

                    ## 🎯 Ders Özeti
                    (Konuyu çok basit ve net bir dille özetle.)

                    ---

                    ## 🧠 Temel Kavramlar
                    (Her kavramı 1-2 basit cümleyle anlat. Emoji kullan.)

                    ---

                    ## 💡 Dikkat Edilmesi Gerekenler
                    (Sık yapılan hatalar veya karıştırılan yerler. Basit dille.)

                    ---

                    ## ❓ Pekiştirme Soruları
                    (3 basit soru yaz. Cevapları düşündürsün ama çok zor olmasın.)

                    ---

                    ## ✅ Kısaca Hatırla
                    (3-5 maddelik hap bilgi.)

                    Sadece Markdown formatında yaz. Gereksiz giriş cümleleri kullanma.
                    """;
                summary = await _factory.CompleteChatAsync(Orka.Core.Enums.AgentRole.Summarizer, fallbackPrompt, userPrompt);
                _logger.LogInformation("[WikiCurator] Sade ama yapısal yedek wiki üretildi.");
            }

            session.Summary = summary;
            await wikiService.AutoUpdateWikiAsync(topicId, summary, "Otomatik Oluşturulan Öğrenci Dostu Wiki", "GitHubModels-WikiArchitect");
            await db.SaveChangesAsync();
            InvalidateNotebookTools(topicId);

            _logger.LogInformation("[WikiCurator] Başarıyla Wiki'ye aktarıldı. Topic={Topic}", topicTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WikiCurator] Tüm sağlayıcılar başarısız. TopicId={TopicId}", topicId);
        }
    }

    // ── NotebookLM Tarzı Briefing Document ──────────────────────────────────

    private static readonly ConcurrentDictionary<Guid, (BriefingDocument Doc, DateTime At)> _briefingCache = new();
    private static readonly ConcurrentDictionary<Guid, (IReadOnlyList<GlossaryItemDto> Items, DateTime At)> _glossaryCache = new();
    private static readonly ConcurrentDictionary<Guid, (IReadOnlyList<TimelineItemDto> Items, DateTime At)> _timelineCache = new();
    private static readonly ConcurrentDictionary<Guid, (MindMapDto Map, DateTime At)> _mindMapCache = new();
    private static readonly ConcurrentDictionary<Guid, (IReadOnlyList<StudyCardDto> Cards, DateTime At)> _studyCardCache = new();
    private static readonly TimeSpan BriefingCacheTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan NotebookToolCacheTtl = TimeSpan.FromHours(24);

    public void InvalidateNotebookTools(Guid topicId)
    {
        _briefingCache.TryRemove(topicId, out _);
        _glossaryCache.TryRemove(topicId, out _);
        _timelineCache.TryRemove(topicId, out _);
        _mindMapCache.TryRemove(topicId, out _);
        _studyCardCache.TryRemove(topicId, out _);
        _ = _redis.BumpTopicVersionAsync(topicId, "summarizer-invalidate");
    }

    public async Task<IReadOnlyList<StudyRecommendationDto>> GenerateRecommendationsAsync(Guid topicId, Guid userId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var learning = scope.ServiceProvider.GetRequiredService<ILearningSignalService>();
        return await learning.GetRecommendationsAsync(userId, topicId, ct);
    }

    public async Task<BriefingDocument> GenerateBriefingAsync(Guid topicId, Guid userId, CancellationToken ct = default)
    {
        // ── Cache kontrolü (1 saat) ─────────────────────────────────────────
        if (await TryGetNotebookCacheAsync<BriefingDocument>("briefing", topicId, userId, ct) is { } cached)
            return cached;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var wikiService = scope.ServiceProvider.GetRequiredService<IWikiService>();

        var topic = await db.Topics.FindAsync(new object[] { topicId }, ct);
        var topicTitle = topic?.Title ?? "Konu";

        // Wiki içeriğini al
        var wikiContent = await wikiService.GetWikiFullContentAsync(topicId, userId);

        // Korteks raporu varsa kaynak olarak ekle
        var korteksReport = await _redis.GetKorteksResearchReportAsync(topicId);

        // Hiçbiri yoksa minimal briefing dön
        if (string.IsNullOrWhiteSpace(wikiContent) && string.IsNullOrWhiteSpace(korteksReport))
        {
            return new BriefingDocument(
                topicTitle,
                "Bu konu için henüz yeterli içerik üretilmedi.",
                new List<string> { "Konuyu öğrenmeye başlamak için Orka'ya soru sorabilirsin." },
                new List<string> { "Bu konu nedir?", "Nereden başlamalıyım?" },
                DateTime.UtcNow);
        }

        // Kaynakları birleştir, prompt boyutunu kontrol et
        var combinedSource = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(wikiContent))
        {
            var wiki = wikiContent.Length > 4000 ? wikiContent[..4000] + "..." : wikiContent;
            combinedSource.AppendLine("[WIKI İÇERİĞİ]:");
            combinedSource.AppendLine(wiki);
        }
        if (!string.IsNullOrWhiteSpace(korteksReport))
        {
            var k = korteksReport.Length > 2000 ? korteksReport[..2000] + "..." : korteksReport;
            combinedSource.AppendLine("\n[KORTEKS ARAŞTIRMA RAPORU]:");
            combinedSource.AppendLine(k);
        }

        var systemPrompt = $$"""
            Sen NotebookLM-tarzı bir "Briefing Document" üreticisisin.
            Konu: {{topicTitle}}

            Verilen kaynak materyalden kullanıcının okumadan önce HIZLA göz atabileceği bir özet üret:
            - 1 cümlelik TL;DR (en fazla 25 kelime)
            - 5 maddelik "Anahtar Çıkarımlar" (her biri 1 cümle, kavram + neden önemli)
            - 3 öneri soru (kullanıcının merakını uyandıracak, derinleştirici)

            ÇIKTI: Sadece şu JSON formatında dön. Başka metin EKLEME, markdown bloğu kullanma:
            {
              "tldr": "Bir cümle...",
              "keyTakeaways": ["1...", "2...", "3...", "4...", "5..."],
              "suggestedQuestions": ["Soru 1?", "Soru 2?", "Soru 3?"]
            }

            DİL: Türkçe. Net, sade, akademik değil.
            """;

        var userPrompt = $"Kaynak Materyal:\n\n{combinedSource}";

        try
        {
            var raw = await _factory.CompleteChatAsync(AgentRole.Summarizer, systemPrompt, userPrompt, ct);
            var clean = raw.Replace("```json", "").Replace("```", "").Trim();

            var s = clean.IndexOf('{');
            var e = clean.LastIndexOf('}');
            if (s < 0 || e <= s) throw new InvalidOperationException("Briefing JSON parse edilemedi.");

            using var doc = JsonDocument.Parse(clean[s..(e + 1)]);
            var root = doc.RootElement;

            string tldr = root.TryGetProperty("tldr", out var t) ? t.GetString() ?? "" : "";
            var takeaways = root.TryGetProperty("keyTakeaways", out var k)
                ? k.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                : new List<string>();
            var questions = root.TryGetProperty("suggestedQuestions", out var q)
                ? q.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                : new List<string>();

            var briefing = new BriefingDocument(topicTitle, tldr, takeaways, questions, DateTime.UtcNow);
            _briefingCache[topicId] = (briefing, DateTime.UtcNow);
            await SetNotebookCacheAsync("briefing", topicId, userId, briefing, BriefingCacheTtl);
            return briefing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Briefing] Üretim başarısız. TopicId={TopicId}", topicId);
            return new BriefingDocument(
                topicTitle,
                "Briefing şu anda hazırlanamadı. Lütfen daha sonra tekrar dene.",
                new List<string>(),
                new List<string>(),
                DateTime.UtcNow);
        }
    }

    public async Task<IReadOnlyList<GlossaryItemDto>> GenerateGlossaryAsync(Guid topicId, Guid userId, CancellationToken ct = default)
    {
        if (await TryGetNotebookCacheAsync<List<GlossaryItemDto>>("glossary", topicId, userId, ct) is { } cached)
            return cached;

        var source = await BuildNotebookToolSourceAsync(topicId, userId, ct);
        if (string.IsNullOrWhiteSpace(source))
            return [];

        var systemPrompt = """
            Sen NotebookLM tarzı otomatik sözlük üreticisisin.
            Verilen kaynak metindeki en zor veya teknik 10 terimi bul.
            Çıktı SADECE JSON array olsun:
            [
              { "term": "Terim", "simpleExplanation": "Basit açıklama" }
            ]
            Dil Türkçe. Açıklamalar tek cümle ve öğrenci dostu olsun.
            """;

        try
        {
            var raw = await _factory.CompleteChatAsync(AgentRole.Summarizer, systemPrompt, source, ct);
            var items = ParseGlossary(raw);
            _glossaryCache[topicId] = (items, DateTime.UtcNow);
            await SetNotebookCacheAsync("glossary", topicId, userId, items, NotebookToolCacheTtl);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Glossary] Üretim başarısız. TopicId={TopicId}", topicId);
            return [];
        }
    }

    public async Task<IReadOnlyList<TimelineItemDto>> GenerateTimelineAsync(Guid topicId, Guid userId, CancellationToken ct = default)
    {
        if (await TryGetNotebookCacheAsync<List<TimelineItemDto>>("timeline", topicId, userId, ct) is { } cached)
            return cached;

        var source = await BuildNotebookToolSourceAsync(topicId, userId, ct);
        if (string.IsNullOrWhiteSpace(source))
            return [];

        var systemPrompt = """
            Sen NotebookLM tarzı zaman çizelgesi üreticisisin.
            Verilen kaynak metinde tarih, dönem, yıl veya sıralı olay varsa kronolojik olarak çıkar.
            Çıktı SADECE JSON array olsun:
            [
              { "year": "Yıl/Dönem", "event": "Olay" }
            ]
            Eğer belirgin tarihsel akış yoksa boş array döndür.
            Dil Türkçe.
            """;

        try
        {
            var raw = await _factory.CompleteChatAsync(AgentRole.Summarizer, systemPrompt, source, ct);
            var items = ParseTimeline(raw);
            _timelineCache[topicId] = (items, DateTime.UtcNow);
            await SetNotebookCacheAsync("timeline", topicId, userId, items, NotebookToolCacheTtl);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Timeline] Üretim başarısız. TopicId={TopicId}", topicId);
            return [];
        }
    }

    public async Task<MindMapDto> GenerateMindMapAsync(Guid topicId, Guid userId, CancellationToken ct = default)
    {
        if (await TryGetNotebookCacheAsync<MindMapDto>("mindmap", topicId, userId, ct) is { } cached)
            return cached;

        var source = await BuildNotebookToolSourceAsync(topicId, userId, ct);
        if (string.IsNullOrWhiteSpace(source))
            return new MindMapDto("mindmap\n  root((Henüz kaynak yok))", []);

        var systemPrompt = """
            Sen NotebookLM tarzı Mind Map üreticisisin.
            Kaynak metindeki ana fikirleri dallanan bir öğrenme haritasına çevir.
            Çıktı SADECE JSON olsun:
            {
              "nodes": [
                { "id": "root", "label": "Ana konu", "parentId": null, "depth": 0 },
                { "id": "n1", "label": "Alt fikir", "parentId": "root", "depth": 1 }
              ]
            }
            Kurallar:
            - 1 root, en fazla 18 node.
            - depth 0-3 arası.
            - Kısa, tıklanabilir etiketler kullan.
            - Diagram açıklaması yazma, sadece JSON.
            """;

        try
        {
            var raw = await _factory.CompleteChatAsync(AgentRole.Summarizer, systemPrompt, source, ct);
            var map = ParseMindMap(raw);
            _mindMapCache[topicId] = (map, DateTime.UtcNow);
            await SetNotebookCacheAsync("mindmap", topicId, userId, map, NotebookToolCacheTtl);
            return map;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MindMap] Üretim başarısız. TopicId={TopicId}", topicId);
            return new MindMapDto("mindmap\n  root((Harita hazırlanamadı))", []);
        }
    }

    public async Task<IReadOnlyList<StudyCardDto>> GenerateStudyCardsAsync(Guid topicId, Guid userId, CancellationToken ct = default)
    {
        if (await TryGetNotebookCacheAsync<List<StudyCardDto>>("study-cards", topicId, userId, ct) is { } cached)
            return cached;

        var source = await BuildNotebookToolSourceAsync(topicId, userId, ct);
        if (string.IsNullOrWhiteSpace(source))
            return [];

        var systemPrompt = """
            Sen NotebookLM tarzı flashcard ve pekiştirme kartı üreticisisin.
            Kaynak metinden 8 kısa çalışma kartı çıkar.
            Çıktı SADECE JSON array olsun:
            [
              { "front": "Soru / kavram", "back": "Kısa cevap ve açıklama", "sourceHint": "Belge/Wiki/Korteks ipucu" }
            ]
            Kartlar ezber değil, kavrama ve ilişki kurmayı pekiştirsin.
            Dil Türkçe.
            """;

        try
        {
            var raw = await _factory.CompleteChatAsync(AgentRole.Summarizer, systemPrompt, source, ct);
            var cards = ParseStudyCards(raw);
            _studyCardCache[topicId] = (cards, DateTime.UtcNow);
            await SetNotebookCacheAsync("study-cards", topicId, userId, cards, NotebookToolCacheTtl);
            return cards;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StudyCards] Üretim başarısız. TopicId={TopicId}", topicId);
            return [];
        }
    }

    private async Task<T?> TryGetNotebookCacheAsync<T>(string tool, Guid topicId, Guid userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        var version = await _redis.GetTopicVersionAsync(topicId);
        var key = RedisMemoryService.NotebookToolKey(tool, userId, topicId, version);
        var cached = await _redis.GetJsonAsync(key);
        sw.Stop();

        if (string.IsNullOrWhiteSpace(cached))
        {
            await _redis.RecordCacheMetricAsync($"notebook-{tool}", hit: false, tool: tool, latencyMs: sw.Elapsed.TotalMilliseconds);
            return default;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(cached);
            if (parsed is not null)
            {
                await _redis.RecordCacheMetricAsync($"notebook-{tool}", hit: true, tool: tool, latencyMs: sw.Elapsed.TotalMilliseconds);
                return parsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[NotebookLM] Redis cache parse edilemedi. Tool={Tool} TopicId={TopicId}", tool, topicId);
        }

        await _redis.RecordCacheMetricAsync($"notebook-{tool}", hit: false, tool: tool, latencyMs: sw.Elapsed.TotalMilliseconds);
        return default;
    }

    private async Task SetNotebookCacheAsync<T>(string tool, Guid topicId, Guid userId, T value, TimeSpan ttl)
    {
        var version = await _redis.GetTopicVersionAsync(topicId);
        var key = RedisMemoryService.NotebookToolKey(tool, userId, topicId, version);
        await _redis.SetJsonAsync(key, JsonSerializer.Serialize(value), ttl);
    }

    private async Task<string> BuildNotebookToolSourceAsync(Guid topicId, Guid userId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var wikiService = scope.ServiceProvider.GetRequiredService<IWikiService>();

        var wikiContent = await wikiService.GetWikiFullContentAsync(topicId, userId);
        var korteksReport = await _redis.GetKorteksResearchReportAsync(topicId);
        var combined = $"{wikiContent}\n\n{korteksReport}".Trim();
        return combined.Length > 7000 ? combined[..7000] + "\n[...kırpıldı]" : combined;
    }

    private static IReadOnlyList<GlossaryItemDto> ParseGlossary(string raw)
    {
        try
        {
            var json = ExtractJsonArray(raw);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                .Select(x => new GlossaryItemDto(
                    ReadString(x, "term", "terim"),
                    ReadString(x, "simpleExplanation", "simple_explanation", "basit_aciklama", "açıklama")))
                .Where(x => !string.IsNullOrWhiteSpace(x.Term))
                .Take(10)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<TimelineItemDto> ParseTimeline(string raw)
    {
        try
        {
            var json = ExtractJsonArray(raw);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                .Select(x => new TimelineItemDto(
                    ReadString(x, "year", "yil", "yıl", "date"),
                    ReadString(x, "event", "olay")))
                .Where(x => !string.IsNullOrWhiteSpace(x.Event))
                .Take(20)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static MindMapDto ParseMindMap(string raw)
    {
        var json = ExtractJsonObject(raw);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var nodesEl = root.TryGetProperty("nodes", out var n) ? n : root;
        var nodes = nodesEl.EnumerateArray()
            .Select((x, i) => new MindMapNodeDto(
                ReadString(x, "id") is { Length: > 0 } id ? id : $"n{i}",
                ReadString(x, "label", "title"),
                x.TryGetProperty("parentId", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null,
                x.TryGetProperty("depth", out var d) && d.TryGetInt32(out var depth) ? depth : 1))
            .Where(x => !string.IsNullOrWhiteSpace(x.Label))
            .Take(18)
            .ToList();

        if (nodes.Count == 0)
            return new MindMapDto("mindmap\n  root((Harita))", []);

        if (nodes.All(x => x.ParentId != null))
            nodes.Insert(0, new MindMapNodeDto("root", "Ana Konu", null, 0));

        return new MindMapDto(BuildMermaidMindMap(nodes), nodes);
    }

    private static IReadOnlyList<StudyCardDto> ParseStudyCards(string raw)
    {
        try
        {
            var json = ExtractJsonArray(raw);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                .Select(x => new StudyCardDto(
                    ReadString(x, "front", "question", "soru"),
                    ReadString(x, "back", "answer", "cevap"),
                    ReadString(x, "sourceHint", "source", "kaynak")))
                .Where(x => !string.IsNullOrWhiteSpace(x.Front) && !string.IsNullOrWhiteSpace(x.Back))
                .Take(8)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string ExtractJsonArray(string raw)
    {
        var clean = raw.Replace("```json", "").Replace("```", "").Trim();
        var s = clean.IndexOf('[');
        var e = clean.LastIndexOf(']');
        if (s < 0 || e <= s) throw new InvalidOperationException("JSON array bulunamadı.");
        return clean[s..(e + 1)];
    }

    private static string ExtractJsonObject(string raw)
    {
        var clean = raw.Replace("```json", "").Replace("```", "").Trim();
        var s = clean.IndexOf('{');
        var e = clean.LastIndexOf('}');
        if (s < 0 || e <= s) throw new InvalidOperationException("JSON object bulunamadı.");
        return clean[s..(e + 1)];
    }

    private static string BuildMermaidMindMap(IReadOnlyList<MindMapNodeDto> nodes)
    {
        var safeIds = nodes.ToDictionary(n => n.Id, n => SanitizeMermaidId(n.Id));
        var children = nodes
            .Where(n => n.ParentId != null)
            .GroupBy(n => n.ParentId!)
            .ToDictionary(g => g.Key, g => g.ToList());
        var roots = nodes.Where(n => n.ParentId == null || !safeIds.ContainsKey(n.ParentId)).ToList();
        var sb = new System.Text.StringBuilder("mindmap\n");

        void AppendNode(MindMapNodeDto node, int depth)
        {
            var indent = new string(' ', Math.Max(1, depth + 1) * 2);
            sb.AppendLine($"{indent}{safeIds[node.Id]}(({EscapeMermaidLabel(node.Label)}))");
            if (!children.TryGetValue(node.Id, out var kids)) return;
            foreach (var child in kids.OrderBy(x => x.Depth).ThenBy(x => x.Label))
                AppendNode(child, depth + 1);
        }

        foreach (var root in roots)
            AppendNode(root, 0);

        return sb.ToString();
    }

    private static string SanitizeMermaidId(string id)
    {
        var chars = id.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var clean = new string(chars);
        return string.IsNullOrWhiteSpace(clean) || char.IsDigit(clean[0]) ? $"n_{clean}" : clean;
    }

    private static string EscapeMermaidLabel(string label) =>
        label.Replace("(", "").Replace(")", "").Replace("\n", " ").Trim();

    private static string ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop))
                return prop.GetString() ?? string.Empty;
        }
        return string.Empty;
    }
}

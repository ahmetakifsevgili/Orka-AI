using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Pekiştirme Sınavı Üreticisi.
///
/// Dinamik Soru Sayısı Mantığı:
///   - Alt Ders (ParentTopicId != null) → 3-5 soru (hızlı kavrama kontrolü)
///   - Üst Modül / Müfredat (ParentTopicId == null) → 15-20 soru (kapsamlı değerlendirme)
///
/// Ajan Organizasyonu (Peer Review):
///   Sorular üretildikten sonra IGraderAgent'a sunulur.
///   "Bu sorular ders içeriği ile uyumlu mu?" kontrolünden geçmeden veritabanına yazılmaz.
///   Eğer Grader reddederse, daha basit 3 soruluk yedek set alınır (Self-Refining).
///
/// Faz 16: Adaptive Quiz entegrasyonu — Redis'ten öğrenci zayıf yönleri + goal context
///         çekilerek quiz üretim prompt'una enjekte ediliyor. Race condition koruması eklendi.
/// </summary>
public class QuizAgent : IQuizAgent
{
    private readonly IAIAgentFactory _factory;
    private readonly IGraderAgent _grader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<QuizAgent> _logger;

    public QuizAgent(
        IAIAgentFactory factory,
        IGraderAgent grader,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<QuizAgent> logger)
    {
        _factory = factory;
        _grader = grader;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task GeneratePendingQuizAsync(Guid sessionId, Guid topicId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var session = await db.Sessions.Include(s => s.Messages).FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null) return;

        // Faz 16: Race condition koruması — Orkestratör zaten QuizMode'a geçtiyse çakışma
        if (session.CurrentState == SessionState.QuizMode && !string.IsNullOrWhiteSpace(session.PendingQuiz))
        {
            _logger.LogInformation("[QuizAgent] Session zaten QuizMode'da ve quiz mevcut — çift üretim engellendi. SessionId={SessionId}", sessionId);
            return;
        }

        var topic = await db.Topics.FindAsync(topicId);
        bool isModuleLevel = topic?.ParentTopicId == null;
        int minQuestions = isModuleLevel ? 15 : 3;
        int maxQuestions = isModuleLevel ? 20 : 5;
        string quizLevelType = isModuleLevel ? "Müfredat Değerlendirme Sınavı" : "Konu Kavrama Testi";
        string topicTitle = topic?.Title ?? "Konu";

        _logger.LogInformation("[QuizAgent] Seviye: {Level} | Soru Aralığı: {Min}-{Max} | Konu: {Topic}",
            quizLevelType, minQuestions, maxQuestions, topicTitle);

        // ── 0. Adaptive Quiz: Redis'ten öğrenci profil + goal context çek ─────
        string weaknessInfo = "";
        string goalContext = topic?.PhaseMetadata ?? "";
        try
        {
            var redis = scope.ServiceProvider.GetRequiredService<IRedisMemoryService>();
            var studentProfile = await redis.GetStudentProfileAsync(topicId);
            if (studentProfile.HasValue && !string.IsNullOrWhiteSpace(studentProfile.Value.weaknesses))
            {
                weaknessInfo = $"\n\n[ZAYIF YÖN ODAĞI — ADAPTİF SINAV KURALI]:\nBu öğrenci daha önce şu konularda zorlandı:\n{studentProfile.Value.weaknesses}\n\nSoruların en az %40'ı bu zayıf yönlere odaklanmalı.";
                _logger.LogInformation("[QuizAgent] Adaptive: Zayıf yönler quiz'e enjekte ediliyor.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[QuizAgent] Redis weakness fetch başarısız.");
        }

        // ── 1. OpenTrivia DB Entegrasyonu (CS, Math, Science için) ───────────
        string triviaContext = "";
        int? categoryId = topicTitle.ToLower() switch
        {
            var t when t.Contains("bilgisayar") || t.Contains("yazılım") || t.Contains("computer") || t.Contains("kod") => 18,
            var t when t.Contains("matematik") || t.Contains("math") => 19,
            var t when t.Contains("bilim") || t.Contains("science") || t.Contains("doğa") => 17,
            _ => null
        };

        if (categoryId.HasValue)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("OpenTrivia");
                var diff = isModuleLevel ? "medium" : "easy";
                var url = $"api.php?amount=5&category={categoryId.Value}&difficulty={diff}&type=multiple";
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                    {
                        var sb = new StringBuilder("\n[OPEN TRIVIA DB - REFERANS SORULAR]:\n");
                        foreach (var q in results.EnumerateArray())
                        {
                            var ques = System.Web.HttpUtility.HtmlDecode(q.GetProperty("question").GetString() ?? "");
                            var ans = System.Web.HttpUtility.HtmlDecode(q.GetProperty("correct_answer").GetString() ?? "");
                            sb.AppendLine($"- Soru: {ques} (Cevap: {ans})");
                        }
                        triviaContext = sb.ToString();
                        _logger.LogInformation("[QuizAgent] OpenTrivia DB'den referans sorular alındı.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[QuizAgent] OpenTrivia DB bağlantısı başarısız.");
            }
        }

        // ── 2. Bağlam Hazırlığı ──────────────────────────────────────────────
        var context = !string.IsNullOrWhiteSpace(session.Summary)
            ? session.Summary
            : string.Join("\n", session.Messages.TakeLast(8).Select(m => $"{m.Role}: {m.Content}"));

        // ── 3. Quiz Üretim Prompt'u ──────────────────────────────────────────────
        var systemPrompt = $$"""
            Sen Orka AI'nın 'Pedagoji ve Ölçme Değerlendirme' uzmanısın.
            Görevin: "{{topicTitle}}" konusu için {{minQuestions}}-{{maxQuestions}} soruluk yüksek kaliteli bir pekiştirme testi hazırlamak.
            Öğrencinin Özel Hedefi / Sınavı: {{(string.IsNullOrWhiteSpace(goalContext) ? "Genel Başarı" : goalContext)}}

            {{triviaContext}}
            {{weaknessInfo}}

            KALİTE VE KONU KURALLARI (KRİTİK):
            1. Konu 'Yazılım' değilse ASLA yazılım/kod sorusu sorma. Konu neyse ona odaklan.
            2. Basit ezber soruları yerine; mantık yürütme, senaryo analizi ve uygulama odaklı sorular üret.
            3. Şıklar (çeldiriciler) birbirine yakın ve düşündürücü olmalıdır.
            4. Eğer yukarıda Open Trivia referansları varsa, onları ders içeriğiyle harmanlayarak kullanabilirsin.
            5. Testi hazırlarken ÖĞRENCİNİN HEDEFİNİ dikkate al. Zorluk derecesini ve soru tiplerini bu hedefe uygun hale getir.

            ÇIKTI FORMATI (SADECE JSON DIZISI):
            [
              {
                "question": "Soru metni - senaryo veya örneğe dayalı",
                "options": [
                  {"id": "opt-0", "text": "...", "isCorrect": false},
                  {"id": "opt-1", "text": "...", "isCorrect": true},
                  {"id": "opt-2", "text": "...", "isCorrect": false},
                  {"id": "opt-3", "text": "...", "isCorrect": false}
                ],
                "explanation": "Detaylı pedagojik açıklama",
                "topic": "{{topicTitle}}",
                "difficulty": "orta"
              }
            ]
            """;

        var userPrompt = $"Ders İçeriği (Bağlam):\n\n{context}";

        string questions;
        try
        {
            questions = await _factory.CompleteChatAsync(AgentRole.Grader, systemPrompt, userPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizAgent] Soru üretimi başarısız.");
            return;
        }

        // ── 4. Peer Review ──────────────────────────────────────────────────
        bool isApproved = await _grader.IsContextRelevantAsync(
            $"{topicTitle} pekiştirme soruları", questions);

        if (!isApproved)
        {
            _logger.LogWarning("[QuizAgent] Grader reddeti. Fallback çalışıyor.");
            var fallbackPrompt = $"Fiiliyata dayalı çok temel 3 soru hazırla: {topicTitle}. JSON formatında.";
            questions = await _factory.CompleteChatAsync(AgentRole.Grader, fallbackPrompt, userPrompt);
        }

        // ── 5. Kaydet (Race condition koruması: tekrar kontrol et) ───────────
        try
        {
            // DB'den taze session çek — aradaki sürede orkestratör quiz üretmiş olabilir
            var freshSession = await db.Sessions.FindAsync(sessionId);
            if (freshSession != null && freshSession.CurrentState == SessionState.QuizMode && !string.IsNullOrWhiteSpace(freshSession.PendingQuiz))
            {
                _logger.LogInformation("[QuizAgent] Kayıt anında session zaten QuizMode — orkestratör quiz'i öncelikleniyor. QuizAgent quiz'i atıldı.");
                return;
            }

            var cleanJson = questions.Replace("```json", "").Replace("```", "").Trim();
            session.PendingQuiz = cleanJson;
            session.CurrentState = Orka.Core.Enums.SessionState.QuizPending;
            await db.SaveChangesAsync();
            _logger.LogInformation("[QuizAgent] ✅ Quiz başarıyla oluşturuldu.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizAgent] Kayıt hatası.");
        }
    }
}

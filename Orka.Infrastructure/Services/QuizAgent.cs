using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using System;
using System.Linq;
using System.Threading.Tasks;

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
/// </summary>
public class QuizAgent : IQuizAgent
{
    private readonly IAIAgentFactory _factory;
    private readonly IGraderAgent _grader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QuizAgent> _logger;

    public QuizAgent(
        IAIAgentFactory factory,
        IGraderAgent grader,
        IServiceScopeFactory scopeFactory,
        ILogger<QuizAgent> logger)
    {
        _factory      = factory;
        _grader       = grader;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task GeneratePendingQuizAsync(Guid sessionId, Guid topicId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var session = await db.Sessions.Include(s => s.Messages).FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null) return;

        // ── Konu Türünü Belirle: Modül mü? Ders mi? ─────────────────────────
        var topic = await db.Topics.FindAsync(topicId);
        bool isModuleLevel   = topic?.ParentTopicId == null; // Üst konu = modül/müfredat
        int  minQuestions    = isModuleLevel ? 15 : 3;
        int  maxQuestions    = isModuleLevel ? 20 : 5;
        string quizLevelType = isModuleLevel ? "Müfredat Değerlendirme Sınavı" : "Konu Kavrama Testi";
        string topicTitle    = topic?.Title ?? "Konu";

        _logger.LogInformation("[QuizAgent] Seviye: {Level} | Soru Aralığı: {Min}-{Max} | Konu: {Topic}",
            quizLevelType, minQuestions, maxQuestions, topicTitle);

        // ── Bağlam: Son mesajlar veya önceki özet ─────────────────────────────
        var context = !string.IsNullOrWhiteSpace(session.Summary)
            ? session.Summary
            : string.Join("\n", session.Messages.TakeLast(8).Select(m => $"{m.Role}: {m.Content}"));

        // ── Quiz Üretim Prompt'u ──────────────────────────────────────────────
        var systemPrompt = $$"""
            Sen Orka AI'nın sınav üretmekle görevli Pedagoji Uzmanısın.
            Görev: Aşağıdaki ders içeriğine bakarak "{{topicTitle}}" konusunda geçerli, öğretici ve bağlamsal sorular hazırla.

            [{{quizLevelType.ToUpper()}}]
            {{minQuestions}} ile {{maxQuestions}} arasında soru hazırla.
            Konunun derinliğine ve kapsamına göre bu aralıkta kaç soru gerekiyorsa o kadar üret.

            KALİTE KURALLARI:
            - Her soru, ders içeriğinde gerçekten işlenmiş kavramları sormalıdır.
            - Müfredat dışı ya da içerikte bulunmayan şeyler sorulmamalıdır.
            - Sorular öğrenci düzeyine uygun olmalıdır (ilkokul-lise aralığı desteklenecek).
            - Her sorunun 4 şıkkı (A, B, C, D) ve yalnızca 1 doğru cevabı olsun.

            ÇIKTI FORMATI (Kesinlikle bu JSON array formatı):
            [
              {
                "question": "Soru metni buraya",
                "options": [
                  {"id": "opt-0", "text": "Şık A metni", "isCorrect": false},
                  {"id": "opt-1", "text": "Şık B metni", "isCorrect": true},
                  {"id": "opt-2", "text": "Şık C metni", "isCorrect": false},
                  {"id": "opt-3", "text": "Şık D metni", "isCorrect": false}
                ],
                "explanation": "Bu sorunun doğru cevabı neden B olduğunu kısaca açıkla.",
                "topic": "{{topicTitle}}",
                "difficulty": "kolay|orta|zor"
              }
            ]

            SADECE JSON döndür. Başka açıklama, giriş veya kapanış cümlesi YAZMA.
            """;

        var userPrompt = $"Ders İçeriği:\n\n{context}";

        string questions;
        try
        {
            _logger.LogInformation("[QuizAgent] {Level} soruları üretiliyor. Konu: {Topic}", quizLevelType, topicTitle);
            questions = await _factory.CompleteChatAsync(AgentRole.Grader, systemPrompt, userPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizAgent] Soru üretimi başarısız. SessionId={SessionId}", sessionId);
            return;
        }

        // ── Peer Review: Grader Kalite Kontrolü ─────────────────────────────
        _logger.LogInformation("[QuizAgent] Grader kalite denetimi başlatıldı.");
        bool isApproved = await _grader.IsContextRelevantAsync(
            $"{topicTitle} konusu için hazırlanan pekiştirme soruları",
            questions);

        if (!isApproved)
        {
            _logger.LogWarning("[QuizAgent] Grader soruları reddetti. Daha basit 3 soruluk yedek set alınıyor.");

            // ── Self-Refining: Grader Reddederse Basit Set ─────────────────
            var fallbackPrompt = $"""
                Çok kısa ve basit {topicTitle} konusunda 3 soru hazırla.
                Öğrenciler bu konuyu yeni öğrendi, sorular çok temel olmalı.
                Aynı JSON array formatında döndür. SADECE JSON yaz.
                """;

            try
            {
                questions = await _factory.CompleteChatAsync(AgentRole.Grader, fallbackPrompt, userPrompt);
                _logger.LogInformation("[QuizAgent] Yedek soru seti oluşturuldu.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QuizAgent] Yedek soru üretimi de başarısız.");
                return;
            }
        }

        // ── Onaylanan Soruları Kaydet ─────────────────────────────────────────
        try
        {
            var cleanJson = questions.Replace("```json", "").Replace("```", "").Trim();
            session.PendingQuiz = cleanJson;
            session.CurrentState = Orka.Core.Enums.SessionState.QuizPending;
            await db.SaveChangesAsync();

            _logger.LogInformation("[QuizAgent] ✅ {Level} soruları kaydedildi. Konu: {Topic}", quizLevelType, topicTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizAgent] Soru kaydetme başarısız.");
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;
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
    private readonly IRedisMemoryService _redis;
    private readonly IEducatorCoreService _educatorCore;

    public QuizAgent(
        IAIAgentFactory factory,
        IGraderAgent grader,
        IServiceScopeFactory scopeFactory,
        ILogger<QuizAgent> logger,
        IRedisMemoryService redis,
        IEducatorCoreService educatorCore)
    {
        _factory      = factory;
        _grader       = grader;
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _redis        = redis;
        _educatorCore = educatorCore;
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

        _logger.LogInformation("[QuizAgent] Seviye: {Level} | Soru Araligi: {Min}-{Max} | TopicRef={TopicRef}",
            quizLevelType, minQuestions, maxQuestions, LogPrivacyGuard.SafeTextRef(topicTitle, "topic"));

        // ── Bağlam: Son mesajlar veya önceki özet ─────────────────────────────
        var context = !string.IsNullOrWhiteSpace(session.Summary)
            ? session.Summary
            : string.Join("\n", session.Messages.TakeLast(8).Select(m => $"{m.Role}: {m.Content}"));

        // ── Faz 17: YouTube Distractor (Çeldirici) Üretimi ────────────────────
        string youtubeDistractorBlock = "";
        try
        {
            var ytData = await _redis.GetYouTubeContextAsync(topicId);
            if (!string.IsNullOrWhiteSpace(ytData))
            {
                var teachingReference = await _educatorCore.NormalizeTeachingReferenceAsync(topicId, ytData);
                if (teachingReference != null)
                    youtubeDistractorBlock = BuildYouTubeDistractorBlock(teachingReference);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[QuizAgent] YouTube context okunamadi, standart celdiriciler kullanilacak. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(topicId, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }

        // ── Quiz Üretim Prompt'u ──────────────────────────────────────────────
        var systemPrompt = $$"""
            Sen Orka AI'nın sınav üretmekle görevli Pedagoji Uzmanısın.
            Görev: Aşağıdaki ders içeriğine bakarak "{{topicTitle}}" konusunda geçerli, öğretici ve bağlamsal sorular hazırla.

            [{{quizLevelType.ToUpper()}}]
            {{minQuestions}} ile {{maxQuestions}} arasında soru hazırla.
            Konunun derinliğine ve kapsamına göre bu aralıkta kaç soru gerekiyorsa o kadar üret.
            {{youtubeDistractorBlock}}

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
            _logger.LogInformation("[QuizAgent] {Level} sorulari uretiliyor. TopicRef={TopicRef}",
                quizLevelType,
                LogPrivacyGuard.SafeTextRef(topicTitle, "topic"));
            questions = await _factory.CompleteChatAsync(AgentRole.Quiz, systemPrompt, userPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizAgent] Soru uretimi basarisiz. SessionRef={SessionRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(sessionId, "session"),
                LogPrivacyGuard.SafeExceptionType(ex));
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
                questions = await _factory.CompleteChatAsync(AgentRole.Quiz, fallbackPrompt, userPrompt);
                _logger.LogInformation("[QuizAgent] Yedek soru seti oluşturuldu.");
            }
            catch (Exception ex)
            {
                _logger.LogError("[QuizAgent] Yedek soru uretimi de basarisiz. SessionRef={SessionRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeId(sessionId, "session"),
                    LogPrivacyGuard.SafeExceptionType(ex));
                return;
            }
        }

        // ── Onaylanan Soruları Kaydet ─────────────────────────────────────────
        try
        {
            var cleanJson = questions.Replace("```json", "").Replace("```", "").Trim();
            
            // Validate if the JSON compiles and represents a valid array of questions
            try
            {
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(cleanJson);
                if (jsonDoc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    throw new System.Text.Json.JsonException("Quiz JSON is not a JSON Array.");
                }
            }
            catch (Exception jsonEx)
            {
                _logger.LogWarning(jsonEx, "[QuizAgent] LLM generated malformed/invalid JSON quiz. Applying safe local fallback template. Topic={Topic}", topicTitle);
                
                // Construct a syntactically correct safe template using the topic and concepts to avoid crashing the student
                cleanJson = $$"""
                [
                    {
                        "question": "{{topicTitle}} konusundaki ana fikirleri ve temel kavramları genel hatlarıyla hatırlıyor musunuz?",
                        "options": ["Evet, gayet iyi hatırlıyorum.", "Bazı noktaları tekrar etmem gerekebilir.", "Çok az hatırlıyorum.", "Neredeyse tamamen unuttum."],
                        "answer": "Evet, gayet iyi hatırlıyorum."
                    },
                    {
                        "question": "{{topicTitle}} başlığı altındaki kazanımları kendi cümlelerinizle bir başkasına açıklayabilir misiniz?",
                        "options": ["Çok rahat açıklayabilirim.", "Ana hatlarıyla açıklarım ama ayrıntıda takılırım.", "Sadece tanımsal düzeyde açıklayabilirim.", "Açıklamakta çok zorlanırım."],
                        "answer": "Çok rahat açıklayabilirim."
                    },
                    {
                        "question": "{{topicTitle}} konusundaki pratik veya kodlama örneklerini tek başınıza sıfırdan uygulayabilir misiniz?",
                        "options": ["Kesinlikle uygulayabilirim.", "Destek alarak veya dökümana bakarak uygulayabilirim.", "Sadece hazır şablonları düzenleyebilirim.", "Uygulamakta çok zorlanırım."],
                        "answer": "Kesinlikle uygulayabilirim."
                    }
                ]
                """;
            }

            session.PendingQuiz = cleanJson;
            session.CurrentState = Orka.Core.Enums.SessionState.QuizPending;
            await db.SaveChangesAsync();

            _logger.LogInformation("[QuizAgent] {Level} sorulari kaydedildi. TopicRef={TopicRef}",
                quizLevelType,
                LogPrivacyGuard.SafeTextRef(topicTitle, "topic"));
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizAgent] Soru kaydetme basarisiz. SessionRef={SessionRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(sessionId, "session"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }
    }
    private static string BuildYouTubeDistractorBlock(TeachingReference reference)
    {
        var mistakes = reference.CommonMistakes.Count == 0
            ? "No explicit common mistakes were extracted; infer likely misconceptions from the lesson context."
            : string.Join("\n", reference.CommonMistakes.Select(m => $"- {m}"));

        var examples = reference.Examples.Count == 0
            ? "Use only examples from the lesson context."
            : string.Join("\n", reference.Examples.Select(e => $"- {e}"));

        return $"""

            [YOUTUBE PEDAGOGY REFERENCE - DISTRACTOR QUALITY ONLY]
            Source: [youtube:{reference.SourceId}] Status: {reference.Status}
            Use this only to improve wrong options, misconception coverage, and practice style.
            Do not ask questions about unsupported video facts.
            Common mistakes to turn into distractors:
            {mistakes}
            Example style to mirror:
            {examples}
            """;
    }
}

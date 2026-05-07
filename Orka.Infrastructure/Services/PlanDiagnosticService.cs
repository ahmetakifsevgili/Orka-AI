using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class PlanDiagnosticService : IPlanDiagnosticService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan StateTtl = TimeSpan.FromHours(2);

    private readonly OrkaDbContext _db;
    private readonly IKorteksAgent _korteks;
    private readonly IPlanResearchCompressor _compressor;
    private readonly IAIAgentFactory _factory;
    private readonly IPlanDiagnosticStateStore _stateStore;
    private readonly IQuizAttemptRecorder _quizRecorder;
    private readonly IDeepPlanAgent _deepPlan;
    private readonly ILogger<PlanDiagnosticService> _logger;

    public PlanDiagnosticService(
        OrkaDbContext db,
        IKorteksAgent korteks,
        IPlanResearchCompressor compressor,
        IAIAgentFactory factory,
        IPlanDiagnosticStateStore stateStore,
        IQuizAttemptRecorder quizRecorder,
        IDeepPlanAgent deepPlan,
        ILogger<PlanDiagnosticService> logger)
    {
        _db = db;
        _korteks = korteks;
        _compressor = compressor;
        _factory = factory;
        _stateStore = stateStore;
        _quizRecorder = quizRecorder;
        _deepPlan = deepPlan;
        _logger = logger;
    }

    public async Task<StartPlanDiagnosticResponse> StartAsync(
        Guid userId,
        StartPlanDiagnosticRequest request,
        CancellationToken ct = default)
    {
        var topic = await _db.Topics.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TopicId && t.UserId == userId, ct);

        if (topic == null)
        {
            throw new InvalidOperationException("Topic not found for plan diagnostic start.");
        }

        var approvedResearchIntent = NormalizeApprovedIntent(request.ApprovedResearchIntent);
        if (string.IsNullOrWhiteSpace(approvedResearchIntent))
        {
            throw new InvalidOperationException("Approved study intent is required before Korteks research.");
        }

        var approvedMainTopic = CleanOrDefault(request.ApprovedMainTopic, topic.Title);
        var approvedFocusArea = CleanOrDefault(request.ApprovedFocusArea, "genel kapsam");
        var approvedStudyGoal = CleanOrDefault(request.ApprovedStudyGoal, "ogrenme ve pratik");
        var topicTitle = BuildApprovedTopicTitle(request.TopicTitle, approvedMainTopic, approvedFocusArea, topic.Title);
        var requestedQuestionCount = DetermineDiagnosticQuestionCount(approvedMainTopic, approvedFocusArea, approvedResearchIntent);
        var planRequestId = Guid.NewGuid();
        var quizRunId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var state = new PlanDiagnosticStateDto
        {
            PlanRequestId = planRequestId,
            UserId = userId,
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            TopicTitle = topicTitle,
            IntentRequestId = request.IntentRequestId,
            RawStudyRequest = CleanOrDefault(request.RawStudyRequest, topicTitle),
            ApprovedMainTopic = approvedMainTopic,
            ApprovedFocusArea = approvedFocusArea,
            ApprovedStudyGoal = approvedStudyGoal,
            ApprovedResearchIntent = approvedResearchIntent,
            UserLevel = string.IsNullOrWhiteSpace(request.UserLevel) ? topic.LanguageLevel ?? "Bilinmiyor" : request.UserLevel.Trim(),
            Status = PlanDiagnosticStatus.Researching,
            QuizRunId = quizRunId,
            CreatedAt = now,
            ExpiresAt = now.Add(StateTtl)
        };

        await _stateStore.SaveAsync(state, ct);

        try
        {
            var research = await _korteks.RunResearchWithEvidenceAsync(approvedResearchIntent, userId, request.TopicId, ct: ct);
            var compressed = _compressor.Compress(research);
            var compressedBlock = _compressor.BuildPromptBlock(compressed);

            state.Status = PlanDiagnosticStatus.ResearchReady;
            state.CompressedResearchContextJson = JsonSerializer.Serialize(compressed, JsonOptions);
            state.CompressedResearchPromptBlock = compressedBlock;
            state.GroundingMode = compressed.GroundingMode;
            state.SourceCount = compressed.SourceCount;
            await _stateStore.SaveAsync(state, ct);

            var quizJson = await GenerateDiagnosticQuizFromStoredContextAsync(
                topicTitle,
                compressedBlock,
                requestedQuestionCount,
                ct);
            var questionCount = CountQuestions(quizJson);

            _db.QuizRuns.Add(new QuizRun
            {
                Id = quizRunId,
                UserId = userId,
                TopicId = request.TopicId,
                SessionId = request.SessionId,
                QuizType = "baseline",
                Status = "active",
                TotalQuestions = questionCount,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    planRequestId,
                    intentRequestId = request.IntentRequestId,
                    approvedMainTopic,
                    approvedFocusArea,
                    approvedResearchIntent
                }, JsonOptions),
                CreatedAt = now
            });
            await _db.SaveChangesAsync(ct);

            state.Status = PlanDiagnosticStatus.QuizPending;
            state.QuizQuestionCount = questionCount;
            await _stateStore.SaveAsync(state, ct);

            return new StartPlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                QuizRunId = state.QuizRunId,
                TopicId = state.TopicId,
                TopicTitle = state.TopicTitle,
                Status = state.Status,
                QuestionsJson = quizJson,
                GroundingMode = state.GroundingMode,
                SourceCount = state.SourceCount,
                QuizQuestionCount = state.QuizQuestionCount,
                IntentRequestId = state.IntentRequestId,
                ApprovedMainTopic = state.ApprovedMainTopic,
                ApprovedFocusArea = state.ApprovedFocusArea,
                ApprovedStudyGoal = state.ApprovedStudyGoal,
                ApprovedResearchIntent = state.ApprovedResearchIntent
            };
        }
        catch (Exception ex)
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = ex.Message;
            await _stateStore.SaveAsync(state, ct);
            _logger.LogWarning(ex, "[PlanDiagnostic] Start failed. PlanRequestId={PlanRequestId}", planRequestId);
            throw;
        }
    }

    public async Task<PlanDiagnosticAnswerResponse> RecordAnswerAsync(
        Guid userId,
        Guid planRequestId,
        RecordQuizAttemptRequest request,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, planRequestId, ct);
        request.QuizRunId = state.QuizRunId;
        request.TopicId = state.TopicId;
        request.SessionId ??= state.SessionId;

        await _quizRecorder.RecordAsync(userId, request, ct);

        state.AnsweredQuestionCount = await _db.QuizAttempts
            .CountAsync(a => a.UserId == userId && a.QuizRunId == state.QuizRunId, ct);

        if (state.QuizQuestionCount > 0 && state.AnsweredQuestionCount >= state.QuizQuestionCount)
        {
            state.Status = PlanDiagnosticStatus.QuizCompleted;
            state.QuizCompletedAt ??= DateTime.UtcNow;
        }

        await _stateStore.SaveAsync(state, ct);

        return new PlanDiagnosticAnswerResponse
        {
            PlanRequestId = state.PlanRequestId,
            QuizRunId = state.QuizRunId,
            Status = state.Status,
            AnsweredQuestionCount = state.AnsweredQuestionCount,
            QuizQuestionCount = state.QuizQuestionCount
        };
    }

    public async Task<FinalizePlanDiagnosticResponse> FinalizeAsync(
        Guid userId,
        FinalizePlanDiagnosticRequest request,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, request.PlanRequestId, ct);

        if (state.Status == PlanDiagnosticStatus.PlanGenerated && state.GeneratedPlanRootTopicId.HasValue)
        {
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = true,
                Message = "Plan was already generated.",
                GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
                GeneratedTopicIds = await GetGeneratedPlanTopicIdsAsync(userId, state.GeneratedPlanRootTopicId.Value, ct)
            };
        }

        state.AnsweredQuestionCount = await _db.QuizAttempts
            .CountAsync(a => a.UserId == userId && a.QuizRunId == state.QuizRunId, ct);

        if (state.QuizQuestionCount <= 0 || state.AnsweredQuestionCount < state.QuizQuestionCount)
        {
            state.Status = state.AnsweredQuestionCount > 0 ? PlanDiagnosticStatus.QuizPending : state.Status;
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = "Diagnostic quiz is incomplete."
            };
        }

        state.Status = PlanDiagnosticStatus.PlanGenerating;
        await _stateStore.SaveAsync(state, ct);

        var diagnosticSummary = await BuildCurrentDiagnosticSummaryAsync(userId, state.QuizRunId, ct);
        var planResult = await _deepPlan.GenerateAndSaveDeepPlanFromDiagnosticAsync(
            state.TopicId,
            state.TopicTitle,
            userId,
            state.CompressedResearchPromptBlock,
            diagnosticSummary,
            state.UserLevel);

        state.Status = PlanDiagnosticStatus.PlanGenerated;
        state.GeneratedPlanRootTopicId = state.TopicId;
        await _stateStore.SaveAsync(state, ct);

        return new FinalizePlanDiagnosticResponse
        {
            PlanRequestId = state.PlanRequestId,
            Status = state.Status,
            PlanGenerated = true,
            GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
            GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList()
        };
    }

    public async Task<FinalizePlanDiagnosticResponse> SkipAndGenerateAsync(
        Guid userId,
        Guid planRequestId,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, planRequestId, ct);

        state.AnsweredQuestionCount = await _db.QuizAttempts
            .CountAsync(a => a.UserId == userId && a.QuizRunId == state.QuizRunId, ct);

        if (state.Status == PlanDiagnosticStatus.PlanGenerated && state.GeneratedPlanRootTopicId.HasValue)
        {
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = true,
                Message = "Plan was already generated.",
                GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
                GeneratedTopicIds = await GetGeneratedPlanTopicIdsAsync(userId, state.GeneratedPlanRootTopicId.Value, ct)
            };
        }

        state.Status = PlanDiagnosticStatus.PlanGenerating;
        state.QuizCompletedAt ??= DateTime.UtcNow;
        await _stateStore.SaveAsync(state, ct);

        var diagnosticSummary = string.Join("\n", new[]
        {
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]",
            "Mode: StartFromZero",
            $"QuizRunId: {state.QuizRunId}",
            $"Answered: {state.AnsweredQuestionCount}",
            "Correct: 0",
            "Wrong: 0",
            "WeakConcepts: none",
            "MistakePatterns: none",
            "Instruction: The learner explicitly skipped the diagnostic quiz and chose to start from zero. Build a beginner-safe plan, but do not infer weak skills or record fake mistakes from skipped questions."
        });

        var planResult = await _deepPlan.GenerateAndSaveDeepPlanFromDiagnosticAsync(
            state.TopicId,
            state.TopicTitle,
            userId,
            state.CompressedResearchPromptBlock,
            diagnosticSummary,
            state.UserLevel);

        state.Status = PlanDiagnosticStatus.PlanGenerated;
        state.GeneratedPlanRootTopicId = state.TopicId;
        await _stateStore.SaveAsync(state, ct);

        return new FinalizePlanDiagnosticResponse
        {
            PlanRequestId = state.PlanRequestId,
            Status = state.Status,
            PlanGenerated = true,
            Message = "Diagnostic quiz skipped; beginner plan generated without fake quiz mistakes.",
            GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
            GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList()
        };
    }

    private async Task<string> GenerateDiagnosticQuizFromStoredContextAsync(
        string topicTitle,
        string compressedResearchPromptBlock,
        int requestedQuestionCount,
        CancellationToken ct)
    {
        var quizIntelligenceBrief = PlanIntelligenceBriefBuilder.BuildForDiagnosticQuiz(
            topicTitle,
            compressedResearchPromptBlock);

        var systemPrompt = $$"""
            Sen profesyonel bir 'Egitim Tanilama Uzmani' botusun.
            Gorevin: '{{topicTitle}}' konusunda {{requestedQuestionCount}} soruluk seviye tespit quiz'i uretmek.

            {{quizIntelligenceBrief}}

            KURALLAR:
            - Soru sayisi tam olarak {{requestedQuestionCount}} olacak; 15'ten az, 25'ten fazla olmayacak.
            - Ham Korteks raporu varsayma; sadece bu filtrelenmis brief'i ve konu basligini kullan.
            - Korteks kaynak basliklarini veya web/video metinlerini soru kokune kopyalama.
            - Konu Java algoritmalari ise sorular Java + algoritma/veri yapisi/pratik ekseninde kalmali; C#, .NET, Visual Studio veya baska teknoloji sizdirmamalı.
            - Generic pipeline, "input -> transform", "tani sorusu" gibi ic sistem kalibi kullanma.
            - Orka IDE, sandbox veya urun arayuzu etiketlerini soru kokune, seceneklere ya da dogru cevaba yazma; quiz kavrami olcer, urun ozelligini degil.
            - conceptual, procedural, application, analysis ve misconception_probe soru tiplerini karisik kullan.
            - kolay, orta ve zor dagilimini dengeli kur.
            - Soru metinleri birbirinin kopyasi veya yakin tekrari olmasin.
            - En az 8 farkli conceptTag kullan.
            - En az 4 farkli questionType kullan.
            - En az 5 soru beklenen kavram yanilgisini hedeflesin.
            - Teknik konularda en az bir soru gercek kod parcasi, kod okuma veya hata ayiklama senaryosu icersin.
            - Her soru su alanlari icersin: question, options, correctAnswer, explanation, skillTag, difficulty, conceptTag, learningObjective, questionType, expectedMisconceptionCategory.

            SADECE JSON array dondur.
            """;

        var rawQuiz = await _factory.CompleteChatAsync(
            AgentRole.DeepPlan,
            systemPrompt,
            $"Konu: \"{topicTitle}\" icin tam {requestedQuestionCount} adet tanilayici baseline sorusu uret.",
            ct);
        try
        {
            var validatedQuiz = DiagnosticQuizQualityGate.EnsureQualityOrThrow(rawQuiz, topicTitle, requestedQuestionCount, out var quality);
            if (!quality.IsAcceptable)
            {
                _logger.LogWarning(
                    "[PlanDiagnostic] Diagnostic quiz quality failed. Topic={Topic} Failures={Failures}",
                    topicTitle,
                    string.Join(" | ", quality.Failures));
            }

            return validatedQuiz;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "[PlanDiagnostic] Diagnostic quiz provider output failed quality gate; using domain-aware fallback. Topic={Topic}",
                topicTitle);

            var fallback = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint(topicTitle);
            var fallbackCount = DiagnosticQuizQualityGate.CountQuestions(fallback);
            if (fallbackCount is < 15 or > 25)
            {
                throw new InvalidOperationException($"Fallback diagnostic quiz is invalid; count={fallbackCount}.", ex);
            }

            return fallback;
        }
    }

    private async Task<List<Guid>> GetGeneratedPlanTopicIdsAsync(Guid userId, Guid rootTopicId, CancellationToken ct)
    {
        var moduleIds = await _db.Topics.AsNoTracking()
            .Where(t => t.UserId == userId && t.ParentTopicId == rootTopicId && t.PlanIntent == "Module")
            .OrderBy(t => t.Order)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (moduleIds.Count == 0)
        {
            return [];
        }

        var lessonIds = await _db.Topics.AsNoTracking()
            .Where(t => t.UserId == userId && t.ParentTopicId.HasValue && moduleIds.Contains(t.ParentTopicId.Value))
            .OrderBy(t => t.Order)
            .Select(t => t.Id)
            .ToListAsync(ct);

        return lessonIds.Count > 0 ? lessonIds : moduleIds;
    }

    private async Task<string> BuildCurrentDiagnosticSummaryAsync(Guid userId, Guid quizRunId, CancellationToken ct)
    {
        var attempts = await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.QuizRunId == quizRunId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        if (attempts.Count == 0)
        {
            return "[PLAN DIAGNOSTIC QUIZ SUMMARY]\nNo answers were recorded.";
        }

        var correct = attempts.Where(a => a.IsCorrect).ToList();
        var wrong = attempts.Where(a => !a.IsCorrect).ToList();
        var knownSummary = correct
            .Select(ExtractWeakConcept)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();
        var conceptSummary = wrong
            .Select(ExtractWeakConcept)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();
        var fastTrack = knownSummary
            .Where(item => !conceptSummary.Any(weak => weak.StartsWith(item.Split(':')[0], StringComparison.OrdinalIgnoreCase)))
            .Take(8)
            .ToList();
        var mistakeSummary = wrong
            .Select(ExtractMistakePattern)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();
        var accuracyPercent = attempts.Count == 0
            ? 0
            : (int)Math.Round(attempts.Count(a => a.IsCorrect) * 100.0 / attempts.Count);
        var measuredLevel = accuracyPercent switch
        {
            >= 85 => "advanced",
            >= 65 => "intermediate",
            >= 40 => "developing",
            _ => "beginner"
        };

        return string.Join("\n", new[]
        {
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]",
            $"QuizRunId: {quizRunId}",
            $"Answered: {attempts.Count}",
            $"Correct: {attempts.Count(a => a.IsCorrect)}",
            $"Wrong: {wrong.Count}",
            $"AccuracyPercent: {accuracyPercent}",
            $"MeasuredLevel: {measuredLevel}",
            $"KnownConcepts: {(knownSummary.Count == 0 ? "none" : string.Join(" | ", knownSummary))}",
            $"FastTrackConcepts: {(fastTrack.Count == 0 ? "none" : string.Join(" | ", fastTrack))}",
            $"PracticeConcepts: {(knownSummary.Count == 0 ? "none" : string.Join(" | ", knownSummary.Take(8)))}",
            $"WeakConcepts: {(conceptSummary.Count == 0 ? "none" : string.Join(" | ", conceptSummary))}",
            $"MistakePatterns: {(mistakeSummary.Count == 0 ? "none" : string.Join(" | ", mistakeSummary))}",
            "Instruction: Move known concepts faster with short practice. Teach weak or mistaken concepts more slowly, logically, and with examples. Do not claim skipped or unanswered concepts are weaknesses."
        });
    }

    private static string? ExtractWeakConcept(QuizAttempt attempt)
    {
        if (!string.IsNullOrWhiteSpace(attempt.SkillTag))
        {
            return attempt.SkillTag.Trim();
        }

        if (!string.IsNullOrWhiteSpace(attempt.TopicPath))
        {
            var parts = attempt.TopicPath.Split(
                new[] { '/', '>', '|', '\\' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.LastOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(attempt.QuestionHash))
        {
            return attempt.QuestionHash.Trim();
        }

        return null;
    }

    private static string ExtractMistakePattern(QuizAttempt attempt)
    {
        var text = string.Join(" ",
            attempt.Explanation,
            attempt.CognitiveType,
            attempt.SourceRefsJson);

        foreach (var candidate in new[]
                 {
                     "Procedural",
                     "Reading",
                     "MisreadQuestion",
                     "Conceptual",
                     "Application",
                     "Careless"
                 })
        {
            if (text.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return string.IsNullOrWhiteSpace(attempt.CognitiveType)
            ? "IncorrectAnswer"
            : attempt.CognitiveType.Trim();
    }

    private async Task<PlanDiagnosticStateDto> RequireStateAsync(Guid userId, Guid planRequestId, CancellationToken ct)
    {
        var state = await _stateStore.GetAsync(planRequestId, ct);
        if (state == null || state.UserId != userId)
        {
            throw new InvalidOperationException("Plan diagnostic state was not found.");
        }

        return state;
    }

    private static string NormalizeApprovedIntent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 160 ? normalized : normalized[..160];
    }

    private static string CleanOrDefault(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string BuildApprovedTopicTitle(string? requestedTitle, string mainTopic, string focusArea, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(requestedTitle))
        {
            return CleanOrDefault(requestedTitle, fallback);
        }

        if (string.IsNullOrWhiteSpace(focusArea) || focusArea.Equals("genel kapsam", StringComparison.OrdinalIgnoreCase))
        {
            return CleanOrDefault(mainTopic, fallback);
        }

        return $"{mainTopic}: {focusArea}";
    }

    private static int DetermineDiagnosticQuestionCount(string mainTopic, string focusArea, string researchIntent)
    {
        var text = $"{mainTopic} {focusArea} {researchIntent}".ToLowerInvariant();
        var broadSignals = new[]
        {
            "algorithm",
            "algoritma",
            "data structure",
            "veri yap",
            "programming",
            "programlama",
            "sql",
            "kpss",
            "yks",
            "curriculum",
            "roadmap"
        };

        var narrowSignals = new[]
        {
            "temel",
            "intro",
            "giris",
            "syntax",
            "tek konu",
            "single topic"
        };

        var score = broadSignals.Count(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase)) -
                    narrowSignals.Count(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));

        return Math.Clamp(18 + score * 2, 15, 25);
    }

    private static int CountQuestions(string quizJson)
    {
        try
        {
            var count = DiagnosticQuizQualityGate.CountQuestions(quizJson);
            return count > 0 ? count : 20;
        }
        catch
        {
            // The model response will still be returned; default to the intended diagnostic size.
        }

        return 20;
    }
}

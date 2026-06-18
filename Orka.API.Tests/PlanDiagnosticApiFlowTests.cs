using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orka.Core.DTOs;
using Orka.Core.Constants;
using Orka.Core.DTOs.Chat;
using Orka.Core.DTOs.Korteks;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class PlanDiagnosticApiFlowTests
{
    [Fact]
    public async Task AsyncPlanDiagnosticApi_FollowsFrontendJourneyContract()
    {
        await using var factory = new ApiSmokeFactory("Development", configureServices: services =>
        {
            services.RemoveAll<IAIAgentFactory>();
            services.AddScoped<IAIAgentFactory, PlanDiagnosticApiAgentFactory>();
            services.RemoveAll<IDeepPlanAgent>();
            services.AddScoped<IDeepPlanAgent, PlanDiagnosticApiDeepPlanAgent>();
            services.RemoveAll<IBackgroundTaskQueue>();
            services.AddSingleton<IBackgroundTaskQueue, InlineScopedBackgroundTaskQueue>();
        });
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-api-flow");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "SQL index tuning");

        var intent = await user.Client.PostAsJsonAsync("/api/quiz/plan-diagnostic/intent", new
        {
            rawRequest = "SQL index ve sorgu optimizasyonu icin seviyemi olc ve plan cikar.",
            topicId,
            existingTopicTitle = "SQL index tuning"
        });
        Assert.Equal(HttpStatusCode.OK, intent.StatusCode);
        var intentBody = await intent.Content.ReadFromJsonAsync<StudyIntentPreviewResponse>();
        Assert.NotNull(intentBody);
        Assert.True(intentBody!.RequiresUserConfirmation);
        Assert.False(string.IsNullOrWhiteSpace(intentBody.ResearchIntent));

        var start = await user.Client.PostAsJsonAsync("/api/quiz/plan-diagnostic/start-async", new
        {
            topicId,
            topicTitle = "SQL index tuning",
            userLevel = "Bilinmiyor",
            intentRequestId = intentBody.IntentRequestId,
            rawStudyRequest = intentBody.RawRequest,
            approvedMainTopic = intentBody.MainTopic,
            approvedFocusArea = intentBody.FocusArea,
            approvedStudyGoal = intentBody.StudyGoal,
            approvedResearchIntent = intentBody.ResearchIntent
        });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var startBody = await start.Content.ReadFromJsonAsync<StartPlanDiagnosticResponse>();
        Assert.NotNull(startBody);
        Assert.True(startBody!.IsAsync);
        Assert.NotEqual(Guid.Empty, startBody.PlanRequestId);

        var status = await user.Client.GetAsync($"/api/quiz/plan-diagnostic/{startBody.PlanRequestId}/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var ready = await status.Content.ReadFromJsonAsync<StartPlanDiagnosticResponse>();
        Assert.NotNull(ready);
        Assert.True(ready!.IsReady, ready.ErrorMessage ?? ready.Message ?? "Queued diagnostic did not become ready.");
        Assert.Equal(PlanDiagnosticStatus.QuizPending, ready.Status);
        Assert.NotEqual(Guid.Empty, ready.QuizRunId);
        Assert.True(ready.QuizQuestionCount >= 15);
        Assert.Contains("assessmentItemId", ready.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", ready.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctOptionId", ready.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"isCorrect\"", ready.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", ready.QuestionsJson, StringComparison.OrdinalIgnoreCase);

        var questions = ParseDiagnosticQuestions(ready.QuestionsJson);
        Assert.Equal(ready.QuizQuestionCount, questions.Count);
        var firstAnswer = await user.Client.PostAsJsonAsync($"/api/quiz/plan-diagnostic/{ready.PlanRequestId}/attempt", BuildAnswer(questions[0], topicId, ready.QuizRunId, isCorrect: false));
        Assert.Equal(HttpStatusCode.OK, firstAnswer.StatusCode);
        var firstAnswerBody = await firstAnswer.Content.ReadFromJsonAsync<PlanDiagnosticAnswerResponse>();
        Assert.NotNull(firstAnswerBody);
        Assert.Equal(1, firstAnswerBody!.AnsweredQuestionCount);

        var duplicateFirstAnswer = await user.Client.PostAsJsonAsync($"/api/quiz/plan-diagnostic/{ready.PlanRequestId}/attempt", BuildAnswer(questions[0], topicId, ready.QuizRunId, isCorrect: false));
        Assert.Equal(HttpStatusCode.OK, duplicateFirstAnswer.StatusCode);
        var duplicateFirstAnswerBody = await duplicateFirstAnswer.Content.ReadFromJsonAsync<PlanDiagnosticAnswerResponse>();
        Assert.NotNull(duplicateFirstAnswerBody);
        Assert.Equal(1, duplicateFirstAnswerBody!.AnsweredQuestionCount);

        PlanDiagnosticAnswerResponse? lastAnswerBody = firstAnswerBody;
        for (var i = 1; i < questions.Count; i++)
        {
            var answer = await user.Client.PostAsJsonAsync($"/api/quiz/plan-diagnostic/{ready.PlanRequestId}/attempt", BuildAnswer(questions[i], topicId, ready.QuizRunId, isCorrect: i % 3 != 0));
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            lastAnswerBody = await answer.Content.ReadFromJsonAsync<PlanDiagnosticAnswerResponse>();
        }
        Assert.NotNull(lastAnswerBody);
        Assert.Equal(ready.QuizQuestionCount, lastAnswerBody!.AnsweredQuestionCount);

        var completeStatus = await user.Client.GetFromJsonAsync<StartPlanDiagnosticResponse>($"/api/quiz/plan-diagnostic/{ready.PlanRequestId}/status");
        Assert.NotNull(completeStatus);
        Assert.Equal(PlanDiagnosticStatus.QuizCompleted, completeStatus!.Status);

        var finalize = await user.Client.PostAsJsonAsync("/api/quiz/plan-diagnostic/finalize", new { ready.PlanRequestId });
        Assert.Equal(HttpStatusCode.OK, finalize.StatusCode);
        var finalizeBody = await finalize.Content.ReadFromJsonAsync<FinalizePlanDiagnosticResponse>();
        Assert.NotNull(finalizeBody);
        Assert.Equal(PlanDiagnosticStatus.PlanGenerated, finalizeBody!.Status);
        Assert.True(finalizeBody.PlanGenerated);
        Assert.NotEmpty(finalizeBody.GeneratedTopicIds);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var firstAttempts = await db.QuizAttempts
            .AsNoTracking()
            .Where(a => a.UserId == user.UserId && a.AssessmentItemId == questions[0].AssessmentItemId)
            .ToListAsync();
        var firstAttempt = Assert.Single(firstAttempts);
        Assert.Equal(1, await db.LearningSignals.CountAsync(s =>
            s.UserId == user.UserId &&
            s.QuizAttemptId == firstAttempt.Id &&
            s.SignalType == LearningSignalTypes.QuizAnswered));
        Assert.Equal(1, await db.LearningSignals.CountAsync(s =>
            s.UserId == user.UserId &&
            s.QuizAttemptId == firstAttempt.Id &&
            s.SignalType == LearningSignalTypes.WeaknessDetected));
        Assert.True(await db.WikiBlocks.CountAsync(b =>
            b.QuizAttemptId == firstAttempt.Id &&
            !b.IsDeleted) <= 2);

        var projectionBeforeTutor = await user.Client.GetFromJsonAsync<OrkaLearningStateDto>($"/api/learning/orka-state?topicId={topicId}")
            ?? throw new InvalidOperationException("Pre-tutor projection missing.");
        var missionBeforeTutor = await user.Client.GetFromJsonAsync<OrkaMissionControlDto>($"/api/learning/mission-control?topicId={topicId}")
            ?? throw new InvalidOperationException("Pre-tutor mission missing.");
        Assert.StartsWith("lsv_", projectionBeforeTutor.LearningStateVersion);
        Assert.Equal(projectionBeforeTutor.LearningStateVersion, missionBeforeTutor.LearningStateVersion);

        var tutor = await user.Client.PostAsJsonAsync("/api/chat/message", new
        {
            content = "Ilk yanlis yaptigim kavrami telafi et ve sonra mikro kontrol sor.",
            topicId
        });
        tutor.EnsureSuccessStatusCode();
        var tutorBody = await tutor.Content.ReadFromJsonAsync<ChatMessageResponse>();
        Assert.NotNull(tutorBody);
        Assert.True(
            tutorBody!.Metadata?.RemediationLesson != null ||
            tutorBody.Metadata?.TutorToolDecision?.SelectedAction == "start_remediation");
        var remediationStartedSignals = await db.LearningSignals
            .AsNoTracking()
            .Where(s =>
            s.UserId == user.UserId &&
            s.TopicId == topicId &&
                s.SignalType == LearningSignalTypes.RemediationStarted)
            .ToListAsync();
        Assert.NotEmpty(remediationStartedSignals);
        Assert.All(remediationStartedSignals, signal =>
        {
            Assert.False(string.IsNullOrWhiteSpace(signal.PayloadJson));
            using var payload = JsonDocument.Parse(signal.PayloadJson!);
            Assert.Equal("orka.remediation-lifecycle.v1", payload.RootElement.GetProperty("schemaVersion").GetString());
            Assert.True(payload.RootElement.TryGetProperty("recordedAt", out var recordedAt) && recordedAt.ValueKind == JsonValueKind.String);
            Assert.True(
                payload.RootElement.TryGetProperty("selectedAction", out var selectedAction) && !string.IsNullOrWhiteSpace(selectedAction.GetString()) ||
                payload.RootElement.TryGetProperty("deliveryMode", out var deliveryMode) && !string.IsNullOrWhiteSpace(deliveryMode.GetString()) ||
                payload.RootElement.TryGetProperty("sourceBasis", out var sourceBasis) && !string.IsNullOrWhiteSpace(sourceBasis.GetString()));
        });
        Assert.True(await db.WikiBlocks.AnyAsync(b =>
            b.WikiPage.UserId == user.UserId &&
            b.WikiPage.TopicId == topicId &&
            b.BlockType == WikiBlockType.RepairNote &&
            !b.IsDeleted));

        var projectionAfterTutor = await user.Client.GetFromJsonAsync<OrkaLearningStateDto>($"/api/learning/orka-state?topicId={topicId}")
            ?? throw new InvalidOperationException("Post-tutor projection missing.");
        var missionAfterTutor = await user.Client.GetFromJsonAsync<OrkaMissionControlDto>($"/api/learning/mission-control?topicId={topicId}")
            ?? throw new InvalidOperationException("Post-tutor mission missing.");
        var contextPackAfterTutor = await user.Client.GetFromJsonAsync<LearningContextPackDto>($"/api/learning/context-pack?topicId={topicId}")
            ?? throw new InvalidOperationException("Post-tutor context pack missing.");
        Assert.StartsWith("lsv_", projectionAfterTutor.LearningStateVersion);
        Assert.NotEqual(projectionBeforeTutor.LearningStateVersion, projectionAfterTutor.LearningStateVersion);
        Assert.Equal(projectionAfterTutor.LearningStateVersion, missionAfterTutor.LearningStateVersion);
        Assert.Equal(projectionAfterTutor.LearningStateVersion, contextPackAfterTutor.LearningStateVersion);
    }

    [Fact]
    public async Task AsyncPlanDiagnosticApi_IsPlanRequestOwnerScoped()
    {
        await using var factory = new ApiSmokeFactory("Development", configureServices: services =>
        {
            services.RemoveAll<IAIAgentFactory>();
            services.AddScoped<IAIAgentFactory, PlanDiagnosticApiAgentFactory>();
            services.RemoveAll<IDeepPlanAgent>();
            services.AddScoped<IDeepPlanAgent, PlanDiagnosticApiDeepPlanAgent>();
            services.RemoveAll<IBackgroundTaskQueue>();
            services.AddSingleton<IBackgroundTaskQueue, InlineScopedBackgroundTaskQueue>();
        });
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-owner");
        var outsider = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-outsider");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Async ownership");

        var start = await owner.Client.PostAsJsonAsync("/api/quiz/plan-diagnostic/start-async", new
        {
            topicId,
            topicTitle = "Async ownership",
            userLevel = "Bilinmiyor",
            intentRequestId = Guid.NewGuid(),
            rawStudyRequest = "async ownership plan",
            approvedMainTopic = "Async ownership",
            approvedFocusArea = "API flow",
            approvedStudyGoal = "validate scoped async plan diagnostics",
            approvedResearchIntent = "async plan diagnostic API ownership test"
        });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var body = await start.Content.ReadFromJsonAsync<StartPlanDiagnosticResponse>();
        Assert.NotNull(body);

        var crossUserStatus = await outsider.Client.GetAsync($"/api/quiz/plan-diagnostic/{body!.PlanRequestId}/status");
        Assert.Equal(HttpStatusCode.NotFound, crossUserStatus.StatusCode);

        var crossUserAttempt = await outsider.Client.PostAsJsonAsync($"/api/quiz/plan-diagnostic/{body.PlanRequestId}/attempt", new
        {
            quizRunId = body.QuizRunId,
            topicId,
            assessmentItemId = Guid.NewGuid(),
            questionId = "cross-user",
            question = "Should be rejected",
            selectedOptionId = "A",
            isCorrect = true
        });
        Assert.Equal(HttpStatusCode.NotFound, crossUserAttempt.StatusCode);
    }

    private static List<DiagnosticQuestion> ParseDiagnosticQuestions(string questionsJson)
    {
        using var doc = JsonDocument.Parse(questionsJson);
        return doc.RootElement.EnumerateArray()
            .Select((item, index) => new DiagnosticQuestion(
                GetString(item, "id") ?? GetString(item, "questionId") ?? $"q{index + 1}",
                GetString(item, "question") ?? GetString(item, "stem") ?? $"Question {index + 1}",
                Guid.Parse(GetString(item, "assessmentItemId") ?? throw new InvalidOperationException("assessmentItemId missing.")),
                GetString(item, "conceptTag") ?? GetString(item, "conceptKey") ?? "diagnostic",
                GetString(item, "questionType") ?? GetString(item, "cognitiveSkill") ?? "conceptual",
                GetString(item, "difficulty") ?? "orta"))
            .ToList();
    }

    private static object BuildAnswer(DiagnosticQuestion question, Guid topicId, Guid quizRunId, bool isCorrect) =>
        new
        {
            quizRunId,
            topicId,
            assessmentItemId = question.AssessmentItemId,
            questionId = question.QuestionId,
            question = question.Question,
            selectedOptionId = isCorrect
                ? "Kavrami, senaryoyu ve beklenen sonucu birlikte kontrol etmek."
                : "Kavram yerine benzer gorunen terimi secmek.",
            isCorrect,
            explanation = isCorrect ? "server-authored correct answer" : "diagnostic misconception evidence",
            skillTag = question.ConceptTag,
            conceptTag = question.ConceptTag,
            conceptKey = question.ConceptTag,
            questionType = question.QuestionType,
            difficulty = question.Difficulty,
            assessmentMode = "plan_diagnostic",
            responseTimeMs = 1200,
            questionHash = $"plan-api-{question.AssessmentItemId:N}"
        };

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record DiagnosticQuestion(
        string QuestionId,
        string Question,
        Guid AssessmentItemId,
        string ConceptTag,
        string QuestionType,
        string Difficulty);

    private sealed class PlanDiagnosticApiAgentFactory : IAIAgentFactory
    {
        private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

        public string GetModel(AgentRole role) => "plan-diagnostic-api-test-model";

        public string GetProvider(AgentRole role) => "plan-diagnostic-api-test-provider";

        public Task<string> CompleteChatAsync(
            AgentRole role,
            string systemPrompt,
            string userMessage,
            CancellationToken ct = default) =>
            Task.FromResult(role switch
            {
                AgentRole.Analyzer => BuildIntentPreview(),
                AgentRole.DeepPlan => BuildLearningResearchBrief(),
                AgentRole.Quiz or AgentRole.Diagnostic => BuildQuizBatch(userMessage.Contains("assessmentItemId=", StringComparison.OrdinalIgnoreCase)
                    ? userMessage
                    : systemPrompt),
                _ => "OK"
            });

        public async IAsyncEnumerable<string> StreamChatAsync(
            AgentRole role,
            string systemPrompt,
            string userMessage,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return await CompleteChatAsync(role, systemPrompt, userMessage, ct);
        }

        public Task<string> CompleteChatWithHistoryAsync(
            AgentRole role,
            string systemPrompt,
            IEnumerable<(string Role, string Content)> messages,
            CancellationToken ct = default) =>
            CompleteChatAsync(role, systemPrompt, string.Join("\n", messages.Select(m => m.Content)), ct);

        private static string BuildIntentPreview() =>
            JsonSerializer.Serialize(new
            {
                mainTopic = "SQL",
                focusArea = "index tuning",
                studyGoal = "SQL index ve sorgu optimizasyonu icin uygulanabilir bir ogrenme plani cikarmak",
                researchIntent = "SQL index tuning diagnostic learning path",
                confirmationText = "SQL index tuning icin seviye tespiti baslatilsin mi?",
                language = "tr",
                intentKind = "learning_path",
                goalType = "learn_and_practice",
                targetExamCode = (string?)null,
                sourceMode = "source_aware_if_available",
                timeHorizon = "unspecified",
                learnerConstraints = Array.Empty<string>(),
                requiredClarifications = Array.Empty<string>(),
                confidence = 0.91,
                clarifyingNotes = Array.Empty<string>()
            }, WebJsonOptions);

        private static string BuildLearningResearchBrief() =>
            """
            [DIRECT LEARNING RESEARCH BRIEF]
            LearningRoute
            SQL index temelleri -> sorgu planini okuma -> composite index secimi -> olcumle dogrulama.
            ReliableSources
            Deterministic API flow test fixture.
            YouTubeLearningReferences
            none
            Prerequisites
            SELECT, WHERE, JOIN, ORDER BY.
            SubConcepts
            cardinality, selectivity, covering index, query plan, composite index.
            CommonMistakes
            Her kolonu indekslemek; sorgu planini okumadan indeks secmek; yazma maliyetini yok saymak.
            PracticeOrder
            Plani oku, filtre/siralama ihtiyacini belirle, aday indeksi sec, olcumle dogrula.
            QuizScope
            concept, scenario, misconception, application, transfer.
            RecommendedQuestionCount
            15
            PlanningNotes
            Deterministic API flow test brief.
            """;

        private static string BuildQuizBatch(string prompt)
        {
            var specs = ExtractAssessmentSpecs(prompt).ToList();
            if (specs.Count == 0)
            {
                specs.AddRange(Enumerable.Range(1, 15).Select(i => new AssessmentSpec(
                    Guid.NewGuid(),
                    $"fallback-item-{i}",
                    $"sql-index-{i}",
                    i % 4 == 0 ? "misconception_probe" : "application",
                    i % 3 == 0 ? "zor" : i % 2 == 0 ? "orta" : "kolay",
                    $"sql-index-misconception-{i}",
                    $"Learner explains index decision evidence {i}.")));
            }

            var questions = specs.Select((spec, index) =>
            {
                var itemKey = spec.AssessmentItemId.ToString("N")[..8];
                var codePrompt = index == 1
                    ? "\n```sql\nSELECT * FROM Orders WHERE CustomerId = 42 ORDER BY CreatedAt DESC;\n```"
                    : "";
                return new
                {
                    type = "multiple_choice",
                    assessmentItemId = spec.AssessmentItemId,
                    assessmentItemKey = string.IsNullOrWhiteSpace(spec.AssessmentItemKey)
                        ? $"grammar:{spec.ConceptKey}:{itemKey}"
                        : spec.AssessmentItemKey,
                    conceptKey = spec.ConceptKey,
                    cognitiveSkill = spec.CognitiveSkill,
                    misconceptionTarget = spec.MisconceptionTarget,
                    evidenceExpected = spec.EvidenceExpected,
                    scoringRule = "selected_option_exact_match",
                    learningOutcomeIds = new[] { $"{spec.ConceptKey}-outcome" },
                    question = $"item-{itemKey} {spec.ConceptKey} icin SQL index karari verirken hangi kanit once incelenmelidir?{codePrompt}",
                    options = BuildOptions(index),
                    correctAnswer = "Kavrami, senaryoyu ve beklenen sonucu birlikte kontrol etmek.",
                    explanation = "Dogru cevap sorgu kosulu, beklenen sonuc ve olcum kanitini birlikte kullanir.",
                    skillTag = spec.ConceptKey,
                    difficulty = spec.Difficulty,
                    conceptTag = spec.ConceptKey,
                    learningObjective = $"Learner applies {spec.ConceptKey} in a diagnostic scenario.",
                    questionType = spec.CognitiveSkill,
                    expectedMisconceptionCategory = "Conceptual",
                    topic = "SQL index tuning"
                };
            });

            return JsonSerializer.Serialize(questions, WebJsonOptions);
        }

        private static object[] BuildOptions(int index)
        {
            var correct = new { text = "Kavrami, senaryoyu ve beklenen sonucu birlikte kontrol etmek.", isCorrect = true, rationale = "Matches the requested evidence.", misconceptionKey = "" };
            var distractors = new object[]
            {
                new { text = "Metni okumadan ilk gorunen satiri silmek.", isCorrect = false, rationale = "Skips evidence.", misconceptionKey = $"skip-evidence-{index + 1}" },
                new { text = "Kavram yerine benzer gorunen terimi secmek.", isCorrect = false, rationale = "Confuses nearby concepts.", misconceptionKey = $"nearby-concept-{index + 1}" },
                new { text = "Sonucu yalnizca ilk kelimeye bakarak tahmin etmek.", isCorrect = false, rationale = "Uses surface clues.", misconceptionKey = $"surface-clue-{index + 1}" }
            };

            var options = new List<object>(distractors);
            options.Insert(index % 4, correct);
            return options.ToArray();
        }

        private static IEnumerable<AssessmentSpec> ExtractAssessmentSpecs(string prompt)
        {
            foreach (var line in prompt.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!line.Contains("assessmentItemId=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!Guid.TryParse(ReadField(line, "assessmentItemId"), out var id))
                {
                    continue;
                }

                var conceptKey = ReadField(line, "conceptKey", $"sql-index-{id:N}"[..18]);
                yield return new AssessmentSpec(
                    id,
                    ReadField(line, "assessmentItemKey", $"grammar:{conceptKey}:{id:N}"),
                    conceptKey,
                    ReadField(line, "cognitiveSkill", "application"),
                    ReadField(line, "difficulty", "orta"),
                    ReadField(line, "misconceptionTarget", "index-evidence-confusion"),
                    ReadField(line, "evidenceExpected", $"Learner cites query-plan evidence for {conceptKey}."));
            }
        }

        private static string ReadField(string line, string field, string fallback = "")
        {
            var match = Regex.Match(line, $@"{Regex.Escape(field)}=([^;]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : fallback;
        }

        private sealed record AssessmentSpec(
            Guid AssessmentItemId,
            string AssessmentItemKey,
            string ConceptKey,
            string CognitiveSkill,
            string Difficulty,
            string MisconceptionTarget,
            string EvidenceExpected);
    }

    private sealed class PlanDiagnosticApiDeepPlanAgent : IDeepPlanAgent
    {
        private readonly OrkaDbContext _db;

        public PlanDiagnosticApiDeepPlanAgent(OrkaDbContext db)
        {
            _db = db;
        }

        public async Task<List<Topic>> GenerateAndSaveDeepPlanAsync(
            Guid parentTopicId,
            string topicTitle,
            Guid userId,
            string userLevel = "Bilinmiyor",
            string? researchContext = null,
            string? failedTopics = null)
        {
            var topics = BuildTopics(parentTopicId, topicTitle, userId);
            _db.Topics.AddRange(topics);
            await _db.SaveChangesAsync();
            return topics;
        }

        public async Task<DeepPlanGenerationWithGroundingResultDto> GenerateAndSaveDeepPlanWithGroundingAsync(
            Guid parentTopicId,
            string topicTitle,
            Guid userId,
            string userLevel = "Bilinmiyor",
            string? researchContext = null,
            string? failedTopics = null)
        {
            var topics = await GenerateAndSaveDeepPlanAsync(parentTopicId, topicTitle, userId, userLevel, researchContext, failedTopics);
            return new DeepPlanGenerationWithGroundingResultDto { Topics = topics };
        }

        public async Task<DeepPlanGenerationWithGroundingResultDto> GenerateAndSaveDeepPlanFromDiagnosticAsync(
            Guid parentTopicId,
            string topicTitle,
            Guid userId,
            string compressedResearchPromptBlock,
            string diagnosticQuizSummary,
            string userLevel = "Bilinmiyor")
        {
            var topics = await GenerateAndSaveDeepPlanAsync(parentTopicId, topicTitle, userId, userLevel, compressedResearchPromptBlock, diagnosticQuizSummary);
            return new DeepPlanGenerationWithGroundingResultDto { Topics = topics };
        }

        public Task<string> GenerateBaselineQuizAsync(string topicTitle, Guid topicId, string language, int questionCount) =>
            Task.FromResult("""[{"question":"SQL index ne zaman ise yarar?","options":[{"text":"Sorgu filtresi ve okuma paterniyle uyumluysa","isCorrect":true},{"text":"Her kolona eklendiginde","isCorrect":false}],"correctAnswer":"Sorgu filtresi ve okuma paterniyle uyumluysa","explanation":"Indeks karari sorgu paterniyle birlikte verilir."}]""");

        private static List<Topic> BuildTopics(Guid parentTopicId, string topicTitle, Guid userId)
        {
            var now = DateTime.UtcNow;
            var themes = new[]
            {
                "SQL index prerequisite map",
                "SQL query plan reading",
                "SQL composite index design",
                "SQL selectivity and cardinality",
                "SQL index misconception repair",
                "SQL index measurement checkpoint"
            };

            var allTopics = new List<Topic>();
            for (var moduleIndex = 0; moduleIndex < themes.Length; moduleIndex++)
            {
                var module = new Topic
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ParentTopicId = parentTopicId,
                    Title = $"{topicTitle} - {themes[moduleIndex]}",
                    Category = "Plan",
                    PlanIntent = "Module",
                    Order = moduleIndex,
                    TotalSections = 4,
                    CreatedAt = now,
                    LastAccessedAt = now
                };
                allTopics.Add(module);

                for (var lessonIndex = 0; lessonIndex < 4; lessonIndex++)
                {
                    var ordinal = moduleIndex * 4 + lessonIndex + 1;
                    var conceptKey = $"sql-index-{moduleIndex + 1}-{lessonIndex + 1}";
                    allTopics.Add(new Topic
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        ParentTopicId = module.Id,
                        Title = $"{themes[moduleIndex]} lesson {lessonIndex + 1}",
                        Category = "Plan:Lesson",
                        PlanIntent = moduleIndex == 4 && lessonIndex < 2 ? "Remediation" : "Core",
                        Order = lessonIndex,
                        TotalSections = 1,
                        CreatedAt = now,
                        LastAccessedAt = now,
                        MetadataJson = JsonSerializer.Serialize(new
                        {
                            contractVersion = "plan-diagnostic-api-test-v1",
                            moduleTitle = themes[moduleIndex],
                            conceptKey,
                            skillTag = conceptKey,
                            learningObjective = $"{topicTitle} icin {themes[moduleIndex]} dersinde diagnostic kanita dayali karar verir.",
                            sequenceReason = $"Ders {ordinal}, SQL index kararini once kavram sonra sorgu kaniti sonra olcum sirasiyla ilerletir.",
                            quizHook = new
                            {
                                hookType = "retrieval_practice",
                                conceptKey,
                                difficultyBand = moduleIndex >= 3 ? "advanced" : "core"
                            },
                            tutorHook = new
                            {
                                activeConceptKey = conceptKey,
                                tutorMove = moduleIndex == 4 ? "misconception_repair" : "explain_then_check"
                            },
                            successCriteria = new[]
                            {
                                $"{themes[moduleIndex]} kavramini kendi cumlesiyle aciklar.",
                                "Kisa SQL senaryosunda dogru indeks kararini gerekcelendirir.",
                                "Mikro kontrol sorusunda kanit ve kisiti birlikte kullanir."
                            }
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    });
                }
            }

            return allTopics;
        }
    }

    private sealed class InlineScopedBackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public InlineScopedBackgroundTaskQueue(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async ValueTask QueueAsync(BackgroundTaskItem item, CancellationToken ct = default)
        {
            if (item.ScopedWork != null)
            {
                using var scope = _scopeFactory.CreateScope();
                await item.ScopedWork(scope.ServiceProvider, ct);
                return;
            }

            await item.Work(ct);
        }
    }
}

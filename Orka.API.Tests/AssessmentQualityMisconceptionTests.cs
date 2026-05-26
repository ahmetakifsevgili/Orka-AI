using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class AssessmentQualityMisconceptionTests
{
    [Fact]
    public async Task DiagnosticBlueprint_TargetsConceptGraphAndCarriesSafeSourceReadiness()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "assessment-blueprint");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Quadratics");
        await SeedConceptGraphAsync(factory, user.UserId, topicId,
            ("quadratic-basics", "Quadratic basics", 0),
            ("factoring", "Factoring quadratics", 1));

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAssessmentBlueprintService>();
        var blueprint = await service.BuildDiagnosticBlueprintAsync(user.UserId, topicId);

        Assert.Equal("diagnostic_check", blueprint.AssessmentMode);
        Assert.Contains(blueprint.TargetConcepts, c => c.ConceptKey == "quadratic-basics");
        Assert.Contains(blueprint.MisconceptionTargets, m => m.ConceptKey == "factoring");
        Assert.Contains(blueprint.LeakageSafetyRequirements, r => r.Contains("correct", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual("source_grounded", blueprint.EvidenceMode);
        Assert.DoesNotContain("official", JsonSerializer.Serialize(blueprint), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanStepBlueprint_ConsumesPlanQualityHook()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "assessment-plan-step");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Limits");

        using var scope = factory.Services.CreateScope();
        var planService = scope.ServiceProvider.GetRequiredService<IPlanSequencingService>();
        var plan = await planService.EvaluatePlanSequenceAsync(user.UserId, new PlanQualityEvaluationRequestDto
        {
            TopicId = topicId,
            ProposedSteps =
            [
                new PlanStepContractDto
                {
                    StepId = "limit-step-1",
                    Title = "Limit notation",
                    Objective = "Limit notation kavramini ornek uzerinde ayirt et.",
                    ConceptKey = "limit-notation",
                    ConceptLabel = "Limit notation",
                    DifficultyBand = "core",
                    SequenceReason = "Once sembol ve yaklasma fikri yerlesmeli.",
                    QuizHook = new PlanStepAssessmentHookDto
                    {
                        HookType = "micro_quiz",
                        ConceptKey = "limit-notation",
                        TargetMisconceptions = ["limit equals function value"],
                        DifficultyBand = "core",
                        UserSafeReason = "Bu adim kisa kavram kontrolu ister."
                    },
                    TutorHook = new PlanStepTutorHookDto
                    {
                        TutorMove = "example",
                        ActiveConceptKey = "limit-notation",
                        UserSafeReason = "Ornekle ogret."
                    },
                    SuccessCriteria = ["Ogrenci limit sembolunu yorumlar."],
                    NextStepTrigger = "micro_check_passed"
                }
            ]
        });

        var assessment = scope.ServiceProvider.GetRequiredService<IAssessmentBlueprintService>();
        var blueprint = await assessment.BuildBlueprintForPlanStepAsync(user.UserId, new AssessmentBlueprintRequestDto
        {
            TopicId = topicId,
            PlanQualitySnapshotId = plan.SnapshotId,
            PlanStepId = "limit-step-1"
        });

        Assert.Equal("micro_quiz", blueprint.AssessmentMode);
        Assert.Contains(blueprint.TargetConcepts, c => c.ConceptKey == "limit-notation");
        Assert.Contains(blueprint.MisconceptionTargets, m => m.UserSafeLabel.Contains("limit equals", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MisconceptionProbe_RequiresDistractorRationales()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "assessment-rationale");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Fractions");

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAssessmentBlueprintService>();
        var result = await service.EvaluateAssessmentContractAsync(user.UserId, new AssessmentQualityEvaluationRequestDto
        {
            TopicId = topicId,
            Blueprint = new AssessmentBlueprintDto
            {
                TopicId = topicId,
                AssessmentMode = "misconception_probe",
                TargetConcepts = [new AssessmentBlueprintConceptDto { ConceptKey = "fraction-equivalence", Label = "Fraction equivalence" }],
                MisconceptionTargets = [new AssessmentMisconceptionTargetDto { ConceptKey = "fraction-equivalence", MisconceptionKey = "larger-denominator", UserSafeLabel = "Payda buyudukce deger buyur sanma" }]
            },
            Items =
            [
                new AssessmentItemContractDto
                {
                    ItemId = "item-1",
                    Stem = "1/2 ile 2/4 arasindaki iliski nedir?",
                    ConceptKey = "fraction-equivalence",
                    Explanation = "Iki kesir ayni butun parcasini temsil eder.",
                    OptionTexts = ["Esittir", "2/4 daha buyuktur", "1/2 daha buyuktur", "Iliski yoktur"]
                }
            ]
        });

        Assert.Equal("needs_revision", result.QualityStatus);
        Assert.Contains(result.BlockingIssues, i => i.Code == "misconception_rationale_missing" || i.Code == "distractor_rationale_missing");
    }

    [Fact]
    public async Task ProductTriviaAndAnswerLeakage_AreBlocked()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "assessment-leak");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Safe Items");

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAssessmentBlueprintService>();
        var result = await service.EvaluateAssessmentContractAsync(user.UserId, new AssessmentQualityEvaluationRequestDto
        {
            TopicId = topicId,
            Blueprint = new AssessmentBlueprintDto
            {
                TopicId = topicId,
                AssessmentMode = "micro_quiz",
                TargetConcepts = [new AssessmentBlueprintConceptDto { ConceptKey = "safe-concept", Label = "Safe concept" }]
            },
            Items =
            [
                new AssessmentItemContractDto
                {
                    ItemId = "leaky",
                    Stem = "Orka IDE icinde dogru cevap A olarak isaretlenirse ne olur?",
                    ConceptKey = "safe-concept",
                    Explanation = "Dogru cevap A.",
                    OptionTexts = ["Correct answer: A", "B", "C", "D"],
                    PublicDtoContainsCorrectAnswer = true
                }
            ]
        });

        Assert.Contains(result.BlockingIssues, i => i.Code == "product_trivia_item");
        Assert.Contains(result.BlockingIssues, i => i.Code == "answer_leakage");
    }

    [Fact]
    public async Task QuizAttempt_ReturnsLearningImpactAndStoresNoRawInvalidSourceRefs()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "assessment-impact");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Rates");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, topicId, DateTime.UtcNow);
        var (quizRunId, assessmentItemId) = await CoordinationTestHelpers.SeedDurableAssessmentItemAsync(
            factory,
            user.UserId,
            topicId,
            questionId: "rate-impact-q",
            question: "Degisim orani neyi olcer?",
            conceptKey: "rate-of-change",
            correctOptionId: "A",
            correctOptionText: "Iki niceligin birlikte nasil degistigini",
            wrongOptionId: "B",
            wrongOptionText: "Tek bir noktanin yerini",
            explanation: "Degisim orani iki niceligin birlikte nasil degistigini anlatir.");

        var response = await user.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            topicId,
            sessionId,
            quizRunId,
            assessmentItemId,
            questionId = "rate-impact-q",
            question = "Degisim orani neyi olcer?",
            selectedOptionId = "B) Tek bir noktanin yerini",
            isCorrect = false,
            explanation = "Degisim orani iki niceligin birlikte nasil degistigini anlatir.",
            skillTag = "rate-of-change",
            conceptKey = "rate-of-change",
            conceptTag = "rate-of-change",
            misconceptionTarget = "slope-as-point",
            assessmentMode = "misconception_probe",
            sourceRefsJson = "{not-json",
            questionHash = Guid.NewGuid().ToString("N")
        });

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        var impact = root.GetProperty("learningImpact");

        Assert.Equal("misconception_probe", impact.GetProperty("assessmentMode").GetString());
        Assert.Equal("wrong", impact.GetProperty("result").GetString());
        Assert.Equal("insert_remediation_step", impact.GetProperty("nextPlanAction").GetString());
        Assert.NotEqual("none", impact.GetProperty("remediationNeed").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.True(await db.KnowledgeTracingStates.AnyAsync(s => s.UserId == user.UserId && s.ConceptKey == "rate-of-change"));
        var attempt = await db.QuizAttempts.AsNoTracking().OrderByDescending(a => a.CreatedAt).FirstAsync(a => a.UserId == user.UserId);
        Assert.DoesNotContain("rawSourceRefs", attempt.SourceRefsJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid_json_ignored", attempt.SourceRefsJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WrongQuizAttempt_CarriesRemediationIntoTutorMovePlanActionAndWikiTrace()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "assessment-remediation-chain");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Functions");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, topicId, DateTime.UtcNow);
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, topicId, "Function misconceptions", "safe wiki note");
        var (quizRunId, assessmentItemId) = await CoordinationTestHelpers.SeedDurableAssessmentItemAsync(
            factory,
            user.UserId,
            topicId,
            questionId: "function-domain-q",
            question: "Bir fonksiyonun tanim kumesi neyi belirler?",
            conceptKey: "function-domain",
            correctOptionId: "A",
            correctOptionText: "Girdilerin hangi degerlerden gelebilecegini",
            wrongOptionId: "B",
            wrongOptionText: "Ciktilarin her zaman pozitif olacagini",
            explanation: "Tanim kumesi fonksiyona verilebilecek girdi degerlerini sinirlar.");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var page = await db.WikiPages.SingleAsync(p => p.Id == pageId);
            page.ConceptKey = "function-domain";
            var block = await db.WikiBlocks.SingleAsync(b => b.WikiPageId == pageId);
            block.ConceptKey = "function-domain";
            await db.SaveChangesAsync();
        }

        var response = await user.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            topicId,
            sessionId,
            quizRunId,
            assessmentItemId,
            questionId = "function-domain-q",
            question = "Bir fonksiyonun tanim kumesi neyi belirler?",
            selectedOptionId = "B) Ciktilarin her zaman pozitif olacagini",
            skillTag = "function-domain",
            conceptKey = "function-domain",
            conceptTag = "function-domain",
            misconceptionTarget = "range-as-domain",
            assessmentMode = "misconception_probe",
            questionHash = Guid.NewGuid().ToString("N")
        });

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        var impact = root.GetProperty("learningImpact");
        var attemptId = root.GetProperty("id").GetGuid();

        Assert.Equal("wrong", impact.GetProperty("result").GetString());
        Assert.Equal("insert_remediation_step", impact.GetProperty("nextPlanAction").GetString());
        Assert.Contains(
            impact.GetProperty("nextTutorMove").GetString(),
            new[] { "guided_practice", "misconception_repair", "prerequisite_scaffold", "source_review_then_explain" });
        Assert.NotEqual("none", impact.GetProperty("remediationNeed").GetString());
        Assert.True(impact.TryGetProperty("remediationLesson", out var lesson) && lesson.ValueKind == JsonValueKind.Object);
        Assert.True(lesson.GetProperty("checkpoint").GetProperty("avoidsPreSubmitReveal").GetBoolean());

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var signal = await verifyDb.LearningSignals
            .AsNoTracking()
            .Where(s => s.UserId == user.UserId && s.QuizAttemptId == attemptId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync(s => s.PayloadJson != null && s.PayloadJson.Contains("remediationSeed"));
        Assert.Contains("misconceptionSignal", signal.PayloadJson);

        var wikiBlocks = await verifyDb.WikiBlocks
            .AsNoTracking()
            .Where(b => b.QuizAttemptId == attemptId)
            .ToListAsync();
        Assert.Contains(wikiBlocks, b => b.BlockType == WikiBlockType.QuizResult);
        var repairBlock = Assert.Single(wikiBlocks.Where(b => b.BlockType is WikiBlockType.RepairNote or WikiBlockType.MisconceptionNote));
        Assert.Equal("highlighted", repairBlock.Visibility);
        Assert.Contains("Tutor sonraki hamlesi", repairBlock.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Plan aksiyonu", repairBlock.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", repairBlock.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", repairBlock.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QualitySnapshot_IsCallerScopedThroughApi()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "assessment-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "assessment-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Ownership");

        var create = await owner.Client.PostAsJsonAsync("/api/assessment/quality/evaluate", new AssessmentQualityEvaluationRequestDto
        {
            TopicId = topicId,
            Blueprint = new AssessmentBlueprintDto
            {
                TopicId = topicId,
                AssessmentMode = "micro_quiz",
                TargetConcepts = [new AssessmentBlueprintConceptDto { ConceptKey = "ownership-concept", Label = "Ownership concept" }]
            },
            Items =
            [
                new AssessmentItemContractDto
                {
                    ItemId = "safe",
                    Stem = "Bu kavramin ayirt edici ozelligi nedir?",
                    ConceptKey = "ownership-concept",
                    Explanation = "Ayirt edici ozellik kavramin sinirlarini netlestirir.",
                    OptionTexts = ["Siniri belirginlestirir", "Ezber yaptirir", "Konuyu siler", "Cevabi sizdirir"]
                }
            ]
        });
        create.EnsureSuccessStatusCode();
        var snapshot = await create.Content.ReadFromJsonAsync<AssessmentQualityEvaluationDto>();
        Assert.NotNull(snapshot);

        var ownerRead = await owner.Client.GetAsync($"/api/assessment/quality/snapshots/{snapshot!.SnapshotId}");
        ownerRead.EnsureSuccessStatusCode();

        var otherRead = await other.Client.GetAsync($"/api/assessment/quality/snapshots/{snapshot.SnapshotId}");
        Assert.Equal(HttpStatusCode.NotFound, otherRead.StatusCode);
    }

    private static async Task<Guid> SeedConceptGraphAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid topicId,
        params (string Key, string Label, int Order)[] concepts)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var snapshotId = Guid.NewGuid();
        db.ConceptGraphSnapshots.Add(new ConceptGraphSnapshot
        {
            Id = snapshotId,
            UserId = userId,
            TopicId = topicId,
            IntentHash = Guid.NewGuid().ToString("N"),
            ApprovedResearchIntent = "assessment test intent",
            TopicTitle = "assessment test topic",
            Domain = "test",
            SourceConfidence = "low",
            GraphJson = "{}",
            CreatedAt = DateTime.UtcNow
        });

        foreach (var (key, label, order) in concepts)
        {
            db.LearningConcepts.Add(new LearningConcept
            {
                Id = Guid.NewGuid(),
                ConceptGraphSnapshotId = snapshotId,
                StableKey = key,
                Label = label,
                Description = label,
                DifficultyBand = order == 0 ? "foundation" : "core",
                Order = order,
                PrerequisitesJson = "[]",
                MisconceptionsJson = order == 0 ? "[]" : JsonSerializer.Serialize(new[] { $"{label} yanilgisi" }),
                LearningOutcomeKeysJson = "[]",
                SourceEvidenceJson = "[]",
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return snapshotId;
    }
}

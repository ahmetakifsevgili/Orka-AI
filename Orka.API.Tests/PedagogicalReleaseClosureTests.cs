using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Chat;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class PedagogicalReleaseClosureTests
{
    [Fact]
    public async Task ProviderFreeLearningLoop_ConnectsPedagogicalProductizationSurfaces()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "pedagogy-final-loop");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Linear Functions Release");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, tree.RootId, DateTime.UtcNow);
        var conceptGraphId = await SeedConceptGraphAsync(factory, user.UserId, tree.RootId);
        var pageId = await SeedWikiPageAsync(factory, user.UserId, tree.RootId, conceptGraphId);
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            tree.RootId,
            "Linear Function Notes",
            "linear function evidence about slope as the constant rate of change between two points");

        using (var scope = factory.Services.CreateScope())
        {
            var planSequencing = scope.ServiceProvider.GetRequiredService<IPlanSequencingService>();
            var initialPlan = await planSequencing.BuildPlanSequenceAsync(user.UserId, new PlanQualityEvaluationRequestDto
            {
                TopicId = tree.RootId,
                SessionId = sessionId
            });

            Assert.Equal("diagnostic-first", initialPlan.Steps.First().StepId);
            Assert.Equal("lesson", initialPlan.AdaptiveDiagnostic.Intent);
            Assert.Equal("needs_diagnostic", initialPlan.AdaptiveDiagnostic.PlanReadiness);
            Assert.InRange(initialPlan.AdaptiveDiagnostic.RecommendedQuestions.Count, 2, 5);

            await SeedWeakLearnerStateAsync(scope.ServiceProvider.GetRequiredService<OrkaDbContext>(), user.UserId, tree.RootId, conceptGraphId);

            var quality = await planSequencing.EvaluatePlanSequenceAsync(user.UserId, new PlanQualityEvaluationRequestDto
            {
                TopicId = tree.RootId,
                SessionId = sessionId,
                PlanTitle = "Linear functions adaptive route"
            });

            Assert.Equal("needs_repair", quality.PlanContract.AdaptiveDiagnostic.PlanReadiness);
            Assert.Equal("needs_repair", quality.PlanContract.CoursePlanQuality.ReadinessStatus);
            Assert.Contains(quality.PlanContract.CoursePlanQuality.RepairLoops, loop => loop.ConceptKey == "slope");

            var tutorPlanner = scope.ServiceProvider.GetRequiredService<ITutorActionPlanner>();
            var actionPlan = await tutorPlanner.PlanAsync(new TutorTurnStateDto
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = tree.RootId,
                SessionId = sessionId,
                ConceptGraphSnapshotId = conceptGraphId,
                UserMessage = "I do not understand slope yet; can we repair it?",
                ActiveConceptKey = "slope",
                ActiveConceptLabel = "Slope",
                LearnerState = "remediation_ready",
                MasteryProbability = 0.22m,
                Confidence = 0.70m,
                RemediationNeed = "high",
                PracticeReadiness = "guided",
                AffectiveState = "confused",
                CognitiveLoad = "high",
                GroundingStatus = "model_only",
                SourceEvidenceCount = 0,
                EvidenceQuality = new EvidenceQualityDto { Status = "evidence_insufficient" },
                CurrentPlanStepId = quality.PlanContract.Steps.First().StepId,
                CurrentPlanStepTitle = quality.PlanContract.Steps.First().Title,
                CurrentPlanTutorMove = quality.PlanContract.Steps.First().TutorHook.TutorMove,
                CurrentPlanQuizHook = quality.PlanContract.Steps.First().QuizHook.HookType,
                PlanSourceReadiness = quality.PlanContract.SourceReadiness,
                AdaptiveDiagnostic = quality.PlanContract.AdaptiveDiagnostic,
                CoursePlanQuality = quality.PlanContract.CoursePlanQuality,
                LatestAssessmentMode = "blank",
                LatestMisconceptionConfidence = "none",
                SourceReadiness = "evidence_insufficient",
                HasWikiContext = true,
                HasNotebookContext = true,
                CreatedAt = DateTimeOffset.UtcNow
            });

            Assert.Equal("start_remediation", actionPlan.ToolDecision?.SelectedAction);
            Assert.Equal("prerequisite_repair", actionPlan.LessonDelivery?.DeliveryMode);
            Assert.NotNull(actionPlan.RemediationLesson);
            Assert.Equal("prerequisite_repair", actionPlan.RemediationLesson!.RepairType);
            Assert.True(actionPlan.RemediationLesson.Checkpoint.AvoidsPreSubmitReveal);
        }

        var (quizRunId, assessmentItemId) = await CoordinationTestHelpers.SeedDurableAssessmentItemAsync(
            factory,
            user.UserId,
            tree.RootId,
            questionId: "phase7-slope-check",
            question: "What does slope describe?",
            conceptKey: "slope",
            correctOptionId: "A",
            correctOptionText: "Rate of change",
            wrongOptionId: "B",
            wrongOptionText: "Only the x intercept",
            explanation: "Slope describes rate of change.");

        var quizResponse = await user.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            quizRunId,
            topicId = tree.RootId,
            sessionId,
            assessmentItemId,
            questionId = "phase7-slope-check",
            question = "What does slope describe?",
            selectedOptionId = "skip",
            wasSkipped = true,
            conceptKey = "slope",
            conceptTag = "slope",
            skillTag = "slope",
            assessmentMode = "checkpoint",
            questionHash = "phase7-slope-blank"
        });

        quizResponse.EnsureSuccessStatusCode();
        using var quizJson = await JsonDocument.ParseAsync(await quizResponse.Content.ReadAsStreamAsync());
        var learningImpact = quizJson.RootElement.GetProperty("learningImpact");
        Assert.Equal("blank", learningImpact.GetProperty("result").GetString());
        Assert.Equal("prerequisite_repair", learningImpact.GetProperty("remediationLesson").GetProperty("repairType").GetString());
        Assert.Contains("blank_answer_not_misconception", learningImpact.GetProperty("remediationLesson").GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));
        AssertNoPublicLeak(quizJson.RootElement.GetRawText());

        var activeSnapshotResponse = await user.Client.PostAsJsonAsync("/api/learning-snapshots/active-lesson/refresh", new
        {
            topicId = tree.RootId,
            sessionId,
            conceptGraphSnapshotId = conceptGraphId,
            approvedIntent = "lesson",
            approvedMainTopic = "Linear functions",
            approvedFocusArea = "slope",
            approvedStudyGoal = "Repair slope before continuing",
            groundingMode = "model_assisted"
        });
        activeSnapshotResponse.EnsureSuccessStatusCode();
        var activeSnapshot = await activeSnapshotResponse.Content.ReadFromJsonAsync<ActiveLessonSnapshotDto>();
        Assert.NotNull(activeSnapshot);
        Assert.Equal("slope", activeSnapshot!.ActiveConceptKey);
        Assert.Contains(activeSnapshot.RemediationNeed, new[] { "medium", "high" });

        var copilotResponse = await user.Client.GetAsync($"/api/wiki/page/{pageId}/copilot");
        copilotResponse.EnsureSuccessStatusCode();
        var copilot = await copilotResponse.Content.ReadFromJsonAsync<WikiCopilotContextDto>();
        Assert.NotNull(copilot);
        Assert.Equal("repair_pending", copilot!.RepairState);
        Assert.Equal("start_repair", copilot.PrimaryAction?.ActionType);
        Assert.Contains(copilot.SuggestedActions, action => action.ActionType == "generate_checkpoint");
        AssertNoPublicLeak(JsonSerializer.Serialize(copilot));

        var packResponse = await user.Client.PostAsJsonAsync($"/api/notebook-studio/wiki-page/{pageId}/pack", new
        {
            sessionId,
            packType = "misconception_repair",
            focusConceptKey = "slope",
            includeArtifacts = true
        });
        packResponse.EnsureSuccessStatusCode();
        var pack = await packResponse.Content.ReadFromJsonAsync<LearningNotebookPackDto>();
        Assert.NotNull(pack);
        Assert.Equal(pageId, pack!.WikiPageId);
        Assert.Contains("slope", pack.WeakConceptKeys);
        Assert.Contains(pack.NextActions, action => action.ActionType.Contains("repair", StringComparison.OrdinalIgnoreCase) || action.ActionType.Contains("review", StringComparison.OrdinalIgnoreCase));
        AssertNoPublicLeak(JsonSerializer.Serialize(pack));

        var sourceNotebookResponse = await user.Client.GetAsync($"/api/sources/topic/{tree.RootId}/notebook");
        sourceNotebookResponse.EnsureSuccessStatusCode();
        var sourceNotebookJson = await sourceNotebookResponse.Content.ReadAsStringAsync();
        Assert.Contains(sourceId.ToString(), sourceNotebookJson);
        AssertNoPublicLeak(sourceNotebookJson);

        var dashboardResponse = await user.Client.GetAsync("/api/dashboard/today");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
    }

    private static async Task<Guid> SeedConceptGraphAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        var graphId = Guid.NewGuid();
        db.ConceptGraphSnapshots.Add(new ConceptGraphSnapshot
        {
            Id = graphId,
            UserId = userId,
            TopicId = topicId,
            IntentHash = "phase7-linear-functions",
            ApprovedResearchIntent = "Teach linear functions with diagnostic and repair",
            TopicTitle = "Linear Functions",
            Domain = "math",
            SourceConfidence = "medium",
            SourceBundleHash = "phase7-source",
            GraphJson = "{}",
            CreatedAt = now
        });
        db.LearningConcepts.AddRange(
            Concept(graphId, "coordinate-plane", "Coordinate plane", 0, "foundation"),
            Concept(graphId, "slope", "Slope", 1, "core", prerequisites: """["coordinate-plane"]""", misconceptions: """["slope-as-point"]"""),
            Concept(graphId, "linear-equation", "Linear equation", 2, "core", prerequisites: """["slope"]"""));
        db.ConceptRelations.AddRange(
            Relation(graphId, "coordinate-plane", "slope"),
            Relation(graphId, "slope", "linear-equation"));
        await db.SaveChangesAsync();
        return graphId;
    }

    private static async Task<Guid> SeedWikiPageAsync(ApiSmokeFactory factory, Guid userId, Guid topicId, Guid graphId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        var pageId = Guid.NewGuid();
        db.WikiPages.Add(new WikiPage
        {
            Id = pageId,
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = graphId,
            Title = "Slope",
            PageKey = "concept:slope",
            PageType = "concept",
            ConceptKey = "slope",
            SourceReadiness = "evidence_insufficient",
            EvidenceStatus = "evidence_insufficient",
            SafeSummary = "Slope page keeps curated repair and checkpoint context.",
            Status = "learning",
            OrderIndex = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.WikiBlocks.AddRange(
            new WikiBlock
            {
                Id = Guid.NewGuid(),
                WikiPageId = pageId,
                BlockType = WikiBlockType.TutorExplanation,
                Title = "Slope short explanation",
                Content = "Slope describes rate of change in a linear relation.",
                Source = "tutor",
                SourceBasis = "tutor_generated",
                ConceptKey = "slope",
                OrderIndex = 1,
                CreatedAt = now,
                UpdatedAt = now
            },
            new WikiBlock
            {
                Id = Guid.NewGuid(),
                WikiPageId = pageId,
                BlockType = WikiBlockType.RepairNote,
                Title = "Slope repair",
                Content = "Learner needs a short prerequisite repair before continuing.",
                Source = "quiz_attempt",
                SourceBasis = "assessment_verified",
                ConceptKey = "slope",
                Visibility = "highlighted",
                OrderIndex = 2,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();
        return pageId;
    }

    private static async Task SeedWeakLearnerStateAsync(OrkaDbContext db, Guid userId, Guid topicId, Guid graphId)
    {
        var now = DateTime.UtcNow;
        db.KnowledgeTracingStates.Add(new KnowledgeTracingState
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = graphId,
            ConceptKey = "slope",
            Label = "Slope",
            EvidenceCount = 4,
            CorrectCount = 1,
            IncorrectCount = 3,
            MasteryProbability = 0.22m,
            Confidence = 0.75m,
            RemediationNeed = "high",
            PracticeReadiness = "guided",
            LastEvidenceAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ConceptMasteries.Add(new ConceptMastery
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = graphId,
            ConceptKey = "slope",
            Label = "Slope",
            MasteryScore = 0.25m,
            Confidence = 0.70m,
            RemediationNeed = "high",
            PracticeReadiness = "guided",
            Attempts = 4,
            Correct = 1,
            LastEvidenceAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static LearningConcept Concept(
        Guid graphId,
        string key,
        string label,
        int order,
        string difficulty,
        string prerequisites = "[]",
        string misconceptions = "[]") => new()
        {
            Id = Guid.NewGuid(),
            ConceptGraphSnapshotId = graphId,
            StableKey = key,
            Label = label,
            DifficultyBand = difficulty,
            Order = order,
            PrerequisitesJson = prerequisites,
            MisconceptionsJson = misconceptions,
            LearningOutcomeKeysJson = $"""["outcome:{key}"]""",
            CreatedAt = DateTime.UtcNow
        };

    private static ConceptRelation Relation(Guid graphId, string source, string target) => new()
    {
        Id = Guid.NewGuid(),
        ConceptGraphSnapshotId = graphId,
        SourceConceptKey = source,
        TargetConceptKey = target,
        RelationType = "prerequisite",
        Weight = 1,
        CreatedAt = DateTime.UtcNow
    };

    private static void AssertNoPublicLeak(string json)
    {
        Assert.DoesNotContain("rawPrompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hiddenPrompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("systemPrompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("developerPrompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawToolPayload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debugTrace", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", json, StringComparison.OrdinalIgnoreCase);
    }
}

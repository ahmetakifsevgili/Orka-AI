using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class PlanQualitySequencingTests
{
    [Fact]
    public async Task GenericPlanIsMarkedNeedsRevisionWithoutRawPayloadExposure()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-quality-generic");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Linear Algebra");

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPlanSequencingService>();
        var result = await service.EvaluatePlanSequenceAsync(user.UserId, new PlanQualityEvaluationRequestDto
        {
            TopicId = topicId,
            PlanTitle = "Study harder",
            PlanSummary = "review basics",
            ProposedSteps =
            [
                new PlanStepContractDto
                {
                    StepId = "generic-1",
                    Title = "Review basics",
                    Objective = "",
                    ConceptKey = "",
                    ConceptLabel = "",
                    SequenceReason = "",
                    QuizHook = new PlanStepAssessmentHookDto(),
                    TutorHook = new PlanStepTutorHookDto()
                }
            ]
        });

        Assert.Equal("needs_revision", result.QualityStatus);
        Assert.Contains(result.BlockingIssues, i => i.Code is "plan_too_generic" or "step_missing_concept_or_objective");
        var publicJson = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("raw prompt", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider payload", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debug trace", publicJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TopicSpecificPlanPassesWithQuizTutorAndSequenceReason()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-quality-pass");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Photosynthesis");

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPlanSequencingService>();
        var result = await service.EvaluatePlanSequenceAsync(user.UserId, new PlanQualityEvaluationRequestDto
        {
            TopicId = topicId,
            PlanTitle = "Photosynthesis concept route",
            ProposedSteps =
            [
                StrongStep("light-reactions", "Light reactions", "Chlorophyll energy transfer is measured before Calvin cycle.", []),
                StrongStep("calvin-cycle", "Calvin cycle", "This step follows light reactions because ATP/NADPH are prerequisites.", ["light-reactions"])
            ]
        });

        Assert.True(result.QualityStatus is "usable" or "strong");
        Assert.Empty(result.BlockingIssues);
        Assert.All(result.PlanContract.Steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.QuizHook.HookType));
            Assert.False(string.IsNullOrWhiteSpace(step.TutorHook.TutorMove));
            Assert.False(string.IsNullOrWhiteSpace(step.SequenceReason));
        });
    }

    [Fact]
    public async Task PrerequisiteOrderViolationIsBlocked()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-quality-order");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Functions");

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPlanSequencingService>();
        var result = await service.EvaluatePlanSequenceAsync(user.UserId, new PlanQualityEvaluationRequestDto
        {
            TopicId = topicId,
            ProposedSteps =
            [
                StrongStep("composition", "Function composition", "Composition depends on function notation.", ["notation"]),
                StrongStep("notation", "Function notation", "Notation is the prerequisite concept.", [])
            ]
        });

        Assert.Equal("needs_revision", result.QualityStatus);
        Assert.Contains(result.BlockingIssues, i => i.Code == "prerequisite_order_violation");
    }

    [Fact]
    public async Task BuildSequenceUsesConceptGraphAndStartsDiagnosticWhenLearnerEvidenceIsLow()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-quality-graph");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Fractions");

        await SeedConceptGraphAsync(factory, user.UserId, topicId,
            ("fraction-basics", "Fraction basics", 0),
            ("equivalent-fractions", "Equivalent fractions", 1),
            ("fraction-comparison", "Fraction comparison", 2));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var graphId = await db.ConceptGraphSnapshots.Where(g => g.UserId == user.UserId && g.TopicId == topicId).Select(g => g.Id).SingleAsync();
        db.ConceptRelations.Add(new ConceptRelation
        {
            Id = Guid.NewGuid(),
            ConceptGraphSnapshotId = graphId,
            SourceConceptKey = "fraction-basics",
            TargetConceptKey = "equivalent-fractions",
            RelationType = "prerequisite",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<IPlanSequencingService>();
        var sequence = await service.BuildPlanSequenceAsync(user.UserId, new PlanQualityEvaluationRequestDto { TopicId = topicId });

        Assert.Equal("diagnostic-first", sequence.Steps[0].StepId);
        Assert.Equal("lesson", sequence.AdaptiveDiagnostic.Intent);
        Assert.Equal("needs_diagnostic", sequence.AdaptiveDiagnostic.PlanReadiness);
        Assert.Equal("unknown", sequence.AdaptiveDiagnostic.LearnerLevel);
        Assert.InRange(sequence.AdaptiveDiagnostic.RecommendedQuestions.Count, 2, 5);
        Assert.Equal("needs_diagnostic", sequence.CoursePlanQuality.ReadinessStatus);
        Assert.True(sequence.CoursePlanQuality.MilestoneCount > 0);
        Assert.True(sequence.CoursePlanQuality.CheckpointCoverage > 0);
        var prerequisiteIndex = sequence.Steps.ToList().FindIndex(s => s.ConceptKey == "fraction-basics");
        var dependentIndex = sequence.Steps.ToList().FindIndex(s => s.ConceptKey == "equivalent-fractions");
        Assert.True(prerequisiteIndex >= 0);
        Assert.True(dependentIndex > prerequisiteIndex);
        Assert.All(sequence.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step.SequenceReason)));
    }

    [Fact]
    public async Task WeakConceptIsPrioritizedWhenLearnerEvidenceExists()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-quality-weak");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Probability");
        await SeedConceptGraphAsync(factory, user.UserId, topicId,
            ("sample-space", "Sample space", 0),
            ("conditional-probability", "Conditional probability", 1));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        db.KnowledgeTracingStates.Add(new KnowledgeTracingState
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId,
            TopicId = topicId,
            ConceptKey = "conditional-probability",
            Label = "Conditional probability",
            MasteryProbability = 0.20m,
            Confidence = 0.80m,
            EvidenceCount = 5,
            IncorrectCount = 4,
            RemediationNeed = "high",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<IPlanSequencingService>();
        var sequence = await service.BuildPlanSequenceAsync(user.UserId, new PlanQualityEvaluationRequestDto { TopicId = topicId });

        Assert.Equal("conditional-probability", sequence.Steps.First().ConceptKey);
        Assert.Equal("needs_repair", sequence.AdaptiveDiagnostic.PlanReadiness);
        Assert.Equal("beginner", sequence.AdaptiveDiagnostic.LearnerLevel);
        Assert.Equal("needs_repair", sequence.CoursePlanQuality.ReadinessStatus);
        Assert.Contains(sequence.CoursePlanQuality.RepairLoops, loop => loop.ConceptKey == "conditional-probability");
        Assert.Equal("misconception_probe", sequence.Steps.First().QuizHook.HookType);
        Assert.True(sequence.Steps.First().TutorHook.TutorMove is "scaffold" or "misconception_repair");
    }

    [Fact]
    public async Task StaleSourceAddsWarningWithoutClaimingSourceCertainty()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-quality-source");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Cell transport");
        await SeedConceptGraphAsync(factory, user.UserId, topicId, ("osmosis", "Osmosis", 0));
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Transport Source", "osmosis source evidence");

        using var scope = factory.Services.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();
        await lifecycle.BuildSourceEvidenceBundleAsync(user.UserId, topicId, question: "osmosis");
        await lifecycle.MarkSourceStaleAsync(user.UserId, sourceId, "changed");

        var service = scope.ServiceProvider.GetRequiredService<IPlanSequencingService>();
        var result = await service.EvaluatePlanSequenceAsync(user.UserId, new PlanQualityEvaluationRequestDto { TopicId = topicId });

        Assert.Contains(result.WarningIssues, i => i.Code == "source_readiness_limited");
        Assert.Contains(result.PlanContract.Steps, s => s.Evidence.Warnings.Any(w => w.Contains("sinirli", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain("resmi mufredat tamam", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EndpointsAreAuthenticatedAndCallerScoped()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-quality-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-quality-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Owner Plan");

        var eval = await owner.Client.PostAsJsonAsync("/api/plan-quality/evaluate", new PlanQualityEvaluationRequestDto
        {
            TopicId = topicId,
            ProposedSteps = [StrongStep("owner-concept", "Owner concept", "Owner sequence reason", [])]
        });
        eval.EnsureSuccessStatusCode();
        var dto = await eval.Content.ReadFromJsonAsync<PlanQualityEvaluationDto>();
        Assert.NotNull(dto);

        var ownerLatest = await owner.Client.GetAsync($"/api/plan-quality/topic/{topicId}/latest");
        ownerLatest.EnsureSuccessStatusCode();

        var crossUser = await other.Client.GetAsync($"/api/plan-quality/snapshots/{dto!.SnapshotId}");
        Assert.Equal(HttpStatusCode.NotFound, crossUser.StatusCode);

        var crossTopic = await other.Client.GetAsync($"/api/plan-quality/topic/{topicId}/readiness");
        Assert.Equal(HttpStatusCode.NotFound, crossTopic.StatusCode);
    }

    [Fact]
    public async Task TutorTurnStateConsumesLatestPlanStepSafely()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-quality-tutor");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Derivatives");

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPlanSequencingService>();
        var quality = await service.EvaluatePlanSequenceAsync(user.UserId, new PlanQualityEvaluationRequestDto
        {
            TopicId = topicId,
            ProposedSteps = [StrongStep("limit-definition", "Limit definition", "Limits come before derivative rules.", [])]
        });

        var assembler = scope.ServiceProvider.GetRequiredService<ITutorTurnStateAssembler>();
        var turn = await assembler.BuildAsync(
            user.UserId,
            topicId,
            null,
            "Bunu anlatir misin?",
            "",
            "",
            "",
            "",
            "",
            new TutorPolicyContextDto { GroundingStatus = "model_only" });

        Assert.Equal(quality.SnapshotId, turn.PlanQualitySnapshotId);
        Assert.Equal("limit-definition", turn.CurrentPlanStepId is null ? null : quality.PlanContract.Steps[0].ConceptKey);
        Assert.Equal("retrieval_practice", turn.CurrentPlanQuizHook);
        Assert.False(turn.PromptBlock.Contains("provider payload", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(turn.AdaptiveDiagnostic);
        Assert.NotNull(turn.CoursePlanQuality);
        Assert.Equal(quality.PlanContract.CoursePlanQuality.ReadinessStatus, turn.CoursePlanQuality!.ReadinessStatus);
        Assert.Contains("planReadiness", turn.PromptBlock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adaptiveDiagnosticIntent", turn.PromptBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", JsonSerializer.Serialize(turn), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ThinProvidedPlanIsMarkedThinWithSafeDiagnosticQuestions()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "plan-quality-thin");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Java algorithms");

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPlanSequencingService>();
        var result = await service.EvaluatePlanSequenceAsync(user.UserId, new PlanQualityEvaluationRequestDto
        {
            TopicId = topicId,
            ProposedSteps =
            [
                StrongStep("arrays", "Arrays", "Arrays are the first measurable concept.", [])
            ]
        });

        Assert.Equal("thin_plan", result.AdaptiveDiagnostic.PlanReadiness);
        Assert.Equal("thin_plan", result.CoursePlanQuality.ReadinessStatus);
        Assert.Equal("build_concept_graph", result.CoursePlanQuality.RecommendedNextAction);
        Assert.InRange(result.AdaptiveDiagnostic.RecommendedQuestions.Count, 2, 5);
        var publicJson = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("rawPrompt", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerId", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", publicJson, StringComparison.OrdinalIgnoreCase);
    }

    private static PlanStepContractDto StrongStep(string conceptKey, string title, string sequenceReason, IReadOnlyList<string> prerequisites) => new()
    {
        StepId = conceptKey,
        Title = title,
        Objective = $"{title} kavramini ornekle aciklamak ve mikro kontrolle olcmek.",
        ConceptKey = conceptKey,
        ConceptLabel = title,
        PrerequisiteConceptKeys = prerequisites,
        MasteryTarget = "micro_check_ready",
        EstimatedMinutes = 20,
        LearnerState = "needs_scaffold",
        RemediationNeed = "none",
        DifficultyBand = "core",
        SequenceReason = sequenceReason,
        Evidence = new PlanStepEvidenceDto
        {
            EvidenceBasis = ["concept_graph", "student_context"],
            SourceReadiness = "evidence_insufficient"
        },
        QuizHook = new PlanStepAssessmentHookDto
        {
            HookType = "retrieval_practice",
            ConceptKey = conceptKey,
            DifficultyBand = "core",
            UserSafeReason = "Bu adim kisa pratikle olculur."
        },
        TutorHook = new PlanStepTutorHookDto
        {
            TutorMove = "explain",
            ActiveConceptKey = conceptKey,
            UserSafeReason = "Tutor bu kavrami adim adim anlatir."
        },
        WikiHook = new PlanStepWikiHookDto
        {
            SourceReadiness = "evidence_insufficient"
        },
        SuccessCriteria = ["Kavrami aciklar.", "Mikro kontrolu tamamlar."],
        NextStepTrigger = "micro_check_passed",
        FallbackIfEvidenceWeak = "Once kisa diagnostic kontrol yap."
    };

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
            ApprovedResearchIntent = "test intent",
            TopicTitle = "test topic",
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

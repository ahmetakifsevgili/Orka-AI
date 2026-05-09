using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Korteks;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class LearningArchitectureTests
{
    [Fact]
    public async Task ConceptGraphBuilder_BuildsStableGraphWithPrerequisitesAndMisconceptions()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var builder = new ConceptGraphBuilder(db, null, NullLogger<ConceptGraphBuilder>.Instance);

        var result = await builder.BuildOrLoadAsync(
            userId,
            topicId,
            Guid.NewGuid(),
            "SQL query optimization indexes and execution plans",
            "SQL optimization",
            "SQL",
            "index and execution plan",
            ResearchContext());

        Assert.NotEqual(Guid.Empty, result.SnapshotId);
        Assert.False(string.IsNullOrWhiteSpace(result.Graph.IntentHash));
        Assert.True(result.Graph.Concepts.Count >= 6);
        Assert.Contains(result.Graph.Concepts, c => c.PrerequisiteKeys.Count > 0);
        Assert.Contains(result.Graph.Concepts, c => c.Misconceptions.Count > 0);
        Assert.Equal(result.Graph.IntentHash, ConceptGraphBuilder.ComputeIntentHash(
            "SQL query optimization indexes and execution plans",
            "SQL",
            "index and execution plan"));
    }

    [Fact]
    public async Task AssessmentGrammarEngine_AttachesRequiredMetadataToQuizJson()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var graphBuilder = new ConceptGraphBuilder(db, null, NullLogger<ConceptGraphBuilder>.Instance);
        var graph = await graphBuilder.BuildOrLoadAsync(userId, topicId, Guid.NewGuid(), "Java algorithms", "Java algorithms", "Java", "algorithms", ResearchContext());
        var grammarEngine = new AssessmentGrammarEngine(db, null, NullLogger<AssessmentGrammarEngine>.Instance);
        var draft = await grammarEngine.BuildOrLoadDraftAsync(userId, topicId, Guid.NewGuid(), Guid.NewGuid(), graph.Graph, 15);

        var rawQuiz = JsonSerializer.Serialize(Enumerable.Range(1, 15).Select(i => new
        {
            question = $"Question {i}",
            options = new[]
            {
                new { text = "Correct option", isCorrect = true },
                new { text = "Wrong option 1", isCorrect = false },
                new { text = "Wrong option 2", isCorrect = false },
                new { text = "Wrong option 3", isCorrect = false }
            },
            correctAnswer = "Correct option",
            explanation = "because",
            skillTag = $"skill-{i}",
            difficulty = "orta",
            conceptTag = $"concept-{i}",
            learningObjective = $"objective-{i}",
            questionType = "conceptual",
            expectedMisconceptionCategory = "Conceptual"
        }));

        var enriched = await grammarEngine.AttachQuestionMetadataAsync(rawQuiz, draft.Grammar);

        DiagnosticQuizQualityGate.EnsureAssessmentMetadataOrThrow(enriched, 15);
        Assert.Contains("assessmentItemId", enriched);
        Assert.Equal(15, await db.AssessmentItems.CountAsync());
        Assert.Equal(15, await db.AssessmentItems.CountAsync(i => i.GeneratedQuestionJson != null));
    }

    [Fact]
    public async Task DiagnosticProfileBuilder_ComputesConceptMasteryAndPersistsLearningState()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var quizRunId = Guid.NewGuid();
        db.QuizRuns.Add(new QuizRun { Id = quizRunId, UserId = userId, TopicId = topicId, TotalQuestions = 3 });
        db.QuizAttempts.AddRange(
            Attempt(userId, topicId, quizRunId, "indexes", true),
            Attempt(userId, topicId, quizRunId, "indexes", false),
            Attempt(userId, topicId, quizRunId, "joins", false));
        await db.SaveChangesAsync();

        var mastery = new ConceptMasteryService(db, NullLogger<ConceptMasteryService>.Instance);
        var builder = new DiagnosticProfileBuilder(db, null, mastery, NullLogger<DiagnosticProfileBuilder>.Instance);

        var profile = await builder.BuildAndSaveAsync(new PlanDiagnosticStateDto
        {
            UserId = userId,
            TopicId = topicId,
            QuizRunId = quizRunId,
            PlanRequestId = Guid.NewGuid(),
            QuizQuestionCount = 3
        }, await db.QuizAttempts.AsNoTracking().OrderBy(a => a.CreatedAt).ToListAsync());

        Assert.Equal(3, profile.AnsweredCount);
        Assert.Equal(33, profile.AccuracyPercent);
        Assert.Contains(profile.ConceptMasteries, m => m.ConceptKey == "indexes" && m.RemediationNeed == "medium");
        Assert.Contains(profile.ConceptMasteries, m => m.ConceptKey == "joins" && m.RemediationNeed == "high");
        Assert.Equal(2, await db.ConceptMasteries.CountAsync());
        Assert.Equal(1, await db.DiagnosticProfiles.CountAsync());
    }

    [Fact]
    public async Task ConceptGraphQualityService_FlagsCyclesDuplicatesAndCoverage()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var snapshotId = Guid.NewGuid();
        db.ConceptGraphSnapshots.Add(new ConceptGraphSnapshot
        {
            Id = snapshotId,
            UserId = userId,
            TopicId = topicId,
            PlanRequestId = Guid.NewGuid(),
            IntentHash = "intent",
            TopicTitle = "Quality",
            ApprovedResearchIntent = "Quality",
            Domain = "general",
            SourceConfidence = "low",
            SourceBundleHash = "source",
            GraphJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var graph = new ConceptGraphDto
        {
            SnapshotId = snapshotId,
            Concepts =
            [
                new LearningConceptDto { StableKey = "a", Label = "Alpha", LearningOutcomeKeys = ["a-outcome"] },
                new LearningConceptDto { StableKey = "b", Label = "Alpha", Misconceptions = ["mixes A and B"] },
                new LearningConceptDto { StableKey = "c", Label = "Gamma" }
            ],
            Relations =
            [
                new ConceptRelationDto { SourceConceptKey = "a", TargetConceptKey = "b", RelationType = "prerequisite" },
                new ConceptRelationDto { SourceConceptKey = "b", TargetConceptKey = "a", RelationType = "prerequisite" }
            ]
        };

        var service = new ConceptGraphQualityService(db, null, NullLogger<ConceptGraphQualityService>.Instance);
        var quality = await service.EvaluateAndSaveAsync(userId, topicId, Guid.NewGuid(), graph);

        Assert.Equal("critical", quality.QualityStatus);
        Assert.True(quality.HasPrerequisiteCycle);
        Assert.True(quality.DuplicateRatio > 0);
        Assert.Contains("prerequisite_cycle", quality.Failures);
        Assert.Equal(1, await db.ConceptGraphQualityRuns.CountAsync());
    }

    [Fact]
    public async Task AssessmentQualityService_ComputesCoverageSpreadAndItemStats()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var snapshotId = Guid.NewGuid();
        db.ConceptGraphSnapshots.Add(new ConceptGraphSnapshot
        {
            Id = snapshotId,
            UserId = userId,
            TopicId = topicId,
            PlanRequestId = Guid.NewGuid(),
            IntentHash = "intent",
            TopicTitle = "Assessment",
            ApprovedResearchIntent = "Assessment",
            Domain = "general",
            SourceConfidence = "medium",
            SourceBundleHash = "source",
            GraphJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var graph = new ConceptGraphDto
        {
            SnapshotId = snapshotId,
            Concepts = Enumerable.Range(1, 5)
                .Select(i => new LearningConceptDto { StableKey = $"c{i}", Label = $"Concept {i}", LearningOutcomeKeys = [$"o{i}"] })
                .ToList()
        };
        var grammar = new AssessmentGrammarDto
        {
            DraftId = Guid.NewGuid(),
            ConceptGraphSnapshotId = snapshotId,
            Items = graph.Concepts.Select((concept, index) => new AssessmentItemSpecDto
            {
                AssessmentItemId = Guid.NewGuid(),
                ConceptKey = concept.StableKey,
                ConceptLabel = concept.Label,
                CognitiveSkill = index switch { 0 => "conceptual", 1 => "procedural", 2 => "application", 3 => "analysis", _ => "misconception_probe" },
                Difficulty = index < 2 ? "kolay" : index < 4 ? "orta" : "zor",
                MisconceptionTarget = index == 4 ? "common misconception" : string.Empty,
                LearningOutcomeKeys = concept.LearningOutcomeKeys,
                OptionQualityRules = ["correct", "plausible distractor", "no labels"],
                ScoringRule = "selected_option_exact_match",
                Order = index
            }).ToList()
        };

        var service = new AssessmentQualityService(db, null, NullLogger<AssessmentQualityService>.Instance);
        var quality = await service.EvaluateAndSaveAsync(userId, topicId, Guid.NewGuid(), Guid.NewGuid(), grammar, graph);

        Assert.Equal(1m, quality.ConceptCoverage);
        Assert.True(quality.DifficultySpread >= 3);
        Assert.True(quality.CognitiveSkillSpread >= 5);
        Assert.NotEqual("critical", quality.QualityStatus);

        var item = new AssessmentItem
        {
            Id = grammar.Items[0].AssessmentItemId,
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = snapshotId,
            AssessmentItemKey = "item",
            ConceptKey = "c1",
            ConceptLabel = "Concept 1",
            QuestionType = "conceptual",
            CognitiveSkill = "conceptual",
            Difficulty = "kolay",
            CreatedAt = DateTime.UtcNow
        };
        db.AssessmentItems.Add(item);
        await db.SaveChangesAsync();

        var stat = await service.UpdateItemStatsAsync(new QuizAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            AssessmentItemId = item.Id,
            Question = "Q",
            UserAnswer = "A",
            IsCorrect = true,
            Explanation = "E",
            CreatedAt = DateTime.UtcNow
        });

        Assert.NotNull(stat);
        Assert.Equal(1, stat!.Attempts);
        Assert.Equal(1m, stat.CorrectRate);
    }

    [Fact]
    public async Task KnowledgeTracingService_UpdatesProbabilityWithoutOverconfidence()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var service = new KnowledgeTracingService(db, null, NullLogger<KnowledgeTracingService>.Instance);

        var first = await service.UpdateFromAttemptAsync(Attempt(userId, topicId, Guid.NewGuid(), "indexes", true));
        var second = await service.UpdateFromAttemptAsync(Attempt(userId, topicId, Guid.NewGuid(), "indexes", false));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(first!.MasteryProbability > 0.35m);
        Assert.True(second!.MasteryProbability < first.MasteryProbability);
        Assert.Equal(2, second.EvidenceCount);
        Assert.True(second.Confidence <= 0.55m);
        Assert.Equal("evidence_insufficient", second.RemediationNeed);
    }

    [Fact]
    public async Task AssessmentCalibrationService_ComputesItemReadinessAndExposure()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var snapshotId = await SeedConceptSnapshotAsync(db, userId, topicId);
        var item = SeedAssessmentItem(db, userId, topicId, snapshotId, "joins", "orta");
        db.AssessmentItemStats.Add(new AssessmentItemStat
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            AssessmentItemId = item.Id,
            ConceptGraphSnapshotId = snapshotId,
            ConceptKey = "joins",
            Attempts = 6,
            Correct = 4,
            Incorrect = 2,
            CorrectRate = 0.6667m,
            SkipRate = 0m,
            QualityStatus = "healthy",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new AssessmentCalibrationService(db);
        var run = await service.RunAsync(userId, topicId);

        Assert.Equal("watch", run.CalibrationStatus);
        Assert.Equal("not_ready", run.AdaptiveReadiness);
        Assert.Equal("thin", run.ItemBankHealth);
        Assert.Contains(run.Items, i => i.ConceptKey == "joins" && i.CalibrationStatus == "healthy");
        Assert.Equal(1, await db.AssessmentCalibrationRuns.CountAsync());
    }

    [Fact]
    public async Task AdaptiveAssessmentSelector_PrioritizesWeakConceptAndPenalizesExposure()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var snapshotId = await SeedConceptSnapshotAsync(db, userId, topicId);
        var weak = SeedAssessmentItem(db, userId, topicId, snapshotId, "indexes", "orta");
        var overexposed = SeedAssessmentItem(db, userId, topicId, snapshotId, "joins", "orta");
        db.KnowledgeTracingStates.AddRange(
            new KnowledgeTracingState
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                ConceptKey = "indexes",
                Label = "Indexes",
                MasteryProbability = 0.35m,
                Confidence = 0.35m,
                EvidenceCount = 2,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new KnowledgeTracingState
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                ConceptKey = "joins",
                Label = "Joins",
                MasteryProbability = 0.82m,
                Confidence = 0.80m,
                EvidenceCount = 5,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        db.AssessmentItemStats.AddRange(
            Stat(userId, topicId, weak.Id, "indexes", exposure: 0),
            Stat(userId, topicId, overexposed.Id, "joins", exposure: 20));
        await db.SaveChangesAsync();

        var selector = new AdaptiveAssessmentSelector(db);
        var selected = await selector.SelectNextAsync(new AdaptiveAssessmentSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            QuizRunId = Guid.NewGuid(),
            ConceptGraphSnapshotId = snapshotId,
            TargetConceptsJson = """["indexes","joins"]""",
            CreatedAt = DateTime.UtcNow
        });

        Assert.NotNull(selected);
        Assert.Equal("indexes", selected!.ConceptKey);
        Assert.Contains("kanıt", selected.DecisionReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.AssessmentItemStats.Where(s => s.AssessmentItemId == weak.Id).Select(s => s.ExposureCount).FirstAsync());
    }

    [Fact]
    public async Task LearningEventSchemaService_LogsViolationsForMalformedPayload()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var service = new LearningEventSchemaService(db, NullLogger<LearningEventSchemaService>.Instance);
        var entity = new LearningEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            EventType = "unknown.signal",
            Actor = "learner",
            Verb = "experienced",
            ObjectType = "activity",
            PayloadJson = "{}",
            OccurredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.LearningEvents.Add(entity);
        await db.SaveChangesAsync();

        var result = await service.ValidateAndLogAsync(entity);

        Assert.False(result.IsValid);
        Assert.Equal("learning.activity", result.NormalizedEventType);
        Assert.True(await db.LearningEventSchemaViolations.AnyAsync(v => v.ViolationCode == "missing_schema_version"));
    }

    [Fact]
    public async Task TutorPolicyTraceService_FlagsNoSourceAndNoActiveConceptRisk()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var service = new TutorPolicyTraceService(db, null, NullLogger<TutorPolicyTraceService>.Instance);

        var trace = await service.CreateTraceAsync(
            userId,
            topicId,
            sessionId: null,
            "Kaynak vererek direkt cevap verir misin?",
            new TutorPolicyContextDto
            {
                GroundingStatus = "model_only",
                SourceEvidenceCount = 0,
                DirectAnswerRisk = true,
                NextPedagogicalMove = "teach the next step",
                LearnerState = "no concept mastery yet"
            });

        Assert.Contains("no_active_concept", trace.PolicyViolations);
        Assert.Contains("source_claim_without_source_risk", trace.PolicyViolations);
        Assert.Contains("answer_before_hint_risk", trace.PolicyViolations);
        Assert.Equal(1, await db.TutorPolicyTraces.CountAsync());
    }

    [Fact]
    public async Task LearnerProfileService_UsesSessionStyleSignalWithoutOverclaimingLowEvidence()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var service = new LearnerProfileService(
            db,
            new LearningStyleSignalService(db),
            new AffectiveSignalService(db),
            new CognitiveLoadService(db));

        var profile = await service.BuildOrUpdateAsync(
            userId,
            topicId,
            Guid.NewGuid(),
            "Anlamadım, görsel çizerek adım adım anlatır mısın?",
            learningSignalContext: "",
            ideContext: "");

        Assert.Equal("visual", profile.PreferredStyleMode);
        Assert.Equal("confused", profile.AffectiveState);
        Assert.True(profile.IsLowEvidence);
        Assert.Equal(1, await db.LearningStyleSignals.CountAsync());
        Assert.Equal(1, await db.AffectiveSignals.CountAsync());
        Assert.Equal(1, await db.CognitiveLoadSignals.CountAsync());
    }

    [Fact]
    public async Task TutorActionPlanner_LowMasteryChoosesRemediationAndArtifacts()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var planner = new TutorActionPlanner(db, new TestTutorWorkingMemoryService());
        var state = new TutorTurnStateDto
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = Guid.NewGuid(),
            UserMessage = "Direkt cevabı ver, anlamadım",
            ActiveConceptKey = "indexes",
            ActiveConceptLabel = "Indexes",
            LearnerState = "needs_remediation",
            MasteryProbability = 0.30m,
            Confidence = 0.20m,
            AffectiveState = "confused",
            CognitiveLoad = "high",
            StyleMode = "step_by_step",
            DirectAnswerRisk = true
        };

        var plan = await planner.PlanAsync(state);

        Assert.Equal("remediate", plan.TeachingMode);
        Assert.Equal("hint_first_then_scaffold", plan.DirectAnswerPolicy);
        Assert.Contains(plan.ArtifactPlans, a => a.ArtifactType == "worked_example");
        Assert.Contains(plan.ArtifactPlans, a => a.ArtifactType == "micro_quiz");
        Assert.Equal(1, await db.TutorActionTraces.CountAsync());
    }

    [Fact]
    public async Task TutorReflectionService_LogsNoSourceViolationAndLearningEvent()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var service = new TutorReflectionService(
            db,
            new LearningEventSchemaService(db, NullLogger<LearningEventSchemaService>.Instance),
            new TestTutorWorkingMemoryService());
        var state = new TutorTurnStateDto
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = Guid.NewGuid(),
            ActiveConceptKey = "joins",
            SourceEvidenceCount = 0,
            DirectAnswerRisk = true
        };
        var plan = new TutorActionPlanDto
        {
            Id = Guid.NewGuid(),
            TutorTurnStateId = state.Id,
            UserId = userId,
            TopicId = topicId,
            SessionId = state.SessionId,
            TeachingMode = "explain",
            DirectAnswerPolicy = "hint_first_then_scaffold",
            GroundingPolicy = "model_ok_no_source_claim"
        };

        var reflection = await service.ReflectAsync(
            state,
            plan,
            "Kaynağa göre önce ipucu verelim. Sence joins neyi birleştirir?",
            []);

        Assert.True(reflection.SourceClaimWithoutSource);
        Assert.True(reflection.DirectAnswerRiskHandled);
        Assert.Equal(1, await db.TutorPolicyViolationsV2.CountAsync(v => v.ViolationType == "source_claim_without_source"));
        Assert.Equal(1, await db.LearningEvents.CountAsync(e => e.EventType == "tutor.policy.applied"));
    }

    [Fact]
    public async Task StandardsValidationAndExport_CoverCaseQtiAndCaliperProfiles()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var snapshotId = await SeedConceptSnapshotAsync(db, userId, topicId);
        db.LearningConcepts.Add(new LearningConcept
        {
            Id = Guid.NewGuid(),
            ConceptGraphSnapshotId = snapshotId,
            StableKey = "indexes",
            Label = "Indexes",
            LearningOutcomeKeysJson = """["outcome:indexes"]""",
            CreatedAt = DateTime.UtcNow
        });
        db.LearningOutcomes.Add(new LearningOutcome
        {
            Id = Guid.NewGuid(),
            ConceptGraphSnapshotId = snapshotId,
            StableKey = "outcome:indexes",
            Label = "Index kullanımını açıklar",
            Description = "Öğrenci index kullanımını açıklar.",
            CreatedAt = DateTime.UtcNow
        });
        db.AssessmentItems.Add(new AssessmentItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = snapshotId,
            AssessmentItemKey = "item:indexes",
            ConceptKey = "indexes",
            ConceptLabel = "Indexes",
            CognitiveSkill = "apply",
            Difficulty = "orta",
            ScoringRuleJson = """{"type":"exact"}""",
            LearningOutcomeKeysJson = """["outcome:indexes"]""",
            CreatedAt = DateTime.UtcNow
        });
        db.LearningEvents.Add(new LearningEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            EventType = "assessment.item.answered",
            Actor = "learner",
            Verb = "answered",
            ObjectType = "assessment_item",
            PayloadJson = """{"schemaVersion":"orka.learning-event.v1"}""",
            CreatedAt = DateTime.UtcNow,
            OccurredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var alignment = new StandardsAlignmentService(db);
        var validation = new StandardsValidationService(db, alignment);
        var export = new StandardsExportService(db, alignment);

        var run = await validation.ValidateAsync(userId, topicId);
        var exported = await export.ExportAsync(userId, topicId);

        Assert.Equal("healthy", run.Status);
        Assert.Equal(0, run.IssueCount);
        Assert.Contains("qtiLike", exported.PayloadJson);
        Assert.Equal(1, await db.StandardsValidationRuns.CountAsync());
        Assert.Equal(3, await db.StandardsExportItems.CountAsync());
    }

    [Fact]
    public async Task RetentionCleanupService_PurgesAudioBytesButKeepsLearningRecords()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var expired = DateTime.UtcNow.AddDays(-1);
        var job = new AudioOverviewJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            Status = "ready",
            Script = "[HOCA]: Kalıcı script.",
            AudioBytes = [1, 2, 3],
            AudioByteLength = 3,
            AudioExpiresAt = expired,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow.AddDays(-10)
        };
        var interaction = new ClassroomInteraction
        {
            Id = Guid.NewGuid(),
            ClassroomSessionId = Guid.NewGuid(),
            Question = "Anlamadım",
            AnswerScript = "[HOCA]: Tekrar anlatalım.",
            AudioBytes = [4, 5],
            AudioByteLength = 2,
            AudioExpiresAt = expired,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };
        db.AudioOverviewJobs.Add(job);
        db.ClassroomInteractions.Add(interaction);
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Retention:AudioBytesDays"] = "7" })
            .Build();
        var service = new RetentionCleanupService(db, config);

        var summary = await service.PurgeExpiredAudioAsync();

        var storedJob = await db.AudioOverviewJobs.SingleAsync();
        var storedInteraction = await db.ClassroomInteractions.SingleAsync();
        Assert.Null(storedJob.AudioBytes);
        Assert.Null(storedInteraction.AudioBytes);
        Assert.Equal("[HOCA]: Kalıcı script.", storedJob.Script);
        Assert.Equal("[HOCA]: Tekrar anlatalım.", storedInteraction.AnswerScript);
        Assert.Equal(2, summary.PurgedAudioCount);
    }

    private static OrkaDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<OrkaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<(Guid UserId, Guid TopicId)> SeedAsync(OrkaDbContext db)
    {
        var userId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, Email = $"{userId:N}@example.com", CreatedAt = DateTime.UtcNow });
        db.Topics.Add(new Topic { Id = topicId, UserId = userId, Title = "Learning Architecture", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return (userId, topicId);
    }

    private static async Task<Guid> SeedConceptSnapshotAsync(OrkaDbContext db, Guid userId, Guid topicId)
    {
        var snapshotId = Guid.NewGuid();
        db.ConceptGraphSnapshots.Add(new ConceptGraphSnapshot
        {
            Id = snapshotId,
            UserId = userId,
            TopicId = topicId,
            PlanRequestId = Guid.NewGuid(),
            IntentHash = $"intent-{snapshotId:N}",
            TopicTitle = "Adaptive",
            ApprovedResearchIntent = "Adaptive",
            Domain = "general",
            SourceConfidence = "medium",
            SourceBundleHash = "source",
            GraphJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return snapshotId;
    }

    private static AssessmentItem SeedAssessmentItem(OrkaDbContext db, Guid userId, Guid topicId, Guid snapshotId, string conceptKey, string difficulty)
    {
        var item = new AssessmentItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = snapshotId,
            AssessmentItemKey = $"item:{conceptKey}",
            ConceptKey = conceptKey,
            ConceptLabel = conceptKey,
            CognitiveSkill = "conceptual",
            Difficulty = difficulty,
            GeneratedQuestionJson = $$"""
            {"question":"{{conceptKey}} nedir?","options":[{"id":"A","text":"Doğru açıklama","isCorrect":true},{"id":"B","text":"Yanlış açıklama","isCorrect":false}],"explanation":"A doğru.","skillTag":"{{conceptKey}}","conceptKey":"{{conceptKey}}"}
            """,
            CreatedAt = DateTime.UtcNow
        };
        db.AssessmentItems.Add(item);
        return item;
    }

    private static AssessmentItemStat Stat(Guid userId, Guid topicId, Guid itemId, string conceptKey, int exposure) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TopicId = topicId,
        AssessmentItemId = itemId,
        ConceptKey = conceptKey,
        Attempts = 6,
        Correct = 4,
        Incorrect = 2,
        CorrectRate = 0.6667m,
        DiscriminationProxy = 0.1667m,
        SkipRate = 0m,
        QualityStatus = "healthy",
        DifficultyEstimate = 0.3333m,
        DiscriminationEstimate = 0.3334m,
        ExposureCount = exposure,
        CalibrationStatus = "healthy",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static CompressedPlanResearchContextDto ResearchContext() => new()
    {
        Topic = "SQL optimization",
        GroundingMode = GroundingMode.SourceGrounded,
        SourceCount = 3,
        CurriculumMapHints =
        [
            "schema reading curriculum module",
            "index fundamentals practice",
            "execution plan lesson",
            "join optimization lab"
        ],
        PrerequisiteHints = ["SELECT WHERE JOIN basics", "table relationship vocabulary"],
        LikelyMisconceptions = ["index every column", "ignore query plan evidence"],
        KeyFacts = ["filter selectivity matters", "covering indexes can reduce lookups"],
        TopSources =
        [
            new SourceEvidenceDto("Docs", "Search", "https://example.com/sql", "SQL docs", "snippet", null, DateTimeOffset.UtcNow, 1, "web", null, null),
            new SourceEvidenceDto("Wiki", "Search", "https://example.com/wiki", "Query plan", "snippet", null, DateTimeOffset.UtcNow, 1, "web", null, null),
            new SourceEvidenceDto("Course", "Search", "https://example.com/course", "Optimization course", "snippet", null, DateTimeOffset.UtcNow, 1, "web", null, null)
        ]
    };

    private static QuizAttempt Attempt(Guid userId, Guid topicId, Guid quizRunId, string conceptKey, bool correct) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TopicId = topicId,
        QuizRunId = quizRunId,
        Question = $"Question {conceptKey}",
        UserAnswer = "A",
        IsCorrect = correct,
        Explanation = "explanation",
        SkillTag = conceptKey,
        SourceRefsJson = $$"""{"conceptKey":"{{conceptKey}}","conceptTag":"{{conceptKey}}","misconceptionTarget":"Conceptual"}""",
        CreatedAt = DateTime.UtcNow
    };

    private sealed class TestTutorWorkingMemoryService : ITutorWorkingMemoryService
    {
        public Task<TutorWorkingMemorySnapshot> SaveTurnSnapshotAsync(TutorTurnStateDto state, CancellationToken ct = default) =>
            Task.FromResult(new TutorWorkingMemorySnapshot
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                TopicId = state.TopicId,
                SessionId = state.SessionId,
                ActiveConceptKey = state.ActiveConceptKey,
                SnapshotJson = "{}",
                CreatedAt = DateTime.UtcNow
            });

        public Task<TutorMemoryPatchDto> ApplyPatchAsync(Guid userId, Guid? topicId, Guid? sessionId, string patchType, object patch, CancellationToken ct = default) =>
            Task.FromResult(new TutorMemoryPatchDto
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                SessionId = sessionId,
                PatchType = patchType,
                PatchJson = "{}"
            });

        public Task RecordStreamEventAsync(Guid sessionId, string eventType, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}

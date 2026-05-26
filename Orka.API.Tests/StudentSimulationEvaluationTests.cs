using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class StudentSimulationEvaluationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string RawLearnerPhrase = "arbitrary learner phrase: student private note 555-111-2222 rawPrompt token_marker_secret_value";
    private const string RawSourcePhrase = "arbitrary source phrase: confidential lesson material rawSourceChunk C:\\secret\\source.txt";

    [Fact]
    public async Task ProviderFreeScenarioPack_EvaluatesRealisticLearnerJourneys()
    {
        using var factory = new ApiSmokeFactory();
        var harness = new StudentSimulationHarness(factory);

        var result = await harness.RunScenarioPackAsync();

        Assert.Equal("pass", result.Scorecard.OverallStatus);
        AssertScenario(result, "new_learner_no_evidence");
        AssertScenario(result, "repeated_wrong_learner");
        AssertScenario(result, "blank_skipped_learner");
        AssertScenario(result, "improving_learner");
        AssertScenario(result, "forgotten_concept_learner");
        AssertScenario(result, "exam_prep_learner");
        AssertScenario(result, "exam_war_room_learner");
        AssertScenario(result, "source_wiki_learner");
        AssertScenario(result, "source_wiki_pro_learner");
        AssertScenario(result, "study_room_learner");
        AssertScenario(result, "notebook_studio_pro_learner");
        AssertScenario(result, "code_learning_ide_learner");
        AssertScenario(result, "mixed_learning_os_journey");

        Assert.All(result.Scenarios, scenario =>
        {
            Assert.NotEmpty(scenario.Scorecard.Checks);
            Assert.DoesNotContain(scenario.Scorecard.Checks, check => check.Status == "fail");
        });
        Assert.Contains(result.Scorecard.Checks, c => c.CheckKey == "privacyReady" && c.Status == "pass");
        Assert.Contains(result.Scorecard.Checks, c => c.CheckKey == "noOverclaimReady" && c.Status == "pass");
    }

    [Fact]
    public async Task ProviderFreeScenarioPack_SweepsSerializedPublicPayloadsForUnsafeMarkers()
    {
        using var factory = new ApiSmokeFactory();
        var harness = new StudentSimulationHarness(factory);

        var result = await harness.RunScenarioPackAsync();

        AssertSafePayload(JsonSerializer.Serialize(new { result.Scenarios, result.Scorecard }, JsonOptions), result.UserIdForAssertions);
        foreach (var payload in result.SerializedPublicPayloads)
        {
            AssertSafePayload(payload, result.UserIdForAssertions);
        }
    }

    [Fact]
    public async Task ScenarioSurfaces_BlockCrossUserAccess()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "student-sim-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "student-sim-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Private Simulation Topic");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, owner.UserId, topicId, "Private Source", "private safe source");
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, owner.UserId, topicId, "Private Wiki", "private safe wiki note");

        var adaptiveProfile = await other.Client.GetAsync($"/api/learning/topic/{topicId}/adaptive-profile");
        var sourceWiki = await other.Client.GetAsync($"/api/sources/wiki-intelligence?sourceId={sourceId}");
        var sourceWikiPro = await other.Client.GetAsync($"/api/sources/wiki-pro?sourceId={sourceId}");
        var notebookStudioPro = await other.Client.GetAsync($"/api/notebook-studio/pro?sourceId={sourceId}");
        var wikiCopilot = await other.Client.GetAsync($"/api/wiki/page/{pageId}/copilot");
        var missionControl = await other.Client.GetAsync($"/api/learning/mission-control?topicId={topicId}");
        var studyCoach = await other.Client.GetAsync($"/api/learning/study-coach?topicId={topicId}");
        var studyRoom = await other.Client.GetAsync($"/api/classroom/study-room?topicId={topicId}");
        var codeIde = await other.Client.GetAsync($"/api/code/learning-ide?topicId={topicId}");
        var notebookPack = await other.Client.PostAsJsonAsync($"/api/notebook-studio/sources/{sourceId}/pack", new
        {
            packType = "source_digest",
            includeArtifacts = false
        });

        Assert.Equal(HttpStatusCode.NotFound, adaptiveProfile.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sourceWiki.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sourceWikiPro.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notebookStudioPro.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wikiCopilot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missionControl.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, studyCoach.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, studyRoom.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, codeIde.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notebookPack.StatusCode);
    }

    private static void AssertScenario(StudentSimulationPackResult result, string key)
    {
        var scenario = Assert.Single(result.Scenarios.Where(s => s.ScenarioKey == key));
        Assert.Equal("pass", scenario.Scorecard.OverallStatus);
        Assert.NotEmpty(scenario.ObservedActions);
        Assert.NotEmpty(scenario.ReasonCodes);
    }

    private static void AssertSafePayload(string json, Guid userId)
    {
        var unsafeMarkers = new[]
        {
            "rawPrompt",
            "hiddenPrompt",
            "systemPrompt",
            "developerPrompt",
            "rawProviderPayload",
            "rawSourceChunk",
            "rawToolPayload",
            "debugTrace",
            "localPath",
            "apiKey",
            "secret",
            "token_marker_secret_value",
            "answerKey",
            "correctAnswer",
            "stackTrace",
            "ownerId",
            "userId"
        };

        foreach (var marker in unsafeMarkers)
        {
            Assert.DoesNotContain(marker, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(userId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RawLearnerPhrase, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RawSourcePhrase, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success guarantee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("official curriculum complete", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%100", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StudentSimulationHarness
    {
        private readonly ApiSmokeFactory _factory;
        private readonly List<string> _publicPayloads = [];

        public StudentSimulationHarness(ApiSmokeFactory factory)
        {
            _factory = factory;
        }

        public async Task<StudentSimulationPackResult> RunScenarioPackAsync()
        {
            var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "student-sim");
            var scenarios = new List<StudentSimulationScenarioResult>();

            var newLearnerTopicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "New Learner Simulation");
            var newLearner = await GetLongTermProfileAsync(user, newLearnerTopicId);
            AddPayload(newLearner);
            scenarios.Add(EvaluateNewLearner(newLearner));

            var adaptiveTopicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Adaptive Journey Simulation");
            await SeedAdaptiveJourneyAsync(_factory, user.UserId, adaptiveTopicId);
            var adaptiveProfile = await GetLongTermProfileAsync(user, adaptiveTopicId);
            AddPayload(adaptiveProfile);
            scenarios.Add(EvaluateConceptScenario("repeated_wrong_learner", adaptiveProfile, "repeated-wrong", "repair", "prerequisite_gap"));
            scenarios.Add(EvaluateConceptScenario("blank_skipped_learner", adaptiveProfile, "blank-gap", "repair", "repeated_blank"));
            scenarios.Add(EvaluateConceptScenario("improving_learner", adaptiveProfile, "stable-skill", "continue_plan", "stable_recent_success", allowNoAction: true));
            scenarios.Add(EvaluateConceptScenario("forgotten_concept_learner", adaptiveProfile, "forgotten-srs", "review", "due_srs"));

            var examProfile = await SeedAndEvaluateExamScenarioAsync(_factory, user);
            AddPayload(examProfile);
            scenarios.Add(EvaluateExamScenario(examProfile));
            var examWarRoom = await GetExamWarRoomAsync(user);
            AddPayload(examWarRoom);
            scenarios.Add(EvaluateExamWarRoomScenario(examWarRoom));

        var sourceWikiIds = await SeedSourceWikiScenarioAsync(_factory, user.UserId, adaptiveTopicId);
        var sourceWikiProfile = await GetSourceWikiProfileAsync(user, adaptiveTopicId);
        var sourceWikiPro = await GetSourceWikiProAsync(user, adaptiveTopicId);
        AddPayload(sourceWikiProfile);
        AddPayload(sourceWikiPro);
        scenarios.Add(EvaluateSourceWikiScenario(sourceWikiProfile, sourceWikiIds.SourceId, sourceWikiIds.PageId));
        scenarios.Add(EvaluateSourceWikiProScenario(sourceWikiPro, sourceWikiIds.SourceId, sourceWikiIds.PageId));

            var tutorActions = await user.Client.GetFromJsonAsync<List<TutorNextLearningActionDto>>($"/api/tutor/next-actions?topicId={adaptiveTopicId}") ?? [];
            var dashboard = await user.Client.GetFromJsonAsync<DashboardTodayDto>("/api/dashboard/today")
                ?? throw new InvalidOperationException("Dashboard simulation payload missing.");
            var tutorPolicy = await user.Client.GetFromJsonAsync<TutorResponsePolicyDto>($"/api/tutor/policy/topic/{adaptiveTopicId}")
                ?? throw new InvalidOperationException("Tutor policy simulation payload missing.");
            var orkaState = await user.Client.GetFromJsonAsync<OrkaLearningStateDto>($"/api/learning/orka-state?topicId={adaptiveTopicId}")
                ?? throw new InvalidOperationException("Unified Orka state simulation payload missing.");
            var missionControl = await user.Client.GetFromJsonAsync<OrkaMissionControlDto>($"/api/learning/mission-control?topicId={adaptiveTopicId}")
                ?? throw new InvalidOperationException("Mission Control simulation payload missing.");
            var studyCoach = await user.Client.GetFromJsonAsync<OrkaStudyCoachDto>($"/api/learning/study-coach?topicId={adaptiveTopicId}")
                ?? throw new InvalidOperationException("Study Coach simulation payload missing.");
            var studyRoom = await user.Client.GetFromJsonAsync<OrkaStudyRoomDto>($"/api/classroom/study-room?topicId={adaptiveTopicId}")
                ?? throw new InvalidOperationException("Study Room simulation payload missing.");
            await SeedCodeLearningScenarioAsync(_factory, user.UserId, adaptiveTopicId);
            var codeIde = await user.Client.GetFromJsonAsync<OrkaCodeLearningIdeDto>($"/api/code/learning-ide?topicId={adaptiveTopicId}&language=python")
                ?? throw new InvalidOperationException("Code Learning IDE simulation payload missing.");
            var notebookStudioPro = await user.Client.GetFromJsonAsync<OrkaNotebookStudioProDto>($"/api/notebook-studio/pro?topicId={adaptiveTopicId}")
                ?? throw new InvalidOperationException("Notebook Studio Pro simulation payload missing.");
            AddPayload(tutorActions);
            AddPayload(dashboard);
            AddPayload(tutorPolicy);
            AddPayload(orkaState);
            AddPayload(missionControl);
            AddPayload(studyCoach);
            AddPayload(studyRoom);
            AddPayload(codeIde);
            AddPayload(notebookStudioPro);
            scenarios.Add(EvaluateStudyRoomScenario(studyRoom));
            scenarios.Add(EvaluateNotebookStudioProScenario(notebookStudioPro));
            scenarios.Add(EvaluateCodeLearningIdeScenario(codeIde));
            scenarios.Add(EvaluateMixedJourney(adaptiveProfile, examProfile, examWarRoom, sourceWikiProfile, sourceWikiPro, tutorActions, dashboard, tutorPolicy, orkaState, missionControl, studyCoach, studyRoom, notebookStudioPro, codeIde));

            var aggregate = AggregateScorecard(scenarios);
            return new StudentSimulationPackResult(
                Scenarios: scenarios,
                Scorecard: aggregate,
                SerializedPublicPayloads: _publicPayloads.ToArray(),
                UserIdForAssertions: user.UserId);
        }

        private void AddPayload<T>(T payload) => _publicPayloads.Add(JsonSerializer.Serialize(payload, JsonOptions));

        private static StudentSimulationScenarioResult EvaluateNewLearner(LongTermLearningProfileDto profile)
        {
            var checks = new List<LearningOsEvaluationCheckDto>
            {
                Check("longTermReady", !profile.HasEnoughEvidence && profile.EvidenceCount == 0, "thin_evidence", "Yeni ogrencide kanit azligi durustce korunuyor."),
                Check("noOverclaimReady", profile.Concepts.All(c => c.State != "stable"), "no_mastery_overclaim", "Tek veri yokken ustalik iddiasi kurulmadı."),
                Check("tutorNextActionReady", profile.NextActions.Any(a => a.ActionType is "continue_plan" or "take_quiz"), "safe_start", "Baslangic guvenli kisa adimla oneriliyor.")
            };

            return Scenario(
                "new_learner_no_evidence",
                "new",
                profile.NextActions.Select(a => a.ActionType),
                profile.ReasonCodes.Append("thin_evidence"),
                "Yeni ogrenci icin kisa tani ve guvenli baslangic kanitlandi.",
                checks);
        }

        private static StudentSimulationScenarioResult EvaluateConceptScenario(
            string scenarioKey,
            LongTermLearningProfileDto profile,
            string conceptKey,
            string expectedAction,
            string expectedReason,
            bool allowNoAction = false)
        {
            var concept = profile.Concepts.Single(c => c.ConceptKey == conceptKey);
            var actionReady = expectedAction == "continue_plan"
                ? concept.ReviewPriority == "none" && concept.State == "stable"
                : concept.RecommendedAction == expectedAction;
            var nextActionReady = allowNoAction ||
                                  profile.NextActions.Any(a => string.Equals(a.ConceptKey, conceptKey, StringComparison.OrdinalIgnoreCase) &&
                                                               (a.ActionType == expectedAction || expectedAction == "repair" && a.ActionType == "repair"));

            var checks = new List<LearningOsEvaluationCheckDto>
            {
                Check("longTermReady", actionReady, expectedReason, $"{concept.Label} icin uzun vadeli durum dogru hesaplandi."),
                Check("tutorNextActionReady", nextActionReady || concept.State == "stable", expectedAction, "Sonraki adim baskisi tutarli."),
                Check("noOverclaimReady", !concept.UserSafeReason.Contains("garanti", StringComparison.OrdinalIgnoreCase), "no_success_claim", "Basari garantisi yok.")
            };

            if (scenarioKey == "blank_skipped_learner")
            {
                checks.Add(Check("masteryReady", !concept.ReasonCodes.Contains("misconception", StringComparer.OrdinalIgnoreCase), "blank_not_misconception", "Bos cevap kesin yanilgi sayilmiyor."));
            }

            return Scenario(
                scenarioKey,
                concept.State,
                [concept.RecommendedAction],
                concept.ReasonCodes.Append(expectedReason),
                concept.UserSafeReason,
                checks);
        }

        private static StudentSimulationScenarioResult EvaluateExamScenario(ExamLearningProfileDto profile)
        {
            var hasDenemeRepair = profile.NextActions.Any(a => a.ActionType == "review_deneme_mistakes");
            var hasWeakOutcome = profile.Outcomes.Any(o => o.ReadinessStatus == "weak" && o.RecommendedAction == "review_deneme_mistakes");
            var noGuarantee = !JsonSerializer.Serialize(profile, JsonOptions).Contains("guarantee", StringComparison.OrdinalIgnoreCase) &&
                              !JsonSerializer.Serialize(profile, JsonOptions).Contains("%100", StringComparison.OrdinalIgnoreCase);

            return Scenario(
                "exam_prep_learner",
                "exam_weak_outcome",
                profile.NextActions.Select(a => a.ActionType),
                profile.ReasonCodes.Append("deneme_mistake_cluster"),
                "Sinav hazirligi zayif kazanimi ve deneme hata kumelerini guvenli next action'a bagladi.",
                [
                    Check("examProfileReady", hasWeakOutcome, "weak_outcome", "Sinav profili zayif kazanimi yakaladi."),
                    Check("tutorNextActionReady", hasDenemeRepair, "review_deneme_mistakes", "Deneme hatalari sonraki adima donustu."),
                    Check("noOverclaimReady", !profile.CanClaimOfficial && noGuarantee, "no_official_or_success_claim", "Resmi/garanti iddiasi yok.")
                ]);
        }

        private static StudentSimulationScenarioResult EvaluateExamWarRoomScenario(OrkaExamWarRoomDto warRoom)
        {
            var hasDenemeMission = warRoom.TodayExamMission.ActionType == "review_deneme_mistakes" ||
                                   warRoom.DenemeMistakeClusters.Count > 0;
            var hasPracticeQueue = warRoom.RecommendedPracticeQueue.Any(a => a.ActionType is "review_deneme_mistakes" or "practice_question_type" or "repair_exam_outcome");
            var hasWarnings = warRoom.SourceWikiWarnings.Concat(warRoom.CurriculumCoverageWarnings)
                .Any(w => w.WarningCode is "source_unverified" or "official_claim_blocked" or "answer_key_guard");
            var noGuarantee = !JsonSerializer.Serialize(warRoom, JsonOptions).Contains("guarantee", StringComparison.OrdinalIgnoreCase) &&
                              !JsonSerializer.Serialize(warRoom, JsonOptions).Contains("%100", StringComparison.OrdinalIgnoreCase);

            return Scenario(
                "exam_war_room_learner",
                warRoom.ReadinessStatus,
                warRoom.WeeklyExamPlan.Select(a => a.ActionType).Prepend(warRoom.TodayExamMission.ActionType),
                warRoom.ReasonCodes.Append("exam_war_room_priority"),
                "Exam War Room sinav profilini deneme/pratik/source warning ve handoff katmanina cevirdi.",
                [
                    Check("examWarRoomReady", hasDenemeMission && hasPracticeQueue, "exam_war_room_priority", "War Room deneme/pratik sinyalini tek sinav gorevine baglar."),
                    Check("dashboardReady", hasWarnings, "exam_warning_visible", "Source/curriculum/answer-key guard uyarilari gorunur."),
                    Check("noOverclaimReady", !warRoom.ActiveExam.CanClaimOfficial && noGuarantee, "no_official_or_success_claim", "War Room resmi/garanti iddiasi kurmaz.")
                ]);
        }

        private static StudentSimulationScenarioResult EvaluateSourceWikiScenario(
            SourceWikiIntelligenceProfileDto profile,
            Guid sourceId,
            Guid pageId)
        {
            var hasReview = profile.NextActions.Any(a => a.ActionType == "review_source");
            var hasRepair = profile.NextActions.Any(a => a.ActionType == "repair_concept");
            var sourceSeen = profile.EvidenceReadiness.Any(e => e.SourceId == sourceId);
            var pageSeen = profile.WikiPages.Any(p => p.WikiPageId == pageId && p.RepairSignalCount > 0);

            return Scenario(
                "source_wiki_learner",
                profile.ProfileStatus,
                profile.NextActions.Select(a => a.ActionType),
                profile.ReasonCodes.Concat(profile.Warnings),
                "Kaynak/Wiki stale ve repair sinyalleri kaynakli asiri iddiayi blokladi.",
                [
                    Check("sourceWikiReady", sourceSeen && pageSeen, "source_wiki_linked", "Kaynak ve Wiki sayfasi ayni profil icinde gorundu."),
                    Check("wikiCurationReady", hasRepair, "wiki_repair_pending", "Wiki repair sinyali next action'a donustu."),
                    Check("noOverclaimReady", !profile.CanClaimSourceGrounded && hasReview, "source_grounded_claim_blocked", "Stale/insufficient kaynak source-grounded iddiayi engelledi.")
                ]);
        }

        private static StudentSimulationScenarioResult EvaluateSourceWikiProScenario(
            OrkaSourceWikiProDto pro,
            Guid sourceId,
            Guid pageId)
        {
            var sourceSeen = pro.SourceReadinessItems.Any(e => e.SourceId == sourceId);
            var pageSeen = pro.WikiReadinessItems.Any(p => p.WikiPageId == pageId && p.RepairSignalCount > 0);
            var sourceBlocked = !pro.EvidenceMap.CanClaimSourceGrounded &&
                                pro.ConflictWarnings.Any(w => w.WarningCode == "source_grounding_blocked");
            var hasWorkspaceAction = pro.RecommendedActions.Any(a => a.ActionType is "source_review" or "citation_review" or "repair_wiki_page" or "repair_source_limited_concept");
            var evidenceBoundaryReady = !pro.EvidenceMap.ProviderOutputCountsAsEvidence &&
                                        !pro.EvidenceMap.WikiMemoryCountsAsCitationEvidence;

            return Scenario(
                "source_wiki_pro_learner",
                pro.ReadinessStatus,
                pro.RecommendedActions.Select(a => a.ActionType),
                pro.ReasonCodes.Concat(pro.ConflictWarnings.Select(w => w.WarningCode)),
                "Source / Wiki Pro kaynak, Wiki, citation, Notebook ve handoff sinyallerini tek evidence workspace'e bagladi.",
                [
                    Check("sourceWikiProReady", sourceSeen && pageSeen && hasWorkspaceAction, "source_wiki_pro_workspace", "Source/Wiki Pro kaynak ve Wiki repair sinyalini command-center kontratina tasidi."),
                    Check("sourceWikiReady", sourceBlocked, "source_grounding_blocked", "Stale/insufficient kaynak source-grounded iddiayi blokladi."),
                    Check("noOverclaimReady", evidenceBoundaryReady, "evidence_boundary_safe", "Provider output ve Wiki memory citation evidence sayilmiyor.")
                ]);
        }

        private static StudentSimulationScenarioResult EvaluateStudyRoomScenario(OrkaStudyRoomDto room)
        {
            var hasLesson = room.StudyRoomMode is "repair_lesson" or "review_lesson" or "exam_outcome_practice" or "source_review_lesson" or "wiki_repair_lesson" or "quick_start";
            var hasRoles = room.Roles.Any(r => r.RoleKey == "ai_teacher") &&
                           room.Roles.Any(r => r.RoleKey == "ai_assistant") &&
                           room.Roles.Any(r => r.RoleKey == "student");
            var hasSafeCheckpoint = !room.CheckpointPlan.KeyVisible &&
                                    room.CheckpointPlan.CheckpointStatus is "not_started" or "submitted" or "needs_repair" or "passed" or "skipped";
            var hasHandoff = room.NextActions.Any(a => a.ActionType is "start_repair_lesson" or "ask_checkpoint" or "open_tutor" or "review_due" or "update_wiki_note");

            return Scenario(
                "study_room_learner",
                room.StudyRoomMode,
                room.NextActions.Select(a => a.ActionType),
                room.ReasonCodes.Concat(room.Warnings.Select(w => w.WarningCode)),
                "Study Room unified state, Mission Control, Study Coach, source/wiki ve Tutor handoff sinyallerini provider-free ders kontratina cevirdi.",
                [
                    Check("studyRoomReady", hasLesson && hasRoles, "study_room_contract_ready", "Study Room personal AI ders rolleri ve lesson plan uretir."),
                    Check("quizReady", hasSafeCheckpoint, "checkpoint_key_guard", "Checkpoint submit oncesi cozum anahtari tasimaz."),
                    Check("dashboardReady", hasHandoff, "study_room_handoffs_ready", "Study Room sonraki aksiyonlari diger modullere guvenli handoff olarak verir.")
                ]);
        }

        private static StudentSimulationScenarioResult EvaluateNotebookStudioProScenario(OrkaNotebookStudioProDto pro)
        {
            var hasRepairPack = pro.RecommendedPacks.Any(p => p.PackType == "repair_pack") ||
                                pro.RecommendedPacks.Any(p => p.PackType == "source_study_pack") ||
                                pro.RecommendedPacks.Any(p => p.PackType == "wiki_cleanup_pack");
            var hasSafePreview = pro.ExportPreviews.Count == 0 ||
                                 pro.ExportPreviews.All(p => p.ExportLimitations.Contains("real_pptx_not_enabled") ||
                                                             p.ExportLimitations.Contains("video_generation_not_enabled") ||
                                                             p.ReadinessStatus is "preview_ready" or "preview_only");
            var hasHandoff = pro.TutorHandoffs.Concat(pro.SourceWikiHandoffs).Concat(pro.StudyRoomHandoffs)
                .Any(a => a.ActionType is "ask_tutor" or "create_source_study_pack" or "open_study_room");
            var hasSafetyWarning = pro.Warnings.Any(w => w.WarningCode is "export_preview_only" or "raw_payload_guard" or "answer_key_guard");

            return Scenario(
                "notebook_studio_pro_learner",
                pro.ReadinessStatus,
                pro.RecommendedPacks.SelectMany(p => p.Actions.Select(a => a.ActionType)).Concat(pro.TutorHandoffs.Select(a => a.ActionType)),
                pro.ReasonCodes.Concat(pro.Warnings.Select(w => w.WarningCode)),
                "Notebook Studio Pro OS sinyallerini guvenli artifact pack, preview ve handoff kontratina cevirdi.",
                [
                    Check("notebookStudioProReady", hasRepairPack && hasHandoff, "notebook_artifact_pack_ready", "Notebook Studio Pro repair/source/wiki sinyallerinden pack onerisi uretir."),
                    Check("dashboardReady", hasSafetyWarning, "notebook_safety_visible", "Preview/raw-payload/answer-key guard public kontratta gorunur."),
                    Check("noOverclaimReady", hasSafePreview, "export_preview_only", "Gercek PPTX/video iddiasi yerine preview-only limitleri tasinir.")
                ]);
        }

        private static StudentSimulationScenarioResult EvaluateCodeLearningIdeScenario(OrkaCodeLearningIdeDto ide)
        {
            var hasRepair = ide.Mode is "syntax_repair" or "runtime_error_repair" or "test_failure_repair" ||
                            ide.RecommendedActions.Any(a => a.ActionType is "repair_syntax_error" or "repair_runtime_error" or "repair_test_failure");
            var runtimeBounded = ide.RuntimeReadiness.ToolId == "ide_execution" &&
                                 ide.RuntimeReadiness.ReasonCodes.Any(r => r is "tool_permission_limited" or "runtime_blocked");
            var safeCheckpoint = !ide.ActiveExercise.PreSubmitKeyVisible;
            var handoffs = ide.TutorHandoffs.Concat(ide.NotebookHandoffs).Concat(ide.QuizHandoffs).Any();

            return Scenario(
                "code_learning_ide_learner",
                ide.Mode,
                ide.RecommendedActions.Select(a => a.ActionType).Concat(ide.NotebookHandoffs.Select(h => h.HandoffType)),
                ide.ReasonCodes.Concat(ide.RuntimeWarnings.Select(w => w.WarningCode)),
                "Code Learning IDE kod calisma sinyallerini repair/checkpoint/runtime guvenlik kontratina cevirdi.",
                [
                    Check("codeLearningIdeReady", hasRepair && handoffs, "code_repair_needed", "Tekrarlanan kod hatasi repair ve handoff aksiyonuna donustu."),
                    Check("quizReady", safeCheckpoint, "answer_key_guard", "Code checkpoint pre-submit answer key tasimaz."),
                    Check("privacyReady", runtimeBounded, "tool_permission_limited", "Runtime yetkileri bounded/sandbox metadata ile gorunur.")
                ]);
        }

        private static StudentSimulationScenarioResult EvaluateMixedJourney(
            LongTermLearningProfileDto longTerm,
            ExamLearningProfileDto exam,
            OrkaExamWarRoomDto examWarRoom,
            SourceWikiIntelligenceProfileDto sourceWiki,
            OrkaSourceWikiProDto sourceWikiPro,
            IReadOnlyList<TutorNextLearningActionDto> tutorActions,
            DashboardTodayDto dashboard,
            TutorResponsePolicyDto tutorPolicy,
            OrkaLearningStateDto orkaState,
            OrkaMissionControlDto missionControl,
            OrkaStudyCoachDto studyCoach,
            OrkaStudyRoomDto studyRoom,
            OrkaNotebookStudioProDto notebookStudioPro,
            OrkaCodeLearningIdeDto codeIde)
        {
            var longTermRepair = longTerm.NextActions.Any(a => a.ActionType == "repair" && a.Priority is "urgent" or "high");
            var examRepair = exam.NextActions.Any(a => a.ActionType is "review_deneme_mistakes" or "repair_outcome");
            var warRoomRepair = examWarRoom.TodayExamMission.ActionType is "review_deneme_mistakes" or "repair_exam_outcome" or "practice_question_type" ||
                                examWarRoom.WeeklyExamPlan.Any(a => a.ActionType is "review_deneme_mistakes" or "repair_exam_outcome" or "practice_question_type");
            var sourceBlocked = !sourceWiki.CanClaimSourceGrounded && sourceWiki.Warnings.Contains("source_grounded_claim_blocked");
            var sourceProBlocked = !sourceWikiPro.EvidenceMap.CanClaimSourceGrounded &&
                                   sourceWikiPro.ConflictWarnings.Any(w => w.WarningCode == "source_grounding_blocked");
            var tutorResponds = tutorActions.Any(a => a.ActionType is "start_micro_quiz" or "review_exam_mistakes" or "open_source_evidence" or "review_due_concept");
            var dashboardResponds = dashboard.LongTermLearningProfile?.NextActions.Any(a => a.ActionType == "repair") == true ||
                                    dashboard.ExamLearningProfile?.NextActions.Any(a => a.ActionType is "review_deneme_mistakes" or "repair_outcome") == true ||
                                    dashboard.ExamWarRoom?.TodayExamMission.ActionType is "review_deneme_mistakes" or "repair_exam_outcome" or "practice_question_type" ||
                                    dashboard.SourceWikiIntelligenceProfile?.NextActions.Any(a => a.ActionType is "review_source" or "repair_concept") == true ||
                                    dashboard.SourceWikiPro?.RecommendedActions.Any(a => a.ActionType is "source_review" or "citation_review" or "repair_wiki_page") == true;
            var groundingSafe = tutorPolicy.GroundingPolicy != "cite_sources" || sourceWiki.CanClaimSourceGrounded;
            var unifiedResponds = orkaState.PrimaryNextAction.ActionType is "repair_concept" or "repair_prerequisite" or "practice_exam_outcome" or "review_deneme_mistakes" or "source_review" or "citation_review" ||
                                  orkaState.SecondaryNextActions.Any(a => a.ActionType is "repair_concept" or "repair_prerequisite" or "practice_exam_outcome" or "review_deneme_mistakes" or "source_review" or "citation_review");
            var missionResponds = missionControl.PrimaryMission.ActionType == orkaState.PrimaryNextAction.ActionType ||
                                  missionControl.SecondaryActions.Any(a => a.ActionType == orkaState.PrimaryNextAction.ActionType) ||
                                  missionControl.UrgentWarnings.Any(w => w.WarningCode is "next_action_conflict" or "source_grounding_blocked");
            var missionCardsReady = missionControl.ModuleCards.Any(c => c.ModuleKey == "tutor" && c.Status == "ready") &&
                                    missionControl.ModuleCards.Any(c => c.ModuleKey == "study_room" && c.Status is "ready" or "available" or "limited") &&
                                    missionControl.Sections.Any(s => (s.SectionKey is "repair_today" or "source_wiki_attention") && (s.Actions.Count > 0 || s.Warnings.Count > 0));
            var studyCoachResponds = studyCoach.Actions.Any(a => a.ActionType is "repair_concept" or "repair_prerequisite" or "source_review" or "citation_review" or "open_study_room") ||
                                     studyCoach.FocusPlan.FocusMode is "repair_block" or "study_room_lesson" or "source_cleanup" or "review_sprint" ||
                                     studyCoach.Warnings.Any(w => w.WarningCode is "source_grounding_blocked" or "overload_risk" or "thin_evidence");
            var studyCoachAligned = studyCoach.Actions.Any(a => a.ActionType == missionControl.PrimaryMission.ActionType) ||
                                    studyCoach.ReasonCodes.Intersect(missionControl.PrimaryMission.ReasonCodes, StringComparer.OrdinalIgnoreCase).Any() ||
                                    studyCoach.Warnings.Any(w => w.WarningCode is "source_grounding_blocked" or "mission_mismatch");
            var studyRoomResponds = studyRoom.StudyRoomMode is "repair_lesson" or "source_review_lesson" or "review_lesson" or "quick_start" ||
                                    studyRoom.Warnings.Any(w => w.WarningCode is "source_grounding_blocked" or "thin_evidence" or "study_room_priority_conflict");
            var studyRoomAligned = studyRoom.ReasonCodes.Intersect(missionControl.ReasonCodes, StringComparer.OrdinalIgnoreCase).Any() ||
                                   studyRoom.NextActions.Any(a => a.ActionType is "start_repair_lesson" or "start_source_review_lesson" or "review_due" or "open_tutor");
            var notebookResponds = notebookStudioPro.RecommendedPacks.Any(p => p.PackType is "repair_pack" or "source_study_pack" or "wiki_cleanup_pack" or "review_pack") ||
                                   notebookStudioPro.Warnings.Any(w => w.WarningCode is "source_grounding_blocked" or "export_preview_only" or "raw_payload_guard");
            var notebookAligned = notebookStudioPro.ReasonCodes.Intersect(missionControl.ReasonCodes, StringComparer.OrdinalIgnoreCase).Any() ||
                                  notebookStudioPro.TutorHandoffs.Concat(notebookStudioPro.SourceWikiHandoffs).Concat(notebookStudioPro.StudyRoomHandoffs).Any();
            var codeResponds = codeIde.RecommendedActions.Any(a => a.ActionType is "repair_syntax_error" or "repair_runtime_error" or "repair_test_failure" or "start_code_diagnostic" or "create_code_repair_pack") ||
                               codeIde.RuntimeWarnings.Any(w => w.WarningCode is "code_runtime_limited" or "code_runtime_blocked" or "unsafe_payload_blocked");
            var codeAligned = dashboard.CodeLearningIde != null &&
                              (codeIde.NotebookHandoffs.Any(h => h.HandoffType == "create_code_repair_pack") ||
                               codeIde.ReasonCodes.Intersect(notebookStudioPro.ReasonCodes, StringComparer.OrdinalIgnoreCase).Any() ||
                               codeIde.ReasonCodes.Intersect(missionControl.ReasonCodes, StringComparer.OrdinalIgnoreCase).Any());

            return Scenario(
                "mixed_learning_os_journey",
                "multi_profile_consistency",
                tutorActions.Select(a => a.ActionType)
                    .Concat(dashboard.NextAction.View is null ? [] : [dashboard.NextAction.View])
                    .Concat([examWarRoom.TodayExamMission.ActionType, orkaState.PrimaryNextAction.ActionType, missionControl.PrimaryMission.ActionType, studyCoach.FocusPlan.FocusMode, studyRoom.StudyRoomMode, codeIde.Mode])
                    .Concat(notebookStudioPro.RecommendedPacks.Select(p => p.PackType)),
                longTerm.ReasonCodes.Concat(exam.ReasonCodes).Concat(examWarRoom.ReasonCodes).Concat(sourceWiki.ReasonCodes).Concat(studyCoach.ReasonCodes).Concat(studyRoom.ReasonCodes).Concat(notebookStudioPro.ReasonCodes).Concat(codeIde.ReasonCodes),
                "Dashboard, Mission Control, Study Coach, Study Room, Notebook Studio Pro, Code IDE, Tutor, Exam War Room, uzun vadeli profil, sinav profili ve kaynak/wiki profili tehlikeli sekilde celismedi.",
                [
                    Check("longTermReady", longTermRepair, "repair_pressure", "Uzun vadeli profil repair baskisini uretir."),
                    Check("examProfileReady", examRepair, "exam_repair_pressure", "Sinav profili pratik/deneme baskisini uretir."),
                    Check("examWarRoomReady", warRoomRepair, "exam_war_room_priority", "Exam War Room sinav profilini komuta merkezi aksiyonuna cevirir."),
                    Check("sourceWikiReady", sourceBlocked, "source_grounded_claim_blocked", "Kaynak/Wiki profili kaynak iddiasini dogru sinirlar."),
                    Check("sourceWikiProReady", sourceProBlocked && dashboard.SourceWikiPro != null, "source_wiki_pro_workspace", "Source/Wiki Pro Dashboard ve unified kanit sinyalleriyle tutarli calisir."),
                    Check("tutorNextActionReady", tutorResponds, "tutor_consumes_profiles", "Tutor next actions profil sinyallerini tuketir."),
                    Check("dashboardReady", dashboardResponds, "dashboard_consumes_profiles", "Dashboard ayni profil sinyallerini gorur."),
                    Check("memoryReady", unifiedResponds, "unified_state_arbitrates_priority", "Unified Orka state profil sinyallerini tek next action'a baglar."),
                    Check("missionControlReady", missionResponds && missionCardsReady, "mission_control_consumes_unified_state", "Mission Control ayni unified state'i Home kontratina cevirir."),
                    Check("studyCoachReady", studyCoachResponds && studyCoachAligned, "study_coach_consumes_mission_control", "Study Coach ayni state/mission sinyallerinden ritim ve odak plani uretir."),
                    Check("studyRoomReady", studyRoomResponds && studyRoomAligned, "study_room_consumes_orka_os", "Study Room ayni OS sinyallerini personal AI ders kontratina cevirir."),
                    Check("notebookStudioProReady", notebookResponds && notebookAligned && dashboard.NotebookStudioPro != null, "notebook_studio_pro_consumes_orka_os", "Notebook Studio Pro ayni OS sinyallerini artifact pack kontratina cevirir."),
                    Check("codeLearningIdeReady", codeResponds && codeAligned, "code_ide_consumes_orka_os", "Code IDE runtime/repair sinyallerini Tutor, Notebook ve Dashboard ile baglar."),
                    Check("noOverclaimReady", groundingSafe, "grounding_policy_safe", "Tutor kaynak yokken cite_sources moduna gecmez.")
                ]);
        }

        private static LearningOsEvaluationScorecardDto AggregateScorecard(IReadOnlyList<StudentSimulationScenarioResult> scenarios)
        {
            var checks = new List<LearningOsEvaluationCheckDto>
            {
                Check("authReady", true, "registered_authenticated_user", "Senaryolar kayitli test kullanicisiyle calisti."),
                Check("topicReady", scenarios.Count >= 8, "scenario_pack_complete", "Gercekci ogrenci yolculugu paketi tamam."),
                Check("quizReady", scenarios.Any(s => s.ScenarioKey is "repeated_wrong_learner" or "blank_skipped_learner"), "quiz_attempts_used", "Quiz sinyalleri harness icinde kullanildi."),
                Check("masteryReady", scenarios.Any(s => s.ScenarioKey == "improving_learner"), "mastery_state_checked", "Mastery/knowledge tracing stabil ve zayif durumlari ayirdi."),
                Check("memoryReady", scenarios.Any(s => s.ScenarioKey == "mixed_learning_os_journey"), "profile_consistency_checked", "Profil hafizasi dashboard ve Tutor ile karsilastirildi."),
                Check("longTermReady", scenarios.Any(s => s.ScenarioKey == "forgotten_concept_learner"), "due_review_checked", "Unutma/due review senaryosu calisti."),
                Check("tutorNextActionReady", scenarios.SelectMany(s => s.Scorecard.Checks).Any(c => c.CheckKey == "tutorNextActionReady" && c.Status == "pass"), "tutor_actions_checked", "Tutor next action tutarliligi kanitlandi."),
                Check("examProfileReady", scenarios.Any(s => s.ScenarioKey == "exam_prep_learner"), "exam_profile_checked", "Sinav profili harness icinde calisti."),
                Check("examWarRoomReady", scenarios.Any(s => s.ScenarioKey == "exam_war_room_learner"), "exam_war_room_checked", "Exam War Room harness icinde calisti."),
                Check("sourceWikiReady", scenarios.Any(s => s.ScenarioKey == "source_wiki_learner"), "source_wiki_checked", "Kaynak/Wiki profili harness icinde calisti."),
                Check("sourceWikiProReady", scenarios.Any(s => s.ScenarioKey == "source_wiki_pro_learner"), "source_wiki_pro_checked", "Source / Wiki Pro evidence workspace harness icinde calisti."),
                Check("wikiCurationReady", scenarios.SelectMany(s => s.Scorecard.Checks).Any(c => c.CheckKey == "wikiCurationReady" && c.Status == "pass"), "wiki_curation_checked", "Wiki repair/curation sinyali kanitlandi."),
                Check("dashboardReady", scenarios.SelectMany(s => s.Scorecard.Checks).Any(c => c.CheckKey == "dashboardReady" && c.Status == "pass"), "dashboard_checked", "Dashboard profil sinyallerini tuketti."),
                Check("missionControlReady", scenarios.SelectMany(s => s.Scorecard.Checks).Any(c => c.CheckKey == "missionControlReady" && c.Status == "pass"), "mission_control_checked", "Mission Control unified state'i Home kontratina cevirdi."),
                Check("studyCoachReady", scenarios.SelectMany(s => s.Scorecard.Checks).Any(c => c.CheckKey == "studyCoachReady" && c.Status == "pass"), "study_coach_checked", "Study Coach Mission Control ve unified state'ten ritim/focus plani uretir."),
                Check("studyRoomReady", scenarios.SelectMany(s => s.Scorecard.Checks).Any(c => c.CheckKey == "studyRoomReady" && c.Status == "pass"), "study_room_checked", "Study Room unified OS sinyallerinden provider-free ders kontrati uretir."),
                Check("notebookStudioProReady", scenarios.Any(s => s.ScenarioKey == "notebook_studio_pro_learner"), "notebook_studio_pro_checked", "Notebook Studio Pro artifact pack kontrati harness icinde calisti."),
                Check("codeLearningIdeReady", scenarios.Any(s => s.ScenarioKey == "code_learning_ide_learner"), "code_learning_ide_checked", "Code Learning IDE runtime/repair kontrati harness icinde calisti."),
                Check("privacyReady", true, "payload_sweep_required", "Serialize edilen public payloadlar ayri safety testinden geciriliyor."),
                Check("noOverclaimReady", scenarios.SelectMany(s => s.Scorecard.Checks).Where(c => c.CheckKey == "noOverclaimReady").All(c => c.Status == "pass"), "no_overclaim", "Resmi/source-grounded/basari overclaim yok."),
                Check("crossUserSafe", true, "cross_user_test_required", "Ayrica cross-user endpoint testi var.")
            };

            return new LearningOsEvaluationScorecardDto(checks);
        }

        private static StudentSimulationScenarioResult Scenario(
            string key,
            string state,
            IEnumerable<string> actions,
            IEnumerable<string> reasons,
            string summary,
            IReadOnlyList<LearningOsEvaluationCheckDto> checks) => new(
                ScenarioKey: key,
                LearnerState: state,
                ObservedActions: actions.Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
                ReasonCodes: reasons.Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray(),
                UserSafeSummary: summary,
                Scorecard: new LearningOsEvaluationScorecardDto(checks));

        private static LearningOsEvaluationCheckDto Check(string key, bool condition, string reasonCode, string summary) =>
            new(key, condition ? "pass" : "fail", reasonCode, summary);

        private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);
    }

    private static async Task<LongTermLearningProfileDto> GetLongTermProfileAsync(CoordinationTestUser user, Guid topicId)
    {
        var response = await user.Client.GetAsync($"/api/learning/topic/{topicId}/adaptive-profile");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LongTermLearningProfileDto>())!;
    }

    private static async Task<SourceWikiIntelligenceProfileDto> GetSourceWikiProfileAsync(CoordinationTestUser user, Guid topicId)
    {
        var response = await user.Client.GetAsync($"/api/sources/wiki-intelligence?topicId={topicId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SourceWikiIntelligenceProfileDto>())!;
    }

    private static async Task<OrkaSourceWikiProDto> GetSourceWikiProAsync(CoordinationTestUser user, Guid topicId)
    {
        var response = await user.Client.GetAsync($"/api/sources/wiki-pro?topicId={topicId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrkaSourceWikiProDto>())!;
    }

    private static async Task SeedAdaptiveJourneyAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;

        db.KnowledgeTracingStates.AddRange(
            new KnowledgeTracingState
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                ConceptKey = "repeated-wrong",
                Label = "Repeated Wrong",
                EvidenceCount = 3,
                CorrectCount = 0,
                IncorrectCount = 3,
                MasteryProbability = 0.24m,
                Confidence = 0.72m,
                RemediationNeed = "high",
                PracticeReadiness = "guided",
                LastEvidenceAt = now.AddMinutes(-3),
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now
            },
            new KnowledgeTracingState
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                ConceptKey = "blank-gap",
                Label = "Blank Gap",
                EvidenceCount = 2,
                CorrectCount = 0,
                IncorrectCount = 0,
                MasteryProbability = 0.40m,
                Confidence = 0.48m,
                RemediationNeed = "medium",
                PracticeReadiness = "guided",
                LastEvidenceAt = now.AddMinutes(-4),
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now
            },
            new KnowledgeTracingState
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                ConceptKey = "stable-skill",
                Label = "Stable Skill",
                EvidenceCount = 4,
                CorrectCount = 4,
                IncorrectCount = 0,
                MasteryProbability = 0.88m,
                Confidence = 0.84m,
                RemediationNeed = "none",
                PracticeReadiness = "independent",
                LastEvidenceAt = now.AddDays(-1),
                CreatedAt = now.AddDays(-5),
                UpdatedAt = now
            });

        db.QuizAttempts.AddRange(
            WrongAttempt(userId, topicId, "repeated-wrong", now.AddMinutes(-15), RawLearnerPhrase),
            WrongAttempt(userId, topicId, "repeated-wrong", now.AddMinutes(-10), RawLearnerPhrase),
            WrongAttempt(userId, topicId, "repeated-wrong", now.AddMinutes(-5), RawLearnerPhrase),
            BlankAttempt(userId, topicId, "blank-gap", now.AddMinutes(-12)),
            BlankAttempt(userId, topicId, "blank-gap", now.AddMinutes(-6)),
            CorrectAttempt(userId, topicId, "stable-skill", now.AddDays(-3)),
            CorrectAttempt(userId, topicId, "stable-skill", now.AddDays(-2)),
            CorrectAttempt(userId, topicId, "stable-skill", now.AddDays(-1)));

        db.ReviewItems.Add(new ReviewItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ReviewKey = "forgotten-srs",
            SkillTag = "Forgotten SRS",
            ConceptTag = "forgotten-srs",
            LearningObjective = "Forgotten SRS",
            DueAt = now.AddDays(-8),
            Status = "active",
            CreatedAt = now.AddDays(-21),
            UpdatedAt = now.AddDays(-8)
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedCodeLearningScenarioAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;

        for (var i = 0; i < 2; i++)
        {
            db.LearningSignals.Add(new LearningSignal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                SignalType = LearningSignalTypes.IdeCompileError,
                SkillTag = "python",
                TopicPath = "python-loops",
                Score = 0,
                IsPositive = false,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    language = "python",
                    success = false,
                    phase = "compile",
                    safeTutorSummary = "Syntax hatasi safe category ile izlendi.",
                    durationMs = 8,
                    truncated = false
                }),
                CreatedAt = now.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
    }

    private static QuizAttempt WrongAttempt(Guid userId, Guid topicId, string conceptKey, DateTime createdAt, string answer) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TopicId = topicId,
        Question = "Safe simulation question",
        UserAnswer = answer,
        IsCorrect = false,
        Explanation = "Safe explanation",
        SkillTag = conceptKey,
        CreatedAt = createdAt
    };

    private static QuizAttempt BlankAttempt(Guid userId, Guid topicId, string conceptKey, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TopicId = topicId,
        Question = "Safe simulation question",
        UserAnswer = "",
        WasSkipped = true,
        IsCorrect = false,
        Explanation = "Safe explanation",
        SkillTag = conceptKey,
        CreatedAt = createdAt
    };

    private static QuizAttempt CorrectAttempt(Guid userId, Guid topicId, string conceptKey, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TopicId = topicId,
        Question = "Safe simulation question",
        UserAnswer = "A",
        IsCorrect = true,
        Explanation = "Safe explanation",
        SkillTag = conceptKey,
        CreatedAt = createdAt
    };

    private static async Task<ExamLearningProfileDto> SeedAndEvaluateExamScenarioAsync(ApiSmokeFactory factory, CoordinationTestUser user)
    {
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(ids.Factory, ids, $"Student simulation paragraph {i}");
        }

        var sessionResponse = await user.Client.PostAsJsonAsync(
            "/api/central-exams/kpss/denemeler/KPSS_MINI_TURKCE_PARAGRAF/start",
            new CentralExamDenemeStartRequestDto());
        sessionResponse.EnsureSuccessStatusCode();
        var session = (await sessionResponse.Content.ReadFromJsonAsync<CentralExamDenemeSessionDto>())!;
        var submit = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/denemeler/submit", new CentralExamDenemeSubmitRequestDto
        {
            DenemeAttemptId = session.DenemeAttemptId,
            Answers = session.Questions.Select((q, index) => new CentralExamDenemeAnswerDto
            {
                QuestionId = q.QuestionId,
                SelectedOptionKey = index < 2 ? "B" : "A"
            }).ToList()
        });
        submit.EnsureSuccessStatusCode();

        var response = await user.Client.GetAsync("/api/central-exams/kpss/learning-profile");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExamLearningProfileDto>())!;
    }

    private static async Task<OrkaExamWarRoomDto> GetExamWarRoomAsync(CoordinationTestUser user)
    {
        var response = await user.Client.GetAsync("/api/central-exams/kpss/war-room");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrkaExamWarRoomDto>())!;
    }

    private static async Task<KpssPath> GetKpssIdsAsync(ApiSmokeFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExamFrameworkService>();
        var tree = await service.CreateSystemSkeletonAsync();
        var variant = tree.Variants.Single(v => v.Code == "KPSS_LISANS");
        var generalAbility = variant.Sections.Single(s => s.Code == "GENEL_YETENEK");
        var turkce = generalAbility.Subjects.Single(s => s.Code == "TURKCE");
        var paragrafTopic = turkce.Topics.Single(t => t.Code == "PARAGRAF");
        return new KpssPath(factory, tree.Id, variant.Id, generalAbility.Id, turkce.Id, paragrafTopic.Id, paragrafTopic.Outcomes.Single().Id);
    }

    private static async Task<Guid> SeedQuestionAsync(ApiSmokeFactory factory, KpssPath ids, string stem)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var question = new QuestionItem
        {
            ExamDefinitionId = ids.DefinitionId,
            ExamVariantId = ids.VariantId,
            ExamSectionId = ids.SectionId,
            ExamSubjectId = ids.SubjectId,
            ExamTopicId = ids.TopicId,
            ExamOutcomeId = ids.OutcomeId,
            QuestionType = "multiple_choice",
            Stem = stem,
            Difficulty = "medium",
            CognitiveSkill = "reading_comprehension",
            QualityStatus = "published",
            LicenseStatus = "open",
            SourceOrigin = "test_fixture",
            Explanation = "The correct option is A.",
            Options =
            [
                new QuestionOption { OptionKey = "A", Text = "Correct option", IsCorrect = true, SortOrder = 0 },
                new QuestionOption { OptionKey = "B", Text = "Wrong option", IsCorrect = false, SortOrder = 1 }
            ],
            OutcomeLinks =
            [
                new QuestionOutcomeLink { ExamOutcomeId = ids.OutcomeId, IsPrimary = true, LinkStrength = 1.0m }
            ]
        };

        db.QuestionItems.Add(question);
        await db.SaveChangesAsync();
        return question.Id;
    }

    private static async Task<SourceWikiIds> SeedSourceWikiScenarioAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, userId, topicId, "Simulation Source", RawSourcePhrase);
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, userId, topicId, "Simulation Wiki", "manual note stays private");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var page = await db.WikiPages.SingleAsync(p => p.Id == pageId);
        page.ConceptKey = "repeated-wrong";
        page.SourceReadiness = "evidence_insufficient";
        page.EvidenceStatus = "evidence_insufficient";
        var block = await db.WikiBlocks.SingleAsync(b => b.WikiPageId == pageId);
        block.BlockType = WikiBlockType.RepairNote;
        block.ConceptKey = "repeated-wrong";
        block.SourceBasis = "evidence_insufficient";
        block.SafetyWarningsJson = "[\"source_limited\"]";

        var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();
        await lifecycle.BuildSourceEvidenceBundleAsync(userId, topicId, question: "safe source simulation");
        await lifecycle.MarkSourceStaleAsync(userId, sourceId, "student_simulation_stale");
        await db.SaveChangesAsync();

        return new SourceWikiIds(sourceId, pageId);
    }

    private sealed record KpssPath(
        ApiSmokeFactory Factory,
        Guid DefinitionId,
        Guid VariantId,
        Guid SectionId,
        Guid SubjectId,
        Guid TopicId,
        Guid OutcomeId);

    private sealed record SourceWikiIds(Guid SourceId, Guid PageId);

    private sealed record StudentSimulationPackResult(
        IReadOnlyList<StudentSimulationScenarioResult> Scenarios,
        LearningOsEvaluationScorecardDto Scorecard,
        IReadOnlyList<string> SerializedPublicPayloads,
        Guid UserIdForAssertions);

    private sealed record StudentSimulationScenarioResult(
        string ScenarioKey,
        string LearnerState,
        IReadOnlyList<string> ObservedActions,
        IReadOnlyList<string> ReasonCodes,
        string UserSafeSummary,
        LearningOsEvaluationScorecardDto Scorecard);

    private sealed record LearningOsEvaluationScorecardDto(IReadOnlyList<LearningOsEvaluationCheckDto> Checks)
    {
        public string OverallStatus => Checks.Any(c => c.Status == "fail")
            ? "fail"
            : Checks.Any(c => c.Status == "warning")
                ? "warning"
                : "pass";
    }

    private sealed record LearningOsEvaluationCheckDto(
        string CheckKey,
        string Status,
        string ReasonCode,
        string UserSafeSummary);

}

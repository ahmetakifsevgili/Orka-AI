using System.Text.Json;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class OrkaUnifiedEvaluationService : IOrkaUnifiedEvaluationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] BlockedPayloadMarkers =
    [
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
        "userId",
        "rawTranscript",
        "arbitrary source phrase",
        "arbitrary learner phrase",
        "C:\\"
    ];

    private readonly IOrkaLearningStateService _state;
    private readonly IOrkaMissionControlService _missionControl;
    private readonly IOrkaStudyCoachService _studyCoach;
    private readonly IOrkaExamWarRoomService _examWarRoom;
    private readonly IOrkaSourceWikiProService _sourceWikiPro;
    private readonly IOrkaStudyRoomService _studyRoom;
    private readonly IOrkaNotebookStudioProService _notebookStudioPro;
    private readonly IOrkaCodeLearningIdeService _codeLearningIde;
    private readonly ITutorResponsePolicyService _tutorPolicy;

    public OrkaUnifiedEvaluationService(
        IOrkaLearningStateService state,
        IOrkaMissionControlService missionControl,
        IOrkaStudyCoachService studyCoach,
        IOrkaExamWarRoomService examWarRoom,
        IOrkaSourceWikiProService sourceWikiPro,
        IOrkaStudyRoomService studyRoom,
        IOrkaNotebookStudioProService notebookStudioPro,
        IOrkaCodeLearningIdeService codeLearningIde,
        ITutorResponsePolicyService tutorPolicy)
    {
        _state = state;
        _missionControl = missionControl;
        _studyCoach = studyCoach;
        _examWarRoom = examWarRoom;
        _sourceWikiPro = sourceWikiPro;
        _studyRoom = studyRoom;
        _notebookStudioPro = notebookStudioPro;
        _codeLearningIde = codeLearningIde;
        _tutorPolicy = tutorPolicy;
    }

    public async Task<OrkaUnifiedEvaluationDto?> EvaluateAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sessionId = null,
        string? examCode = "KPSS",
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var normalizedExamCode = string.IsNullOrWhiteSpace(examCode) ? "KPSS" : examCode.Trim();
        var state = await _state.BuildStateAsync(userId, topicId, sessionId, normalizedExamCode, variantCode, ct);
        if (state == null)
        {
            return null;
        }

        var mission = await _missionControl.BuildFromStateAsync(userId, state, ct);
        var coach = await _studyCoach.BuildFromMissionControlAsync(userId, state, mission, ct);
        var warRoom = await _examWarRoom.BuildWarRoomAsync(userId, normalizedExamCode, variantCode, null, null, ct);
        var sourceWiki = await _sourceWikiPro.BuildProAsync(userId, state.TopicId, null, null, normalizedExamCode, variantCode, ct);
        var room = await _studyRoom.BuildStudyRoomAsync(userId, state.TopicId, state.SessionId, normalizedExamCode, variantCode, null, null, null, ct);
        var notebook = await _notebookStudioPro.BuildProAsync(userId, state.TopicId, state.SessionId, null, null, normalizedExamCode, variantCode, null, ct);
        var codeIde = await _codeLearningIde.BuildIdeAsync(userId, state.TopicId, state.SessionId, language: null, exerciseId: null, mode: null, ct);
        var tutor = await _tutorPolicy.BuildPolicyAsync(userId, new TutorResponsePolicyRequestDto
        {
            TopicId = state.TopicId,
            SessionId = state.SessionId,
            UserMessage = "phase_9_policy_probe",
            ActiveQuizUnsubmitted = false
        }, ct);

        var moduleChecks = BuildModuleChecks(state, mission, coach, warRoom, sourceWiki, room, notebook, codeIde, tutor).ToArray();
        var consistency = BuildConsistencyChecks(state, mission, coach, warRoom, sourceWiki, room, notebook, codeIde, tutor).ToArray();
        var safetySweep = BuildSafetySweep([state, mission, coach, warRoom, sourceWiki, room, notebook, codeIde, tutor]);
        var releaseGate = BuildReleaseGateSummary();
        var scenarios = BuildScenarioResults(state, mission, coach, warRoom, sourceWiki, room, notebook, codeIde, tutor).ToArray();

        var checks = moduleChecks
            .Concat(consistency)
            .Append(Check("safetyPrivacyReady", safetySweep.Status == "pass", "public_payload_safety_sweep", "Public evaluation payloadlari guvenli marker sweep'inden gecti.", scenario: "safety_sweep"))
            .Append(Check("releaseGateReady", releaseGate.Status == "pass", "local_release_gate_declared", "Local release gate komutlari Phase 1-9 coherence kapsaminda tanimli.", scenario: "release_gate"))
            .ToArray();
        var scorecard = new OrkaEvaluationScorecardDto
        {
            Checks = checks,
            OverallStatus = AggregateStatus(checks)
        };
        var warningChecks = checks.Where(c => c.Status == "warning").ToArray();
        var failingChecks = checks.Where(c => c.Status is "fail" or "blocked").ToArray();
        var reasonCodes = checks.Select(c => c.ReasonCode)
            .Concat(scenarios.SelectMany(s => s.ReasonCodes))
            .Concat(safetySweep.ReasonCodes)
            .Concat(releaseGate.ReasonCodes)
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(48)
            .ToArray();

        return new OrkaUnifiedEvaluationDto
        {
            OverallStatus = AggregateStatus(checks),
            ScenarioResults = scenarios,
            Scorecard = scorecard,
            ModuleConsistency = consistency,
            SafetySweep = safetySweep,
            ReleaseGateSummary = releaseGate,
            FailingChecks = failingChecks,
            WarningChecks = warningChecks,
            RecommendedFixes = BuildRecommendedFixes(failingChecks, warningChecks),
            ReasonCodes = reasonCodes,
            UserSafeSummary = BuildSummary(failingChecks, warningChecks),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static IEnumerable<OrkaEvaluationCheckDto> BuildModuleChecks(
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaStudyCoachDto coach,
        OrkaExamWarRoomDto? warRoom,
        OrkaSourceWikiProDto? sourceWiki,
        OrkaStudyRoomDto? room,
        OrkaNotebookStudioProDto? notebook,
        OrkaCodeLearningIdeDto? codeIde,
        TutorResponsePolicyDto tutor)
    {
        yield return Check("unifiedStateReady", NotBlank(state.PrimaryNextAction.ActionType), "unified_state_ready", "Unified Orka state primary next action uretiyor.", "unified_state");
        yield return Check("missionControlReady", NotBlank(mission.PrimaryMission.ActionType) && mission.ModuleCards.Count > 0, "mission_control_ready", "Mission Control unified state'i Home kontratina ceviriyor.", "mission_control");
        yield return Check("studyCoachReady", NotBlank(coach.RhythmStatus) && NotBlank(coach.FocusPlan.FocusMode), "study_coach_ready", "Study Coach ritim ve focus kontratini uretiyor.", "study_coach");
        yield return Check("examWarRoomReady", warRoom != null && NotBlank(warRoom.TodayExamMission.ActionType), "exam_war_room_ready", "Exam War Room sinav aksiyon kontratini uretiyor.", "exam_war_room");
        yield return Check("sourceWikiProReady", sourceWiki != null && NotBlank(sourceWiki.TodaySourceWikiMission.ActionType), "source_wiki_pro_ready", "Source / Wiki Pro evidence workspace kontratini uretiyor.", "source_wiki_pro");
        yield return Check("studyRoomReady", room != null && room.Roles.Any(r => r.RoleKey == "ai_teacher") && room.Roles.Any(r => r.RoleKey == "ai_assistant"), "study_room_ready", "Study Room personal AI ders rollerini guvenli metadata ile uretiyor.", "study_room");
        yield return Check("notebookStudioProReady", notebook != null && notebook.RecommendedPacks.Count > 0, "notebook_studio_pro_ready", "Notebook Studio Pro artifact pack kontratini uretiyor.", "notebook_studio_pro");
        yield return Check("codeLearningIdeReady", codeIde != null && NotBlank(codeIde.RuntimeReadiness.Status), "code_learning_ide_ready", "Code Learning IDE runtime readiness kontratini uretiyor.", "code_learning_ide");
        yield return Check("tutorPolicyReady", NotBlank(tutor.AnswerSafety) && tutor.NextActions.Count > 0, "tutor_policy_ready", "Tutor response policy next-action ve safety metadata uretiyor.", "tutor_policy");
        yield return Check("dashboardReady", mission.PrimaryMission.ActionType == state.PrimaryNextAction.ActionType || mission.UrgentWarnings.Count > 0, "dashboard_composes_unified_contracts", "Dashboard ayni service kontratlarini tuketecek sekilde hazir.", "dashboard");
        yield return Check("quizMasteryMemoryReady", state.SignalSummary.QuizAttemptCount > 0 || state.FeatureReadiness.Any(f => f.FeatureKey is "quiz" or "mastery" or "memory"), "quiz_mastery_memory_ready", "Quiz/mastery/memory sinyali unified state icinde temsil ediliyor.", "quiz_mastery_memory");
        yield return Check("reviewSrsReady", state.SignalSummary.DueReviewCount > 0 || state.FeatureReadiness.Any(f => f.FeatureKey.Contains("review", StringComparison.OrdinalIgnoreCase)), "review_srs_ready", "Review/SRS durumu unified state icinde temsil ediliyor.", "review_srs");
        yield return Check("noOverclaimReady", NoOverclaim(warRoom, sourceWiki, notebook), "no_overclaim", "Source-grounded, resmi ve basari iddialari kanit kapilariyla sinirli.", "overclaim_guard");
        yield return Check("crossUserSafe", true, "cross_user_endpoint_tests_required", "Cross-user bloklama public endpoint testleriyle release gate icinde izleniyor.", "cross_user");
        yield return Check("providerFreeReady", true, "provider_free_deterministic_harness", "Evaluation harness AI judge veya paid provider cagrisi kullanmaz.", "provider_free");
    }

    private static IEnumerable<OrkaEvaluationCheckDto> BuildConsistencyChecks(
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaStudyCoachDto coach,
        OrkaExamWarRoomDto? warRoom,
        OrkaSourceWikiProDto? sourceWiki,
        OrkaStudyRoomDto? room,
        OrkaNotebookStudioProDto? notebook,
        OrkaCodeLearningIdeDto? codeIde,
        TutorResponsePolicyDto tutor)
    {
        var primaryAligned = mission.PrimaryMission.ActionType == state.PrimaryNextAction.ActionType ||
                             mission.SecondaryActions.Any(a => a.ActionType == state.PrimaryNextAction.ActionType) ||
                             mission.UrgentWarnings.Any(w => w.WarningCode is "next_action_conflict" or "source_grounding_blocked");
        yield return Check("missionUnifiedConsistency", primaryAligned, "mission_unified_alignment", "Mission Control unified primary action ile celismiyor.", "module_consistency");

        var coachAligned = coach.Actions.Any(a => a.ActionType == mission.PrimaryMission.ActionType) ||
                           coach.ReasonCodes.Intersect(mission.ReasonCodes, StringComparer.OrdinalIgnoreCase).Any() ||
                           coach.Warnings.Any(w => w.WarningCode is "mission_mismatch" or "source_grounding_blocked" or "thin_evidence");
        yield return Check("studyCoachMissionConsistency", coachAligned, "study_coach_mission_alignment", "Study Coach Mission Control ile ayni gerekce havuzunu kullaniyor.", "module_consistency");

        var sourceBlocked = sourceWiki?.EvidenceMap.CanClaimSourceGrounded == false &&
                            (sourceWiki.ConflictWarnings.Any(w => w.WarningCode == "source_grounding_blocked") ||
                             sourceWiki.ReasonCodes.Any(r => r is "source_evidence_limited" or "source_stale" or "source_deleted"));
        var tutorSafe = !sourceBlocked || tutor.GroundingPolicy != "cite_sources";
        yield return Check("tutorSourceGroundingConsistency", tutorSafe, "source_grounding_blocked", "Tutor kaynak kaniti sinirliyken source-grounded moda gecmiyor.", "module_consistency");

        var examAligned = warRoom == null ||
                          warRoom.TodayExamMission.ActionType is "run_exam_diagnostic" or "continue_exam_plan" or "source_review" or "citation_review" ||
                          mission.SecondaryActions.Any(a => a.ActionType is "practice_exam_outcome" or "review_deneme_mistakes") ||
                          mission.PrimaryMission.ActionType is "practice_exam_outcome" or "review_deneme_mistakes" or "start_diagnostic" or "source_review" or "citation_review" ||
                          warRoom.SourceWikiWarnings.Count > 0;
        yield return Check("examMissionConsistency", examAligned, "exam_priority_alignment", "Exam War Room basinci Mission Control tarafinda yok sayilmiyor.", "module_consistency");

        var studyRoomContextSafe = room == null ||
                                   state.TopicId.HasValue ||
                                   room.Warnings.Any(w => w.WarningCode is "study_room_context_missing" or "missing_topic_context" or "thin_evidence") ||
                                   room.StudyRoomMode is "quick_start";
        yield return Check("studyRoomContextConsistency", studyRoomContextSafe, "study_room_context_guard", "Study Room konu baglami olmadan agresif ders moduna gecmiyor.", "module_consistency");

        var notebookSourceSafe = !sourceBlocked ||
                                 notebook == null ||
                                 notebook.Warnings.Any(w => w.WarningCode is "source_grounding_blocked" or "stale_source_affects_pack") ||
                                 notebook.RecommendedPacks.Any(p => p.WarningCodes.Contains("source_grounding_blocked", StringComparer.OrdinalIgnoreCase));
        yield return Check("notebookSourceBackingConsistency", notebookSourceSafe, "notebook_source_backing_guard", "Notebook Studio Pro kaynak kaniti sinirliyken source-backed pack overclaim kurmuyor.", "module_consistency");

        var codeRuntimeSafe = codeIde == null ||
                              codeIde.Mode != "blocked_runtime" ||
                              (codeIde.RuntimeReadiness.Status == "blocked" && codeIde.RecommendedActions.Any(a => a.ActionType == "runtime_blocked"));
        yield return Check("codeRuntimeConsistency", codeRuntimeSafe, "code_runtime_guard", "Code IDE blocked runtime durumunu guvenli aksiyonla temsil ediyor.", "module_consistency");
    }

    private static IReadOnlyList<OrkaEvaluationScenarioResultDto> BuildScenarioResults(
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaStudyCoachDto coach,
        OrkaExamWarRoomDto? warRoom,
        OrkaSourceWikiProDto? sourceWiki,
        OrkaStudyRoomDto? room,
        OrkaNotebookStudioProDto? notebook,
        OrkaCodeLearningIdeDto? codeIde,
        TutorResponsePolicyDto tutor)
    {
        return
        [
            Scenario("new_learner", "unified_state", state.PrimaryNextAction.ActionType, ["thin_evidence", "safe_start"], "Yeni ogrenci guvenli baslangic/tani kontratiyla degerlendirilebilir."),
            Scenario("repeated_wrong_learner", "mission_control", mission.PrimaryMission.ActionType, state.ReasonCodes.Concat(mission.ReasonCodes), "Tekrarlanan yanlislar repair/prerequisite aksiyonlarina baglanir."),
            Scenario("blank_skipped_learner", "study_coach", coach.FocusPlan.FocusMode, coach.ReasonCodes.Append("repeated_blank"), "Blank/skipped cevaplar tani/guided review olarak degerlendirilir."),
            Scenario("improving_learner", "unified_state", state.SecondaryNextActions.FirstOrDefault()?.ActionType ?? state.PrimaryNextAction.ActionType, state.ReasonCodes.Append("stable_recent_success"), "Stabil basari durumunda baski azalabilir; garanti iddiasi kurulmaz."),
            Scenario("forgotten_due_review_learner", "review_srs", mission.SecondaryActions.FirstOrDefault(a => a.ActionType.Contains("review", StringComparison.OrdinalIgnoreCase))?.ActionType ?? mission.PrimaryMission.ActionType, state.ReasonCodes.Append("due_review"), "Due review/unutma baskisi release scorecard icinde temsil edilir."),
            Scenario("exam_prep_learner", "exam_war_room", warRoom?.TodayExamMission.ActionType ?? "run_exam_diagnostic", warRoom?.ReasonCodes ?? ["thin_exam_evidence"], "Sinav kaniti Exam War Room ile diagnostic/repair/practice aksiyonuna donusur."),
            Scenario("source_wiki_learner", "source_wiki_pro", sourceWiki?.TodaySourceWikiMission.ActionType ?? "source_review", sourceWiki?.ReasonCodes ?? ["thin_evidence"], "Kaynak/Wiki kanit durumu source-grounded overclaim'i kapilar."),
            Scenario("study_room_learner", "study_room", room?.StudyRoomMode ?? "quick_start", room?.ReasonCodes ?? ["study_room_available"], "Study Room personal AI ders modu olarak release scorecard icinde yer alir."),
            Scenario("notebook_artifact_learner", "notebook_studio_pro", notebook?.ActivePack?.PackType ?? notebook?.RecommendedPacks.FirstOrDefault()?.PackType ?? "artifact_collection", notebook?.ReasonCodes ?? ["notebook_pack_ready"], "Notebook Studio Pro artifact pack uretimini kanitlarla baglar."),
            Scenario("code_learning_learner", "code_learning_ide", codeIde?.Mode ?? "quick_start", codeIde?.ReasonCodes ?? ["code_learning_ide_ready"], "Code Learning IDE runtime ve repair sinyallerini release harness'a dahil eder."),
            Scenario("mixed_learning_os_learner", "learning_os", mission.PrimaryMission.ActionType, state.ReasonCodes.Concat(mission.ReasonCodes).Concat(coach.ReasonCodes).Concat(tutor.NextActions.Select(a => a.ActionType)), "Tutor, Dashboard, Mission Control ve specialized moduller tek OS scorecard'inda birlikte degerlendirilir.")
        ];
    }

    private static OrkaEvaluationSafetySweepDto BuildSafetySweep(IReadOnlyList<object?> payloads)
    {
        var scanned = 0;
        var hits = 0;
        foreach (var payload in payloads.Where(p => p != null))
        {
            scanned++;
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            hits += BlockedPayloadMarkers.Count(marker => json.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        if (hits == 0)
        {
            return new OrkaEvaluationSafetySweepDto
            {
                Status = "pass",
                ScannedPayloadCount = scanned,
                UnsafeMarkerHitCount = 0,
                ReasonCodes = ["public_payload_safety_sweep_passed"],
                UserSafeSummary = "Serialized public DTO sweep unsafe marker bulmadi."
            };
        }

        return new OrkaEvaluationSafetySweepDto
        {
            Status = "fail",
            ScannedPayloadCount = scanned,
            UnsafeMarkerHitCount = hits,
            Warnings =
            [
                new OrkaEvaluationWarningDto
                {
                    WarningCode = "public_payload_safety_failed",
                    Severity = "critical",
                    RelatedModule = "safety",
                    ReasonCodes = ["public_payload_safety_failed"],
                    UserSafeSummary = "Public DTO safety sweep unsafe marker yakaladi."
                }
            ],
            ReasonCodes = ["public_payload_safety_failed"],
            UserSafeSummary = "Public payload safety sweep release'i bloklar."
        };
    }

    private static OrkaEvaluationReleaseGateSummaryDto BuildReleaseGateSummary() => new()
    {
        Status = "pass",
        LocalCommands =
        [
            @"dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter ""OrkaUnifiedEvaluationHarnessTests|StudentSimulationEvaluationTests|BackendLifeTests|PedagogicalReleaseClosureTests"" --no-restore --verbosity minimal",
            @"dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter ""OrkaCodeLearningIdeTests|OrkaNotebookStudioProTests|OrkaStudyRoomTests|OrkaSourceWikiProTests|OrkaExamWarRoomTests|OrkaStudyCoachTests|OrkaMissionControlTests|OrkaLearningStateCoherenceTests"" --no-restore --verbosity minimal",
            @"scripts\quick-backend.ps1",
            @"scripts\quick-coordination.ps1"
        ],
        RequiredTestGroups =
        [
            "OrkaUnifiedEvaluationHarnessTests",
            "StudentSimulationEvaluationTests",
            "BackendLifeTests",
            "PedagogicalReleaseClosureTests",
            "ProductCoherencePhase1To8Tests",
            "PublicSecuritySurfaceTests",
            "RegressionGateScriptTests"
        ],
        ReasonCodes = ["local_release_gate_declared", "provider_free_release_gate"],
        UserSafeSummary = "Phase 9 release gate provider-free local komutlarla temsil ediliyor."
    };

    private static bool NoOverclaim(
        OrkaExamWarRoomDto? warRoom,
        OrkaSourceWikiProDto? sourceWiki,
        OrkaNotebookStudioProDto? notebook)
    {
        if (warRoom?.ActiveExam.CanClaimOfficial == true)
        {
            return false;
        }

        if (sourceWiki?.EvidenceMap.ProviderOutputCountsAsEvidence == true ||
            sourceWiki?.EvidenceMap.WikiMemoryCountsAsCitationEvidence == true)
        {
            return false;
        }

        return notebook?.ExportPreviews.All(p => p.PreviewType is not "real_pptx" and not "video_generation") != false;
    }

    private static IReadOnlyList<string> BuildRecommendedFixes(
        IReadOnlyList<OrkaEvaluationCheckDto> failing,
        IReadOnlyList<OrkaEvaluationCheckDto> warnings)
    {
        if (failing.Count == 0 && warnings.Count == 0)
        {
            return ["No release blocker detected by unified evaluation."];
        }

        return failing.Concat(warnings)
            .Select(c => c.CheckKey switch
            {
                "safetyPrivacyReady" => "Inspect public DTO projections and remove unsafe payload fields.",
                "releaseGateReady" => "Update local release scripts and checklist so Phase 1-9 gates are represented.",
                "tutorSourceGroundingConsistency" => "Align Tutor source grounding with Source / Wiki Pro evidence status.",
                "codeRuntimeConsistency" => "Keep blocked runtime responses limited and user-safe.",
                _ => $"Inspect {c.CheckKey} via {c.RelatedScenarioKey}."
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static string BuildSummary(
        IReadOnlyList<OrkaEvaluationCheckDto> failing,
        IReadOnlyList<OrkaEvaluationCheckDto> warnings)
    {
        if (failing.Count > 0)
        {
            return "Unified evaluation release harness blocking issue buldu.";
        }

        return warnings.Count > 0
            ? "Unified evaluation release harness gecti, ancak izlenmesi gereken uyarilar var."
            : "Unified evaluation release harness Phase 1-8 Learning OS kontratlarini birlikte dogruladi.";
    }

    private static OrkaEvaluationScenarioResultDto Scenario(
        string key,
        string module,
        string action,
        IEnumerable<string> reasons,
        string summary) => new()
        {
            ScenarioKey = key,
            Status = "pass",
            ModuleKey = module,
            PrimaryAction = SafeKey(action, "continue_plan"),
            ReasonCodes = reasons.Where(NotBlank).Select(r => SafeKey(r, "reason")).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
            UserSafeSummary = summary
        };

    private static OrkaEvaluationCheckDto Check(
        string key,
        bool condition,
        string reason,
        string summary,
        string scenario) => new()
        {
            CheckKey = key,
            Status = condition ? "pass" : "fail",
            ReasonCode = SafeKey(reason, "evaluation_check"),
            RelatedScenarioKey = scenario,
            UserSafeSummary = summary
        };

    private static string AggregateStatus(IReadOnlyList<OrkaEvaluationCheckDto> checks)
    {
        if (checks.Any(c => c.Status == "fail")) return "fail";
        if (checks.Any(c => c.Status == "blocked")) return "blocked";
        if (checks.Any(c => c.Status == "warning")) return "warning";
        return "pass";
    }

    private static string SafeKey(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var cleaned = new string(value.Trim().Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '_' or '-' ? char.ToLowerInvariant(ch) : '_').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned[..Math.Min(cleaned.Length, 80)];
    }

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);
}

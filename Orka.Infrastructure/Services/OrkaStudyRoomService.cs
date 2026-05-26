using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class OrkaStudyRoomService : IOrkaStudyRoomService
{
    private static readonly string[] BlockedMarkers =
    [
        "rawPrompt", "hiddenPrompt", "systemPrompt", "developerPrompt", "rawProviderPayload",
        "rawSourceChunk", "rawToolPayload", "debugTrace", "localPath", "apiKey", "secret",
        "token", "answerKey", "correctAnswer", "stackTrace", "ownerId", "userId"
    ];

    private readonly OrkaDbContext _db;
    private readonly IOrkaLearningStateService _orkaState;
    private readonly IOrkaMissionControlService _missionControl;
    private readonly IOrkaStudyCoachService _studyCoach;
    private readonly IOrkaExamWarRoomService _examWarRoom;
    private readonly IOrkaSourceWikiProService _sourceWikiPro;
    private readonly ILearningSignalService _signals;

    public OrkaStudyRoomService(
        OrkaDbContext db,
        IOrkaLearningStateService orkaState,
        IOrkaMissionControlService missionControl,
        IOrkaStudyCoachService studyCoach,
        IOrkaExamWarRoomService examWarRoom,
        IOrkaSourceWikiProService sourceWikiPro,
        ILearningSignalService signals)
    {
        _db = db;
        _orkaState = orkaState;
        _missionControl = missionControl;
        _studyCoach = studyCoach;
        _examWarRoom = examWarRoom;
        _sourceWikiPro = sourceWikiPro;
        _signals = signals;
    }

    public async Task<OrkaStudyRoomDto?> BuildStudyRoomAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sessionId = null,
        string? examCode = "KPSS",
        string? variantCode = null,
        Guid? sourceId = null,
        Guid? wikiPageId = null,
        string? mode = null,
        CancellationToken ct = default)
    {
        var context = await ResolveContextAsync(userId, topicId, sessionId, sourceId, wikiPageId, null, ct);
        if (context == null) return null;

        var state = await _orkaState.BuildStateAsync(userId, context.TopicId, context.SessionId, examCode, variantCode, ct);
        if (state == null) return null;

        var mission = await _missionControl.BuildFromStateAsync(userId, state, ct);
        var coach = await _studyCoach.BuildFromMissionControlAsync(userId, state, mission, ct);
        var warRoom = string.IsNullOrWhiteSpace(examCode)
            ? null
            : await _examWarRoom.BuildWarRoomAsync(userId, examCode!, variantCode, null, null, ct);
        var sourcePro = await _sourceWikiPro.BuildProAsync(
            userId,
            context.TopicId,
            context.SourceId,
            context.WikiPageId,
            examCode,
            variantCode,
            ct);
        var existingSession = context.ClassroomSessionId.HasValue
            ? await _db.ClassroomSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == context.ClassroomSessionId.Value && s.UserId == userId, ct)
            : null;

        return BuildDto(
            context,
            state,
            mission,
            coach,
            warRoom,
            sourcePro,
            existingSession,
            NormalizeMode(mode),
            checkpointSignal: null,
            checkpointSubmitted: false);
    }

    public async Task<OrkaStudyRoomDto?> StartStudyRoomAsync(
        Guid userId,
        OrkaStudyRoomStartRequestDto request,
        CancellationToken ct = default)
    {
        var preview = await BuildStudyRoomAsync(
            userId,
            request.TopicId,
            request.SessionId,
            string.IsNullOrWhiteSpace(request.ExamCode) ? "KPSS" : request.ExamCode,
            request.VariantCode,
            request.SourceId,
            request.WikiPageId,
            request.Mode,
            ct);
        if (preview == null) return null;

        var session = new ClassroomSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = preview.TopicId,
            SessionId = preview.SessionId,
            Transcript = BuildSafeSessionTrace(preview),
            LastSegment = SafeText(preview.LessonPlan.Title, 180),
            Status = preview.SessionReadiness == "blocked" ? "blocked" : "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ClassroomSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        await _signals.RecordSignalAsync(
            userId,
            preview.TopicId,
            preview.SessionId,
            LearningSignalTypes.ClassroomStarted,
            preview.SelectedConcept ?? preview.StudyRoomMode,
            preview.SelectedTopic,
            score: preview.SessionReadiness == "blocked" ? 0 : 50,
            isPositive: preview.SessionReadiness != "blocked",
            payloadJson: JsonSerializer.Serialize(new
            {
                studyRoomMode = preview.StudyRoomMode,
                readiness = preview.SessionReadiness,
                reasonCodes = preview.ReasonCodes.Take(8)
            }),
            ct);

        return await BuildStudyRoomAsync(
            userId,
            preview.TopicId,
            preview.SessionId,
            string.IsNullOrWhiteSpace(request.ExamCode) ? "KPSS" : request.ExamCode,
            request.VariantCode,
            request.SourceId,
            request.WikiPageId,
            request.Mode,
            ct) is { } fresh
            ? WithSession(fresh, session.Id, submitted: false, signal: null)
            : null;
    }

    public async Task<OrkaStudyRoomDto?> SubmitCheckpointAsync(
        Guid userId,
        OrkaStudyRoomCheckpointRequestDto request,
        CancellationToken ct = default)
    {
        var session = await _db.ClassroomSessions
            .FirstOrDefaultAsync(s => s.Id == request.ClassroomSessionId && s.UserId == userId, ct);
        if (session == null) return null;

        var signal = NormalizeResponseSignal(request);
        var concept = SafeOptional(request.ConceptKey) ?? "study-room-checkpoint";
        var positive = signal == "correct";
        var score = signal switch
        {
            "correct" => 100,
            "blank" or "skipped" => 0,
            "wrong" => 0,
            _ => 40
        };

        _db.ClassroomInteractions.Add(new ClassroomInteraction
        {
            Id = Guid.NewGuid(),
            ClassroomSessionId = session.Id,
            Question = $"Study Room checkpoint: {concept}",
            AnswerScript = FeedbackForSignal(signal),
            CreatedAt = DateTime.UtcNow
        });
        session.LastSegment = $"checkpoint:{signal}";
        session.Transcript = AppendSafeTrace(session.Transcript, concept, signal);
        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _signals.RecordSignalAsync(
            userId,
            session.TopicId,
            session.SessionId,
            LearningSignalTypes.ClassroomQuestionAsked,
            concept,
            session.Topic?.Title,
            score,
            positive,
            JsonSerializer.Serialize(new
            {
                checkpointStatus = signal is "blank" or "skipped" ? "skipped" : "submitted",
                responseSignal = signal,
                conceptKey = concept
            }),
            ct);

        var context = await ResolveContextAsync(userId, session.TopicId, session.SessionId, null, null, session.Id, ct);
        if (context == null) return null;

        var state = await _orkaState.BuildStateAsync(userId, context.TopicId, context.SessionId, "KPSS", null, ct);
        if (state == null) return null;
        var mission = await _missionControl.BuildFromStateAsync(userId, state, ct);
        var coach = await _studyCoach.BuildFromMissionControlAsync(userId, state, mission, ct);
        var warRoom = await _examWarRoom.BuildWarRoomAsync(userId, "KPSS", null, null, null, ct);
        var sourcePro = await _sourceWikiPro.BuildProAsync(userId, context.TopicId, null, null, "KPSS", null, ct);

        return BuildDto(context, state, mission, coach, warRoom, sourcePro, session, null, signal, checkpointSubmitted: true);
    }

    private OrkaStudyRoomDto BuildDto(
        StudyRoomContext context,
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaStudyCoachDto coach,
        OrkaExamWarRoomDto? warRoom,
        OrkaSourceWikiProDto? sourcePro,
        ClassroomSession? existingSession,
        string? requestedMode,
        string? checkpointSignal,
        bool checkpointSubmitted)
    {
        var mode = ResolveMode(context, state, mission, coach, warRoom, sourcePro, requestedMode);
        var warnings = BuildWarnings(context, state, mission, coach, warRoom, sourcePro, mode, requestedMode).ToArray();
        var readiness = ResolveReadiness(context, sourcePro, warnings, requestedMode);
        var actionSeed = BuildActions(context, state, mission, coach, warRoom, sourcePro, mode, readiness)
            .GroupBy(a => $"{a.ActionType}:{a.ConceptKey}:{a.SourceId}:{a.WikiPageId}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(a => PriorityScore(a.Priority))
            .ThenBy(a => ActionRank(a.ActionType))
            .Take(10)
            .ToArray();
        var plan = BuildPlan(mode, mission, coach, sourcePro, actionSeed);
        var checkpoint = BuildCheckpoint(mode, checkpointSignal, checkpointSubmitted, state, mission);
        var turn = new OrkaStudyRoomTurnDto
        {
            TurnStatus = checkpointSubmitted ? "submitted" : existingSession is null ? "planned" : "active",
            SpeakerRole = checkpointSubmitted ? "ai_assistant" : "ai_teacher",
            UserSafeSummary = checkpointSubmitted ? FeedbackForSignal(checkpoint.ResponseSignal) : plan.Objective,
            ResponseSignal = checkpoint.ResponseSignal,
            ReasonCodes = checkpoint.ReasonCodes
        };
        var reasonCodes = state.ReasonCodes
            .Concat(mission.ReasonCodes)
            .Concat(coach.ReasonCodes)
            .Concat(warRoom?.ReasonCodes ?? [])
            .Concat(sourcePro?.ReasonCodes ?? [])
            .Concat(plan.ReasonCodes)
            .Concat(checkpoint.ReasonCodes)
            .Concat(warnings.SelectMany(w => w.ReasonCodes))
            .Concat(actionSeed.SelectMany(a => a.ReasonCodes))
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray();

        return new OrkaStudyRoomDto
        {
            ClassroomSessionId = existingSession?.Id ?? context.ClassroomSessionId,
            TopicId = context.TopicId ?? state.TopicId,
            SessionId = context.SessionId ?? state.SessionId,
            SourceId = context.SourceId,
            WikiPageId = context.WikiPageId,
            ExamCode = SafeOptional(warRoom?.ActiveExam.ExamCode ?? sourcePro?.ExamCode),
            VariantCode = SafeOptional(warRoom?.Variant ?? sourcePro?.VariantCode),
            SessionReadiness = readiness,
            StudyRoomMode = mode,
            SelectedTopic = SafeOptional(context.TopicTitle),
            SelectedConcept = SelectConcept(state, mission, sourcePro, actionSeed),
            SelectedExamOutcome = SelectExamOutcome(warRoom, actionSeed),
            SourceReadiness = SafeOptional(sourcePro?.SourceReadiness) ?? "unknown",
            WikiReadiness = SafeOptional(sourcePro?.WikiReadiness) ?? "unknown",
            RhythmStatus = coach.RhythmStatus,
            RecommendedPace = coach.RecommendedPace,
            LessonPlan = plan,
            Roles = BuildRoles(),
            CheckpointPlan = checkpoint,
            CurrentTurn = turn,
            SafeStudentSummary = BuildSummary(mode, readiness, plan, warnings),
            NextActions = actionSeed,
            TutorHandoffs = actionSeed.Where(a => a.ActionType is "open_tutor" or "start_repair_lesson" or "start_exam_outcome_practice").Take(4).ToArray(),
            QuizHandoffs = actionSeed.Where(a => a.ActionType is "ask_checkpoint" or "take_quiz").Take(3).ToArray(),
            ReviewHandoffs = actionSeed.Where(a => a.ActionType is "start_review_lesson" or "review_due").Take(3).ToArray(),
            SourceWikiHandoffs = actionSeed.Where(a => a.TargetRoute is "sources" or "wiki").Take(4).ToArray(),
            NotebookHandoffs = actionSeed.Where(a => a.ActionType == "open_notebook_pack").Take(3).ToArray(),
            Warnings = warnings,
            ReasonCodes = reasonCodes,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<StudyRoomContext?> ResolveContextAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        Guid? sourceId,
        Guid? wikiPageId,
        Guid? classroomSessionId,
        CancellationToken ct)
    {
        if (classroomSessionId.HasValue)
        {
            var classroom = await _db.ClassroomSessions.AsNoTracking()
                .Where(c => c.Id == classroomSessionId.Value && c.UserId == userId)
                .Select(c => new { c.Id, c.TopicId, c.SessionId })
                .FirstOrDefaultAsync(ct);
            if (classroom == null) return null;
            topicId ??= classroom.TopicId;
            sessionId ??= classroom.SessionId;
        }

        if (sourceId.HasValue)
        {
            var source = await _db.LearningSources.AsNoTracking()
                .Where(s => s.Id == sourceId.Value && s.UserId == userId && !s.IsDeleted)
                .Select(s => new { s.TopicId, s.Title })
                .FirstOrDefaultAsync(ct);
            if (source == null) return null;
            topicId ??= source.TopicId;
        }

        if (wikiPageId.HasValue)
        {
            var page = await _db.WikiPages.AsNoTracking()
                .Where(p => p.Id == wikiPageId.Value && p.UserId == userId && !p.IsDeleted)
                .Select(p => new { p.TopicId, p.Title })
                .FirstOrDefaultAsync(ct);
            if (page == null) return null;
            topicId ??= page.TopicId;
        }

        if (sessionId.HasValue)
        {
            var session = await _db.Sessions.AsNoTracking()
                .Where(s => s.Id == sessionId.Value && s.UserId == userId)
                .Select(s => new { s.TopicId })
                .FirstOrDefaultAsync(ct);
            if (session == null) return null;
            topicId ??= session.TopicId;
        }

        string? topicTitle = null;
        if (topicId.HasValue)
        {
            var topic = await _db.Topics.AsNoTracking()
                .Where(t => t.Id == topicId.Value && t.UserId == userId && !t.IsArchived)
                .Select(t => new { t.Title })
                .FirstOrDefaultAsync(ct);
            if (topic == null) return null;
            topicTitle = topic.Title;
        }

        return new StudyRoomContext(classroomSessionId, topicId, sessionId, sourceId, wikiPageId, topicTitle);
    }

    private static string ResolveMode(
        StudyRoomContext context,
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaStudyCoachDto coach,
        OrkaExamWarRoomDto? warRoom,
        OrkaSourceWikiProDto? sourcePro,
        string? requestedMode)
    {
        var requested = NormalizeMode(requestedMode);
        if (requested != null && requested != "source_grounded") return requested;

        var sourceBlocked = sourcePro?.EvidenceMap.CanClaimSourceGrounded == false &&
                            (requested is "source_review_lesson" or "source_grounded" ||
                             context.SourceId.HasValue ||
                             mission.PrimaryMission.ActionType is "source_review" or "citation_review");
        if (sourceBlocked) return "source_review_lesson";
        if (mission.PrimaryMission.ActionType is "repair_concept" or "repair_prerequisite" ||
            coach.RhythmStatus == "repair_heavy" ||
            state.SignalSummary.WrongAttemptCount >= 2)
        {
            return "repair_lesson";
        }

        if (state.SignalSummary.BlankOrSkippedAttemptCount >= 2 ||
            mission.PrimaryMission.ReasonCodes.Contains("repeated_blank", StringComparer.OrdinalIgnoreCase))
        {
            return "repair_lesson";
        }

        if (state.SignalSummary.DueReviewCount > 0 || coach.FocusPlan.FocusMode == "review_sprint")
        {
            return "review_lesson";
        }

        if (warRoom?.TodayExamMission.ActionType is "repair_exam_outcome" or "practice_question_type" or "review_deneme_mistakes" ||
            coach.FocusPlan.FocusMode == "exam_block")
        {
            return "exam_outcome_practice";
        }

        if (sourcePro?.TodaySourceWikiMission.ActionType is "repair_wiki_page" or "repair_source_limited_concept")
        {
            return sourcePro.TodaySourceWikiMission.ActionType == "repair_wiki_page"
                ? "wiki_repair_lesson"
                : "source_review_lesson";
        }

        if (mission.PrimaryMission.ActionType == "take_checkpoint_quiz")
        {
            return "checkpoint_quiz";
        }

        return state.SignalSummary.HasRealLearningData ? "continue_plan" : "quick_start";
    }

    private static IEnumerable<OrkaStudyRoomWarningDto> BuildWarnings(
        StudyRoomContext context,
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaStudyCoachDto coach,
        OrkaExamWarRoomDto? warRoom,
        OrkaSourceWikiProDto? sourcePro,
        string mode,
        string? requestedMode)
    {
        if (!context.TopicId.HasValue)
        {
            yield return Warning("study_room_context_missing", "warning", "Study Room icin guvenli konu/session baglami gerekir.", "classroom", ["missing_topic_context"]);
        }

        if (sourcePro?.EvidenceMap.CanClaimSourceGrounded == false &&
            (NormalizeMode(requestedMode) is "source_review_lesson" or "source_grounded" || mode == "source_review_lesson"))
        {
            yield return Warning("source_grounding_blocked", "warning", "Kaynak kaniti sinirli; source-grounded ders iddiasi kurulmaz.", "sources", ["source_grounding_blocked", "source_evidence_limited"]);
        }

        if (mission.PrimaryMission.ActionType is "repair_concept" or "repair_prerequisite" &&
            mode is "continue_plan" or "quick_start")
        {
            yield return Warning("study_room_priority_conflict", "warning", "Mission repair derken Study Room normal akisa dusuyor; repair onceligi korunur.", "classroom", ["study_room_priority_conflict"]);
        }

        if (coach.FocusPlan.FocusMode == "study_room_lesson" && !context.TopicId.HasValue)
        {
            yield return Warning("missing_topic_context", "warning", "Study Coach Study Room oneriyor ama konu baglami yok.", "dashboard", ["missing_topic_context"]);
        }

        if (warRoom?.ActiveExam.CanClaimOfficial == false)
        {
            yield return Warning("official_claim_blocked", "info", "Study Room resmi sinav uyumu veya basari iddiasi kurmaz.", "central-exams", ["official_claim_blocked"]);
        }

        if (state.SignalSummary.EvidenceCount <= 1)
        {
            yield return Warning("thin_evidence", "info", "Kanit ince; Study Room kisa baslangic veya tani ile sinirli ilerler.", "classroom", ["thin_evidence"]);
        }

        yield return Warning("raw_payload_guard", "info", "Study Room public kontrati raw transcript, prompt, source chunk veya debug payload tasimaz.", "classroom", ["raw_payload_guard"]);
        yield return Warning("key_guard", "info", "Checkpoint cevabi submit oncesi cozum anahtari tasimaz.", "classroom", ["answer_key_guard"]);
    }

    private static string ResolveReadiness(
        StudyRoomContext context,
        OrkaSourceWikiProDto? sourcePro,
        IReadOnlyList<OrkaStudyRoomWarningDto> warnings,
        string? requestedMode)
    {
        if (warnings.Any(w => w.WarningCode == "source_grounding_blocked") &&
            NormalizeMode(requestedMode) is "source_review_lesson" or "source_grounded")
        {
            return "blocked";
        }

        if (!context.TopicId.HasValue)
        {
            return "limited";
        }

        if (sourcePro?.ReadinessStatus is "needs_attention" or "thin_evidence")
        {
            return "limited";
        }

        return "ready";
    }

    private static IEnumerable<OrkaStudyRoomActionDto> BuildActions(
        StudyRoomContext context,
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaStudyCoachDto coach,
        OrkaExamWarRoomDto? warRoom,
        OrkaSourceWikiProDto? sourcePro,
        string mode,
        string readiness)
    {
        yield return mode switch
        {
            "repair_lesson" => Action("start_repair_lesson", "Repair dersini baslat", "Tek zayif kavram veya on kosul hedefiyle kisa ders.", "high", "open_study_room", "classroom", context, SelectConcept(state, mission, sourcePro, []), null, ["repeated_wrong", "repair_pending"]),
            "review_lesson" => Action("start_review_lesson", "Tekrar dersini baslat", "Zamani gelen tekrar Study Room dersine donusebilir.", "high", "open_study_room", "classroom", context, SelectConcept(state, mission, sourcePro, []), null, ["due_review"]),
            "exam_outcome_practice" => Action("start_exam_outcome_practice", "Sinav outcome pratigi", "Zayif sinav outcome'u kisa pratik dersine donustur.", "high", "open_study_room", "classroom", context, SelectConcept(state, mission, sourcePro, []), SelectExamOutcome(warRoom, []), ["exam_weak_outcome"]),
            "source_review_lesson" => Action("start_source_review_lesson", "Kaynak review dersi", "Kaynak/citation kaniti sinirli; once evidence kontrolu gerekir.", readiness == "blocked" ? "urgent" : "high", "source_review", "sources", context, sourcePro?.TodaySourceWikiMission.ConceptKey, null, ["source_evidence_limited", "source_grounding_blocked"]),
            "wiki_repair_lesson" => Action("start_wiki_repair_lesson", "Wiki repair dersi", "Repair pending Wiki sayfasi Study Room hedefi olabilir.", "high", "update_wiki_note", "wiki", context, sourcePro?.TodaySourceWikiMission.ConceptKey, null, ["wiki_repair_pending"]),
            "checkpoint_quiz" => Action("ask_checkpoint", "Checkpoint sor", "Ders sonunda kisa checkpoint uygula.", "medium", "take_checkpoint_quiz", "chat", context, SelectConcept(state, mission, sourcePro, []), null, ["checkpoint_needed"]),
            "continue_plan" => Action("continue_plan", "Plana devam et", "Kritik repair/source bloklayicisi yoksa normal akisa devam.", "normal", "continue_plan", "dashboard", context, SelectConcept(state, mission, sourcePro, []), null, ["continue_plan"]),
            _ => Action("quick_start", "Kisa baslangic yap", "Kanit ince; once guvenli tek adimla basla.", "medium", "ask_tutor", "chat", context, SelectConcept(state, mission, sourcePro, []), null, ["thin_evidence"])
        };

        if (context.TopicId.HasValue)
        {
            yield return Action("open_tutor", "Tutor repair ac", "Study Room sonrasi Tutor ile kisa aciklama veya repair alinabilir.", "medium", "ask_tutor", "chat", context, SelectConcept(state, mission, sourcePro, []), null, ["tutor_handoff"]);
        }

        if (state.SignalSummary.DueReviewCount > 0 || mode == "review_lesson")
        {
            yield return Action("review_due", "Due review bitir", "Review/SRS baskisi Study Room sonrasi kapatilabilir.", "medium", "review_due_concept", "learning", context, SelectConcept(state, mission, sourcePro, []), null, ["due_review"]);
        }

        if (mode is "repair_lesson" or "checkpoint_quiz" or "exam_outcome_practice")
        {
            yield return Action("ask_checkpoint", "Kisa checkpoint yap", "Cevap submit edilene kadar cozum anahtari public kontratta yoktur.", "medium", "take_checkpoint_quiz", "chat", context, SelectConcept(state, mission, sourcePro, []), null, ["checkpoint_needed", "answer_key_guard"]);
        }

        if (warRoom?.StudyRoomHandoffs.Count > 0)
        {
            yield return Action("start_exam_outcome_practice", "War Room handoff", "Exam War Room zayif outcome'u Study Room'a aktarabilir.", "medium", "open_study_room", "classroom", context, null, SelectExamOutcome(warRoom, []), ["exam_weak_outcome", "study_room_available"]);
        }

        if (sourcePro?.StudyRoomHandoffs.Count > 0 || mode is "source_review_lesson" or "wiki_repair_lesson")
        {
            yield return Action("update_wiki_note", "Wiki/source notunu toparla", "Source/Wiki Pro handoff'u Study Room sonrasi not aksiyonuna doner.", "normal", "update_wiki_note", "wiki", context, sourcePro?.TodaySourceWikiMission.ConceptKey, null, ["wiki_repair_pending", "source_limited"]);
        }

        if (sourcePro?.NotebookHandoffs.Count > 0)
        {
            yield return Action("open_notebook_pack", "Notebook pack ac", "Study Room ciktilari Notebook Studio pack'i ile devam edebilir.", "low", "open_notebook_pack", "notebook-studio", context, sourcePro.TodaySourceWikiMission.ConceptKey, null, ["notebook_pack_ready"]);
        }

        foreach (var secondary in mission.SecondaryActions.Take(3))
        {
            yield return Action(
                NormalizeAction(secondary.ActionType),
                secondary.Label,
                secondary.Reason,
                secondary.Priority,
                secondary.EntryPoint,
                secondary.TargetRoute,
                context,
                secondary.ConceptKey,
                null,
                secondary.ReasonCodes);
        }
    }

    private static OrkaStudyRoomPlanDto BuildPlan(
        string mode,
        OrkaMissionControlDto mission,
        OrkaStudyCoachDto coach,
        OrkaSourceWikiProDto? sourcePro,
        IReadOnlyList<OrkaStudyRoomActionDto> actions)
    {
        var primary = actions.FirstOrDefault();
        var title = mode switch
        {
            "repair_lesson" => "Repair dersi",
            "review_lesson" => "Tekrar dersi",
            "exam_outcome_practice" => "Sinav outcome pratigi",
            "source_review_lesson" => "Kaynak review dersi",
            "wiki_repair_lesson" => "Wiki repair dersi",
            "checkpoint_quiz" => "Checkpoint dersi",
            "continue_plan" => "Planli ders",
            _ => "Kisa baslangic"
        };

        return new OrkaStudyRoomPlanDto
        {
            PlanKey = mode,
            Title = title,
            Objective = primary?.Reason ?? mission.PrimaryMission.Reason,
            DurationBand = coach.FocusPlan.DurationBand,
            Steps = StepsForMode(mode, primary?.Label ?? mission.PrimaryMission.Label, sourcePro),
            StopCondition = mode switch
            {
                "repair_lesson" => "after one repair",
                "review_lesson" => "after due review checkpoint",
                "exam_outcome_practice" => "after one exam checkpoint",
                "source_review_lesson" => "after source warning review",
                "wiki_repair_lesson" => "after wiki repair note",
                "checkpoint_quiz" => "after checkpoint",
                _ => "after short check"
            },
            ReasonCodes = (primary?.ReasonCodes ?? mission.PrimaryMission.ReasonCodes)
                .Concat(coach.FocusPlan.ReasonCodes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray()
        };
    }

    private static OrkaStudyRoomCheckpointDto BuildCheckpoint(
        string mode,
        string? signal,
        bool submitted,
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission)
    {
        var responseSignal = signal ?? "needs_review";
        var status = !submitted
            ? "not_started"
            : responseSignal switch
            {
                "correct" => "passed",
                "blank" or "skipped" => "skipped",
                "wrong" => "needs_repair",
                _ => "submitted"
            };

        var reasons = new List<string>();
        if (responseSignal == "wrong") reasons.AddRange(["recent_wrong_answer", "repair_pending"]);
        if (responseSignal is "blank" or "skipped") reasons.AddRange(["repeated_blank", "prerequisite_gap"]);
        if (responseSignal == "correct") reasons.Add("stable_recent_success");
        if (mode == "checkpoint_quiz") reasons.Add("checkpoint_needed");
        reasons.AddRange(mission.PrimaryMission.ReasonCodes.Take(4));

        return new OrkaStudyRoomCheckpointDto
        {
            CheckpointStatus = status,
            Prompt = PromptForMode(mode, state),
            ResponseSignal = responseSignal,
            PostSubmitFeedback = submitted ? FeedbackForSignal(responseSignal) : string.Empty,
            KeyVisible = false,
            ReasonCodes = reasons.Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray()
        };
    }

    private static IReadOnlyList<OrkaStudyRoomRoleDto> BuildRoles() =>
    [
        new()
        {
            RoleKey = "ai_teacher",
            Label = "AI Teacher",
            Responsibility = "Kisa, hedefli anlatim ve repair/review akisini yonetir."
        },
        new()
        {
            RoleKey = "ai_assistant",
            Label = "AI Assistant",
            Responsibility = "Ozet, checkpoint ve sonraki adimlari guvenli metadata ile toparlar."
        },
        new()
        {
            RoleKey = "student",
            Label = "Student",
            Responsibility = "Cevap verir; sonuc bounded learning signal'a doner."
        }
    ];

    private static IReadOnlyList<string> StepsForMode(string mode, string primaryLabel, OrkaSourceWikiProDto? sourcePro) => mode switch
    {
        "repair_lesson" => [primaryLabel, "Hoca kisa ornekle on kosulu ayirir.", "Ogrenci tek checkpoint cevabi verir.", "Asistan sonraki action'i ozetler."],
        "review_lesson" => [primaryLabel, "Due review kavrami kisa hatirlatilir.", "Tek checkpoint ile unutma baskisi kontrol edilir."],
        "exam_outcome_practice" => [primaryLabel, "Outcome hedefi ve soru tipi netlestirilir.", "Kisa pratik/deneme hatasi uzerinden repair yapilir.", "Sinav garantisi kurulmadan checkpoint ile kapatilir."],
        "source_review_lesson" => [primaryLabel, "Kaynak/citation readiness kontrol edilir.", sourcePro?.EvidenceMap.CanClaimSourceGrounded == true ? "Source-backed metadata guvenle kullanilir." : "Source-grounded iddia downgrade edilir.", "Gerekirse citation review'a donulur."],
        "wiki_repair_lesson" => [primaryLabel, "Wiki repair notu manuel notlara dokunmadan ele alinir.", "Kisa repair ozeti ve update_wiki_note handoff'u uretilir."],
        "checkpoint_quiz" => [primaryLabel, "Checkpoint sorusu gosterilir.", "Submit sonrasi sadece safe feedback verilir."],
        "continue_plan" => [primaryLabel, "Plan akisi kisa dersle surer.", "Kapanista checkpoint veya Wiki notu onerilir."],
        _ => [primaryLabel, "Kisa kontrol ile baslanir.", "Sonuca gore Tutor, Study Room veya quiz handoff'u secilir."]
    };

    private static string PromptForMode(string mode, OrkaLearningStateDto state)
    {
        var concept = SafeOptional(state.PrimaryNextAction.ConceptKey) ?? "bugunku hedef";
        return mode switch
        {
            "repair_lesson" => $"{concept} icin kisa repair checkpoint'i.",
            "review_lesson" => $"{concept} icin due review checkpoint'i.",
            "exam_outcome_practice" => "Zayif exam outcome icin kisa checkpoint.",
            "source_review_lesson" => "Kaynak/citation readiness checkpoint'i.",
            "wiki_repair_lesson" => "Wiki repair notu checkpoint'i.",
            _ => "Kisa Study Room checkpoint'i."
        };
    }

    private static string FeedbackForSignal(string signal) => signal switch
    {
        "correct" => "Cevap dogru sinyal verdi; yine de bu tek basina kesin ustalik iddiasi degildir.",
        "wrong" => "Yanlis sinyal geldi; Study Room repair veya Tutor handoff'u uygun.",
        "blank" or "skipped" => "Bos/atlanan cevap on kosul veya guided review gerektirir; kesin misconception iddiasi kurulmaz.",
        _ => "Cevap daha fazla review gerektiriyor."
    };

    private static string BuildSummary(
        string mode,
        string readiness,
        OrkaStudyRoomPlanDto plan,
        IReadOnlyList<OrkaStudyRoomWarningDto> warnings)
    {
        var warningNote = warnings.Any(w => w.Severity == "warning")
            ? " Uyari var; kaynak/konu sinirlari korunur."
            : string.Empty;
        return $"Study Room {mode} modunda {readiness}. {plan.Objective}{warningNote}";
    }

    private static string SelectConcept(
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaSourceWikiProDto? sourcePro,
        IReadOnlyList<OrkaStudyRoomActionDto> actions)
    {
        return SafeOptional(actions.FirstOrDefault(a => NotBlank(a.ConceptKey))?.ConceptKey) ??
               SafeOptional(mission.PrimaryMission.ConceptKey) ??
               SafeOptional(state.PrimaryNextAction.ConceptKey) ??
               SafeOptional(sourcePro?.TodaySourceWikiMission.ConceptKey) ??
               SafeOptional(state.LongTermLearningProfile.Concepts.FirstOrDefault()?.ConceptKey) ??
               "current_concept";
    }

    private static string? SelectExamOutcome(OrkaExamWarRoomDto? warRoom, IReadOnlyList<OrkaStudyRoomActionDto> actions)
    {
        return SafeOptional(actions.FirstOrDefault(a => NotBlank(a.ExamOutcomeCode))?.ExamOutcomeCode) ??
               SafeOptional(warRoom?.TodayExamMission.OutcomeCode) ??
               SafeOptional(warRoom?.WeakOutcomes.FirstOrDefault()?.OutcomeCode);
    }

    private static OrkaStudyRoomActionDto Action(
        string actionType,
        string label,
        string reason,
        string priority,
        string entryPoint,
        string route,
        StudyRoomContext context,
        string? conceptKey,
        string? examOutcome,
        IReadOnlyList<string> reasonCodes) => new()
    {
        ActionType = SafeText(actionType, 80),
        Label = SafeText(label, 180),
        Reason = SafeText(reason, 260),
        Priority = NormalizePriority(priority),
        EntryPoint = SafeText(entryPoint, 80),
        TargetRoute = SafeText(route, 80),
        TopicId = context.TopicId,
        SourceId = context.SourceId,
        WikiPageId = context.WikiPageId,
        ConceptKey = SafeOptional(conceptKey),
        ExamOutcomeCode = SafeOptional(examOutcome),
        ReasonCodes = reasonCodes.Select(r => SafeText(r, 120)).Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray()
    };

    private static OrkaStudyRoomWarningDto Warning(
        string code,
        string severity,
        string label,
        string route,
        IReadOnlyList<string> reasonCodes) => new()
    {
        WarningCode = SafeText(code, 120),
        Severity = string.IsNullOrWhiteSpace(severity) ? "info" : SafeText(severity, 40),
        Label = SafeText(label, 220),
        TargetRoute = SafeText(route, 80),
        ReasonCodes = reasonCodes.Select(r => SafeText(r, 120)).Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray()
    };

    private static OrkaStudyRoomDto WithSession(OrkaStudyRoomDto dto, Guid sessionId, bool submitted, string? signal)
    {
        dto.ClassroomSessionId = sessionId;
        dto.CurrentTurn.TurnStatus = submitted ? "submitted" : "active";
        if (signal != null)
        {
            dto.CurrentTurn.ResponseSignal = signal;
        }

        return dto;
    }

    private static string BuildSafeSessionTrace(OrkaStudyRoomDto dto) =>
        JsonSerializer.Serialize(new
        {
            source = "study_room",
            mode = dto.StudyRoomMode,
            readiness = dto.SessionReadiness,
            concept = dto.SelectedConcept,
            reasonCodes = dto.ReasonCodes.Take(8)
        });

    private static string AppendSafeTrace(string current, string concept, string signal)
    {
        var safe = JsonSerializer.Serialize(new { checkpoint = SafeOptional(concept), responseSignal = signal });
        if (string.IsNullOrWhiteSpace(current)) return safe;
        var combined = $"{current}\n{safe}";
        return combined.Length <= 4000 ? combined : combined[^4000..];
    }

    private static string NormalizeResponseSignal(OrkaStudyRoomCheckpointRequestDto request)
    {
        if (request.Skipped) return "skipped";
        if (string.IsNullOrWhiteSpace(request.AnswerText) && string.IsNullOrWhiteSpace(request.ResponseSignal)) return "blank";
        return (request.ResponseSignal ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "correct" => "correct",
            "wrong" => "wrong",
            "blank" => "blank",
            "skipped" => "skipped",
            "needs_review" => "needs_review",
            _ => string.IsNullOrWhiteSpace(request.AnswerText) ? "blank" : "needs_review"
        };
    }

    private static string NormalizeAction(string actionType) => actionType switch
    {
        "repair_concept" or "repair_prerequisite" => "start_repair_lesson",
        "review_due_concept" => "start_review_lesson",
        "practice_exam_outcome" or "review_deneme_mistakes" => "start_exam_outcome_practice",
        "source_review" or "citation_review" => "start_source_review_lesson",
        "update_wiki_note" => "start_wiki_repair_lesson",
        "take_checkpoint_quiz" => "ask_checkpoint",
        "open_notebook_pack" => "open_notebook_pack",
        _ => actionType
    };

    private static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return null;
        return mode.Trim().ToLowerInvariant() switch
        {
            "repair" or "repair_lesson" => "repair_lesson",
            "review" or "review_lesson" => "review_lesson",
            "exam" or "exam_outcome_practice" => "exam_outcome_practice",
            "source" or "source_review" or "source_review_lesson" => "source_review_lesson",
            "source_grounded" or "source-grounded" => "source_grounded",
            "wiki" or "wiki_repair" or "wiki_repair_lesson" => "wiki_repair_lesson",
            "checkpoint" or "checkpoint_quiz" => "checkpoint_quiz",
            "continue" or "continue_plan" => "continue_plan",
            "quick_start" => "quick_start",
            _ => mode.Trim().Length <= 60 ? mode.Trim().ToLowerInvariant() : "quick_start"
        };
    }

    private static string NormalizePriority(string? priority) => priority switch
    {
        "urgent" or "high" or "medium" or "normal" or "low" => priority,
        _ => "normal"
    };

    private static int PriorityScore(string? priority) => priority switch
    {
        "urgent" => 5,
        "high" => 4,
        "medium" => 3,
        "normal" => 2,
        "low" => 1,
        _ => 0
    };

    private static int ActionRank(string? actionType) => actionType switch
    {
        "start_source_review_lesson" => 0,
        "start_repair_lesson" => 1,
        "start_exam_outcome_practice" => 2,
        "start_review_lesson" => 3,
        "start_wiki_repair_lesson" => 4,
        "ask_checkpoint" => 5,
        "open_tutor" => 6,
        _ => 20
    };

    private static string SafeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var text = Regex.Replace(value.Trim(), @"\s+", " ");
        text = Regex.Replace(text, @"[A-Za-z]:\\[^\s,;]+", "[path]");
        foreach (var marker in BlockedMarkers)
        {
            text = text.Replace(marker, "[redacted]", StringComparison.OrdinalIgnoreCase);
        }

        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static string? SafeOptional(string? value)
    {
        var text = SafeText(value, 120);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    private sealed record StudyRoomContext(
        Guid? ClassroomSessionId,
        Guid? TopicId,
        Guid? SessionId,
        Guid? SourceId,
        Guid? WikiPageId,
        string? TopicTitle);
}

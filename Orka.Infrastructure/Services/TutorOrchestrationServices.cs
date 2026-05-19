using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class LearningStyleSignalService : ILearningStyleSignalService
{
    private readonly OrkaDbContext _db;

    public LearningStyleSignalService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<LearningStyleSignalDto> DetectAndRecordAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        CancellationToken ct = default)
    {
        var normalized = TutorSignalHeuristics.Normalize(userMessage);
        var (style, confidence, weight) = TutorSignalHeuristics.DetectStyle(normalized);
        var signal = new LearningStyleSignal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            StyleMode = style,
            EvidenceType = "message",
            Weight = weight,
            Confidence = confidence,
            PayloadJson = JsonSerializer.Serialize(new { schemaVersion = "orka.tutor-style.v1", sample = TutorSignalHeuristics.Trim(userMessage, 240) }),
            CreatedAt = DateTime.UtcNow
        };

        _db.LearningStyleSignals.Add(signal);
        await _db.SaveChangesAsync(ct);
        return new LearningStyleSignalDto(signal.Id, signal.StyleMode, signal.EvidenceType, signal.Weight, signal.Confidence, signal.CreatedAt);
    }
}

public sealed class AffectiveSignalService : IAffectiveSignalService
{
    private readonly OrkaDbContext _db;

    public AffectiveSignalService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<AffectiveSignalDto> DetectAndRecordAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        CancellationToken ct = default)
    {
        var normalized = TutorSignalHeuristics.Normalize(userMessage);
        var (state, confidence) = TutorSignalHeuristics.DetectAffectiveState(normalized);
        var signal = new AffectiveSignal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            AffectiveState = state,
            EvidenceType = "message",
            Confidence = confidence,
            PayloadJson = JsonSerializer.Serialize(new { schemaVersion = "orka.tutor-affect.v1", sample = TutorSignalHeuristics.Trim(userMessage, 240) }),
            CreatedAt = DateTime.UtcNow
        };

        _db.AffectiveSignals.Add(signal);
        await _db.SaveChangesAsync(ct);
        return new AffectiveSignalDto(signal.Id, signal.AffectiveState, signal.EvidenceType, signal.Confidence, signal.CreatedAt);
    }
}

public sealed class CognitiveLoadService : ICognitiveLoadService
{
    private readonly OrkaDbContext _db;

    public CognitiveLoadService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<CognitiveLoadSignalDto> DetectAndRecordAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        string learningSignalContext,
        string ideContext,
        CancellationToken ct = default)
    {
        var normalized = TutorSignalHeuristics.Normalize(userMessage);
        var (load, confidence) = TutorSignalHeuristics.DetectCognitiveLoad(normalized, learningSignalContext, ideContext);
        var signal = new CognitiveLoadSignal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            CognitiveLoad = load,
            EvidenceType = "message_context",
            Confidence = confidence,
            PayloadJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.tutor-cognitive-load.v1",
                sample = TutorSignalHeuristics.Trim(userMessage, 240),
                hasLearningSignals = !string.IsNullOrWhiteSpace(learningSignalContext),
                hasIdeContext = !string.IsNullOrWhiteSpace(ideContext)
            }),
            CreatedAt = DateTime.UtcNow
        };

        _db.CognitiveLoadSignals.Add(signal);
        await _db.SaveChangesAsync(ct);
        return new CognitiveLoadSignalDto(signal.Id, signal.CognitiveLoad, signal.EvidenceType, signal.Confidence, signal.CreatedAt);
    }
}

public sealed class LearnerProfileService : ILearnerProfileService
{
    private readonly OrkaDbContext _db;
    private readonly ILearningStyleSignalService _styleSignals;
    private readonly IAffectiveSignalService _affectiveSignals;
    private readonly ICognitiveLoadService _cognitiveLoadSignals;

    public LearnerProfileService(
        OrkaDbContext db,
        ILearningStyleSignalService styleSignals,
        IAffectiveSignalService affectiveSignals,
        ICognitiveLoadService cognitiveLoadSignals)
    {
        _db = db;
        _styleSignals = styleSignals;
        _affectiveSignals = affectiveSignals;
        _cognitiveLoadSignals = cognitiveLoadSignals;
    }

    public async Task<LearnerProfileDto> BuildOrUpdateAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        string learningSignalContext,
        string ideContext,
        CancellationToken ct = default)
    {
        var style = await _styleSignals.DetectAndRecordAsync(userId, topicId, sessionId, userMessage, ct);
        var affective = await _affectiveSignals.DetectAndRecordAsync(userId, topicId, sessionId, userMessage, ct);
        var cognitive = await _cognitiveLoadSignals.DetectAndRecordAsync(userId, topicId, sessionId, userMessage, learningSignalContext, ideContext, ct);

        var profile = await _db.LearnerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId && p.TopicId == topicId, ct);

        if (profile == null)
        {
            profile = new LearnerProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                CreatedAt = DateTime.UtcNow
            };
            _db.LearnerProfiles.Add(profile);
        }

        profile.EvidenceCount += 1;
        if (profile.EvidenceCount <= 3 || style.Confidence >= profile.StyleConfidence || style.StyleMode != "step_by_step")
        {
            profile.PreferredStyleMode = style.StyleMode;
            profile.StyleConfidence = TutorSignalHeuristics.Clamp01((profile.StyleConfidence + style.Confidence) / 2m + 0.05m);
        }

        if (affective.Confidence >= 0.45m)
        {
            profile.AffectiveState = affective.AffectiveState;
        }

        if (cognitive.Confidence >= 0.45m)
        {
            profile.CognitiveLoad = cognitive.CognitiveLoad;
        }

        profile.ProfileJson = JsonSerializer.Serialize(new
        {
            schemaVersion = "orka.learner-profile.v1",
            lowEvidence = profile.EvidenceCount < 3 || profile.StyleConfidence < 0.55m,
            latestStyleSignalId = style.Id,
            latestAffectiveSignalId = affective.Id,
            latestCognitiveLoadSignalId = cognitive.Id
        });
        profile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return ToDto(profile);
    }

    private static LearnerProfileDto ToDto(LearnerProfile profile) => new()
    {
        Id = profile.Id,
        UserId = profile.UserId,
        TopicId = profile.TopicId,
        PreferredStyleMode = profile.PreferredStyleMode,
        StyleConfidence = profile.StyleConfidence,
        AffectiveState = profile.AffectiveState,
        CognitiveLoad = profile.CognitiveLoad,
        EvidenceCount = profile.EvidenceCount,
        UpdatedAt = profile.UpdatedAt
    };
}

public sealed class TutorWorkingMemoryService : ITutorWorkingMemoryService
{
    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService _redis;
    private readonly ILogger<TutorWorkingMemoryService> _logger;

    public TutorWorkingMemoryService(
        OrkaDbContext db,
        IRedisMemoryService redis,
        ILogger<TutorWorkingMemoryService> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<TutorWorkingMemorySnapshot> SaveTurnSnapshotAsync(
        TutorTurnStateDto state,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = "orka.tutor-working-memory.v3",
            state.Id,
            state.UserId,
            state.TopicId,
            state.SessionId,
            state.ActiveConceptKey,
            state.ActiveConceptLabel,
            state.LearnerState,
            state.MasteryProbability,
            state.Confidence,
            state.StyleMode,
            state.AffectiveState,
            state.CognitiveLoad,
            state.GroundingStatus,
            state.CurrentPlanStepId,
            state.CurrentPlanStepTitle,
            state.CurrentPlanTutorMove,
            state.CurrentPlanQuizHook,
            state.PlanSourceReadiness,
            state.DirectAnswerRisk,
            state.CreatedAt
        });

        var snapshot = new TutorWorkingMemorySnapshot
        {
            Id = Guid.NewGuid(),
            UserId = state.UserId,
            TopicId = state.TopicId,
            SessionId = state.SessionId,
            WorkingMemoryVersion = 3,
            ActiveConceptKey = state.ActiveConceptKey,
            TeachingMode = "pending",
            StyleMode = state.StyleMode,
            AffectiveState = state.AffectiveState,
            CognitiveLoad = state.CognitiveLoad,
            Source = "redis+sql",
            IsDegraded = false,
            SnapshotJson = payload,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(6)
        };

        _db.TutorWorkingMemorySnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);

        try
        {
            if (state.SessionId.HasValue)
            {
                await _redis.SetJsonAsync($"orka:v3:tutor-session:{state.SessionId.Value}", payload, TimeSpan.FromHours(6));
                await _redis.SetJsonAsync($"orka:v3:lesson-state:{state.SessionId.Value}", JsonSerializer.Serialize(new
                {
                    schemaVersion = "orka.lesson-state.v3",
                    state.ActiveConceptKey,
                    state.ActiveConceptLabel,
                    activeMode = "pending",
                    lastArtifact = (string?)null,
                    nextCheck = "pending"
                }), TimeSpan.FromHours(6));
            }

            var topicKey = state.TopicId?.ToString() ?? "global";
            await _redis.SetJsonAsync($"orka:v3:tutor-working-memory:{state.UserId}:{topicKey}", payload, TimeSpan.FromDays(7));
        }
        catch (Exception ex)
        {
            snapshot.IsDegraded = true;
            snapshot.Source = "sql_fallback";
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("[TutorWorkingMemory] Redis write degraded. UserRef={UserRef} TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(state.UserId, "usr"),
                LogPrivacyGuard.SafeId(state.TopicId, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }

        return snapshot;
    }

    public async Task<TutorMemoryPatchDto> ApplyPatchAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string patchType,
        object patch,
        CancellationToken ct = default)
    {
        var patchJson = JsonSerializer.Serialize(new
        {
            schemaVersion = "orka.tutor-memory-patch.v1",
            patch
        });
        var entity = new TutorMemoryPatch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            PatchType = string.IsNullOrWhiteSpace(patchType) ? "turn" : patchType,
            PatchJson = patchJson,
            Source = "tutor",
            CreatedAt = DateTime.UtcNow
        };

        _db.TutorMemoryPatches.Add(entity);
        await _db.SaveChangesAsync(ct);

        if (sessionId.HasValue)
        {
            await _redis.SetJsonAsync($"orka:v3:pending-tool-plan:{sessionId.Value}", patchJson, TimeSpan.FromMinutes(20));
        }

        return new TutorMemoryPatchDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            TopicId = entity.TopicId,
            SessionId = entity.SessionId,
            PatchType = entity.PatchType,
            PatchJson = entity.PatchJson,
            Source = entity.Source,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task RecordStreamEventAsync(
        Guid sessionId,
        string eventType,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken ct = default)
    {
        var enriched = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase)
        {
            ["eventType"] = eventType,
            ["schemaVersion"] = "orka.tutor-events.v3",
            ["recordedAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        await _redis.AddStreamEventAsync($"orka:v3:tutor-events:{sessionId}", enriched, TimeSpan.FromDays(2));
    }
}

public sealed class TutorTurnStateAssembler : ITutorTurnStateAssembler
{
    private static readonly JsonSerializerOptions LearningLoopJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrkaDbContext _db;
    private readonly ILearnerProfileService _learnerProfile;
    private readonly ITutorWorkingMemoryService _workingMemory;
    private readonly IActiveLessonSnapshotService? _learningSnapshots;
    private readonly IPlanSequencingService? _planSequencing;

    public TutorTurnStateAssembler(
        OrkaDbContext db,
        ILearnerProfileService learnerProfile,
        ITutorWorkingMemoryService workingMemory,
        IActiveLessonSnapshotService? learningSnapshots = null,
        IPlanSequencingService? planSequencing = null)
    {
        _db = db;
        _learnerProfile = learnerProfile;
        _workingMemory = workingMemory;
        _learningSnapshots = learningSnapshots;
        _planSequencing = planSequencing;
    }

    public async Task<TutorTurnStateDto> BuildAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        string conversationContext,
        string notebookContext,
        string wikiContext,
        string learningSignalContext,
        string ideContext,
        TutorPolicyContextDto policyContext,
        CancellationToken ct = default)
    {
        var graph = await _db.ConceptGraphSnapshots
            .AsNoTracking()
            .Where(g => g.UserId == userId && g.TopicId == topicId)
            .OrderByDescending(g => g.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var ktStates = await _db.KnowledgeTracingStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId)
            .OrderBy(s => s.MasteryProbability)
            .ThenByDescending(s => s.UpdatedAt)
            .Take(6)
            .ToListAsync(ct);

        var masteries = await _db.ConceptMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.TopicId == topicId)
            .OrderBy(m => m.MasteryScore)
            .ThenByDescending(m => m.UpdatedAt)
            .Take(6)
            .ToListAsync(ct);

        string? firstGraphConceptKey = null;
        string? activeGraphConceptLabel = null;
        if (graph != null)
        {
            firstGraphConceptKey = await _db.LearningConcepts
                .AsNoTracking()
                .Where(c => c.ConceptGraphSnapshotId == graph.Id)
                .OrderBy(c => c.Order)
                .Select(c => c.StableKey)
                .FirstOrDefaultAsync(ct);
        }

        var activeKeyCandidate = FirstNonEmpty(
            policyContext.ActiveConceptKey,
            ktStates.FirstOrDefault()?.ConceptKey,
            masteries.FirstOrDefault()?.ConceptKey,
            firstGraphConceptKey);
        var learningLoopSignal = await LoadLearningLoopSignalAsync(userId, topicId, activeKeyCandidate, ct);
        var activeKey = FirstNonEmpty(
            policyContext.ActiveConceptKey,
            activeKeyCandidate,
            learningLoopSignal?.ConceptKey);

        if (graph != null && !string.IsNullOrWhiteSpace(activeKey))
        {
            activeGraphConceptLabel = await _db.LearningConcepts
                .AsNoTracking()
                .Where(c => c.ConceptGraphSnapshotId == graph.Id && c.StableKey == activeKey)
                .Select(c => c.Label)
                .FirstOrDefaultAsync(ct);
        }

        var activeLabel = FirstNonEmpty(
            policyContext.ActiveConceptLabel,
            ktStates.FirstOrDefault(s => s.ConceptKey == activeKey)?.Label,
            masteries.FirstOrDefault(m => m.ConceptKey == activeKey)?.Label,
            activeGraphConceptLabel,
            learningLoopSignal?.Label,
            activeKey);

        var activeKt = ktStates.FirstOrDefault(s => s.ConceptKey == activeKey);
        var activeMastery = masteries.FirstOrDefault(m => m.ConceptKey == activeKey);
        var profile = await _learnerProfile.BuildOrUpdateAsync(userId, topicId, sessionId, userMessage, learningSignalContext, ideContext, ct);
        var activeLessonSnapshot = _learningSnapshots == null
            ? null
            : await _learningSnapshots.GetActiveLessonSnapshotAsync(userId, topicId, sessionId, ct);
        var studentContextSnapshot = _learningSnapshots == null
            ? null
            : await _learningSnapshots.GetStudentContextSnapshotAsync(userId, topicId, sessionId, ct);
        var latestPlanQuality = topicId.HasValue && _planSequencing != null
            ? await _planSequencing.GetLatestPlanQualitySnapshotAsync(userId, topicId.Value, sessionId, ct)
            : null;
        var currentPlanStep = latestPlanQuality?.PlanContract.Steps.FirstOrDefault();
        var masteryProbability = activeKt?.MasteryProbability ?? (activeMastery?.MasteryScore / 100m);
        var confidence = activeKt?.Confidence ?? activeMastery?.Confidence;
        var learnerState = ApplyLearningLoopState(
            BuildLearnerState(policyContext.LearnerState, masteryProbability, confidence, profile.AffectiveState, profile.CognitiveLoad),
            learningLoopSignal);
        var remediationNeed = FirstNonEmpty(activeKt?.RemediationNeed, activeMastery?.RemediationNeed, learningLoopSignal?.RemediationNeed, "unknown");
        var recentMistakes = policyContext.RecentMistakes
            .Concat(learningLoopSignal?.Hints ?? Array.Empty<string>())
            .Concat(masteries.Where(m => m.MasteryScore < 50).Select(m => m.Label))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        var state = new TutorTurnStateDto
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            ConceptGraphSnapshotId = graph?.Id ?? policyContext.ConceptGraphSnapshotId,
            ActiveLessonSnapshotId = activeLessonSnapshot?.Id,
            StudentContextSnapshotId = studentContextSnapshot?.Id,
            PlanQualitySnapshotId = latestPlanQuality?.SnapshotId,
            UserMessage = userMessage,
            ActiveConceptKey = activeKey,
            ActiveConceptLabel = activeLabel,
            LearnerState = learnerState,
            LessonSnapshotStatus = activeLessonSnapshot?.Status ?? "not_available",
            StudentContextConfidenceStatus = studentContextSnapshot?.ConfidenceStatus ?? "none",
            MasteryProbability = masteryProbability,
            Confidence = confidence,
            RemediationNeed = remediationNeed,
            PracticeReadiness = FirstNonEmpty(activeKt?.PracticeReadiness, activeMastery?.PracticeReadiness, learningLoopSignal?.PracticeReadiness, "guided"),
            StyleMode = profile.PreferredStyleMode,
            AffectiveState = profile.AffectiveState,
            CognitiveLoad = profile.CognitiveLoad,
            GroundingStatus = policyContext.GroundingStatus,
            SourceEvidenceCount = policyContext.SourceEvidenceCount + CountContextEvidence(notebookContext, wikiContext),
            EvidenceQuality = policyContext.EvidenceQuality,
            MisconceptionSignal = learningLoopSignal?.Misconception,
            LearningSignalConfidence = learningLoopSignal?.Confidence,
            RemediationSeed = learningLoopSignal?.RemediationSeed,
            LearningLoopStatus = learningLoopSignal?.Status ?? "signal_pending",
            CurrentPlanStepId = currentPlanStep?.StepId,
            CurrentPlanStepTitle = currentPlanStep?.Title,
            CurrentPlanTutorMove = currentPlanStep?.TutorHook.TutorMove,
            CurrentPlanQuizHook = currentPlanStep?.QuizHook.HookType,
            PlanSourceReadiness = currentPlanStep?.Evidence.SourceReadiness ?? latestPlanQuality?.PlanContract.SourceReadiness,
            AdaptiveDiagnostic = latestPlanQuality?.PlanContract.AdaptiveDiagnostic,
            CoursePlanQuality = latestPlanQuality?.PlanContract.CoursePlanQuality,
            LatestAssessmentMode = currentPlanStep?.QuizHook.HookType ?? "unknown",
            LatestMisconceptionConfidence = learningLoopSignal?.Confidence?.Status ?? "none",
            SourceReadiness = currentPlanStep?.Evidence.SourceReadiness
                ?? latestPlanQuality?.PlanContract.SourceReadiness
                ?? policyContext.GroundingStatus,
            DirectAnswerRisk = policyContext.DirectAnswerRisk || TutorSignalHeuristics.ContainsAny(TutorSignalHeuristics.Normalize(userMessage), "cevabı ver", "direkt cevap", "sonucu söyle", "çözümü ver"),
            HasIdeContext = !string.IsNullOrWhiteSpace(ideContext),
            HasNotebookContext = !string.IsNullOrWhiteSpace(notebookContext),
            HasWikiContext = !string.IsNullOrWhiteSpace(wikiContext),
            RecentMistakes = recentMistakes,
            SourceEvidence = policyContext.SourceEvidence.Take(8).ToList(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var responsePolicy = TutorResponsePolicy.Decide(state);
        state.TutorResponseMode = responsePolicy.TutorResponseMode;
        state.EvidencePolicy = responsePolicy.EvidencePolicy;
        state.PersonalizationMode = responsePolicy.PersonalizationMode;
        state.MasteryBasis = responsePolicy.MasteryBasis;
        state.WeakConceptHints = responsePolicy.WeakConceptHints;
        state.RemediationLesson = BuildTurnStateRemediationLesson(state);

        var snapshot = await _workingMemory.SaveTurnSnapshotAsync(state, ct);
        state.WorkingMemorySnapshotId = snapshot.Id;
        state.PromptBlock = BuildPromptBlock(state, profile);

        var entity = new TutorTurnState
        {
            Id = state.Id,
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            WorkingMemorySnapshotId = state.WorkingMemorySnapshotId,
            ConceptGraphSnapshotId = state.ConceptGraphSnapshotId,
            UserMessageHash = Hash(userMessage),
            ActiveConceptKey = state.ActiveConceptKey,
            TeachingMode = "pending",
            StyleMode = state.StyleMode,
            AffectiveState = state.AffectiveState,
            CognitiveLoad = state.CognitiveLoad,
            GroundingStatus = state.GroundingStatus,
            StateJson = JsonSerializer.Serialize(state),
            CreatedAt = DateTime.UtcNow
        };

        _db.TutorTurnStates.Add(entity);
        await _db.SaveChangesAsync(ct);

        if (sessionId.HasValue)
        {
            await _workingMemory.RecordStreamEventAsync(sessionId.Value, "tutor.turn_state.ready", new Dictionary<string, string>
            {
                ["tutorTurnStateId"] = state.Id.ToString(),
                ["activeConceptKey"] = state.ActiveConceptKey,
                ["learnerState"] = state.LearnerState,
                ["styleMode"] = state.StyleMode,
                ["groundingStatus"] = state.GroundingStatus,
                ["learningLoopStatus"] = state.LearningLoopStatus,
                ["planQualitySnapshotId"] = state.PlanQualitySnapshotId?.ToString() ?? "none",
                ["currentPlanStepId"] = state.CurrentPlanStepId ?? "none",
                ["currentPlanQuizHook"] = state.CurrentPlanQuizHook ?? "none",
                ["planReadiness"] = state.CoursePlanQuality?.ReadinessStatus ?? "unknown",
                ["diagnosticIntent"] = state.AdaptiveDiagnostic?.Intent ?? "unknown"
            }, ct);
        }

        return state;
    }

    private static string BuildPromptBlock(TutorTurnStateDto state, LearnerProfileDto profile)
    {
        var styleEvidence = profile.IsLowEvidence
            ? "Kanıt düşük: öğrenim tarzını kişilik etiketi gibi söyleme; sadece bu tur için uyarlama yap."
            : "Kanıt yeterli: seçilen stil modunu öğretim stratejisi olarak kullan.";

        return $"""

            [TUTOR TURN STATE v3 - AKTIF CALISMA BELLEGI]
            - tutorTurnStateId: {state.Id}
            - workingMemorySnapshotId: {state.WorkingMemorySnapshotId}
            - activeLessonSnapshotId: {state.ActiveLessonSnapshotId?.ToString() ?? "none"} ({state.LessonSnapshotStatus})
            - studentContextSnapshotId: {state.StudentContextSnapshotId?.ToString() ?? "none"} ({state.StudentContextConfidenceStatus})
            - planQualitySnapshotId: {state.PlanQualitySnapshotId?.ToString() ?? "none"}
            - currentPlanStep: {state.CurrentPlanStepId ?? "none"} / {state.CurrentPlanStepTitle ?? "none"}
            - currentPlanTutorMove: {state.CurrentPlanTutorMove ?? "none"}
            - currentPlanQuizHook: {state.CurrentPlanQuizHook ?? "none"}
            - adaptiveDiagnosticIntent: {state.AdaptiveDiagnostic?.Intent ?? "unknown"}
            - adaptiveLearnerLevel: {state.AdaptiveDiagnostic?.LearnerLevel ?? "unknown"}
            - planReadiness: {state.CoursePlanQuality?.ReadinessStatus ?? state.AdaptiveDiagnostic?.PlanReadiness ?? "unknown"}
            - planNextAction: {state.CoursePlanQuality?.RecommendedNextAction ?? state.AdaptiveDiagnostic?.NextAction ?? "unknown"}
            - planSourceReadiness: {state.PlanSourceReadiness ?? "unknown"}
            - sourceReadiness: {state.SourceReadiness ?? "unknown"}
            - latestAssessmentMode: {state.LatestAssessmentMode ?? "unknown"}
            - latestMisconceptionConfidence: {state.LatestMisconceptionConfidence ?? "none"}
            - activeConcept: {state.ActiveConceptKey} / {state.ActiveConceptLabel}
            - learnerState: {state.LearnerState}
            - masteryProbability: {state.MasteryProbability?.ToString("0.00") ?? "unknown"}
            - confidence: {state.Confidence?.ToString("0.00") ?? "unknown"}
            - remediationNeed: {state.RemediationNeed}
            - practiceReadiness: {state.PracticeReadiness}
            - styleMode: {state.StyleMode} ({styleEvidence})
            - affectiveState: {state.AffectiveState}
            - cognitiveLoad: {state.CognitiveLoad}
            - groundingStatus: {state.GroundingStatus}
            - sourceEvidenceCount: {state.SourceEvidenceCount}
            - evidenceQuality: {state.EvidenceQuality?.Status ?? "unknown"}
            - tutorResponseMode: {state.TutorResponseMode ?? "standard"}
            - evidencePolicy: {state.EvidencePolicy ?? "unknown_source_caution"}
            - personalizationMode: {state.PersonalizationMode ?? "unknown"}; masteryBasis: {state.MasteryBasis ?? "default"}
            - learningLoopStatus: {state.LearningLoopStatus}
            - misconceptionSignal: {state.MisconceptionSignal?.UserSafeLabel ?? "none"}
            - learningSignalConfidence: {state.LearningSignalConfidence?.Status ?? "unknown"}
            - remediationSeed: {state.RemediationSeed?.FirstAction ?? "none"} / {state.RemediationSeed?.Reason ?? "none"}
            - remediationLesson: {state.RemediationLesson?.RepairType ?? "none"} / {state.RemediationLesson?.StudentVisibleSummary ?? "none"}
            - directAnswerRisk: {state.DirectAnswerRisk}
            - recentMistakes: {(state.RecentMistakes.Count == 0 ? "none" : string.Join("; ", state.RecentMistakes))}

            [TUTOR STATE KURALI]
            Bu turda cevabi yukaridaki aktif kavram, mastery/confidence ve duygu-yuk sinyaline gore kur. 
            Kaynak yoksa kaynak iddiasinda bulunma. Evidence limited ise temkinli ve kisa konus; confidence dusukse "ogrendin" deme; kanit yetersiz modunda mikro kontrol sor.
            currentPlanStep varsa onu pedagojik rota olarak kullan; plan adimi yanit metnini kilitlemez ama kavram, quiz hook ve Tutor move icin oncelikli baglamdir.
            learningLoopStatus remediation_ready ise bu turu profesyonel telafi turu olarak ele al: once yanilgi ihtimalini nazikce sinirla, sonra remediationSeed aksiyonuna gore kisa ve adim adim duzeltme yap.
            """;
    }

    private static RemediationLessonDto? BuildTurnStateRemediationLesson(TutorTurnStateDto state)
    {
        var confused = state.AffectiveState is "confused" or "frustrated" ||
                       state.LearnerState.Contains("remediation", StringComparison.OrdinalIgnoreCase);
        var weak = state.RemediationNeed is "high" or "medium" ||
                   state.MasteryProbability < 0.45m ||
                   state.CoursePlanQuality?.ReadinessStatus == "needs_repair";
        if (!confused && !weak && state.RemediationSeed == null && state.MisconceptionSignal == null)
        {
            return null;
        }

        var sourceGap = state.SourceReadiness is "insufficient" or "degraded" or "stale" or "deleted" or "evidence_insufficient";
        var triggerType = state.RemediationSeed != null ? "misconception_signal" :
            confused ? "student_confused" :
            sourceGap ? "source_evidence_gap" :
            "weak_concept";
        var repairType = state.RemediationSeed?.FirstAction == "prerequisite_review" ? "prerequisite_repair" :
            sourceGap ? "source_evidence_review" :
            state.MisconceptionSignal != null ? "misconception_repair" :
            weak ? "weak_concept_repair" :
            "guided_reteach";
        var concept = FirstNonEmpty(state.ActiveConceptLabel, state.ActiveConceptKey, state.CurrentPlanStepTitle, "aktif kavram");
        var gap = repairType switch
        {
            "misconception_repair" => FirstNonEmpty(state.MisconceptionSignal?.UserSafeLabel, state.MisconceptionSignal?.SafeHint, state.RemediationSeed?.Reason, "Takilma sinyali kesin tani degil."),
            "prerequisite_repair" => FirstNonEmpty(state.RemediationSeed?.Reason, "Eksik onkosul once kisa telafi istiyor."),
            "source_evidence_review" => "Kaynak kaniti sinirli oldugu icin kaynakli iddia kurulmadan once kanit kontrolu gerekiyor.",
            "weak_concept_repair" => "Mastery veya plan sinyali zayif kavram telafisi istiyor.",
            _ => "Ogrenci mesaji veya ogrenme durumu kisa guided reteach istiyor."
        };

        var warnings = new List<string>();
        if (state.LearningSignalConfidence?.Status == "observed_only") warnings.Add("observed_only_repair_signal");
        if (state.MisconceptionSignal == null && repairType == "guided_reteach") warnings.Add("misconception_not_confirmed");
        if (sourceGap) warnings.Add("source_evidence_limited");

        return new RemediationLessonDto
        {
            TopicId = state.TopicId,
            ConceptKey = string.IsNullOrWhiteSpace(state.ActiveConceptKey) ? null : state.ActiveConceptKey,
            Trigger = new RemediationTriggerDto
            {
                TriggerType = triggerType,
                UserSafeLabel = triggerType switch
                {
                    "student_confused" => "Anlamadim sinyali",
                    "source_evidence_gap" => "Kaynak kaniti siniri",
                    "misconception_signal" => "Takilma sinyali",
                    _ => "Zayif kavram sinyali"
                },
                EvidenceStatus = state.LearningSignalConfidence?.Status ?? "observed_only"
            },
            RepairType = repairType,
            Confidence = state.LearningSignalConfidence?.Status == "usable" ? "medium" : "low",
            Basis = new[]
            {
                string.IsNullOrWhiteSpace(state.LearningLoopStatus) ? null : state.LearningLoopStatus,
                state.MasteryProbability.HasValue ? "mastery_snapshot" : null,
                state.RemediationSeed != null ? "learning_memory" : null,
                confused ? "student_message" : null,
                sourceGap ? "source_review" : null,
                state.CoursePlanQuality?.ReadinessStatus == "needs_repair" ? "plan_quality" : null
            }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(),
            LessonShape = BuildTurnStateRepairLoop(concept, repairType, gap),
            Checkpoint = new RemediationCheckpointDto
            {
                CheckpointType = repairType == "source_evidence_review" ? "evidence_check" : "micro_check",
                UserSafePrompt = repairType == "source_evidence_review"
                    ? "Bu iddiada kaynakta desteklenen kisim hangisi?"
                    : $"{concept} icin bir mini ornegi kendi cumlenle dene.",
                AvoidsPreSubmitReveal = true,
                Required = true
            },
            Outcome = new RemediationOutcomeDto
            {
                ExpectedSignal = repairType == "source_evidence_review" ? "source_review_needed" : "needs_review",
                MasteryPolicy = "do_not_overstate_mastery",
                NextTutorAction = repairType == "source_evidence_review" ? "review_source_then_continue" : "guided_repair_then_check",
                NotebookAction = "repair_pack_available"
            },
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(),
            SourceBasis = repairType == "source_evidence_review" ? "evidence_insufficient" : "tutor_generated",
            StudentVisibleSummary = repairType switch
            {
                "misconception_repair" => "Tutor takilma sinyalini kesin tani gibi sunmadan ornekle onaracak.",
                "prerequisite_repair" => "Tutor once eksik onkosulu kisa telafiyle toparlayacak.",
                "source_evidence_review" => "Tutor kaynak kaniti sinirini acik tutarak ilerleyecek.",
                "weak_concept_repair" => "Tutor zayif kavram icin mikro ders ve kontrol uygulayacak.",
                _ => "Tutor anlamadigin noktayi kisa guided tekrar ile toparlayacak."
            }
        };
    }

    private static RemediationRepairLoopDto BuildTurnStateRepairLoop(string concept, string repairType, string gap) =>
        new()
        {
            Goal = repairType switch
            {
                "misconception_repair" => $"{concept} icin yanilgi ihtimalini nazikce sinirla ve dogru ayrimi kur.",
                "prerequisite_repair" => $"{concept} icin eksik onkosulu once tamamla.",
                "source_evidence_review" => $"{concept} icin kaynak kaniti ile Tutor yorumunu ayir.",
                "weak_concept_repair" => $"{concept} icin zayif parcayi mikro dersle guclendir.",
                _ => $"{concept} icin kisa guided reteach yap."
            },
            MisconceptionOrGap = gap,
            ShortReteach = "Tek kavramsal ayrimi iki kisa adimda yeniden kur.",
            WorkedExample = "Bir cozumlu mini ornek kullan.",
            GuidedPractice = "Ogrenciye tek kucuk adimi kendisi yaptir.",
            Checkpoint = "Cevap anahtari vermeden mikro kontrol sor.",
            NextAction = repairType == "source_evidence_review" ? "review_source_then_continue" : "guided_repair_then_check",
            Steps = new[]
            {
                new RemediationStepDto { StepType = "goal", UserSafeLabel = "Telafi hedefini kur", Required = true },
                new RemediationStepDto { StepType = "short_reteach", UserSafeLabel = "Kisa tekrar", Required = true },
                new RemediationStepDto { StepType = "worked_example", UserSafeLabel = "Cozumlu mini ornek", Required = repairType != "source_evidence_review" },
                new RemediationStepDto { StepType = "guided_practice", UserSafeLabel = "Tek adimlik pratik", Required = true },
                new RemediationStepDto { StepType = "checkpoint", UserSafeLabel = "Cevap anahtarsiz kontrol", Required = true }
            }
        };

    private async Task<LearningLoopSignalProjection?> LoadLearningLoopSignalAsync(
        Guid userId,
        Guid? topicId,
        string? activeConceptKey,
        CancellationToken ct)
    {
        var query = _db.LearningSignals
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.PayloadJson != null);
        query = topicId.HasValue
            ? query.Where(s => s.TopicId == topicId.Value)
            : query.Where(s => s.TopicId == null);

        var rows = await query
            .OrderByDescending(s => s.CreatedAt)
            .Take(30)
            .Select(s => new { s.PayloadJson, s.CreatedAt })
            .ToListAsync(ct);

        var activeKey = NormalizeKey(activeConceptKey);
        return rows
            .Select(row => ParseLearningLoopSignal(row.PayloadJson, row.CreatedAt))
            .Where(signal => signal != null)
            .Select(signal => signal!)
            .Where(signal => signal.Status is "remediation_ready" or "observed_only")
            .Where(signal => string.IsNullOrWhiteSpace(activeKey) || NormalizeKey(signal.ConceptKey) == activeKey)
            .OrderByDescending(signal => signal.Status == "remediation_ready")
            .ThenByDescending(signal => signal.CreatedAt)
            .FirstOrDefault();
    }

    private static LearningLoopSignalProjection? ParseLearningLoopSignal(string? payloadJson, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        try
        {
            var payload = JsonSerializer.Deserialize<LearningLoopSignalPayload>(payloadJson, LearningLoopJsonOptions);
            if (payload?.LearningSignalConfidence is null && payload?.RemediationSeed is null && payload?.MisconceptionSignal is null)
                return null;

            var status = payload.LearningSignalConfidence?.Status ?? payload.RemediationSeed?.ConfidenceStatus;
            var normalizedStatus = NormalizeKey(status);
            var loopStatus = normalizedStatus switch
            {
                "usable" => "remediation_ready",
                "observed_only" => "observed_only",
                _ => "signal_pending"
            };
            var conceptKey = FirstNonEmpty(payload.RemediationSeed?.ConceptKey, payload.MisconceptionSignal?.ConceptKey);
            var label = FirstNonEmpty(payload.RemediationSeed?.Label, payload.MisconceptionSignal?.Label, payload.MisconceptionSignal?.UserSafeLabel);
            var hints = new[]
                {
                    payload.RemediationSeed?.Label,
                    payload.RemediationSeed?.UserSafeMisconceptionLabel,
                    payload.MisconceptionSignal?.UserSafeLabel,
                    payload.MisconceptionSignal?.SafeHint
                }
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => h!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();

            return new LearningLoopSignalProjection(
                payload.MisconceptionSignal,
                payload.LearningSignalConfidence,
                payload.RemediationSeed,
                loopStatus,
                conceptKey,
                label,
                loopStatus == "remediation_ready" ? "high" : "unknown",
                "guided",
                hints,
                createdAt);
        }
        catch
        {
            return null;
        }
    }

    private static string ApplyLearningLoopState(string learnerState, LearningLoopSignalProjection? signal)
    {
        if (signal?.Status == "remediation_ready")
            return "needs_remediation";
        return string.IsNullOrWhiteSpace(learnerState) ? "unknown" : learnerState;
    }

    private static int CountContextEvidence(string notebookContext, string wikiContext)
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(notebookContext)) count += 2;
        if (!string.IsNullOrWhiteSpace(wikiContext)) count += 1;
        return count;
    }

    private static string BuildLearnerState(string policyState, decimal? mastery, decimal? confidence, string affective, string load)
    {
        if (affective is "confused" or "frustrated") return "needs_remediation";
        if (load == "high") return "high_cognitive_load";
        if (mastery.HasValue && mastery.Value < 0.45m) return "weak_mastery";
        if (confidence.HasValue && confidence.Value < 0.35m) return "evidence_insufficient";
        return string.IsNullOrWhiteSpace(policyState) ? "unknown" : policyState;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string NormalizeKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private sealed class LearningLoopSignalPayload
    {
        public MisconceptionSignalDto? MisconceptionSignal { get; set; }
        public LearningSignalConfidenceDto? LearningSignalConfidence { get; set; }
        public RemediationSeedDto? RemediationSeed { get; set; }
    }

    private sealed record LearningLoopSignalProjection(
        MisconceptionSignalDto? Misconception,
        LearningSignalConfidenceDto? Confidence,
        RemediationSeedDto? RemediationSeed,
        string Status,
        string ConceptKey,
        string Label,
        string RemediationNeed,
        string PracticeReadiness,
        IReadOnlyList<string> Hints,
        DateTime CreatedAt);

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed class TutorActionPlanner : ITutorActionPlanner
{
    private readonly OrkaDbContext _db;
    private readonly ITutorWorkingMemoryService _workingMemory;
    private readonly ITeachingEvidenceRouter _evidenceRouter;

    public TutorActionPlanner(
        OrkaDbContext db,
        ITutorWorkingMemoryService workingMemory,
        ITeachingEvidenceRouter? evidenceRouter = null)
    {
        _db = db;
        _workingMemory = workingMemory;
        _evidenceRouter = evidenceRouter ?? new TeachingEvidenceRouter();
    }

    public async Task<TutorActionPlanDto> PlanAsync(TutorTurnStateDto turnState, CancellationToken ct = default)
    {
        var normalized = TutorSignalHeuristics.Normalize(turnState.UserMessage);
        var lowMastery = !turnState.MasteryProbability.HasValue || turnState.MasteryProbability < 0.55m || turnState.Confidence < 0.45m;
        var codeIntent = TutorSignalHeuristics.ContainsAny(normalized, "kod", "java", "sql", "algorithm", "algoritma", "hata", "runtime", "syntax");
        var wantsVisual = turnState.StyleMode == "visual" || TutorSignalHeuristics.ContainsAny(normalized, "görsel", "ciz", "çiz", "graf", "diagram", "şema", "harita");
        var sourceIntent = TutorSignalHeuristics.ContainsAny(normalized, "kaynak", "wiki", "doküman", "dokuman", "notebook", "belgeye göre", "kaynağa göre");
        var reviewIntent = TutorSignalHeuristics.ContainsAny(normalized, "tekrar", "review", "kart", "flashcard", "unut");

        var responsePolicy = TutorResponsePolicy.Decide(turnState);
        turnState.TutorResponseMode = responsePolicy.TutorResponseMode;
        turnState.EvidencePolicy = responsePolicy.EvidencePolicy;
        turnState.PersonalizationMode = responsePolicy.PersonalizationMode;
        turnState.MasteryBasis = responsePolicy.MasteryBasis;
        turnState.WeakConceptHints = responsePolicy.WeakConceptHints;

        var teachingMode = SelectTeachingMode(turnState, lowMastery, codeIntent, wantsVisual, sourceIntent, reviewIntent, responsePolicy);
        var directAnswerPolicy = turnState.DirectAnswerRisk && lowMastery
            ? "hint_first_then_scaffold"
            : turnState.DirectAnswerRisk ? "brief_answer_then_reasoning_check" : "scaffold";
        var groundingPolicy = responsePolicy.EvidencePolicy;

        var toolPlans = BuildToolPlans(turnState, normalized, sourceIntent, reviewIntent, codeIntent, wantsVisual)
            .Concat(_evidenceRouter.Route(turnState, normalized))
            .GroupBy(p => p.ToolId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(8)
            .ToList();
        var artifactPlans = BuildArtifactPlans(turnState, normalized, teachingMode, codeIntent, wantsVisual)
            .Concat(BuildEvidenceArtifactPlans(toolPlans))
            .GroupBy(a => a.ArtifactType, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => g.First())
            .Take(8)
            .ToList();
        var nextCheck = TutorResponsePolicy.NextCheckFor(turnState, teachingMode, responsePolicy);

        var trace = new TutorActionTrace
        {
            Id = Guid.NewGuid(),
            UserId = turnState.UserId,
            TopicId = turnState.TopicId,
            SessionId = turnState.SessionId,
            TutorTurnStateId = turnState.Id,
            TeachingMode = teachingMode,
            ActiveConceptKey = turnState.ActiveConceptKey,
            StyleMode = turnState.StyleMode,
            DirectAnswerPolicy = directAnswerPolicy,
            GroundingPolicy = groundingPolicy,
            ToolPlanJson = JsonSerializer.Serialize(toolPlans),
            ArtifactPlanJson = JsonSerializer.Serialize(artifactPlans),
            NextCheckPrompt = nextCheck,
            CreatedAt = DateTime.UtcNow
        };

        _db.TutorActionTraces.Add(trace);
        await _db.SaveChangesAsync(ct);

        var professionalPolicy = TutorResponsePolicyService.BuildPolicy(
            turnState,
            trace,
            latestAttempt: null,
            latestBundle: null,
            toolCalls: Array.Empty<TutorToolCall>());
        var toolDecision = BuildToolDecision(
            turnState,
            normalized,
            teachingMode,
            toolPlans,
            artifactPlans,
            responsePolicy,
            professionalPolicy);
        var lessonDelivery = BuildLessonDelivery(
            turnState,
            teachingMode,
            toolDecision,
            responsePolicy,
            professionalPolicy,
            nextCheck);
        var remediationLesson = BuildActionPlanRemediationLesson(turnState, toolDecision, lessonDelivery);

        var plan = new TutorActionPlanDto
        {
            Id = trace.Id,
            TutorTurnStateId = turnState.Id,
            UserId = turnState.UserId,
            TopicId = turnState.TopicId,
            SessionId = turnState.SessionId,
            TeachingMode = teachingMode,
            ActiveConceptKey = turnState.ActiveConceptKey,
            LearnerState = turnState.LearnerState,
            StyleMode = turnState.StyleMode,
            DirectAnswerPolicy = directAnswerPolicy,
            GroundingPolicy = groundingPolicy,
            TutorResponseMode = responsePolicy.TutorResponseMode,
            TutorTeachingMove = professionalPolicy.TeachingMove,
            TutorResponseDepth = professionalPolicy.ResponseDepth,
            TutorGroundingPolicy = professionalPolicy.GroundingPolicy,
            TutorRemediationPolicy = professionalPolicy.RemediationPolicy,
            TutorToolPolicy = professionalPolicy.ToolPolicy,
            TutorNextLearningActions = professionalPolicy.NextActions.Select(a => a.ActionType).ToArray(),
            PersonalizationMode = responsePolicy.PersonalizationMode,
            MasteryBasis = responsePolicy.MasteryBasis,
            WeakConceptHints = responsePolicy.WeakConceptHints,
            ToolPlans = toolPlans,
            ArtifactPlans = artifactPlans,
            ToolDecision = toolDecision,
            LessonDelivery = lessonDelivery,
            RemediationLesson = remediationLesson,
            NextCheckPrompt = nextCheck,
            PromptBlock = BuildPromptBlock(trace, toolPlans, artifactPlans, responsePolicy, toolDecision, lessonDelivery, remediationLesson)
        };

        await _workingMemory.ApplyPatchAsync(turnState.UserId, turnState.TopicId, turnState.SessionId, "tutor_action_plan", new
        {
            actionTraceId = plan.Id,
            plan.TeachingMode,
            plan.ActiveConceptKey,
            plan.StyleMode,
            plan.DirectAnswerPolicy,
            plan.GroundingPolicy,
            plan.TutorResponseMode,
            plan.TutorTeachingMove,
            plan.TutorResponseDepth,
            plan.TutorGroundingPolicy,
            plan.TutorRemediationPolicy,
            plan.TutorToolPolicy,
            plan.TutorNextLearningActions,
            plan.PersonalizationMode,
            plan.MasteryBasis,
            plan.WeakConceptHints,
            toolDecision = plan.ToolDecision,
            lessonDelivery = plan.LessonDelivery,
            remediationLesson = plan.RemediationLesson,
            turnState.LearningLoopStatus,
            misconceptionSignal = turnState.MisconceptionSignal?.UserSafeLabel,
            remediationAction = turnState.RemediationSeed?.FirstAction,
            plan.NextCheckPrompt
        }, ct);

        if (turnState.SessionId.HasValue)
        {
            await _workingMemory.RecordStreamEventAsync(turnState.SessionId.Value, "tutor.action_plan.ready", new Dictionary<string, string>
            {
                ["tutorActionTraceId"] = plan.Id.ToString(),
                ["teachingMode"] = plan.TeachingMode,
                ["activeConceptKey"] = plan.ActiveConceptKey,
                ["toolCount"] = toolPlans.Count.ToString(),
                ["artifactCount"] = artifactPlans.Count.ToString(),
                ["tutorTeachingMove"] = plan.TutorTeachingMove ?? "unknown",
                ["tutorGroundingPolicy"] = plan.TutorGroundingPolicy ?? "unknown",
                ["selectedToolAction"] = plan.ToolDecision?.SelectedAction ?? "no_tool",
                ["lessonDeliveryMode"] = plan.LessonDelivery?.DeliveryMode ?? "concept_explanation",
                ["remediationRepairType"] = plan.RemediationLesson?.RepairType ?? "none"
            }, ct);
        }

        return plan;
    }

    private static TutorLessonDeliveryDto BuildLessonDelivery(
        TutorTurnStateDto state,
        string teachingMode,
        TutorToolDecisionDto toolDecision,
        TutorResponsePolicyDecision responsePolicy,
        TutorResponsePolicyDto professionalPolicy,
        string nextCheck)
    {
        var learnerLevel = DetermineLearnerLevel(state, responsePolicy);
        var deliveryMode = DetermineDeliveryMode(state, teachingMode, toolDecision, professionalPolicy);
        var warnings = new List<string>();
        if (toolDecision.SafetyWarnings.Contains("source_grounded_route_blocked", StringComparer.OrdinalIgnoreCase) ||
            professionalPolicy.GroundingPolicy is "evidence_insufficient" or "mention_source_limits" ||
            responsePolicy.TutorResponseMode == "evidence_limited")
        {
            warnings.Add("source_evidence_limited");
        }

        if (professionalPolicy.SafetyIssues.Any(i => i.Code.Contains("answer_key", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("answer_key_guard_active");
        }

        if (state.Confidence < 0.45m || responsePolicy.MasteryBasis == "low_confidence")
        {
            warnings.Add("low_confidence_learner_state");
        }

        var includesRepair = deliveryMode is "misconception_repair" or "prerequisite_repair";
        var includesCheckpoint = deliveryMode is "checkpoint_question" or "guided_example" or "concept_explanation" or "misconception_repair" or "prerequisite_repair";
        var sourceBasis = deliveryMode == "source_grounded_explanation" ? "source_grounded" :
            deliveryMode == "model_assisted_explanation" ? "model_assisted" :
            "tutor_generated";
        var steps = BuildLessonSteps(deliveryMode, sourceBasis);

        return new TutorLessonDeliveryDto
        {
            DeliveryMode = deliveryMode,
            LearnerLevel = learnerLevel,
            Structure = new TutorLessonStructureDto
            {
                Goal = BuildLessonGoal(deliveryMode, state),
                ShortExplanation = BuildShortExplanationGuidance(deliveryMode, learnerLevel),
                Example = BuildExampleGuidance(deliveryMode, learnerLevel),
                Checkpoint = deliveryMode == "ask_clarifying_question" ? "Once hedefi netlestir." : nextCheck,
                NextAction = BuildLessonNextAction(deliveryMode, professionalPolicy)
            },
            RubricSignals = new TutorLessonRubricDto
            {
                UsesLearnerState = !string.IsNullOrWhiteSpace(state.LearnerState) && state.LearnerState != "unknown",
                UsesMasterySignal = state.MasteryProbability.HasValue || state.Confidence.HasValue,
                UsesQuizSignal = !string.IsNullOrWhiteSpace(state.LatestAssessmentMode) ||
                                  state.MisconceptionSignal != null ||
                                  state.RemediationSeed != null ||
                                  professionalPolicy.LatestAssessmentMode != "none",
                UsesSourceEvidence = deliveryMode == "source_grounded_explanation",
                AvoidsPreSubmitReveal = true,
                IncludesCheckpoint = includesCheckpoint,
                IncludesRepairStep = includesRepair,
                BoundedLength = true
            },
            Steps = steps,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(),
            StudentVisibleSummary = BuildLessonDeliverySummary(deliveryMode, warnings)
        };
    }

    private static RemediationLessonDto? BuildActionPlanRemediationLesson(
        TutorTurnStateDto state,
        TutorToolDecisionDto toolDecision,
        TutorLessonDeliveryDto lessonDelivery)
    {
        if (state.RemediationLesson != null)
        {
            return state.RemediationLesson;
        }

        var repairDelivery = lessonDelivery.DeliveryMode is "misconception_repair" or "prerequisite_repair";
        if (toolDecision.SelectedAction != "start_remediation" && !repairDelivery)
        {
            return null;
        }

        var sourceGap = toolDecision.SafetyWarnings.Contains("source_grounded_route_blocked", StringComparer.OrdinalIgnoreCase) ||
                        lessonDelivery.Warnings.Contains("source_evidence_limited", StringComparer.OrdinalIgnoreCase);
        var repairType = lessonDelivery.DeliveryMode == "misconception_repair" ? "misconception_repair" :
            lessonDelivery.DeliveryMode == "prerequisite_repair" ? "prerequisite_repair" :
            sourceGap ? "source_evidence_review" :
            state.MasteryProbability < 0.45m ? "weak_concept_repair" :
            "guided_reteach";
        var triggerType = state.LatestAssessmentMode is "skipped" ? "skipped_answer" :
            state.LatestAssessmentMode is "blank" ? "blank_answer" :
            state.MisconceptionSignal != null ? "misconception_signal" :
            state.AffectiveState is "confused" or "frustrated" ? "student_confused" :
            sourceGap ? "source_evidence_gap" :
            "weak_concept";
        var concept = FirstNonEmptyLocal(state.ActiveConceptLabel, state.ActiveConceptKey, state.CurrentPlanStepTitle) ?? "aktif kavram";
        var gap = repairType switch
        {
            "misconception_repair" => FirstNonEmptyLocal(state.MisconceptionSignal?.UserSafeLabel, state.MisconceptionSignal?.SafeHint, "Takilma sinyali kesin tani degil.")!,
            "prerequisite_repair" => FirstNonEmptyLocal(state.RemediationSeed?.Reason, "Eksik onkosul kisa telafi istiyor.")!,
            "source_evidence_review" => "Kaynak kaniti sinirli; kaynakli iddia kurulmadan once kanit kontrolu gerekir.",
            "weak_concept_repair" => "Mastery sinyali zayif kavrami mikro dersle toparlamayi oneriyor.",
            _ => "Ogrenci bu noktada kisa guided reteach istiyor."
        };

        return new RemediationLessonDto
        {
            TopicId = state.TopicId,
            ConceptKey = string.IsNullOrWhiteSpace(state.ActiveConceptKey) ? null : state.ActiveConceptKey,
            Trigger = new RemediationTriggerDto
            {
                TriggerType = triggerType,
                UserSafeLabel = triggerType switch
                {
                    "blank_answer" => "Bos cevap telafisi",
                    "skipped_answer" => "Atlanan cevap telafisi",
                    "student_confused" => "Anlamadim sinyali",
                    "source_evidence_gap" => "Kaynak kaniti siniri",
                    "misconception_signal" => "Takilma sinyali",
                    _ => "Zayif kavram sinyali"
                },
                EvidenceStatus = state.LearningSignalConfidence?.Status ?? "observed_only"
            },
            RepairType = repairType,
            Confidence = state.LearningSignalConfidence?.Status == "usable" ? "medium" : "low",
            Basis = toolDecision.LearnerSignalsUsed
                .Concat(toolDecision.ReasonCodes)
                .Append("tutor_action_plan")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToArray(),
            LessonShape = new RemediationRepairLoopDto
            {
                Goal = lessonDelivery.Structure.Goal,
                MisconceptionOrGap = gap,
                ShortReteach = lessonDelivery.Structure.ShortExplanation,
                WorkedExample = lessonDelivery.Structure.Example,
                GuidedPractice = "Ogrenciye tek kucuk adimi kendisi yaptir.",
                Checkpoint = lessonDelivery.Structure.Checkpoint,
                NextAction = lessonDelivery.Structure.NextAction,
                Steps = lessonDelivery.Steps
                    .Where(s => s.StepType is "goal" or "short_explanation" or "worked_example" or "repair_step" or "checkpoint")
                    .Select(s => new RemediationStepDto
                    {
                        StepType = s.StepType == "short_explanation" ? "short_reteach" : s.StepType,
                        UserSafeLabel = s.UserSafeLabel,
                        Required = s.Required,
                        SourceBasis = s.SourceBasis
                    })
                    .Take(5)
                    .ToArray()
            },
            Checkpoint = new RemediationCheckpointDto
            {
                CheckpointType = sourceGap ? "evidence_check" : "micro_check",
                UserSafePrompt = lessonDelivery.Structure.Checkpoint,
                AvoidsPreSubmitReveal = true,
                Required = true
            },
            Outcome = new RemediationOutcomeDto
            {
                ExpectedSignal = repairType == "source_evidence_review" ? "source_review_needed" : "needs_review",
                MasteryPolicy = "do_not_overstate_mastery",
                NextTutorAction = lessonDelivery.Structure.NextAction,
                NotebookAction = "repair_pack_available"
            },
            Warnings = lessonDelivery.Warnings
                .Append(state.MisconceptionSignal == null && repairType == "guided_reteach" ? "misconception_not_confirmed" : null)
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => w!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray(),
            SourceBasis = sourceGap ? "evidence_insufficient" : "tutor_generated",
            StudentVisibleSummary = repairType switch
            {
                "misconception_repair" => "Tutor takilma sinyalini kesin tani gibi sunmadan onaracak.",
                "prerequisite_repair" => "Tutor once eksik onkosulu kisa telafiyle toparlayacak.",
                "source_evidence_review" => "Tutor kaynak kaniti sinirini acik tutarak ilerleyecek.",
                "weak_concept_repair" => "Tutor zayif kavram icin mikro ders ve kontrol uygulayacak.",
                _ => "Tutor bu turda kisa guided telafi dersi uygulayacak."
            }
        };
    }

    private static string DetermineLearnerLevel(TutorTurnStateDto state, TutorResponsePolicyDecision responsePolicy)
    {
        if (state.AdaptiveDiagnostic?.LearnerLevel is "beginner" or "developing" or "exam_ready" or "advanced")
            return state.AdaptiveDiagnostic.LearnerLevel;
        if (state.PracticeReadiness == "exam_ready" && state.MasteryProbability >= 0.70m && state.Confidence >= 0.60m)
            return "exam_ready";
        if (string.Equals(responsePolicy.PersonalizationMode, "advanced", StringComparison.OrdinalIgnoreCase))
            return "advanced";
        if (state.MasteryProbability >= 0.78m && state.Confidence >= 0.65m)
            return state.PracticeReadiness == "exam_ready" ? "exam_ready" : "advanced";
        if (state.MasteryProbability >= 0.55m && state.Confidence >= 0.45m)
            return "developing";
        if (state.MasteryProbability.HasValue || state.Confidence.HasValue)
            return "beginner";
        return "unknown";
    }

    private static string DetermineDeliveryMode(
        TutorTurnStateDto state,
        string teachingMode,
        TutorToolDecisionDto toolDecision,
        TutorResponsePolicyDto professionalPolicy)
    {
        if (toolDecision.SelectedAction == "ask_clarifying_question")
            return "ask_clarifying_question";
        if (toolDecision.SelectedAction == "run_diagnostic")
            return "checkpoint_question";
        if (toolDecision.SelectedAction == "generate_quiz")
            return "checkpoint_question";
        if (toolDecision.SelectedAction == "ask_source")
            return HasReadySourceEvidence(state) ? "source_grounded_explanation" : "model_assisted_explanation";
        if (toolDecision.SelectedAction == "start_remediation")
        {
            if (state.RemediationSeed?.FirstAction == "prerequisite_review" ||
                professionalPolicy.RemediationPolicy == "prerequisite_review" ||
                string.Equals(state.LatestAssessmentMode, "skipped", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state.LatestAssessmentMode, "blank", StringComparison.OrdinalIgnoreCase))
            {
                return "prerequisite_repair";
            }

            return state.MisconceptionSignal != null || professionalPolicy.LatestMisconceptionConfidence is "observed" or "strong"
                ? "misconception_repair"
                : "prerequisite_repair";
        }

        if (professionalPolicy.GroundingPolicy is "cite_sources" && HasReadySourceEvidence(state))
            return "source_grounded_explanation";
        if (professionalPolicy.GroundingPolicy is "evidence_insufficient" or "mention_source_limits" &&
            toolDecision.SafetyWarnings.Any(w => w.Contains("source", StringComparison.OrdinalIgnoreCase)))
            return "model_assisted_explanation";
        if (teachingMode == "review" || professionalPolicy.TeachingMove == "retrieval_prompt")
            return "quiz_review";
        if (teachingMode == "challenge" || state.MasteryProbability >= 0.75m && state.Confidence >= 0.60m)
            return "checkpoint_question";
        if (teachingMode == "guided_practice" || state.MasteryProbability < 0.55m || state.Confidence < 0.45m)
            return "guided_example";
        return "concept_explanation";
    }

    private static IReadOnlyList<TutorLessonStepDto> BuildLessonSteps(string deliveryMode, string sourceBasis)
    {
        var steps = new List<TutorLessonStepDto>
        {
            new() { StepType = "goal", UserSafeLabel = "Hedefi kisa kur", Required = true, SourceBasis = sourceBasis },
            new() { StepType = "short_explanation", UserSafeLabel = "Kisa aciklama", Required = true, SourceBasis = sourceBasis }
        };

        if (deliveryMode is "guided_example" or "misconception_repair" or "prerequisite_repair")
        {
            steps.Add(new TutorLessonStepDto { StepType = "worked_example", UserSafeLabel = "Somut ornekle ilerle", Required = true, SourceBasis = sourceBasis });
        }

        if (deliveryMode is "misconception_repair" or "prerequisite_repair")
        {
            steps.Add(new TutorLessonStepDto { StepType = "repair_step", UserSafeLabel = "Telafi adimini ac", Required = true, SourceBasis = "tutor_generated" });
        }

        if (deliveryMode is "checkpoint_question" or "guided_example" or "concept_explanation" or "misconception_repair" or "prerequisite_repair")
        {
            steps.Add(new TutorLessonStepDto { StepType = "checkpoint", UserSafeLabel = "Kisa kontrol sorusu sor", Required = true, SourceBasis = "tutor_generated" });
        }

        steps.Add(new TutorLessonStepDto { StepType = "next_action", UserSafeLabel = "Sonraki adimi soyle", Required = true, SourceBasis = "tutor_generated" });
        return steps.Take(6).ToArray();
    }

    private static string BuildLessonGoal(string deliveryMode, TutorTurnStateDto state)
    {
        var concept = string.IsNullOrWhiteSpace(state.ActiveConceptLabel)
            ? (string.IsNullOrWhiteSpace(state.ActiveConceptKey) ? "aktif kavram" : state.ActiveConceptKey)
            : state.ActiveConceptLabel;
        return deliveryMode switch
        {
            "misconception_repair" => $"{concept} icin takilma sinyalini kesin tani gibi sunmadan onar.",
            "prerequisite_repair" => $"{concept} icin eksik onkosulu kisa telafiyle tamamla.",
            "guided_example" => $"{concept} icin kavrami somut ornekle kur.",
            "checkpoint_question" => $"{concept} icin kisa yoklama yap.",
            "source_grounded_explanation" => $"{concept} aciklamasini hazir kaynak kanitindan ayirarak kur.",
            "model_assisted_explanation" => $"{concept} icin kaynak sinirini belirterek model destekli acikla.",
            "ask_clarifying_question" => "Ogrenme hedefini ve baglami netlestir.",
            _ => $"{concept} icin kisa, hedefli kavram aciklamasi yap."
        };
    }

    private static string BuildShortExplanationGuidance(string deliveryMode, string learnerLevel) =>
        deliveryMode switch
        {
            "ask_clarifying_question" => "Cevap vermeden once tek netlestirme sorusu sor.",
            "checkpoint_question" => "Aciklamayi kisa tut ve cevabi sakli tek kontrol sorusu sor.",
            "misconception_repair" => "Hatayi kesin tani gibi sunmadan dogru ayrimi iki kisa adimda kur.",
            "prerequisite_repair" => "Eksik onkosulu yavas ve kisa parcalarla toparla.",
            "source_grounded_explanation" => "Kaynakta desteklenen kisim ile Tutor yorumunu ayri tut.",
            "model_assisted_explanation" => "Kaynak kaniti sinirliyken kesin kaynak iddiasi kurma.",
            _ => learnerLevel == "advanced" ? "Kisa gerekce ve sinir kosulu ver." : "Tek kavramsal parca ile basla."
        };

    private static string BuildExampleGuidance(string deliveryMode, string learnerLevel) =>
        deliveryMode switch
        {
            "guided_example" or "misconception_repair" or "prerequisite_repair" => "Bir cozumlu ornek veya mini senaryo kullan.",
            "checkpoint_question" => "Ornek yerine cevap anahtari olmayan kontrol sorusu kullan.",
            "ask_clarifying_question" => "Ornek verme; once hedefi netlestir.",
            _ => learnerLevel is "beginner" or "unknown" ? "Gerekirse tek somut ornek ekle." : "Ornegi kisa ve ayirt edici tut."
        };

    private static string BuildLessonNextAction(string deliveryMode, TutorResponsePolicyDto professionalPolicy)
    {
        var next = professionalPolicy.NextActions.FirstOrDefault()?.ActionType;
        if (!string.IsNullOrWhiteSpace(next)) return next;
        return deliveryMode switch
        {
            "checkpoint_question" => "student_attempts_checkpoint",
            "misconception_repair" or "prerequisite_repair" => "guided_repair_then_check",
            "ask_clarifying_question" => "student_clarifies_goal",
            "source_grounded_explanation" => "review_source_citation",
            _ => "continue_lesson"
        };
    }

    private static string BuildLessonDeliverySummary(string deliveryMode, IReadOnlyCollection<string> warnings)
    {
        if (warnings.Contains("source_evidence_limited", StringComparer.OrdinalIgnoreCase))
            return "Tutor kaynak sinirini belirterek model destekli ve kontrollu anlatim yapiyor.";

        return deliveryMode switch
        {
            "guided_example" => "Tutor once kisa hedefi kurup somut ornekle ilerliyor.",
            "checkpoint_question" => "Tutor bu turda kisa kontrol sorusuyla ogrenmeyi yokluyor.",
            "misconception_repair" => "Tutor takilma sinyalini kesin tani gibi sunmadan ornekle onariyor.",
            "prerequisite_repair" => "Tutor eksik onkosulu kisa telafiyle toparliyor.",
            "source_grounded_explanation" => "Tutor hazir kaynak kanitini Tutor yorumundan ayri tutarak acikliyor.",
            "model_assisted_explanation" => "Tutor kaynak kaniti sinirliyken model destekli aciklama yapiyor.",
            "ask_clarifying_question" => "Tutor daha iyi ders kurmak icin once hedefi netlestiriyor.",
            "quiz_review" => "Tutor once tekrar ve quiz sonucunu kisa bir ogrenme hamlesine bagliyor.",
            _ => "Tutor bu turda kisa, hedefli kavram anlatimiyla ilerliyor."
        };
    }

    private static TutorToolDecisionDto BuildToolDecision(
        TutorTurnStateDto state,
        string normalized,
        string teachingMode,
        IReadOnlyList<TutorToolPlanDto> toolPlans,
        IReadOnlyList<TeachingArtifactPlanDto> artifactPlans,
        TutorResponsePolicyDecision responsePolicy,
        TutorResponsePolicyDto professionalPolicy)
    {
        var allowedTools = toolPlans.Select(t => t.ToolId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var blockedTools = new List<string>();
        var reasonCodes = new List<string>();
        var learnerSignals = new List<string>();
        var safetyWarnings = new List<string>();

        var sourceIntent = TutorSignalHeuristics.ContainsAny(normalized, "kaynak", "wiki", "dokuman", "doküman", "notebook", "belgeye göre", "kaynağa göre");
        var quizIntent = TutorSignalHeuristics.ContainsAny(normalized, "quiz", "test", "soru sor", "sinav", "sınav", "yokla", "kontrol et");
        var codeIntent = TutorSignalHeuristics.ContainsAny(normalized, "kod", "java", "sql", "algorithm", "algoritma", "hata", "runtime", "syntax");
        var researchIntent = TutorSignalHeuristics.ContainsAny(normalized, "araştır", "arastir", "research", "literatur", "literatür", "kaynak tara");
        var artifactIntent = TutorSignalHeuristics.ContainsAny(normalized, "paket", "artifact", "study guide", "calisma paketi", "çalışma paketi", "notebook pack", "slayt");
        var confused = state.AffectiveState is "confused" or "frustrated" ||
                       state.LearnerState.Contains("remediation", StringComparison.OrdinalIgnoreCase) ||
                       state.RemediationNeed is "high" or "medium" ||
                       state.RemediationSeed != null ||
                       state.LearningLoopStatus.Contains("remediation", StringComparison.OrdinalIgnoreCase);
        var hasSourceEvidence = HasReadySourceEvidence(state);
        var hasActiveLearningContext = state.TopicId.HasValue ||
                                       !string.IsNullOrWhiteSpace(state.ActiveConceptKey) ||
                                       !string.IsNullOrWhiteSpace(state.CurrentPlanStepId);

        if (!string.IsNullOrWhiteSpace(state.ActiveConceptKey)) learnerSignals.Add("active_concept");
        if (state.MasteryProbability.HasValue || state.Confidence.HasValue) learnerSignals.Add("mastery_confidence");
        if (state.RemediationNeed is "high" or "medium") learnerSignals.Add("remediation_need");
        if (state.MisconceptionSignal != null) learnerSignals.Add("misconception_signal");
        if (state.RemediationSeed != null) learnerSignals.Add("remediation_seed");
        if (state.HasWikiContext) learnerSignals.Add("wiki_context");
        if (state.HasNotebookContext || state.SourceEvidenceCount > 0) learnerSignals.Add("source_context");
        if (state.HasIdeContext) learnerSignals.Add("ide_context");
        if (!string.IsNullOrWhiteSpace(state.AdaptiveDiagnostic?.Intent)) learnerSignals.Add("adaptive_diagnostic");
        if (!string.IsNullOrWhiteSpace(state.CoursePlanQuality?.ReadinessStatus)) learnerSignals.Add("plan_readiness");
        if (!hasSourceEvidence && sourceIntent)
        {
            blockedTools.Add("ask_source");
            blockedTools.Add("source_grounded_answer");
            reasonCodes.Add("source_evidence_insufficient");
            safetyWarnings.Add("source_grounded_route_blocked");
        }

        string selectedAction;
        if (state.CoursePlanQuality?.ReadinessStatus is "needs_diagnostic" or "thin_plan" && !sourceIntent && !quizIntent)
        {
            selectedAction = "run_diagnostic";
            reasonCodes.Add("plan_needs_diagnostic");
        }
        else if (state.CoursePlanQuality?.ReadinessStatus == "needs_prerequisite_check" && !sourceIntent)
        {
            selectedAction = "run_diagnostic";
            reasonCodes.Add("plan_needs_prerequisite_check");
        }
        else if (confused || state.CoursePlanQuality?.ReadinessStatus == "needs_repair")
        {
            selectedAction = "start_remediation";
            reasonCodes.Add("learner_needs_repair");
        }
        else if (sourceIntent && hasSourceEvidence)
        {
            selectedAction = "ask_source";
            reasonCodes.Add("source_intent_ready_evidence");
        }
        else if (sourceIntent)
        {
            selectedAction = hasActiveLearningContext ? "ask_clarifying_question" : "explain";
            reasonCodes.Add("source_intent_evidence_limited");
        }
        else if (quizIntent)
        {
            selectedAction = "generate_quiz";
            reasonCodes.Add("student_requested_check");
        }
        else if (codeIntent || state.HasIdeContext)
        {
            selectedAction = "use_ide_context";
            reasonCodes.Add("code_context_detected");
        }
        else if (researchIntent && allowedTools.Contains("research_context", StringComparer.OrdinalIgnoreCase))
        {
            selectedAction = "use_korteks_research";
            reasonCodes.Add("research_context_available");
        }
        else if (artifactIntent || (artifactPlans.Count > 0 && teachingMode is "visualize" or "code_lab"))
        {
            selectedAction = artifactIntent ? "create_notebook_pack" : "create_artifact";
            reasonCodes.Add("artifact_requested_or_helpful");
        }
        else if (state.HasWikiContext && allowedTools.Contains("wiki_search", StringComparer.OrdinalIgnoreCase))
        {
            selectedAction = "read_wiki_context";
            reasonCodes.Add("wiki_context_available");
        }
        else if (!hasActiveLearningContext)
        {
            selectedAction = "ask_clarifying_question";
            reasonCodes.Add("missing_learning_context");
        }
        else
        {
            selectedAction = "explain";
            reasonCodes.Add("default_tutor_explanation");
        }

        if (state.HasWikiContext && selectedAction is "explain" or "start_remediation" or "ask_source")
        {
            allowedTools.Add("write_wiki_trace");
            reasonCodes.Add("wiki_trace_available");
        }

        if (professionalPolicy.GroundingPolicy is "evidence_insufficient" or "mention_source_limits" ||
            responsePolicy.TutorResponseMode == "evidence_limited")
        {
            safetyWarnings.Add("evidence_limited");
        }

        var confidence = selectedAction switch
        {
            "start_remediation" => 0.88m,
            "ask_source" => 0.84m,
            "ask_clarifying_question" => 0.72m,
            "use_ide_context" => 0.80m,
            "use_korteks_research" => 0.68m,
            _ => 0.70m
        };

        return new TutorToolDecisionDto
        {
            SelectedAction = selectedAction,
            AllowedTools = allowedTools.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            BlockedTools = blockedTools.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            ReasonCodes = reasonCodes.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            Confidence = confidence,
            LearnerSignalsUsed = learnerSignals.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            EvidenceStatus = responsePolicy.EvidencePolicy,
            SourceReadiness = FirstNonEmptyLocal(state.SourceReadiness, state.PlanSourceReadiness, state.EvidenceQuality?.Status, state.GroundingStatus) ?? "unknown",
            SafetyWarnings = safetyWarnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            NextTutorMove = state.CoursePlanQuality?.RecommendedNextAction ?? professionalPolicy.TeachingMove,
            StudentVisibleSummary = BuildDecisionSummary(selectedAction, safetyWarnings, reasonCodes)
        };
    }

    private static bool HasReadySourceEvidence(TutorTurnStateDto state)
    {
        if (state.SourceEvidenceCount > 0) return true;
        if (state.EvidenceQuality is { ReadySourceCount: > 0 }) return true;
        return FirstNonEmptyLocal(state.SourceReadiness, state.PlanSourceReadiness, state.GroundingStatus, state.EvidenceQuality?.Status) is
            "source_grounded" or "mixed" or "ready" or "strong";
    }

    private static string? FirstNonEmptyLocal(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string BuildDecisionSummary(string selectedAction, IReadOnlyCollection<string> safetyWarnings, IReadOnlyCollection<string> reasonCodes)
    {
        if (safetyWarnings.Contains("source_grounded_route_blocked", StringComparer.OrdinalIgnoreCase))
            return "Kaynak kaniti yeterli olmadigi icin Tutor kaynakli iddia kurmadan ilerliyor.";

        return selectedAction switch
        {
            "start_remediation" => "Tutor bu turda eksigi kapatmak icin telafi adimina geciyor.",
            "ask_source" => "Tutor once hazir kaynak kanitini kullanarak cevap verecek.",
            "ask_clarifying_question" => "Tutor daha guvenli ilerlemek icin once baglami netlestirecek.",
            "generate_quiz" => "Tutor ogrenmeyi olcmek icin kisa kontrol sorusu hazirlayacak.",
            "use_ide_context" => "Tutor kod/IDE ciktisini guvenli baglam olarak kullanacak.",
            "use_korteks_research" => "Tutor arastirma baglamini yalnizca destekleyici kanit olarak kullanacak.",
            "create_notebook_pack" => "Tutor mevcut baglamdan calisma paketi hazirlamaya yoneliyor.",
            "create_artifact" => "Tutor anlatimi somutlastirmak icin guvenli bir artifact kullanacak.",
            "read_wiki_context" => "Tutor once Wiki ogrenme hafizasini kontrol edecek.",
            _ => reasonCodes.Contains("missing_learning_context", StringComparer.OrdinalIgnoreCase)
                ? "Tutor once hedefi netlestirerek ilerleyecek."
                : "Tutor bu turda genel anlatimla ilerliyor."
        };
    }

    private static string SelectTeachingMode(TutorTurnStateDto state, bool lowMastery, bool codeIntent, bool wantsVisual, bool sourceIntent, bool reviewIntent, TutorResponsePolicyDecision responsePolicy)
    {
        if (responsePolicy.TutorResponseMode == "evidence_limited" && sourceIntent) return "explain";
        if (state.AffectiveState is "confused" or "frustrated" || state.LearnerState.Contains("remediation", StringComparison.OrdinalIgnoreCase)) return "remediate";
        if (codeIntent || state.HasIdeContext) return "code_lab";
        if (sourceIntent && state.SourceEvidenceCount > 0) return "source_grounded_answer";
        if (reviewIntent) return "review";
        if (wantsVisual) return "visualize";
        if (lowMastery) return "guided_practice";
        if (state.MasteryProbability >= 0.75m && state.Confidence >= 0.60m) return "challenge";
        return "explain";
    }

    private static List<TutorToolPlanDto> BuildToolPlans(TutorTurnStateDto state, string normalized, bool sourceIntent, bool reviewIntent, bool codeIntent, bool wantsVisual)
    {
        var plans = new List<TutorToolPlanDto>();
        if (sourceIntent || state.HasNotebookContext)
            plans.Add(new TutorToolPlanDto("source_search", "Kaynak/doküman zemini kontrol edilecek.", sourceIntent, "low"));
        if (state.HasWikiContext)
            plans.Add(new TutorToolPlanDto("wiki_search", "Konu wiki belleği cevap öncesi değerlendirilecek.", false, "low"));
        if (state.HasIdeContext || codeIntent)
            plans.Add(new TutorToolPlanDto("ide_last_result", "IDE/Piston son çıktısı öğretim örneğine bağlanacak.", state.HasIdeContext, "medium"));
        if (reviewIntent)
            plans.Add(new TutorToolPlanDto("review_query", "Tekrar baskısı ve SRS kayıtları kontrol edilecek.", false, "low"));
        if (TutorSignalHeuristics.ContainsAny(normalized, "flashcard", "kart"))
            plans.Add(new TutorToolPlanDto("flashcard_query", "İlgili tekrar kartları aranacak.", false, "low"));
        if (TutorSignalHeuristics.ContainsAny(normalized, "hesapla", "integral", "türev", "denklem", "olasılık"))
            plans.Add(new TutorToolPlanDto("wolfram_alpha", "Matematiksel doğrulama gerekebilir.", false, "medium"));
        if (TutorSignalHeuristics.ContainsAny(normalized, "hava durumu", "weather"))
            plans.Add(new TutorToolPlanDto("weather", "Canli hava/iklim sinyali eski uyumluluk araci olarak kontrol edilecek.", true, "low"));
        if (TutorSignalHeuristics.ContainsAny(normalized, "haber", "güncel"))
            plans.Add(new TutorToolPlanDto("news", "Güncel haber bilgisi isteniyor.", true, "medium"));
        if (TutorSignalHeuristics.ContainsAny(normalized, "bitcoin", "kripto", "coin"))
            plans.Add(new TutorToolPlanDto("crypto", "Güncel piyasa bilgisi isteniyor.", true, "medium"));
        if (wantsVisual)
            plans.Add(new TutorToolPlanDto("visual_generation", "Görsel/diagram öğretim artifact ihtiyacı var.", false, "medium"));
        if (TutorSignalHeuristics.ContainsAny(normalized, "akış", "workflow", "mimari", "graph", "graf", "süreç", "algoritma"))
            plans.Add(new TutorToolPlanDto("mermaid_graph", "İlişki veya süreç diyagramı üretilecek.", false, "low"));

        return plans
            .GroupBy(p => p.ToolId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(6)
            .ToList();
    }

    private static List<TeachingArtifactPlanDto> BuildArtifactPlans(TutorTurnStateDto state, string normalized, string teachingMode, bool codeIntent, bool wantsVisual)
    {
        var plans = new List<TeachingArtifactPlanDto>();
        if (codeIntent || teachingMode == "code_lab")
            plans.Add(new TeachingArtifactPlanDto("code_lab_task", "Kodlama görevini beklenen çıktı ile somutlaştır.", "markdown"));
        if (wantsVisual || TutorSignalHeuristics.ContainsAny(normalized, "mimari", "süreç", "algoritma", "workflow", "graph", "graf", "diagram"))
            plans.Add(new TeachingArtifactPlanDto("mermaid_graph", "Kavram ilişkisini hızlı görselleştir.", "mermaid"));
        if (TutorSignalHeuristics.ContainsAny(normalized, "tarih", "selçuklu", "osmanlı", "kpss", "kronoloji"))
            plans.Add(new TeachingArtifactPlanDto("timeline", "Tarih/sınav bilgisini zaman çizgisine bağla.", "markdown"));
        if (TutorSignalHeuristics.ContainsAny(normalized, "fark", "karşılaştır", "karsilastir", "benzer"))
            plans.Add(new TeachingArtifactPlanDto("comparison_table", "Karşılaştırmayı tabloyla azalt.", "markdown"));
        if (teachingMode is "remediate" or "guided_practice" or "explain")
            plans.Add(new TeachingArtifactPlanDto("worked_example", "Yanlış kavramı örnek üstünden düzelt.", "markdown"));
        if (state.SourceEvidenceCount > 0)
            plans.Add(new TeachingArtifactPlanDto("retrieval_card", "Kaynak zemininin varlığını kısa kartla açıkla.", "markdown"));
        if (wantsVisual && TutorSignalHeuristics.ContainsAny(normalized, "resim", "foto", "image", "gorsel", "görsel"))
            plans.Add(new TeachingArtifactPlanDto("image_prompt", "Gorsel anlatim icin provider destekli artifact denenecek.", "image"));
        if (teachingMode is "remediate" or "guided_practice" or "challenge")
            plans.Add(new TeachingArtifactPlanDto("micro_quiz", "Cevap sonuna tek mikro kontrol ekle.", "markdown"));

        return plans
            .GroupBy(p => p.ArtifactType, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(4)
            .ToList();
    }

    private static IEnumerable<TeachingArtifactPlanDto> BuildEvidenceArtifactPlans(IEnumerable<TutorToolPlanDto> toolPlans)
    {
        foreach (var tool in toolPlans)
        {
            yield return tool.ToolId switch
            {
                "forum_signal" => new TeachingArtifactPlanDto("forum_pattern", "Forum sinyali yalnizca yaygin hata oruntusu olarak gosterilecek.", "markdown"),
                "geo_context" => new TeachingArtifactPlanDto("map_context", "Konum/enlem/yukselti baglami kanit kartina cevrilecek.", "markdown"),
                "science_context" => new TeachingArtifactPlanDto("science_fact_card", "Bilimsel public veri ders ornegine cevrilecek.", "markdown"),
                "research_context" => new TeachingArtifactPlanDto("research_reading_card", "Akademik/kitap kaynagi ileri okuma kartina cevrilecek.", "markdown"),
                "socioeconomic_context" => new TeachingArtifactPlanDto("real_world_graph", "Sosyoekonomik veri tablo/grafik fikrine cevrilecek.", "markdown"),
                "knowledge_entity" => new TeachingArtifactPlanDto("evidence_card", "Kavram/entity zemini kaynakli kanit karti olarak gosterilecek.", "markdown"),
                _ => new TeachingArtifactPlanDto(string.Empty, string.Empty, string.Empty)
            };
        }
    }

    private static string BuildNextCheck(TutorTurnStateDto state, string teachingMode)
    {
        var concept = string.IsNullOrWhiteSpace(state.ActiveConceptLabel) ? "bu kavram" : state.ActiveConceptLabel;
        return teachingMode switch
        {
            "challenge" => $"{concept} için bir karşı örnek veya daha zor bir uygulama adımı önerebilir misin?",
            "code_lab" => "Bu kod görevinin beklenen çıktısını çalıştırmadan önce tahmin eder misin?",
            "visualize" => $"Diyagramdaki hangi ok {concept} için en kritik ilişkiyi gösteriyor?",
            "remediate" => $"{concept} kısmını kendi cümlenle bir örnekle açıklar mısın?",
            _ => $"{concept} için en küçük doğru örneği birlikte kurabilir miyiz?"
        };
    }

    private static string BuildPromptBlock(
        TutorActionTrace trace,
        IReadOnlyList<TutorToolPlanDto> toolPlans,
        IReadOnlyList<TeachingArtifactPlanDto> artifacts,
        TutorResponsePolicyDecision responsePolicy,
        TutorToolDecisionDto toolDecision,
        TutorLessonDeliveryDto lessonDelivery,
        RemediationLessonDto? remediationLesson) => $"""

        [TUTOR ACTION PLAN v3]
        - tutorActionTraceId: {trace.Id}
        - teachingMode: {trace.TeachingMode}
        - selectedToolAction: {toolDecision.SelectedAction}
        - lessonDeliveryMode: {lessonDelivery.DeliveryMode}
        - learnerLevel: {lessonDelivery.LearnerLevel}
        - lessonGoal: {lessonDelivery.Structure.Goal}
        - lessonSteps: {(lessonDelivery.Steps.Count == 0 ? "none" : string.Join("; ", lessonDelivery.Steps.Select(s => $"{s.StepType}:{s.UserSafeLabel}")))}
        - lessonWarnings: {(lessonDelivery.Warnings.Count == 0 ? "none" : string.Join("; ", lessonDelivery.Warnings))}
        - remediationRepairType: {remediationLesson?.RepairType ?? "none"}
        - remediationTrigger: {remediationLesson?.Trigger.TriggerType ?? "none"} / {remediationLesson?.Trigger.EvidenceStatus ?? "none"}
        - remediationGoal: {remediationLesson?.LessonShape.Goal ?? "none"}
        - remediationCheckpoint: {remediationLesson?.Checkpoint.UserSafePrompt ?? "none"}
        - toolDecisionReasons: {(toolDecision.ReasonCodes.Count == 0 ? "none" : string.Join("; ", toolDecision.ReasonCodes))}
        - blockedTools: {(toolDecision.BlockedTools.Count == 0 ? "none" : string.Join(", ", toolDecision.BlockedTools))}
        - toolSafetyWarnings: {(toolDecision.SafetyWarnings.Count == 0 ? "none" : string.Join("; ", toolDecision.SafetyWarnings))}
        - tutorResponseMode: {responsePolicy.TutorResponseMode}
        - activeConceptKey: {trace.ActiveConceptKey}
        - styleMode: {trace.StyleMode}
        - personalizationMode: {responsePolicy.PersonalizationMode}; masteryBasis: {responsePolicy.MasteryBasis}
        - weakConceptHints: {(responsePolicy.WeakConceptHints.Count == 0 ? "none" : string.Join("; ", responsePolicy.WeakConceptHints))}
        - directAnswerPolicy: {trace.DirectAnswerPolicy}
        - groundingPolicy: {trace.GroundingPolicy}
        - plannedTools: {(toolPlans.Count == 0 ? "none" : string.Join(", ", toolPlans.Select(t => $"{t.ToolId}:{t.RiskLevel}")))}
        - plannedArtifacts: {(artifacts.Count == 0 ? "none" : string.Join(", ", artifacts.Select(a => a.ArtifactType)))}
        - nextCheck: {trace.NextCheckPrompt}

        [ACTION KURALI]
        Cevap bu plana uymali. Direct-answer policy hint-first ise once ipucu ve scaffold ver; kaynak yoksa kaynak iddiasi kurma.
        Response mode concise ise kisa tut; recovery ise adim adim telafi et; evidence_limited ise kesinlik iddiasi kurma ve kaynak kontrolunu onceliklendir.
        [LESSON DELIVERY RUBRIC]
        Tek turda tek ana ogretim hamlesi uygula. Hedefi kisa kur, aciklamayi parcalara bol, gerekiyorsa cozumlu ornek ver, cevap anahtari acmadan checkpoint sor.
        Telafi modunda eksigi kesin tani gibi sunma; kaynak modunda kaynakta desteklenen kisim ile Tutor yorumunu ayir.
        [REMEDIATION LESSON RUBRIC]
        remediationRepairType none degilse kisa mikro ders, cozumlu ornek, guided practice ve cevap anahtarsiz checkpoint sirasi uygula. Bos/atlanmis cevapta yanilgi tani koyma; eksik onkosul veya guven telafisi olarak ele al.
        """;
}

public sealed class TutorToolOrchestrator : ITutorToolOrchestrator
{
    private static readonly HashSet<string> AllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "source_search", "wiki_search", "ide_last_result", "review_query", "flashcard_query",
        "wolfram_alpha", "weather", "news", "crypto", "visual_generation", "mermaid_graph",
        "knowledge_entity", "geo_context", "socioeconomic_context", "science_context", "research_context", "forum_signal"
    };

    private readonly OrkaDbContext _db;
    private readonly ITutorWorkingMemoryService _workingMemory;
    private readonly IWolframProvider _wolfram;
    private readonly INewsProvider _news;
    private readonly IWeatherProvider _weather;
    private readonly IGeocodingProvider _geocoding;
    private readonly IMarketDataProvider _marketData;
    private readonly IVisualArtifactProvider _visuals;
    private readonly IRealWorldEvidenceService? _realWorldEvidence;
    private readonly IUnifiedToolRuntimeService? _toolRuntime;

    public TutorToolOrchestrator(
        OrkaDbContext db,
        ITutorWorkingMemoryService workingMemory,
        IWolframProvider wolfram,
        INewsProvider news,
        IWeatherProvider weather,
        IGeocodingProvider geocoding,
        IMarketDataProvider marketData,
        IVisualArtifactProvider visuals,
        IRealWorldEvidenceService? realWorldEvidence = null,
        IUnifiedToolRuntimeService? toolRuntime = null)
    {
        _db = db;
        _workingMemory = workingMemory;
        _wolfram = wolfram;
        _news = news;
        _weather = weather;
        _geocoding = geocoding;
        _marketData = marketData;
        _visuals = visuals;
        _realWorldEvidence = realWorldEvidence;
        _toolRuntime = toolRuntime;
    }

    public async Task<IReadOnlyList<TutorToolCallDto>> RunAsync(
        TutorActionPlanDto actionPlan,
        TutorTurnStateDto turnState,
        CancellationToken ct = default)
    {
        var results = new List<TutorToolCallDto>();
        foreach (var plan in actionPlan.ToolPlans)
        {
            var startedAt = DateTime.UtcNow;
            if (turnState.SessionId.HasValue)
            {
                await _workingMemory.RecordStreamEventAsync(turnState.SessionId.Value, "tutor.tool.started", new Dictionary<string, string>
                {
                    ["tutorActionTraceId"] = actionPlan.Id.ToString(),
                    ["toolId"] = plan.ToolId,
                    ["required"] = plan.Required.ToString()
                }, ct);
            }

            var sw = Stopwatch.StartNew();
            ToolRuntimeDecisionDto? runtimeDecision = null;
            if (_toolRuntime != null)
            {
                runtimeDecision = await _toolRuntime.DecideAsync(turnState.UserId, new ToolRuntimeRequestDto
                {
                    ToolId = plan.ToolId,
                    Caller = "tutor",
                    TopicId = turnState.TopicId,
                    SessionId = turnState.SessionId,
                    ActiveLessonSnapshotId = turnState.ActiveLessonSnapshotId,
                    StudentContextSnapshotId = turnState.StudentContextSnapshotId,
                    TutorTurnStateId = turnState.Id,
                    TutorActionTraceId = actionPlan.Id,
                    Purpose = plan.Reason,
                    RiskLevel = plan.RiskLevel,
                    InputSummary = BuildRuntimeInputSummary(plan, turnState)
                }, ct);
            }

            var outcome = runtimeDecision is { Allowed: false }
                ? ToolExecutionOutcome.Local(
                    runtimeDecision.Decision == "degrade" ? "degraded" : "blocked",
                    false,
                    "orka_runtime",
                    null,
                    runtimeDecision.ReasonCode,
                    runtimeDecision.UserSafeReason,
                    null,
                    0)
                : await ExecuteAsync(plan, turnState, actionPlan.Id, ct);
            sw.Stop();
            var finishedAt = DateTime.UtcNow;

            var entity = new TutorToolCall
            {
                Id = Guid.NewGuid(),
                UserId = turnState.UserId,
                TopicId = turnState.TopicId,
                SessionId = turnState.SessionId,
                TutorActionTraceId = actionPlan.Id,
                ToolId = plan.ToolId,
                Provider = outcome.Provider,
                Status = outcome.Status,
                Success = outcome.Success,
                RiskLevel = plan.RiskLevel,
                Evidence = outcome.Evidence,
                FallbackReason = outcome.FallbackReason,
                ErrorCode = outcome.ErrorCode,
                SafeMessage = outcome.SafeMessage,
                Confidence = outcome.Confidence,
                SourceCount = outcome.SourceCount,
                ResultJson = JsonSerializer.Serialize(new
                {
                    schemaVersion = "orka.tutor-tool-call.v1",
                    plan.Reason,
                    plan.Required,
                    outcome.Status,
                    outcome.Success,
                    outcome.Provider,
                    outcome.Evidence,
                    outcome.FallbackReason,
                    outcome.ErrorCode,
                    outcome.SafeMessage,
                    outcome.Confidence,
                    outcome.SourceCount,
                    outcome.Citations,
                    outcome.Data
                }),
                LatencyMs = sw.ElapsedMilliseconds,
                StartedAt = startedAt,
                FinishedAt = finishedAt,
                CreatedAt = DateTime.UtcNow
            };

            _db.TutorToolCalls.Add(entity);
            if (_toolRuntime != null)
            {
                await _toolRuntime.RecordResultAsync(turnState.UserId, new ToolRuntimeResultDto
                {
                    TraceId = runtimeDecision?.TraceId,
                    ToolId = plan.ToolId,
                    Caller = "tutor",
                    TopicId = turnState.TopicId,
                    SessionId = turnState.SessionId,
                    ActiveLessonSnapshotId = turnState.ActiveLessonSnapshotId,
                    StudentContextSnapshotId = turnState.StudentContextSnapshotId,
                    TutorTurnStateId = turnState.Id,
                    TutorActionTraceId = actionPlan.Id,
                    Status = outcome.Status,
                    Success = outcome.Success,
                    SafeMessage = outcome.SafeMessage,
                    EvidenceItems = BuildRuntimeEvidence(outcome),
                    Citations = outcome.Citations,
                    FallbackReason = outcome.FallbackReason,
                    Confidence = outcome.Confidence,
                    SourceCount = outcome.SourceCount,
                    LatencyMs = sw.ElapsedMilliseconds
                }, ct);
            }

            if (IsRealWorldEvidenceTool(plan.ToolId))
            {
                var evidenceRows = await _db.TeachingEvidenceItems
                    .Where(e => e.UserId == turnState.UserId &&
                                e.TutorActionTraceId == actionPlan.Id &&
                                e.EvidenceType == plan.ToolId &&
                                e.TutorToolCallId == null)
                    .ToListAsync(ct);
                foreach (var row in evidenceRows)
                {
                    row.TutorToolCallId = entity.Id;
                }
            }

            results.Add(new TutorToolCallDto
            {
                Id = entity.Id,
                ToolId = entity.ToolId,
                Provider = entity.Provider,
                Status = entity.Status,
                Success = entity.Success,
                RiskLevel = entity.RiskLevel,
                Evidence = entity.Evidence,
                FallbackReason = entity.FallbackReason,
                ErrorCode = entity.ErrorCode,
                SafeMessage = entity.SafeMessage,
                Confidence = entity.Confidence,
                SourceCount = entity.SourceCount,
                Citations = outcome.Citations,
                LatencyMs = entity.LatencyMs,
                StartedAt = startedAt,
                FinishedAt = finishedAt
            });

            if (turnState.SessionId.HasValue)
            {
                await _workingMemory.RecordStreamEventAsync(turnState.SessionId.Value, "tutor.tool.finished", new Dictionary<string, string>
                {
                    ["tutorActionTraceId"] = actionPlan.Id.ToString(),
                    ["toolId"] = plan.ToolId,
                    ["toolCallId"] = entity.Id.ToString(),
                    ["status"] = entity.Status,
                    ["success"] = entity.Success.ToString(),
                    ["provider"] = entity.Provider
                }, ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        if (turnState.SessionId.HasValue)
        {
            await _workingMemory.RecordStreamEventAsync(turnState.SessionId.Value, "tutor.tools.evaluated", new Dictionary<string, string>
            {
                ["tutorActionTraceId"] = actionPlan.Id.ToString(),
                ["toolCallIds"] = string.Join(",", results.Select(r => r.Id)),
                ["readyCount"] = results.Count(r => r.Success && r.Status == "ready").ToString()
            }, ct);
        }

        return results;
    }

    private static string BuildRuntimeInputSummary(TutorToolPlanDto plan, TutorTurnStateDto state)
    {
        var concept = string.IsNullOrWhiteSpace(state.ActiveConceptLabel)
            ? state.ActiveConceptKey
            : state.ActiveConceptLabel;
        return $"tool={plan.ToolId}; required={plan.Required}; concept={concept}; learnerState={state.LearnerState}; sourceEvidence={state.SourceEvidenceCount}";
    }

    private static IReadOnlyList<ToolRuntimeEvidenceDto> BuildRuntimeEvidence(ToolExecutionOutcome outcome)
    {
        var evidence = new List<ToolRuntimeEvidenceDto>();
        if (!string.IsNullOrWhiteSpace(outcome.Evidence))
        {
            evidence.Add(new ToolRuntimeEvidenceDto
            {
                EvidenceType = "summary",
                Label = outcome.Evidence,
                Provider = outcome.Provider,
                Confidence = outcome.Confidence
            });
        }

        evidence.AddRange(outcome.Citations.Select(c => new ToolRuntimeEvidenceDto
        {
            EvidenceType = "citation",
            Label = c.Label,
            Url = c.Url,
            Provider = c.SourceName,
            Confidence = c.Confidence
        }));

        return evidence.Take(10).ToList();
    }

    private async Task<ToolExecutionOutcome> ExecuteAsync(TutorToolPlanDto plan, TutorTurnStateDto state, Guid tutorActionTraceId, CancellationToken ct)
    {
        if (!AllowList.Contains(plan.ToolId))
            return ToolExecutionOutcome.Local("blocked", false, "orka_policy", null, "tool_not_allowlisted", "Tool is not allowlisted.", null, 0);

        return plan.ToolId switch
        {
            "source_search" => await ExecuteSourceSearchAsync(plan, state, ct),
            "wiki_search" => await ExecuteWikiSearchAsync(plan, state, ct),
            "ide_last_result" => ExecuteIdeLastResult(plan, state),
            "review_query" => await ExecuteReviewQueryAsync(plan, state, ct),
            "flashcard_query" => await ExecuteFlashcardQueryAsync(plan, state, ct),
            "visual_generation" => ToOutcome(await _visuals.CreateVisualAsync(BuildVisualPrompt(state), "image_prompt", state.UserId, state.SessionId, state.TopicId, ct), plan),
            "mermaid_graph" => ToolExecutionOutcome.Local("ready", true, "orka_mermaid", "local_markdown_mermaid_renderer_available", null, "Mermaid renderer is available.", "mermaid_graph", 1, 0.70),
            "wolfram_alpha" => ToOutcome(await _wolfram.QueryAsync(state.UserMessage, state.UserId, state.SessionId, state.TopicId, ct), plan),
            "weather" => await ExecuteGeographyContextAsync(plan, state, ct),
            "news" => ToOutcome(await _news.SearchAsync(state.UserMessage, "tr", 5, state.UserId, state.SessionId, state.TopicId, ct), plan),
            "crypto" => ToOutcome(await _marketData.GetMarketDataAsync(ExtractCryptoAssets(state.UserMessage), state.UserId, state.SessionId, state.TopicId, ct), plan),
            "knowledge_entity" or "geo_context" or "socioeconomic_context" or "science_context" or "research_context" or "forum_signal" => await ExecuteRealWorldEvidenceAsync(plan, state, tutorActionTraceId, ct),
            _ => ToolExecutionOutcome.Local("skipped", false, "orka", null, "tool_not_implemented", "Tool has no deterministic implementation yet.", null, 0)
        };
    }

    private async Task<ToolExecutionOutcome> ExecuteRealWorldEvidenceAsync(TutorToolPlanDto plan, TutorTurnStateDto state, Guid tutorActionTraceId, CancellationToken ct)
    {
        if (_realWorldEvidence == null)
            return ToolExecutionOutcome.Local("degraded", false, "orka_evidence", null, "evidence_service_unavailable", "Real-world evidence service is not available; do not invent public API data.", null, 0);

        var query = BuildEvidenceQuery(plan.ToolId, state);
        var evidence = await _realWorldEvidence.GetEvidenceAsync(new TeachingEvidenceRequestDto
        {
            UserId = state.UserId,
            TopicId = state.TopicId,
            SessionId = state.SessionId,
            TutorTurnStateId = state.Id,
            TutorActionTraceId = tutorActionTraceId,
            EvidenceType = plan.ToolId,
            Query = query,
            ConceptKey = state.ActiveConceptKey,
            UserMessage = state.UserMessage
        }, ct);

        var status = NormalizeStatus(evidence.Status, evidence.Success, plan.Required);
        return new ToolExecutionOutcome(
            status,
            evidence.Success && status == "ready",
            evidence.Provider,
            evidence.Success ? $"{evidence.EvidenceType}_cards_ready:{evidence.SourceCount}" : null,
            evidence.FallbackUsed ? evidence.ErrorCode ?? "evidence_fallback" : null,
            evidence.SafeMessage,
            evidence.ErrorCode,
            evidence.Confidence,
            evidence.SourceCount,
            evidence.Cards,
            evidence.Cards
                .Where(c => !string.IsNullOrWhiteSpace(c.CitationLabel) || !string.IsNullOrWhiteSpace(c.CitationUrl))
                .Select(c => new ProviderCitationDto(c.CitationLabel, c.CitationUrl, c.Provider, c.CreatedAt.UtcDateTime, c.Confidence))
                .ToList());
    }

    private static bool IsRealWorldEvidenceTool(string toolId) =>
        toolId is "knowledge_entity" or "geo_context" or "socioeconomic_context" or "science_context" or "research_context" or "forum_signal";

    private static string BuildEvidenceQuery(string toolId, TutorTurnStateDto state)
    {
        if (toolId is "geo_context" or "socioeconomic_context")
        {
            var place = ExtractEvidencePlace(state.UserMessage);
            if (!string.IsNullOrWhiteSpace(place)) return place;

            var location = ExtractGeographyLocation(state.UserMessage);
            if (!string.IsNullOrWhiteSpace(location)) return location;
        }

        var concept = !string.IsNullOrWhiteSpace(state.ActiveConceptLabel)
            ? state.ActiveConceptLabel
            : state.ActiveConceptKey;
        if (!string.IsNullOrWhiteSpace(concept) && !concept.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            return concept.Trim();

        var message = state.UserMessage ?? string.Empty;
        return message.Length <= 120 ? message.Trim() : message[..120].Trim();
    }

    private static string? ExtractEvidencePlace(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var folded = FoldTurkish(message.ToLowerInvariant());
        if (folded.Contains("turkiye", StringComparison.OrdinalIgnoreCase) || folded.Contains("turkey", StringComparison.OrdinalIgnoreCase))
            return "Turkey";

        var clean = message;
        foreach (var token in new[] { "nüfus", "nufus", "coğrafya", "cografya", "coğrafi", "cografi", "bilgisi", "verisi", "gerçek", "gercek", "verilerle", "anlat", "iklim", "konum", "ülke", "ulke", "harita" })
        {
            clean = clean.Replace(token, " ", StringComparison.OrdinalIgnoreCase);
        }

        clean = clean.Replace("?", " ", StringComparison.Ordinal)
            .Replace(",", " ", StringComparison.Ordinal)
            .Replace(".", " ", StringComparison.Ordinal)
            .Replace(" ve ", " ", StringComparison.OrdinalIgnoreCase)
            .Trim();
        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 2)
            .Take(3)
            .ToList();
        return words.Count == 0 ? null : string.Join(' ', words);
    }

    private async Task<ToolExecutionOutcome> ExecuteSourceSearchAsync(TutorToolPlanDto plan, TutorTurnStateDto state, CancellationToken ct)
    {
        var sources = await _db.LearningSources
            .AsNoTracking()
            .Where(s => s.UserId == state.UserId && s.TopicId == state.TopicId && !s.IsDeleted && s.Status == "ready")
            .OrderByDescending(s => s.UpdatedAt)
            .Take(5)
            .Select(s => new { s.Id, s.Title, s.SourceType, s.ChunkCount })
            .ToListAsync(ct);

        if (sources.Count == 0 && state.SourceEvidenceCount == 0 && !state.HasNotebookContext)
            return ToolExecutionOutcome.Local(plan.Required ? "needs_input" : "skipped", false, "orka_sql", null, "no_source_context_available", "No notebook/source context is available; do not claim source grounding.", null, 0);

        var evidence = sources.Count == 0 ? "turn_state_source_context_available" : $"{sources.Count} learning sources available";
        return ToolExecutionOutcome.Local("ready", true, "orka_sql", evidence, null, "Source context is available.", sources, Math.Max(sources.Count, state.SourceEvidenceCount), 0.75);
    }

    private async Task<ToolExecutionOutcome> ExecuteWikiSearchAsync(TutorToolPlanDto plan, TutorTurnStateDto state, CancellationToken ct)
    {
        var pages = state.TopicId.HasValue
            ? await _db.WikiPages
                .AsNoTracking()
                .Where(w => w.UserId == state.UserId && w.TopicId == state.TopicId.Value)
                .OrderBy(w => w.OrderIndex)
                .Take(5)
                .Select(w => new { w.Id, w.Title, snippet = w.Content == null ? "" : w.Content.Substring(0, Math.Min(w.Content.Length, 240)) })
                .ToListAsync(ct)
            : await _db.WikiPages
                .AsNoTracking()
                .Where(w => false)
                .Select(w => new { w.Id, w.Title, snippet = string.Empty })
                .ToListAsync(ct);

        if (pages.Count == 0 && !state.HasWikiContext)
            return ToolExecutionOutcome.Local(plan.Required ? "needs_input" : "skipped", false, "orka_wiki", null, "no_wiki_context_available", "No wiki context is available.", null, 0);

        return ToolExecutionOutcome.Local("ready", true, "orka_wiki", pages.Count == 0 ? "turn_state_wiki_context_available" : $"{pages.Count} wiki pages available", null, "Wiki context is available.", pages, Math.Max(1, pages.Count), 0.72);
    }

    private static ToolExecutionOutcome ExecuteIdeLastResult(TutorToolPlanDto plan, TutorTurnStateDto state)
    {
        if (!state.HasIdeContext)
            return ToolExecutionOutcome.Local(plan.Required ? "needs_input" : "skipped", false, "orka_ide", null, "no_ide_result_available", "No IDE result is available for this turn.", null, 0);

        return ToolExecutionOutcome.Local("ready", true, "orka_ide", "ide_last_result_available", null, "Latest IDE/Piston result is present in turn state.", new { state.HasIdeContext }, 1, 0.70);
    }

    private async Task<ToolExecutionOutcome> ExecuteReviewQueryAsync(TutorToolPlanDto plan, TutorTurnStateDto state, CancellationToken ct)
    {
        var due = await _db.ReviewItems
            .AsNoTracking()
            .Where(r => r.UserId == state.UserId && r.TopicId == state.TopicId && r.Status == "active")
            .OrderBy(r => r.DueAt)
            .Take(5)
            .Select(r => new { r.Id, r.ReviewKey, r.SkillTag, r.ConceptTag, r.DueAt, r.LapseCount })
            .ToListAsync(ct);

        return due.Count == 0
            ? ToolExecutionOutcome.Local("skipped", false, "orka_srs", null, "no_review_items_available", "No active review item was found.", null, 0)
            : ToolExecutionOutcome.Local("ready", true, "orka_srs", $"{due.Count} review items available", null, "Review/SRS context is available.", due, due.Count, 0.70);
    }

    private async Task<ToolExecutionOutcome> ExecuteFlashcardQueryAsync(TutorToolPlanDto plan, TutorTurnStateDto state, CancellationToken ct)
    {
        var cards = await _db.Flashcards
            .AsNoTracking()
            .Where(f => f.UserId == state.UserId && f.TopicId == state.TopicId && f.Status == "active")
            .OrderByDescending(f => f.UpdatedAt)
            .Take(5)
            .Select(f => new { f.Id, f.Front, f.Hint, f.SkillTag, f.ConceptTag })
            .ToListAsync(ct);

        return cards.Count == 0
            ? ToolExecutionOutcome.Local("skipped", false, "orka_flashcards", null, "no_flashcards_available", "No flashcards were found for this topic.", null, 0)
            : ToolExecutionOutcome.Local("ready", true, "orka_flashcards", $"{cards.Count} flashcards available", null, "Flashcard context is available.", cards, cards.Count, 0.70);
    }

    private async Task<ToolExecutionOutcome> ExecuteGeographyContextAsync(TutorToolPlanDto plan, TutorTurnStateDto state, CancellationToken ct)
    {
        var location = ExtractGeographyLocation(state.UserMessage);
        if (string.IsNullOrWhiteSpace(location))
            return ToolExecutionOutcome.Local("needs_input", false, "open_meteo", null, "missing_location", "Geography context requires a city, region, country, or coordinate; ask the learner for it.", null, 0);

        var geocode = await _geocoding.GeocodeAsync(location, state.UserId, state.SessionId, state.TopicId, ct);
        if (!geocode.Success || geocode.Data is not GeocodingResultDto geo)
            return ToOutcome(geocode, plan);

        var geography = await _weather.GetGeographyContextAsync(geo.Latitude, geo.Longitude, geo.Location, state.UserId, state.SessionId, state.TopicId, ct);
        return ToOutcome(geography, plan);
    }

    private static ToolExecutionOutcome ToOutcome(ProviderToolResultDto result, TutorToolPlanDto plan)
    {
        var status = NormalizeStatus(result.Status, result.Success, plan.Required);
        return new ToolExecutionOutcome(
            status,
            result.Success && status == "ready",
            result.Provider,
            result.Success ? $"{result.ToolId}_provider_result_ready" : null,
            result.FallbackUsed ? result.ErrorCode ?? "provider_fallback" : null,
            result.SafeMessage,
            result.ErrorCode,
            result.Confidence,
            result.SourceCount,
            result.Data,
            result.Citations);
    }

    private static string NormalizeStatus(string status, bool success, bool required)
    {
        if (success) return "ready";
        if (string.Equals(status, "timeout", StringComparison.OrdinalIgnoreCase)) return "timeout";
        if (string.Equals(status, "needs_input", StringComparison.OrdinalIgnoreCase)) return "needs_input";
        if (string.Equals(status, "blocked", StringComparison.OrdinalIgnoreCase)) return "blocked";
        if (required) return "degraded";
        return status is "disabled" or "empty" or "malformed" ? "degraded" : "skipped";
    }

    private static string ExtractCryptoAssets(string message)
    {
        var normalized = TutorSignalHeuristics.Normalize(message);
        var assets = new List<string>();
        if (TutorSignalHeuristics.ContainsAny(normalized, "bitcoin", "btc")) assets.Add("bitcoin");
        if (TutorSignalHeuristics.ContainsAny(normalized, "ethereum", "eth")) assets.Add("ethereum");
        if (TutorSignalHeuristics.ContainsAny(normalized, "solana", "sol")) assets.Add("solana");
        if (TutorSignalHeuristics.ContainsAny(normalized, "doge", "dogecoin")) assets.Add("dogecoin");
        return assets.Count == 0 ? "bitcoin,ethereum" : string.Join(",", assets.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string? ExtractGeographyLocation(string message)
    {
        var text = (message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = FoldTurkish(TutorSignalHeuristics.Normalize(text));
        foreach (var marker in new[] { "hava durumu", "weather", "cografi konumu", "coğrafi konumu", "cografya", "coğrafya", "iklimi", "iklim", "haritasi", "haritası", "enlemi", "enlem", "boylami", "boylamı", "boylam", "koordinati", "koordinatı", "koordinat", "nerede", "konumu", "konum" })
        {
            var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var before = text[..idx].Trim(' ', '?', ',', '.', ':');
                var after = text[(idx + marker.Length)..].Trim(' ', '?', ',', '.', ':');
                var candidate = !string.IsNullOrWhiteSpace(before) ? before : after;
                candidate = candidate
                    .Replace("bugun", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("bugün", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("yarin", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("yarın", "", StringComparison.OrdinalIgnoreCase)
                    .Trim(' ', '?', ',', '.', ':');
                return candidate.Length >= 2 ? candidate : null;
            }
        }

        return null;
    }

    private static string FoldTurkish(string value) => value
        .Replace('ı', 'i')
        .Replace('ğ', 'g')
        .Replace('ü', 'u')
        .Replace('ş', 's')
        .Replace('ö', 'o')
        .Replace('ç', 'c');

    private static string BuildVisualPrompt(TutorTurnStateDto state)
    {
        var concept = string.IsNullOrWhiteSpace(state.ActiveConceptLabel)
            ? (string.IsNullOrWhiteSpace(state.ActiveConceptKey) ? "educational concept" : state.ActiveConceptKey)
            : state.ActiveConceptLabel;
        return $"Clean educational visual diagram for {concept}; labels; high contrast; no decorative background; show the main relationship clearly.";
    }

    private sealed record ToolExecutionOutcome(
        string Status,
        bool Success,
        string Provider,
        string? Evidence,
        string? FallbackReason,
        string SafeMessage,
        string? ErrorCode,
        double? Confidence,
        int? SourceCount,
        object? Data,
        IReadOnlyList<ProviderCitationDto> Citations)
    {
        public static ToolExecutionOutcome Local(string status, bool success, string provider, string? evidence, string? fallbackReason, string safeMessage, object? data, int? sourceCount, double? confidence = null) =>
            new(status, success, provider, evidence, fallbackReason, safeMessage, fallbackReason, confidence, sourceCount, data, []);
    }
}

public sealed class TeachingArtifactService : ITeachingArtifactService
{
    private readonly OrkaDbContext _db;
    private readonly ITutorWorkingMemoryService _workingMemory;
    private readonly IVisualArtifactProvider _visuals;
    private readonly ILearningArtifactService _learningArtifacts;

    public TeachingArtifactService(
        OrkaDbContext db,
        ITutorWorkingMemoryService workingMemory,
        IVisualArtifactProvider visuals,
        ILearningArtifactService learningArtifacts)
    {
        _db = db;
        _workingMemory = workingMemory;
        _visuals = visuals;
        _learningArtifacts = learningArtifacts;
    }

    public async Task<IReadOnlyList<TeachingArtifactDto>> BuildArtifactsAsync(
        TutorActionPlanDto actionPlan,
        TutorTurnStateDto turnState,
        CancellationToken ct = default)
    {
        var artifacts = new List<TeachingArtifactDto>();
        var entities = new List<TeachingArtifact>();
        foreach (var plan in actionPlan.ArtifactPlans)
        {
            var artifact = await BuildAsync(plan.ArtifactType, actionPlan, turnState, ct);
            var entity = new TeachingArtifact
            {
                Id = artifact.Id,
                UserId = turnState.UserId,
                TopicId = turnState.TopicId,
                SessionId = turnState.SessionId,
                TutorActionTraceId = actionPlan.Id,
                ArtifactType = artifact.ArtifactType,
                Title = artifact.Title,
                Content = artifact.Content,
                RenderFormat = artifact.RenderFormat,
                Status = artifact.Status,
                Provider = artifact.Provider,
                ExternalUrl = artifact.ExternalUrl,
                RenderError = artifact.RenderError,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    schemaVersion = "orka.teaching-artifact.v1",
                    actionPlanId = actionPlan.Id,
                    plan.Reason,
                    artifact.Provider,
                    artifact.ExternalUrl,
                    artifact.RenderError
                }),
                RenderedAt = artifact.RenderedAt?.UtcDateTime,
                CreatedAt = DateTime.UtcNow
            };

            _db.TeachingArtifacts.Add(entity);
            entities.Add(entity);
            artifacts.Add(artifact);
        }

        await _db.SaveChangesAsync(ct);

        foreach (var entity in entities)
        {
            await _learningArtifacts.MirrorTeachingArtifactAsync(
                turnState.UserId,
                entity,
                turnState,
                actionPlan,
                "tutor",
                ct);
        }

        if (turnState.SessionId.HasValue)
        {
            await _workingMemory.RecordStreamEventAsync(turnState.SessionId.Value, "tutor.artifacts.ready", new Dictionary<string, string>
            {
                ["tutorActionTraceId"] = actionPlan.Id.ToString(),
                ["artifactIds"] = string.Join(",", artifacts.Select(a => a.Id)),
                ["artifactTypes"] = string.Join(",", artifacts.Select(a => a.ArtifactType))
            }, ct);
        }

        return artifacts;
    }

    public async Task MarkRenderedAsync(Guid artifactId, Guid userId, string? renderError = null, CancellationToken ct = default)
    {
        var artifact = await _db.TeachingArtifacts
            .FirstOrDefaultAsync(a => a.Id == artifactId && a.UserId == userId, ct);
        if (artifact == null) return;

        artifact.RenderedAt = DateTime.UtcNow;
        artifact.RenderError = string.IsNullOrWhiteSpace(renderError) ? artifact.RenderError : renderError;
        artifact.Status = string.IsNullOrWhiteSpace(renderError) ? "rendered" : "render_degraded";
        await _db.SaveChangesAsync(ct);
    }

    private async Task<TeachingArtifactDto> BuildAsync(string artifactType, TutorActionPlanDto actionPlan, TutorTurnStateDto state, CancellationToken ct)
    {
        var concept = string.IsNullOrWhiteSpace(state.ActiveConceptLabel)
            ? (string.IsNullOrWhiteSpace(state.ActiveConceptKey) ? "aktif kavram" : state.ActiveConceptKey)
            : state.ActiveConceptLabel;

        var id = Guid.NewGuid();
        if (IsEvidenceArtifact(artifactType))
        {
            return await BuildEvidenceArtifactAsync(id, artifactType, actionPlan, state, concept, ct);
        }

        var (title, content, renderFormat) = artifactType switch
        {
            "mermaid_graph" => ($"{concept} kavram haritası", BuildMermaid(concept), "mermaid"),
            "timeline" => ($"{concept} zaman çizgisi", BuildTimeline(concept), "markdown"),
            "comparison_table" => ($"{concept} karşılaştırma tablosu", BuildComparison(concept), "markdown"),
            "code_lab_task" => ($"{concept} kod laboratuvarı", BuildCodeLab(concept), "markdown"),
            "retrieval_card" => ($"{concept} kaynak kartı", BuildRetrievalCard(state), "markdown"),
            "micro_quiz" => ($"{concept} mikro kontrol", $"- {actionPlan.NextCheckPrompt}", "markdown"),
            "image_prompt" => ($"{concept} gorsel anlatim", BuildImageFallback(concept), "image"),
            _ => ($"{concept} çözümlü örnek", BuildWorkedExample(concept), "markdown")
        };

        var provider = "orka";
        string? externalUrl = null;
        string? renderError = null;
        var status = "ready";

        if (artifactType.Equals("image_prompt", StringComparison.OrdinalIgnoreCase))
        {
            var visual = await _visuals.CreateVisualAsync(
                $"Educational diagram for {concept}. Clear labels, high contrast, no decorative background.",
                artifactType,
                state.UserId,
                state.SessionId,
                state.TopicId,
                ct);
            provider = visual.Provider;

            if (visual.Success && visual.Data is VisualArtifactResultDto visualData)
            {
                externalUrl = visualData.ExternalUrl;
                content = string.IsNullOrWhiteSpace(externalUrl)
                    ? visualData.FallbackText
                    : $"![{title}]({externalUrl})\n\n{visualData.FallbackText}";
            }
            else
            {
                status = "degraded";
                renderError = visual.ErrorCode ?? "visual_provider_unavailable";
                content = $"{content}\n\nProvider durumu: {visual.SafeMessage}";
            }
        }

        return new TeachingArtifactDto
        {
            Id = id,
            UserId = state.UserId,
            TopicId = state.TopicId,
            SessionId = state.SessionId,
            TutorActionTraceId = actionPlan.Id,
            ArtifactType = artifactType,
            Title = title,
            Content = content,
            RenderFormat = renderFormat,
            Status = status,
            Provider = provider,
            ExternalUrl = externalUrl,
            RenderError = renderError,
            PromptBlock = $"""

                [TEACHING ARTIFACT v3]
                - artifactId: {id}
                - artifactType: {artifactType}
                - title: {title}
                Cevapta uygunsa bu artifact'i aynen kullan veya daha iyi bir mikro uyarlama ile dahil et:
                {content}
            """,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<TeachingArtifactDto> BuildEvidenceArtifactAsync(Guid id, string artifactType, TutorActionPlanDto actionPlan, TutorTurnStateDto state, string concept, CancellationToken ct)
    {
        var evidenceType = artifactType switch
        {
            "forum_pattern" => "forum_signal",
            "map_context" => "geo_context",
            "science_fact_card" => "science_context",
            "research_reading_card" => "research_context",
            "real_world_graph" => "socioeconomic_context",
            _ => "knowledge_entity"
        };

        var cards = await _db.TeachingEvidenceItems
            .AsNoTracking()
            .Where(e => e.UserId == state.UserId &&
                        e.TutorActionTraceId == actionPlan.Id &&
                        e.EvidenceType == evidenceType &&
                        e.Status == "ready")
            .OrderByDescending(e => e.Confidence)
            .ThenByDescending(e => e.CreatedAt)
            .Take(3)
            .ToListAsync(ct);

        var title = artifactType switch
        {
            "forum_pattern" => $"{concept} yaygin hata sinyali",
            "map_context" => $"{concept} harita ve konum baglami",
            "science_fact_card" => $"{concept} bilimsel veri karti",
            "research_reading_card" => $"{concept} ileri okuma karti",
            "real_world_graph" => $"{concept} gercek veri tablosu",
            _ => $"{concept} kanit karti"
        };

        var status = cards.Count == 0 ? "degraded" : "ready";
        var renderError = cards.Count == 0 ? "evidence_unavailable" : null;
        var content = cards.Count == 0
            ? $"**Kanıt kartı hazırlanamadı.** {concept} için güvenli public API sonucu bulunamadı; Tutor veri uydurmadan genel benzetmeyle devam etmeli."
            : BuildEvidenceMarkdown(artifactType, cards);

        return new TeachingArtifactDto
        {
            Id = id,
            UserId = state.UserId,
            TopicId = state.TopicId,
            SessionId = state.SessionId,
            TutorActionTraceId = actionPlan.Id,
            ArtifactType = artifactType,
            Title = title,
            Content = content,
            RenderFormat = "markdown",
            Status = status,
            Provider = cards.Count == 0 ? "orka_evidence" : string.Join(",", cards.Select(c => c.Provider).Distinct(StringComparer.OrdinalIgnoreCase)),
            RenderError = renderError,
            PromptBlock = $"""

                [REAL WORLD TEACHING EVIDENCE v1]
                - artifactId: {id}
                - artifactType: {artifactType}
                - evidenceType: {evidenceType}
                [KURAL] Bu kartlar ders anlatimini somutlastirir. Forum sinyali hakikat kaynagi degildir. Kaynakli iddia kurarsan citation ayrimini koru.
                {content}
            """,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static bool IsEvidenceArtifact(string artifactType) =>
        artifactType is "evidence_card" or "real_world_graph" or "forum_pattern" or "map_context" or "science_fact_card" or "research_reading_card";

    private static string BuildEvidenceMarkdown(string artifactType, IReadOnlyList<TeachingEvidenceItem> cards)
    {
        if (artifactType == "real_world_graph")
        {
            return $"""
                | Kaynak | Baslik | Kanit | Kullanim |
                |---|---|---|---|
                {string.Join("\n", cards.Select(c => $"| {EscapeTable(c.Provider)} | {EscapeTable(c.Title)} | {EscapeTable(TrimForTable(c.FactualClaim))} | {EscapeTable(TrimForTable(c.ClassroomUse))} |"))}

                Bu tablo grafik fikri icindir; sayisal veri varsa Tutor bunu oran/grafik diliyle aciklamali.
                """;
        }

        var label = artifactType == "forum_pattern"
            ? "**Forum sinyali:** Bu kart dogru bilgi kaynagi degil, yaygin zorlanma oruntusudur."
            : "**Gercek hayat kaniti:** Public/veri kaynagi ders ornegini somutlastirir.";

        return $"""
            {label}

            {string.Join("\n\n", cards.Select(c => $"""
            **{c.Title}**  
            - Kaynak: {c.Provider} ({c.CitationLabel})  
            - Ozet: {c.Summary}  
            - Ders kullanimi: {c.ClassroomUse}  
            - Benzetme/pekiştirme: {c.AnalogyCandidate}  
            - Guven: %{Math.Round(c.Confidence * 100)}
            {(string.IsNullOrWhiteSpace(c.CitationUrl) ? "" : $"- Link: {c.CitationUrl}")}
            """))}
            """;
    }

    private static string EscapeTable(string value) =>
        (value ?? string.Empty).Replace("|", "/", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string TrimForTable(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length <= 160 ? value ?? string.Empty : value[..160] + "...";

    private static string BuildMermaid(string concept) => $"""
        ```mermaid
        flowchart LR
          A["{EscapeMermaid(concept)}"] --> B["Neden önemli?"]
          B --> C["Somut örnek"]
          C --> D["Mikro kontrol"]
          A --> E["Sık hata"]
          E --> C
        ```
        """;

    private static string BuildTimeline(string concept) => $"""
        | Sıra | Odak | Öğrenme görevi |
        |---|---|---|
        | 1 | Bağlam | {concept} nerede ortaya çıkıyor? |
        | 2 | Ana ilişki | Sebep-sonuç bağını kur |
        | 3 | Sınav dili | Bir kısa örnekle ayırt et |
        """;

    private static string BuildComparison(string concept) => $"""
        | Boyut | {concept} | Karıştırılan nokta |
        |---|---|---|
        | Tanım | Ana fikri tek cümlede kur | Ezber ifade |
        | İşaret | Ayırt eden ipucunu bul | Benzer terim |
        | Uygulama | Bir örnekte kullan | Sadece tanım okumak |
        """;

    private static string BuildWorkedExample(string concept) => $"""
        **Çözümlü mini örnek:** {concept} için önce tanımı değil, küçük bir durum seç.  
        1. Verilen ipucunu bul.  
        2. Bu ipucunu kavramla eşleştir.  
        3. Son cümlede neden doğru olduğunu söyle.
        """;

    private static string BuildCodeLab(string concept) => $"""
        **Kod laboratuvarı:** {concept} için küçük bir test yaz.  
        - Girdi: en basit örnek  
        - Beklenen çıktı: önce tahmin  
        - Kontrol: IDE çıktısı tahminle uyuşuyor mu?
        """;

    private static string BuildImageFallback(string concept) => $"""
        **Gorsel anlatim fallback:** {concept} icin sade bir diagram dusun.  
        - Merkez: ana kavram  
        - Sol: on kosul veya neden  
        - Sag: sonuc veya uygulama  
        - Alt: sik hata ve dogru kontrol
        """;

    private static string BuildRetrievalCard(TutorTurnStateDto state) => $"""
        **Kaynak zemini:** Bu cevapta {state.SourceEvidenceCount} kaynak sinyali var.  
        Kaynaklardan açık kanıt yoksa iddia yerine "model bilgisiyle" ayrımı korunmalı.
        """;

    private static string EscapeMermaid(string value) =>
        value.Replace("\"", "'", StringComparison.Ordinal).Replace("[", "(", StringComparison.Ordinal).Replace("]", ")", StringComparison.Ordinal);
}

public sealed class TutorReflectionService : ITutorReflectionService
{
    private readonly OrkaDbContext _db;
    private readonly ILearningEventSchemaService _schema;
    private readonly ITutorWorkingMemoryService _workingMemory;

    public TutorReflectionService(
        OrkaDbContext db,
        ILearningEventSchemaService schema,
        ITutorWorkingMemoryService workingMemory)
    {
        _db = db;
        _schema = schema;
        _workingMemory = workingMemory;
    }

    public async Task<TutorReflectionUpdateDto> ReflectAsync(
        TutorTurnStateDto turnState,
        TutorActionPlanDto actionPlan,
        string assistantAnswer,
        IReadOnlyList<TeachingArtifactDto> artifacts,
        CancellationToken ct = default)
    {
        var normalized = TutorSignalHeuristics.Normalize(assistantAnswer);
        var sourceClaimWithoutSource = turnState.SourceEvidenceCount == 0 &&
            TutorSignalHeuristics.ContainsAny(normalized, "kaynağa göre", "kaynaklara göre", "dokümana göre", "wikiye göre", "belgeye göre");
        var microCheckAsked = assistantAnswer.Contains('?') || normalized.Contains("özetler misin", StringComparison.OrdinalIgnoreCase);
        var artifactRendered = artifacts.Count > 0 ||
            assistantAnswer.Contains("```mermaid", StringComparison.OrdinalIgnoreCase) ||
            assistantAnswer.Contains("|---", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("mikro kontrol", StringComparison.OrdinalIgnoreCase);
        var directAnswerHandled = !turnState.DirectAnswerRisk ||
            normalized.Contains("ipucu", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("adım", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("önce", StringComparison.OrdinalIgnoreCase);

        var entity = new TutorReflectionUpdate
        {
            Id = Guid.NewGuid(),
            UserId = turnState.UserId,
            TopicId = turnState.TopicId,
            SessionId = turnState.SessionId,
            TutorActionTraceId = actionPlan.Id,
            TutorTurnStateId = turnState.Id,
            PolicyApplied = !string.IsNullOrWhiteSpace(actionPlan.TeachingMode),
            SourceClaimWithoutSource = sourceClaimWithoutSource,
            DirectAnswerRiskHandled = directAnswerHandled,
            ArtifactRendered = artifactRendered,
            MicroCheckAsked = microCheckAsked,
            ReflectionJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.tutor-reflection.v1",
                actionPlan.TeachingMode,
                actionPlan.DirectAnswerPolicy,
                actionPlan.GroundingPolicy,
                artifacts = artifacts.Select(a => new { a.Id, a.ArtifactType }).ToArray()
            }),
            CreatedAt = DateTime.UtcNow
        };

        _db.TutorReflectionUpdates.Add(entity);

        if (sourceClaimWithoutSource)
        {
            _db.TutorPolicyViolationsV2.Add(new TutorPolicyViolationV2
            {
                Id = Guid.NewGuid(),
                UserId = turnState.UserId,
                TopicId = turnState.TopicId,
                SessionId = turnState.SessionId,
                TutorActionTraceId = actionPlan.Id,
                ViolationType = "source_claim_without_source",
                Severity = "warning",
                Evidence = "Assistant referenced sources while turn state had zero source evidence.",
                CreatedAt = DateTime.UtcNow
            });
        }

        if (string.IsNullOrWhiteSpace(turnState.ActiveConceptKey))
        {
            _db.TutorPolicyViolationsV2.Add(new TutorPolicyViolationV2
            {
                Id = Guid.NewGuid(),
                UserId = turnState.UserId,
                TopicId = turnState.TopicId,
                SessionId = turnState.SessionId,
                TutorActionTraceId = actionPlan.Id,
                ViolationType = "no_active_concept",
                Severity = "info",
                Evidence = "Tutor turn state did not resolve an active concept.",
                CreatedAt = DateTime.UtcNow
            });
        }

        var learningEvent = new LearningEvent
        {
            Id = Guid.NewGuid(),
            UserId = turnState.UserId,
            TopicId = turnState.TopicId,
            SessionId = turnState.SessionId,
            EventType = "tutor.policy.applied",
            Actor = "tutor",
            Verb = "applied",
            ObjectType = "tutor_action_trace",
            ObjectId = actionPlan.Id.ToString(),
            ConceptKey = turnState.ActiveConceptKey,
            IsPositive = !sourceClaimWithoutSource && directAnswerHandled,
            PayloadJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.learning-event.v2",
                tutorTurnStateId = turnState.Id,
                tutorActionTraceId = actionPlan.Id,
                actionPlan.TeachingMode,
                actionPlan.StyleMode,
                actionPlan.NextCheckPrompt
            }),
            OccurredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _db.LearningEvents.Add(learningEvent);

        await _db.SaveChangesAsync(ct);
        await _schema.ValidateAndLogAsync(learningEvent, ct);

        if (turnState.SessionId.HasValue)
        {
            await _workingMemory.RecordStreamEventAsync(turnState.SessionId.Value, "tutor.reflection.saved", new Dictionary<string, string>
            {
                ["reflectionId"] = entity.Id.ToString(),
                ["sourceClaimWithoutSource"] = sourceClaimWithoutSource.ToString(),
                ["directAnswerRiskHandled"] = directAnswerHandled.ToString(),
                ["artifactRendered"] = artifactRendered.ToString(),
                ["microCheckAsked"] = microCheckAsked.ToString()
            }, ct);
        }

        return new TutorReflectionUpdateDto
        {
            Id = entity.Id,
            TutorActionTraceId = entity.TutorActionTraceId,
            TutorTurnStateId = entity.TutorTurnStateId,
            PolicyApplied = entity.PolicyApplied,
            SourceClaimWithoutSource = entity.SourceClaimWithoutSource,
            DirectAnswerRiskHandled = entity.DirectAnswerRiskHandled,
            ArtifactRendered = entity.ArtifactRendered,
            MicroCheckAsked = entity.MicroCheckAsked,
            CreatedAt = entity.CreatedAt
        };
    }
}

internal static class TutorSignalHeuristics
{
    public static string Normalize(string text) => (text ?? string.Empty).Trim().ToLowerInvariant();

    public static (string StyleMode, decimal Confidence, decimal Weight) DetectStyle(string normalized)
    {
        if (ContainsAny(normalized, "görsel", "gorsel", "çiz", "ciz", "grafik", "diagram", "şema", "resim")) return ("visual", 0.72m, 0.85m);
        if (ContainsAny(normalized, "adım adım", "tek tek", "yavaş", "yavas")) return ("step_by_step", 0.70m, 0.80m);
        if (ContainsAny(normalized, "örnek", "ornek", "misal")) return ("example_first", 0.68m, 0.75m);
        if (ContainsAny(normalized, "kod", "java", "sql", "python", "algoritma")) return ("code_first", 0.66m, 0.75m);
        if (ContainsAny(normalized, "soru sor", "beni sınav", "test et", "socratic")) return ("socratic", 0.64m, 0.70m);
        if (ContainsAny(normalized, "zorla", "challenge", "meydan", "ileri seviye")) return ("challenge_first", 0.63m, 0.70m);
        if (ContainsAny(normalized, "tekrar", "unut", "review")) return ("review_first", 0.62m, 0.70m);
        return ("step_by_step", 0.35m, 0.40m);
    }

    public static (string State, decimal Confidence) DetectAffectiveState(string normalized)
    {
        if (ContainsAny(normalized, "anlamadım", "anlamadim", "karıştı", "karisti", "kafam karıştı", "bilmiyorum")) return ("confused", 0.78m);
        if (ContainsAny(normalized, "sinir", "bıktım", "biktim", "olmuyor", "yapamıyorum", "yapamiyorum")) return ("frustrated", 0.74m);
        if (ContainsAny(normalized, "sıkıldım", "sikildim", "sıkıcı", "sikici")) return ("bored", 0.70m);
        if (ContainsAny(normalized, "acelem", "hızlı", "hizli", "kısa kes", "kisa kes")) return ("rushed", 0.68m);
        if (ContainsAny(normalized, "merak", "ilginç", "ilginc", "neden")) return ("curious", 0.58m);
        if (ContainsAny(normalized, "anladım", "anladim", "kolay", "tamamdır", "tamamdir")) return ("confident", 0.56m);
        return ("neutral", 0.35m);
    }

    public static (string Load, decimal Confidence) DetectCognitiveLoad(string normalized, string learningSignalContext, string ideContext)
    {
        if (ContainsAny(normalized, "çok karışık", "cok karisik", "fazla geldi", "yavaş", "yavas", "basitleştir", "basitlestir")) return ("high", 0.76m);
        if (!string.IsNullOrWhiteSpace(ideContext) && ContainsAny(ideContext.ToLowerInvariant(), "error", "exception", "syntax", "runtime")) return ("high", 0.68m);
        if (!string.IsNullOrWhiteSpace(learningSignalContext) && ContainsAny(learningSignalContext.ToLowerInvariant(), "zayif", "zorlanma", "yanlis")) return ("elevated", 0.58m);
        if (ContainsAny(normalized, "hızlı geç", "hizli gec", "bunu biliyorum", "ileri")) return ("low", 0.56m);
        return ("normal", 0.35m);
    }

    public static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    public static decimal Clamp01(decimal value) => Math.Min(1m, Math.Max(0m, value));

    public static string Trim(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}

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
            _logger.LogWarning(ex, "[TutorWorkingMemory] Redis write degraded. User={UserId} Topic={TopicId}", state.UserId, state.TopicId);
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
    private readonly OrkaDbContext _db;
    private readonly ILearnerProfileService _learnerProfile;
    private readonly ITutorWorkingMemoryService _workingMemory;

    public TutorTurnStateAssembler(
        OrkaDbContext db,
        ILearnerProfileService learnerProfile,
        ITutorWorkingMemoryService workingMemory)
    {
        _db = db;
        _learnerProfile = learnerProfile;
        _workingMemory = workingMemory;
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

        var activeKey = FirstNonEmpty(
            policyContext.ActiveConceptKey,
            ktStates.FirstOrDefault()?.ConceptKey,
            masteries.FirstOrDefault()?.ConceptKey,
            firstGraphConceptKey);

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
            activeKey);

        var activeKt = ktStates.FirstOrDefault(s => s.ConceptKey == activeKey);
        var activeMastery = masteries.FirstOrDefault(m => m.ConceptKey == activeKey);
        var profile = await _learnerProfile.BuildOrUpdateAsync(userId, topicId, sessionId, userMessage, learningSignalContext, ideContext, ct);
        var masteryProbability = activeKt?.MasteryProbability ?? (activeMastery?.MasteryScore / 100m);
        var confidence = activeKt?.Confidence ?? activeMastery?.Confidence;
        var learnerState = BuildLearnerState(policyContext.LearnerState, masteryProbability, confidence, profile.AffectiveState, profile.CognitiveLoad);

        var state = new TutorTurnStateDto
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            ConceptGraphSnapshotId = graph?.Id ?? policyContext.ConceptGraphSnapshotId,
            UserMessage = userMessage,
            ActiveConceptKey = activeKey,
            ActiveConceptLabel = activeLabel,
            LearnerState = learnerState,
            MasteryProbability = masteryProbability,
            Confidence = confidence,
            RemediationNeed = FirstNonEmpty(activeKt?.RemediationNeed, activeMastery?.RemediationNeed, "unknown"),
            PracticeReadiness = FirstNonEmpty(activeKt?.PracticeReadiness, activeMastery?.PracticeReadiness, "guided"),
            StyleMode = profile.PreferredStyleMode,
            AffectiveState = profile.AffectiveState,
            CognitiveLoad = profile.CognitiveLoad,
            GroundingStatus = policyContext.GroundingStatus,
            SourceEvidenceCount = policyContext.SourceEvidenceCount + CountContextEvidence(notebookContext, wikiContext),
            DirectAnswerRisk = policyContext.DirectAnswerRisk || TutorSignalHeuristics.ContainsAny(TutorSignalHeuristics.Normalize(userMessage), "cevabı ver", "direkt cevap", "sonucu söyle", "çözümü ver"),
            HasIdeContext = !string.IsNullOrWhiteSpace(ideContext),
            HasNotebookContext = !string.IsNullOrWhiteSpace(notebookContext),
            HasWikiContext = !string.IsNullOrWhiteSpace(wikiContext),
            RecentMistakes = policyContext.RecentMistakes.Concat(masteries.Where(m => m.MasteryScore < 50).Select(m => m.Label)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(8).ToList(),
            SourceEvidence = policyContext.SourceEvidence.Take(8).ToList(),
            CreatedAt = DateTimeOffset.UtcNow
        };

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
                ["groundingStatus"] = state.GroundingStatus
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
            - directAnswerRisk: {state.DirectAnswerRisk}
            - recentMistakes: {(state.RecentMistakes.Count == 0 ? "none" : string.Join("; ", state.RecentMistakes))}

            [TUTOR STATE KURALI]
            Bu turda cevabi yukaridaki aktif kavram, mastery/confidence ve duygu-yuk sinyaline gore kur. 
            Kaynak yoksa kaynak iddiasinda bulunma. Confidence dusukse "ogrendin" deme; kanit yetersiz modunda mikro kontrol sor.
            """;
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

        var teachingMode = SelectTeachingMode(turnState, lowMastery, codeIntent, wantsVisual, sourceIntent, reviewIntent);
        var directAnswerPolicy = turnState.DirectAnswerRisk && lowMastery
            ? "hint_first_then_scaffold"
            : turnState.DirectAnswerRisk ? "brief_answer_then_reasoning_check" : "scaffold";
        var groundingPolicy = turnState.SourceEvidenceCount > 0
            ? "cite_available_sources"
            : "model_ok_no_source_claim";

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
        var nextCheck = BuildNextCheck(turnState, teachingMode);

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
            ToolPlans = toolPlans,
            ArtifactPlans = artifactPlans,
            NextCheckPrompt = nextCheck,
            PromptBlock = BuildPromptBlock(trace, toolPlans, artifactPlans)
        };

        await _workingMemory.ApplyPatchAsync(turnState.UserId, turnState.TopicId, turnState.SessionId, "tutor_action_plan", new
        {
            actionTraceId = plan.Id,
            plan.TeachingMode,
            plan.ActiveConceptKey,
            plan.StyleMode,
            plan.DirectAnswerPolicy,
            plan.GroundingPolicy,
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
                ["artifactCount"] = artifactPlans.Count.ToString()
            }, ct);
        }

        return plan;
    }

    private static string SelectTeachingMode(TutorTurnStateDto state, bool lowMastery, bool codeIntent, bool wantsVisual, bool sourceIntent, bool reviewIntent)
    {
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

    private static string BuildPromptBlock(TutorActionTrace trace, IReadOnlyList<TutorToolPlanDto> toolPlans, IReadOnlyList<TeachingArtifactPlanDto> artifacts) => $"""

        [TUTOR ACTION PLAN v3]
        - tutorActionTraceId: {trace.Id}
        - teachingMode: {trace.TeachingMode}
        - activeConceptKey: {trace.ActiveConceptKey}
        - styleMode: {trace.StyleMode}
        - directAnswerPolicy: {trace.DirectAnswerPolicy}
        - groundingPolicy: {trace.GroundingPolicy}
        - plannedTools: {(toolPlans.Count == 0 ? "none" : string.Join(", ", toolPlans.Select(t => $"{t.ToolId}:{t.RiskLevel}")))}
        - plannedArtifacts: {(artifacts.Count == 0 ? "none" : string.Join(", ", artifacts.Select(a => a.ArtifactType)))}
        - nextCheck: {trace.NextCheckPrompt}

        [ACTION KURALI]
        Cevap bu plana uymali. Direct-answer policy hint-first ise once ipucu ve scaffold ver; kaynak yoksa kaynak iddiasi kurma.
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

    public TutorToolOrchestrator(
        OrkaDbContext db,
        ITutorWorkingMemoryService workingMemory,
        IWolframProvider wolfram,
        INewsProvider news,
        IWeatherProvider weather,
        IGeocodingProvider geocoding,
        IMarketDataProvider marketData,
        IVisualArtifactProvider visuals,
        IRealWorldEvidenceService? realWorldEvidence = null)
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
            var outcome = await ExecuteAsync(plan, turnState, actionPlan.Id, ct);
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

    public TeachingArtifactService(
        OrkaDbContext db,
        ITutorWorkingMemoryService workingMemory,
        IVisualArtifactProvider visuals)
    {
        _db = db;
        _workingMemory = workingMemory;
        _visuals = visuals;
    }

    public async Task<IReadOnlyList<TeachingArtifactDto>> BuildArtifactsAsync(
        TutorActionPlanDto actionPlan,
        TutorTurnStateDto turnState,
        CancellationToken ct = default)
    {
        var artifacts = new List<TeachingArtifactDto>();
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
            artifacts.Add(artifact);
        }

        await _db.SaveChangesAsync(ct);

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
            ? $"**Kanit karti hazirlanamadi.** {concept} icin guvenli public API sonucu bulunamadi; Tutor veri uydurmadan genel benzetmeyle devam etmeli."
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

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class ActiveLessonSnapshotService : IActiveLessonSnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromHours(6);

    private readonly OrkaDbContext _db;
    private readonly ILearningMemoryService _learningMemory;
    private readonly IRedisMemoryService _redis;
    private readonly ILogger<ActiveLessonSnapshotService> _logger;

    public ActiveLessonSnapshotService(
        OrkaDbContext db,
        ILearningMemoryService learningMemory,
        IRedisMemoryService redis,
        ILogger<ActiveLessonSnapshotService> logger)
    {
        _db = db;
        _learningMemory = learningMemory;
        _redis = redis;
        _logger = logger;
    }

    public async Task<ActiveLessonSnapshotDto> BuildOrRefreshActiveLessonSnapshotAsync(
        Guid userId,
        ActiveLessonSnapshotRequestDto request,
        CancellationToken ct = default)
    {
        await EnsureScopeAsync(userId, request.TopicId, request.SessionId, ct);
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(SnapshotTtl);
        var graph = await ResolveConceptGraphAsync(userId, request.TopicId, request.ConceptGraphSnapshotId, ct);
        var memory = await BuildMemoryAsync(userId, request.TopicId, ct);
        var weakConcepts = memory.WeakConcepts.Take(8).ToArray();
        var activeConcept = weakConcepts.FirstOrDefault()
                            ?? memory.RemediationReadyItems.FirstOrDefault()
                            ?? await LoadFirstGraphConceptAsync(graph?.Id, ct);

        var sourceEvidenceCount = await _db.LearningSources
            .AsNoTracking()
            .CountAsync(s =>
                s.UserId == userId &&
                !s.IsDeleted &&
                s.Status == "ready" &&
                s.Chunks.Any(c => !c.IsDeleted) &&
                (!request.TopicId.HasValue || s.TopicId == request.TopicId), ct);

        var wikiEvidenceCount = await _db.WikiBlocks
            .AsNoTracking()
            .CountAsync(b =>
                b.WikiPage.UserId == userId &&
                (!request.TopicId.HasValue || b.WikiPage.TopicId == request.TopicId), ct);

        var latestSourceBundle = request.TopicId.HasValue
            ? await _db.SourceEvidenceBundles
                .AsNoTracking()
                .Where(b =>
                    b.UserId == userId &&
                    !b.IsDeleted &&
                    b.TopicId == request.TopicId.Value &&
                    (!request.SessionId.HasValue || b.SessionId == request.SessionId))
                .OrderByDescending(b => b.UpdatedAt)
                .FirstOrDefaultAsync(ct)
            : null;

        var toolEvidenceCount = await _db.TutorToolCalls
            .AsNoTracking()
            .CountAsync(t =>
                t.UserId == userId &&
                t.Success &&
                (!request.TopicId.HasValue || t.TopicId == request.TopicId) &&
                (!request.SessionId.HasValue || t.SessionId == request.SessionId), ct);

        toolEvidenceCount += await _db.ToolRuntimeTraces
            .AsNoTracking()
            .CountAsync(t =>
                t.UserId == userId &&
                !t.IsDeleted &&
                t.CompletedAt != null &&
                t.EvidenceJson != "[]" &&
                (!request.TopicId.HasValue || t.TopicId == request.TopicId) &&
                (!request.SessionId.HasValue || t.SessionId == request.SessionId), ct);

        toolEvidenceCount += (await _db.KorteksResearchWorkflows
            .AsNoTracking()
            .Where(w =>
                w.UserId == userId &&
                !w.IsDeleted &&
                w.ToolCallCount > 0 &&
                (!request.TopicId.HasValue || w.TopicId == request.TopicId) &&
                (!request.SessionId.HasValue || w.SessionId == request.SessionId))
            .SumAsync(w => (int?)w.ToolCallCount, ct)) ?? 0;

        var recentAttemptCount = await _db.QuizAttempts
            .AsNoTracking()
            .CountAsync(a =>
                a.UserId == userId &&
                a.CreatedAt >= now.AddDays(-14) &&
                (!request.TopicId.HasValue || a.TopicId == request.TopicId) &&
                (!request.SessionId.HasValue || a.SessionId == request.SessionId), ct);

        var evidence = new LearningSnapshotEvidenceSummaryDto
        {
            SourceEvidenceCount = sourceEvidenceCount,
            WikiEvidenceCount = wikiEvidenceCount,
            ToolEvidenceCount = toolEvidenceCount,
            RecentAttemptCount = recentAttemptCount,
            WeakConceptCount = weakConcepts.Length,
            EvidenceStatus = DetermineEvidenceStatus(sourceEvidenceCount, wikiEvidenceCount, toolEvidenceCount, recentAttemptCount, weakConcepts.Length)
        };

        var activeConceptKey = Clean(activeConcept?.ConceptKey);
        var activeConceptLabel = Clean(activeConcept?.Label ?? activeConceptKey);
        var masteryProbability = await ResolveMasteryProbabilityAsync(userId, request.TopicId, activeConceptKey, ct);
        var confidence = activeConcept?.Confidence ?? await ResolveConfidenceAsync(userId, request.TopicId, activeConceptKey, ct);
        var remediationNeed = DetermineRemediationNeed(memory, weakConcepts.Length);
        var learnerState = DetermineLearnerState(memory, masteryProbability, confidence, remediationNeed, evidence);

        await SupersedePreviousActiveAsync(userId, request.TopicId, request.SessionId, now, ct);

        var entity = new ActiveLessonSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            PlanRequestId = request.PlanRequestId,
            QuizRunId = request.QuizRunId,
            ConceptGraphSnapshotId = graph?.Id,
            SourceBundleHash = Clean(request.SourceBundleHash ?? latestSourceBundle?.BundleHash ?? graph?.SourceBundleHash, 128),
            SnapshotVersion = await NextActiveSnapshotVersionAsync(userId, request.TopicId, request.SessionId, ct),
            Status = "active",
            ActiveConceptKey = activeConceptKey,
            ActiveConceptLabel = activeConceptLabel,
            ApprovedIntent = Clean(request.ApprovedIntent, 256),
            ApprovedMainTopic = Clean(request.ApprovedMainTopic, 256),
            ApprovedFocusArea = Clean(request.ApprovedFocusArea, 256),
            ApprovedStudyGoal = Clean(request.ApprovedStudyGoal, 256),
            GroundingMode = Clean(request.GroundingMode, 128),
            SourceEvidenceCount = evidence.SourceEvidenceCount,
            WikiEvidenceCount = evidence.WikiEvidenceCount,
            ToolEvidenceCount = evidence.ToolEvidenceCount,
            RecentAttemptCount = evidence.RecentAttemptCount,
            WeakConceptCount = evidence.WeakConceptCount,
            RemediationNeed = remediationNeed,
            LearnerState = learnerState,
            Confidence = confidence,
            MasteryProbability = masteryProbability,
            EvidenceSummaryJson = JsonSerializer.Serialize(evidence, JsonOptions),
            SnapshotJson = JsonSerializer.Serialize(new
            {
                evidence,
                memory.ConfidenceStatus,
                SourceReadiness = latestSourceBundle?.EvidenceStatus ?? memory.SourceReadiness,
                memory.Summary,
                weakConcepts = weakConcepts.Select(ToSafeConcept).ToArray()
            }, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = expiresAt
        };

        _db.ActiveLessonSnapshots.Add(entity);
        await _db.SaveChangesAsync(ct);

        var dto = ToDto(entity, evidence);
        await CacheAsync(CacheKey("active", userId, request.TopicId, request.SessionId), dto, ct);
        return dto;
    }

    public async Task<StudentContextSnapshotDto> BuildOrRefreshStudentContextSnapshotAsync(
        Guid userId,
        StudentContextSnapshotRequestDto request,
        CancellationToken ct = default)
    {
        await EnsureScopeAsync(userId, request.TopicId, request.SessionId, ct);
        var now = DateTime.UtcNow;
        var memory = await BuildMemoryAsync(userId, request.TopicId, ct);
        var reviewPressure = await LoadReviewPressureAsync(userId, request.TopicId, ct);
        var strongConcepts = memory.StrongTopics
            .Select(t => new LearningSnapshotConceptDto
            {
                TopicId = t.TopicId,
                ConceptKey = t.Label,
                Label = t.Label,
                MasteryProbability = t.MasteryProbability,
                Confidence = t.Confidence,
                ConfidenceStatus = t.ConfidenceStatus,
                UserSafeReason = t.UserSafeReason,
                EvidenceBasis = t.EvidenceBasis
            })
            .ToArray();
        var weakConcepts = memory.WeakConcepts.Select(ToSafeConcept).ToArray();
        var recentMisconceptions = memory.RecentMisconceptions.Select(ToSafeConcept).ToArray();
        var remediationReady = memory.RemediationReadyItems.Select(ToRemediation).ToArray();
        var confidenceStatus = memory.HasEnoughSignals && memory.ConfidenceStatus == "observed_only" && (weakConcepts.Length > 0 || remediationReady.Length > 0)
            ? "usable"
            : memory.HasEnoughSignals ? memory.ConfidenceStatus : "observed_only";

        var entity = new StudentContextSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            SnapshotVersion = await NextStudentSnapshotVersionAsync(userId, request.TopicId, request.SessionId, ct),
            ConfidenceStatus = confidenceStatus,
            StrongConceptsJson = JsonSerializer.Serialize(strongConcepts, JsonOptions),
            WeakConceptsJson = JsonSerializer.Serialize(weakConcepts, JsonOptions),
            RecentMisconceptionsJson = JsonSerializer.Serialize(recentMisconceptions, JsonOptions),
            RemediationReadyJson = JsonSerializer.Serialize(remediationReady, JsonOptions),
            ReviewPressureJson = JsonSerializer.Serialize(reviewPressure, JsonOptions),
            SourceReadiness = await ResolveSourceReadinessAsync(userId, request.TopicId, request.SessionId, memory.SourceReadiness, ct),
            GoalReadinessJson = JsonSerializer.Serialize(memory.GoalReadiness, JsonOptions),
            LearningMemoryJson = JsonSerializer.Serialize(new
            {
                memory.Summary,
                memory.ConfidenceStatus,
                memory.HasEnoughSignals,
                memory.ConfidenceSummary,
                memory.RecentProgressSignals
            }, JsonOptions),
            SnapshotJson = JsonSerializer.Serialize(new
            {
                memory.Summary,
                memory.ConfidenceStatus,
                weakConcepts,
                recentMisconceptions,
                remediationReady,
                reviewPressure
            }, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.Add(SnapshotTtl)
        };

        _db.StudentContextSnapshots.Add(entity);
        await _db.SaveChangesAsync(ct);

        var dto = ToDto(entity);
        await CacheAsync(CacheKey("student", userId, request.TopicId, request.SessionId), dto, ct);
        return dto;
    }

    public async Task<ActiveLessonSnapshotDto?> GetActiveLessonSnapshotAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var entity = await _db.ActiveLessonSnapshots
            .AsNoTracking()
            .Where(s =>
                s.UserId == userId &&
                !s.IsDeleted &&
                s.TopicId == topicId &&
                s.Status == "active" &&
                (!sessionId.HasValue || s.SessionId == sessionId))
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        return entity == null ? null : ToDto(entity, Parse(entity.EvidenceSummaryJson, new LearningSnapshotEvidenceSummaryDto()));
    }

    public async Task<StudentContextSnapshotDto?> GetStudentContextSnapshotAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var entity = await _db.StudentContextSnapshots
            .AsNoTracking()
            .Where(s =>
                s.UserId == userId &&
                !s.IsDeleted &&
                s.TopicId == topicId &&
                (!sessionId.HasValue || s.SessionId == sessionId))
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        return entity == null ? null : ToDto(entity);
    }

    public async Task MarkActiveLessonSnapshotStaleAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string reason,
        CancellationToken ct = default)
    {
        var snapshots = await _db.ActiveLessonSnapshots
            .Where(s =>
                s.UserId == userId &&
                !s.IsDeleted &&
                s.TopicId == topicId &&
                s.Status == "active" &&
                (!sessionId.HasValue || s.SessionId == sessionId))
            .ToListAsync(ct);

        if (snapshots.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var snapshot in snapshots)
        {
            snapshot.Status = "stale";
            snapshot.UpdatedAt = now;
            snapshot.EvidenceSummaryJson = MergeStaleReason(snapshot.EvidenceSummaryJson, reason);
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureScopeAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
        if (topicId.HasValue)
        {
            var topicExists = await _db.Topics
                .AsNoTracking()
                .AnyAsync(t => t.Id == topicId.Value && t.UserId == userId && !t.IsArchived, ct);
            if (!topicExists)
            {
                throw new InvalidOperationException("Topic not found for learning snapshot.");
            }
        }

        if (sessionId.HasValue)
        {
            var sessionExists = await _db.Sessions
                .AsNoTracking()
                .AnyAsync(s => s.Id == sessionId.Value && s.UserId == userId, ct);
            if (!sessionExists)
            {
                throw new InvalidOperationException("Session not found for learning snapshot.");
            }
        }
    }

    private async Task<ConceptGraphSnapshot?> ResolveConceptGraphAsync(Guid userId, Guid? topicId, Guid? snapshotId, CancellationToken ct)
    {
        if (snapshotId.HasValue)
        {
            var explicitGraph = await _db.ConceptGraphSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == snapshotId.Value && g.UserId == userId, ct);
            if (explicitGraph == null)
            {
                throw new InvalidOperationException("Concept graph snapshot not found.");
            }

            return explicitGraph;
        }

        return await _db.ConceptGraphSnapshots
            .AsNoTracking()
            .Where(g => g.UserId == userId && g.TopicId == topicId)
            .OrderByDescending(g => g.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<LearningMemoryConceptDto?> LoadFirstGraphConceptAsync(Guid? conceptGraphSnapshotId, CancellationToken ct)
    {
        if (!conceptGraphSnapshotId.HasValue)
        {
            return null;
        }

        var concept = await _db.LearningConcepts
            .AsNoTracking()
            .Where(c => c.ConceptGraphSnapshotId == conceptGraphSnapshotId.Value)
            .OrderBy(c => c.Order)
            .Select(c => new { c.StableKey, c.Label })
            .FirstOrDefaultAsync(ct);

        return concept == null
            ? null
            : new LearningMemoryConceptDto
            {
                ConceptKey = concept.StableKey,
                Label = concept.Label,
                ConfidenceStatus = "observed_only",
                EvidenceBasis = ["concept_graph"]
            };
    }

    private async Task<LearningMemoryLiteDto> BuildMemoryAsync(Guid userId, Guid? topicId, CancellationToken ct)
    {
        var scope = topicId.HasValue ? new[] { topicId.Value } : Array.Empty<Guid>();
        return await _learningMemory.BuildAsync(userId, scope, null, ct);
    }

    private async Task<decimal?> ResolveMasteryProbabilityAsync(Guid userId, Guid? topicId, string? conceptKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(conceptKey))
        {
            return null;
        }

        var tracing = await _db.KnowledgeTracingStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId && s.ConceptKey == conceptKey)
            .Select(s => (decimal?)s.MasteryProbability)
            .FirstOrDefaultAsync(ct);

        if (tracing.HasValue)
        {
            return tracing.Value;
        }

        var mastery = await _db.ConceptMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.TopicId == topicId && m.ConceptKey == conceptKey)
            .Select(m => (decimal?)(m.MasteryScore / 100m))
            .FirstOrDefaultAsync(ct);

        return mastery;
    }

    private async Task<decimal?> ResolveConfidenceAsync(Guid userId, Guid? topicId, string? conceptKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(conceptKey))
        {
            return null;
        }

        return await _db.KnowledgeTracingStates
                   .AsNoTracking()
                   .Where(s => s.UserId == userId && s.TopicId == topicId && s.ConceptKey == conceptKey)
                   .Select(s => (decimal?)s.Confidence)
                   .FirstOrDefaultAsync(ct)
               ?? await _db.ConceptMasteries
                   .AsNoTracking()
                   .Where(m => m.UserId == userId && m.TopicId == topicId && m.ConceptKey == conceptKey)
                   .Select(m => (decimal?)m.Confidence)
                   .FirstOrDefaultAsync(ct);
    }

    private async Task<string[]> LoadReviewPressureAsync(Guid userId, Guid? topicId, CancellationToken ct)
    {
        return await _db.ReviewItems
            .AsNoTracking()
            .Where(r =>
                r.UserId == userId &&
                r.Status == "active" &&
                (!topicId.HasValue || r.TopicId == topicId))
            .OrderBy(r => r.DueAt)
            .Take(5)
            .Select(r => r.ConceptTag ?? r.SkillTag ?? r.LearningObjective ?? r.ReviewKey)
            .ToArrayAsync(ct);
    }

    private async Task<string> ResolveSourceReadinessAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string? fallback,
        CancellationToken ct)
    {
        if (!topicId.HasValue)
        {
            return fallback ?? "unknown";
        }

        var latest = await _db.SourceEvidenceBundles
            .AsNoTracking()
            .Where(b =>
                b.UserId == userId &&
                !b.IsDeleted &&
                b.TopicId == topicId.Value &&
                (!sessionId.HasValue || b.SessionId == sessionId))
            .OrderByDescending(b => b.UpdatedAt)
            .Select(b => b.EvidenceStatus)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(latest) ? fallback ?? "unknown" : latest;
    }

    private async Task SupersedePreviousActiveAsync(Guid userId, Guid? topicId, Guid? sessionId, DateTime now, CancellationToken ct)
    {
        var previous = await _db.ActiveLessonSnapshots
            .Where(s =>
                s.UserId == userId &&
                !s.IsDeleted &&
                s.TopicId == topicId &&
                s.SessionId == sessionId &&
                s.Status == "active")
            .ToListAsync(ct);

        foreach (var snapshot in previous)
        {
            snapshot.Status = "superseded";
            snapshot.UpdatedAt = now;
        }
    }

    private async Task<int> NextActiveSnapshotVersionAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
        var latest = await _db.ActiveLessonSnapshots
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId && s.SessionId == sessionId)
            .OrderByDescending(s => s.SnapshotVersion)
            .Select(s => (int?)s.SnapshotVersion)
            .FirstOrDefaultAsync(ct);

        return (latest ?? 0) + 1;
    }

    private async Task<int> NextStudentSnapshotVersionAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
        var latest = await _db.StudentContextSnapshots
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId && s.SessionId == sessionId)
            .OrderByDescending(s => s.SnapshotVersion)
            .Select(s => (int?)s.SnapshotVersion)
            .FirstOrDefaultAsync(ct);

        return (latest ?? 0) + 1;
    }

    private async Task CacheAsync<T>(string key, T dto, CancellationToken ct)
    {
        try
        {
            await _redis.SetJsonAsync(key, JsonSerializer.Serialize(dto, JsonOptions), SnapshotTtl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Learning snapshot cache write skipped. Key={Key}", key);
        }
    }

    private static ActiveLessonSnapshotDto ToDto(ActiveLessonSnapshot entity, LearningSnapshotEvidenceSummaryDto evidence) => new()
    {
        Id = entity.Id,
        TopicId = entity.TopicId,
        SessionId = entity.SessionId,
        PlanRequestId = entity.PlanRequestId,
        QuizRunId = entity.QuizRunId,
        ConceptGraphSnapshotId = entity.ConceptGraphSnapshotId,
        SourceBundleHash = entity.SourceBundleHash,
        SnapshotVersion = entity.SnapshotVersion,
        Status = entity.Status,
        ActiveConceptKey = entity.ActiveConceptKey,
        ActiveConceptLabel = entity.ActiveConceptLabel,
        ApprovedIntent = entity.ApprovedIntent,
        ApprovedMainTopic = entity.ApprovedMainTopic,
        ApprovedFocusArea = entity.ApprovedFocusArea,
        ApprovedStudyGoal = entity.ApprovedStudyGoal,
        GroundingMode = entity.GroundingMode,
        EvidenceSummary = evidence,
        RemediationNeed = entity.RemediationNeed,
        LearnerState = entity.LearnerState,
        Confidence = entity.Confidence,
        MasteryProbability = entity.MasteryProbability,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
        ExpiresAt = entity.ExpiresAt
    };

    private static StudentContextSnapshotDto ToDto(StudentContextSnapshot entity) => new()
    {
        Id = entity.Id,
        TopicId = entity.TopicId,
        SessionId = entity.SessionId,
        SnapshotVersion = entity.SnapshotVersion,
        ConfidenceStatus = entity.ConfidenceStatus,
        StrongConcepts = Parse<IReadOnlyList<LearningSnapshotConceptDto>>(entity.StrongConceptsJson, Array.Empty<LearningSnapshotConceptDto>()),
        WeakConcepts = Parse<IReadOnlyList<LearningSnapshotConceptDto>>(entity.WeakConceptsJson, Array.Empty<LearningSnapshotConceptDto>()),
        RecentMisconceptions = Parse<IReadOnlyList<LearningSnapshotConceptDto>>(entity.RecentMisconceptionsJson, Array.Empty<LearningSnapshotConceptDto>()),
        RemediationReady = Parse<IReadOnlyList<LearningSnapshotRemediationDto>>(entity.RemediationReadyJson, Array.Empty<LearningSnapshotRemediationDto>()),
        ReviewPressure = Parse<IReadOnlyList<string>>(entity.ReviewPressureJson, Array.Empty<string>()),
        SourceReadiness = entity.SourceReadiness ?? "unknown",
        GoalReadiness = Parse(entity.GoalReadinessJson, new GoalReadinessDto()),
        LearningMemorySummary = ParseMemorySummary(entity.LearningMemoryJson),
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
        ExpiresAt = entity.ExpiresAt
    };

    private static LearningSnapshotConceptDto ToSafeConcept(LearningMemoryConceptDto concept) => new()
    {
        TopicId = concept.TopicId,
        ConceptKey = concept.ConceptKey ?? string.Empty,
        Label = concept.Label,
        Confidence = concept.Confidence,
        ConfidenceStatus = concept.ConfidenceStatus,
        UserSafeReason = concept.UserSafeReason,
        EvidenceBasis = concept.EvidenceBasis
    };

    private static LearningSnapshotRemediationDto ToRemediation(LearningMemoryConceptDto concept) => new()
    {
        TopicId = concept.TopicId,
        ConceptKey = concept.ConceptKey ?? concept.RemediationSeed?.ConceptKey ?? string.Empty,
        Label = concept.Label,
        Reason = concept.RemediationSeed?.Reason ?? concept.UserSafeReason,
        Confidence = concept.RemediationSeed?.Confidence ?? concept.Confidence,
        ConfidenceStatus = concept.RemediationSeed?.ConfidenceStatus ?? concept.ConfidenceStatus,
        FirstAction = concept.RemediationSeed?.FirstAction ?? "tutor_explain",
        SecondaryActions = concept.RemediationSeed?.SecondaryActions ?? Array.Empty<string>(),
        EvidenceBasis = concept.RemediationSeed?.EvidenceBasis ?? concept.EvidenceBasis
    };

    private static string DetermineEvidenceStatus(int sourceCount, int wikiCount, int toolCount, int attemptCount, int weakConceptCount)
    {
        var total = sourceCount + wikiCount + toolCount + attemptCount + weakConceptCount;
        return total switch
        {
            0 => "evidence_insufficient",
            < 3 => "observed_only",
            _ => "usable"
        };
    }

    private static string DetermineRemediationNeed(LearningMemoryLiteDto memory, int weakConceptCount)
    {
        if (memory.RemediationReadyItems.Count >= 2 || weakConceptCount >= 3)
        {
            return "high";
        }

        if (memory.RemediationReadyItems.Count == 1 || weakConceptCount > 0)
        {
            return "medium";
        }

        return memory.HasEnoughSignals ? "none" : "evidence_insufficient";
    }

    private static string DetermineLearnerState(
        LearningMemoryLiteDto memory,
        decimal? mastery,
        decimal? confidence,
        string remediationNeed,
        LearningSnapshotEvidenceSummaryDto evidence)
    {
        if (evidence.EvidenceStatus == "evidence_insufficient" || !memory.HasEnoughSignals)
        {
            return "evidence_insufficient";
        }

        if (remediationNeed is "high" or "medium")
        {
            return "remediation";
        }

        if (mastery >= 0.75m && confidence >= 0.60m)
        {
            return "ready_for_challenge";
        }

        return "needs_scaffold";
    }

    private static string MergeStaleReason(string? evidenceJson, string reason)
    {
        var evidence = Parse(evidenceJson, new LearningSnapshotEvidenceSummaryDto());
        return JsonSerializer.Serialize(new
        {
            evidence.SourceEvidenceCount,
            evidence.WikiEvidenceCount,
            evidence.ToolEvidenceCount,
            evidence.RecentAttemptCount,
            evidence.WeakConceptCount,
            evidence.EvidenceStatus,
            staleReason = Clean(reason, 256)
        }, JsonOptions);
    }

    private static T Parse<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string ParseMemorySummary(string? json)
    {
        var summary = Parse(json, new LearningMemorySummaryProjection());
        return string.IsNullOrWhiteSpace(summary.Summary)
            ? "Henüz yeterli öğrenme sinyali yok."
            : summary.Summary;
    }

    private static string CacheKey(string kind, Guid userId, Guid? topicId, Guid? sessionId) =>
        $"orka:v4:{kind}-snapshot:{userId}:{topicId?.ToString() ?? "global"}:{sessionId?.ToString() ?? "global"}";

    private static string? Clean(string? value, int maxLength = 450)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed class LearningMemorySummaryProjection
    {
        public string Summary { get; set; } = string.Empty;
    }
}

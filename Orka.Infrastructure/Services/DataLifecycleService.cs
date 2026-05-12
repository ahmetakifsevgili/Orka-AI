using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class DataLifecycleService : IDataLifecycleService
{
    private static readonly MethodInfo RemoveMatchingEntitiesMethod =
        typeof(DataLifecycleService).GetMethod(nameof(RemoveMatchingEntitiesAsync), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("RemoveMatchingEntitiesAsync helper missing.");

    private static readonly HashSet<Type> ExcludedHardDeleteTypes =
    [
        typeof(User),
        typeof(Topic),
        typeof(Badge),
        typeof(ToolTelemetryEvent),
        typeof(CostRecord)
    ];

    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService _redis;
    private readonly ILogger<DataLifecycleService> _logger;

    public DataLifecycleService(
        OrkaDbContext db,
        IRedisMemoryService redis,
        ILogger<DataLifecycleService> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<bool> DeleteTopicTreeAsync(Guid userId, Guid topicId, CancellationToken ct = default)
    {
        var topicRows = await _db.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new TopicTreeRow(t.Id, t.ParentTopicId))
            .ToListAsync(ct);

        if (!topicRows.Any(t => t.Id == topicId))
            return false;

        var topicDepths = CollectTopicTree(topicRows, topicId);
        var scope = await BuildTopicScopeAsync(userId, topicDepths.Keys.ToHashSet(), ct);

        await using var tx = await BeginTransactionIfSupportedAsync(ct);
        await AnonymizeOperationalRecordsAsync(userId, scope, accountDelete: false, ct);
        await RemoveEntitiesByScopeAsync(scope, ct);
        await _db.SaveChangesAsync(ct);

        await DeleteTopicsLeafFirstAsync(topicDepths, ct);

        if (tx is not null)
            await tx.CommitAsync(ct);

        await InvalidateTopicCachesAsync(userId, topicDepths.Keys, "topic-deleted");
        return true;
    }

    public async Task<bool> DeleteAccountAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return false;

        var topicRows = await _db.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new TopicTreeRow(t.Id, t.ParentTopicId))
            .ToListAsync(ct);

        var topicDepths = CollectAllTopics(topicRows);
        var scope = await BuildTopicScopeAsync(userId, topicDepths.Keys.ToHashSet(), ct);
        scope.Add("UserId", userId);

        await using var tx = await BeginTransactionIfSupportedAsync(ct);
        await AnonymizeOperationalRecordsAsync(userId, scope, accountDelete: true, ct);
        await RemoveEntitiesByScopeAsync(scope, ct);
        await _db.SaveChangesAsync(ct);

        await DeleteTopicsLeafFirstAsync(topicDepths, ct);

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);

        await InvalidateTopicCachesAsync(userId, topicDepths.Keys, "account-deleted");
        return true;
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginTransactionIfSupportedAsync(CancellationToken ct)
    {
        if ((_db.Database.ProviderName ?? string.Empty).Contains("InMemory", StringComparison.OrdinalIgnoreCase))
            return null;

        return await _db.Database.BeginTransactionAsync(ct);
    }

    private async Task<DataLifecycleScope> BuildTopicScopeAsync(Guid userId, HashSet<Guid> topicIds, CancellationToken ct)
    {
        var scope = new DataLifecycleScope();
        scope.Add(DataLifecycleScope.OwnerUserIdKey, userId);
        scope.Add("TopicId", topicIds);
        scope.Add("ParentTopicId", topicIds);

        var sessionIds = await _db.Sessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId.HasValue && topicIds.Contains(s.TopicId.Value))
            .Select(s => s.Id)
            .ToListAsync(ct);
        scope.Add("SessionId", sessionIds);

        var messageIds = await _db.Messages
            .AsNoTracking()
            .Where(m => m.UserId == userId && sessionIds.Contains(m.SessionId))
            .Select(m => m.Id)
            .ToListAsync(ct);
        scope.Add("MessageId", messageIds);

        var wikiPageIds = await _db.WikiPages
            .AsNoTracking()
            .Where(w => w.UserId == userId && topicIds.Contains(w.TopicId))
            .Select(w => w.Id)
            .ToListAsync(ct);
        scope.Add("WikiPageId", wikiPageIds);

        var learningSourceIds = await _db.LearningSources
            .AsNoTracking()
            .Where(s => s.UserId == userId &&
                ((s.TopicId.HasValue && topicIds.Contains(s.TopicId.Value)) ||
                 (s.SessionId.HasValue && sessionIds.Contains(s.SessionId.Value))))
            .Select(s => s.Id)
            .ToListAsync(ct);
        scope.Add("LearningSourceId", learningSourceIds);
        scope.Add("SourceId", learningSourceIds);

        var sourceChunkIds = await _db.SourceChunks
            .AsNoTracking()
            .Where(c => learningSourceIds.Contains(c.LearningSourceId))
            .Select(c => c.Id)
            .ToListAsync(ct);
        scope.Add("SourceChunkId", sourceChunkIds);

        var quizRunIds = await _db.QuizRuns
            .AsNoTracking()
            .Where(q => q.UserId == userId &&
                ((q.TopicId.HasValue && topicIds.Contains(q.TopicId.Value)) ||
                 (q.SessionId.HasValue && sessionIds.Contains(q.SessionId.Value))))
            .Select(q => q.Id)
            .ToListAsync(ct);
        scope.Add("QuizRunId", quizRunIds);

        var quizAttemptIds = await _db.QuizAttempts
            .AsNoTracking()
            .Where(q => q.UserId == userId &&
                ((q.TopicId.HasValue && topicIds.Contains(q.TopicId.Value)) ||
                 (q.SessionId.HasValue && sessionIds.Contains(q.SessionId.Value)) ||
                 (q.QuizRunId.HasValue && quizRunIds.Contains(q.QuizRunId.Value))))
            .Select(q => q.Id)
            .ToListAsync(ct);
        scope.Add("QuizAttemptId", quizAttemptIds);

        var learningSignalIds = await _db.LearningSignals
            .AsNoTracking()
            .Where(s => s.UserId == userId &&
                ((s.TopicId.HasValue && topicIds.Contains(s.TopicId.Value)) ||
                 (s.SessionId.HasValue && sessionIds.Contains(s.SessionId.Value)) ||
                 (s.QuizAttemptId.HasValue && quizAttemptIds.Contains(s.QuizAttemptId.Value))))
            .Select(s => s.Id)
            .ToListAsync(ct);
        scope.Add("LearningSignalId", learningSignalIds);

        var conceptGraphSnapshotIds = await _db.ConceptGraphSnapshots
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId.HasValue && topicIds.Contains(s.TopicId.Value))
            .Select(s => s.Id)
            .ToListAsync(ct);
        scope.Add("ConceptGraphSnapshotId", conceptGraphSnapshotIds);

        var learningConceptIds = await _db.LearningConcepts
            .AsNoTracking()
            .Where(c => conceptGraphSnapshotIds.Contains(c.ConceptGraphSnapshotId))
            .Select(c => c.Id)
            .ToListAsync(ct);
        scope.Add("LearningConceptId", learningConceptIds);

        var learningOutcomeIds = await _db.LearningOutcomes
            .AsNoTracking()
            .Where(o => o.ConceptGraphSnapshotId.HasValue && conceptGraphSnapshotIds.Contains(o.ConceptGraphSnapshotId.Value))
            .Select(o => o.Id)
            .ToListAsync(ct);
        scope.Add("LearningOutcomeId", learningOutcomeIds);

        var assessmentItemIds = await _db.AssessmentItems
            .AsNoTracking()
            .Where(a => a.UserId == userId &&
                ((a.TopicId.HasValue && topicIds.Contains(a.TopicId.Value)) ||
                 (a.QuizRunId.HasValue && quizRunIds.Contains(a.QuizRunId.Value)) ||
                 conceptGraphSnapshotIds.Contains(a.ConceptGraphSnapshotId) ||
                 (a.LearningConceptId.HasValue && learningConceptIds.Contains(a.LearningConceptId.Value))))
            .Select(a => a.Id)
            .ToListAsync(ct);
        scope.Add("AssessmentItemId", assessmentItemIds);

        var learningEventIds = await _db.LearningEvents
            .AsNoTracking()
            .Where(e => e.UserId == userId &&
                ((e.TopicId.HasValue && topicIds.Contains(e.TopicId.Value)) ||
                 (e.SessionId.HasValue && sessionIds.Contains(e.SessionId.Value)) ||
                 (e.QuizAttemptId.HasValue && quizAttemptIds.Contains(e.QuizAttemptId.Value)) ||
                 (e.AssessmentItemId.HasValue && assessmentItemIds.Contains(e.AssessmentItemId.Value))))
            .Select(e => e.Id)
            .ToListAsync(ct);
        scope.Add("LearningEventId", learningEventIds);

        var reviewItemIds = await _db.ReviewItems
            .AsNoTracking()
            .Where(r => r.UserId == userId &&
                ((r.TopicId.HasValue && topicIds.Contains(r.TopicId.Value)) ||
                 (r.QuizAttemptId.HasValue && quizAttemptIds.Contains(r.QuizAttemptId.Value)) ||
                 (r.LearningSignalId.HasValue && learningSignalIds.Contains(r.LearningSignalId.Value))))
            .Select(r => r.Id)
            .ToListAsync(ct);
        scope.Add("ReviewItemId", reviewItemIds);

        var flashcardIds = await _db.Flashcards
            .AsNoTracking()
            .Where(f => f.UserId == userId &&
                ((f.TopicId.HasValue && topicIds.Contains(f.TopicId.Value)) ||
                 (f.LearningSourceId.HasValue && learningSourceIds.Contains(f.LearningSourceId.Value)) ||
                 (f.WikiPageId.HasValue && wikiPageIds.Contains(f.WikiPageId.Value)) ||
                 (f.QuizAttemptId.HasValue && quizAttemptIds.Contains(f.QuizAttemptId.Value))))
            .Select(f => f.Id)
            .ToListAsync(ct);
        scope.Add("FlashcardId", flashcardIds);

        var dailyChallengeIds = await _db.DailyChallenges
            .AsNoTracking()
            .Where(d => d.UserId == userId &&
                ((d.TopicId.HasValue && topicIds.Contains(d.TopicId.Value)) ||
                 (d.ReviewItemId.HasValue && reviewItemIds.Contains(d.ReviewItemId.Value))))
            .Select(d => d.Id)
            .ToListAsync(ct);
        scope.Add("DailyChallengeId", dailyChallengeIds);

        var xpEventIds = await _db.DailyChallengeSubmissions
            .AsNoTracking()
            .Where(s => s.UserId == userId && dailyChallengeIds.Contains(s.DailyChallengeId) && s.XpEventId.HasValue)
            .Select(s => s.XpEventId!.Value)
            .ToListAsync(ct);
        scope.Add("XpEventId", xpEventIds);
        scope.Add("SourceEventId", xpEventIds);
        scope.Add("RelatedEntityId", topicIds.Concat(sessionIds).Concat(messageIds).Concat(learningSourceIds).Concat(reviewItemIds).Concat(flashcardIds).Concat(dailyChallengeIds).Concat(xpEventIds));

        var classroomSessionIds = await _db.ClassroomSessions
            .AsNoTracking()
            .Where(c => c.UserId == userId &&
                ((c.TopicId.HasValue && topicIds.Contains(c.TopicId.Value)) ||
                 (c.SessionId.HasValue && sessionIds.Contains(c.SessionId.Value))))
            .Select(c => c.Id)
            .ToListAsync(ct);
        scope.Add("ClassroomSessionId", classroomSessionIds);

        var audioOverviewJobIds = await _db.AudioOverviewJobs
            .AsNoTracking()
            .Where(a => a.UserId == userId &&
                ((a.TopicId.HasValue && topicIds.Contains(a.TopicId.Value)) ||
                 (a.SessionId.HasValue && sessionIds.Contains(a.SessionId.Value))))
            .Select(a => a.Id)
            .ToListAsync(ct);
        scope.Add("AudioOverviewJobId", audioOverviewJobIds);

        var workingMemorySnapshotIds = await _db.TutorWorkingMemorySnapshots
            .AsNoTracking()
            .Where(s => s.UserId == userId &&
                ((s.TopicId.HasValue && topicIds.Contains(s.TopicId.Value)) ||
                 (s.SessionId.HasValue && sessionIds.Contains(s.SessionId.Value))))
            .Select(s => s.Id)
            .ToListAsync(ct);
        scope.Add("WorkingMemorySnapshotId", workingMemorySnapshotIds);

        var tutorTurnStateIds = await _db.TutorTurnStates
            .AsNoTracking()
            .Where(s => s.UserId == userId &&
                ((s.TopicId.HasValue && topicIds.Contains(s.TopicId.Value)) ||
                 (s.SessionId.HasValue && sessionIds.Contains(s.SessionId.Value)) ||
                 (s.WorkingMemorySnapshotId.HasValue && workingMemorySnapshotIds.Contains(s.WorkingMemorySnapshotId.Value)) ||
                 (s.ConceptGraphSnapshotId.HasValue && conceptGraphSnapshotIds.Contains(s.ConceptGraphSnapshotId.Value))))
            .Select(s => s.Id)
            .ToListAsync(ct);
        scope.Add("TutorTurnStateId", tutorTurnStateIds);

        var tutorActionTraceIds = await _db.TutorActionTraces
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                ((t.TopicId.HasValue && topicIds.Contains(t.TopicId.Value)) ||
                 (t.SessionId.HasValue && sessionIds.Contains(t.SessionId.Value)) ||
                 (t.TutorTurnStateId.HasValue && tutorTurnStateIds.Contains(t.TutorTurnStateId.Value))))
            .Select(t => t.Id)
            .ToListAsync(ct);
        scope.Add("TutorActionTraceId", tutorActionTraceIds);

        var tutorToolCallIds = await _db.TutorToolCalls
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                ((t.TopicId.HasValue && topicIds.Contains(t.TopicId.Value)) ||
                 (t.SessionId.HasValue && sessionIds.Contains(t.SessionId.Value)) ||
                 (t.TutorActionTraceId.HasValue && tutorActionTraceIds.Contains(t.TutorActionTraceId.Value))))
            .Select(t => t.Id)
            .ToListAsync(ct);
        scope.Add("TutorToolCallId", tutorToolCallIds);

        var tutorReflectionUpdateIds = await _db.TutorReflectionUpdates
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                ((t.TopicId.HasValue && topicIds.Contains(t.TopicId.Value)) ||
                 (t.SessionId.HasValue && sessionIds.Contains(t.SessionId.Value)) ||
                 (t.TutorActionTraceId.HasValue && tutorActionTraceIds.Contains(t.TutorActionTraceId.Value)) ||
                 (t.TutorTurnStateId.HasValue && tutorTurnStateIds.Contains(t.TutorTurnStateId.Value))))
            .Select(t => t.Id)
            .ToListAsync(ct);
        scope.Add("TutorReflectionUpdateId", tutorReflectionUpdateIds);

        var ragEvaluationRunIds = await _db.RagEvaluationRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId &&
                ((r.TopicId.HasValue && topicIds.Contains(r.TopicId.Value)) ||
                 (r.ConceptGraphSnapshotId.HasValue && conceptGraphSnapshotIds.Contains(r.ConceptGraphSnapshotId.Value))))
            .Select(r => r.Id)
            .ToListAsync(ct);
        AddRunScopes(scope, ragEvaluationRunIds, "RagEvaluationRunId");

        var sourceRetrievalRunIds = await _db.SourceRetrievalRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId &&
                ((r.TopicId.HasValue && topicIds.Contains(r.TopicId.Value)) ||
                 (r.SessionId.HasValue && sessionIds.Contains(r.SessionId.Value)) ||
                 (r.SourceId.HasValue && learningSourceIds.Contains(r.SourceId.Value))))
            .Select(r => r.Id)
            .ToListAsync(ct);
        AddRunScopes(scope, sourceRetrievalRunIds, "SourceRetrievalRunId");

        var tutorPedagogyEvaluationRunIds = await _db.TutorPedagogyEvaluationRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId &&
                ((r.TopicId.HasValue && topicIds.Contains(r.TopicId.Value)) ||
                 (r.SessionId.HasValue && sessionIds.Contains(r.SessionId.Value)) ||
                 (r.TutorTurnStateId.HasValue && tutorTurnStateIds.Contains(r.TutorTurnStateId.Value)) ||
                 (r.TutorActionTraceId.HasValue && tutorActionTraceIds.Contains(r.TutorActionTraceId.Value)) ||
                 (r.TutorReflectionUpdateId.HasValue && tutorReflectionUpdateIds.Contains(r.TutorReflectionUpdateId.Value))))
            .Select(r => r.Id)
            .ToListAsync(ct);
        AddRunScopes(scope, tutorPedagogyEvaluationRunIds, "EvaluationRunId", "TutorPedagogyEvaluationRunId");

        var assessmentCalibrationRunIds = await _db.AssessmentCalibrationRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId &&
                ((r.TopicId.HasValue && topicIds.Contains(r.TopicId.Value)) ||
                 (r.ConceptGraphSnapshotId.HasValue && conceptGraphSnapshotIds.Contains(r.ConceptGraphSnapshotId.Value))))
            .Select(r => r.Id)
            .ToListAsync(ct);
        AddRunScopes(scope, assessmentCalibrationRunIds, "AssessmentCalibrationRunId");

        var adaptiveAssessmentSessionIds = await _db.AdaptiveAssessmentSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId &&
                ((s.TopicId.HasValue && topicIds.Contains(s.TopicId.Value)) ||
                 (s.SessionId.HasValue && sessionIds.Contains(s.SessionId.Value)) ||
                 (s.QuizRunId.HasValue && quizRunIds.Contains(s.QuizRunId.Value)) ||
                 (s.ConceptGraphSnapshotId.HasValue && conceptGraphSnapshotIds.Contains(s.ConceptGraphSnapshotId.Value))))
            .Select(s => s.Id)
            .ToListAsync(ct);
        AddRunScopes(scope, adaptiveAssessmentSessionIds, "AdaptiveAssessmentSessionId");

        var standardsExportRunIds = await _db.StandardsExportRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId &&
                ((r.TopicId.HasValue && topicIds.Contains(r.TopicId.Value)) ||
                 (r.ConceptGraphSnapshotId.HasValue && conceptGraphSnapshotIds.Contains(r.ConceptGraphSnapshotId.Value))))
            .Select(r => r.Id)
            .ToListAsync(ct);
        AddRunScopes(scope, standardsExportRunIds, "StandardsExportRunId");

        var standardsValidationRunIds = await _db.StandardsValidationRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId &&
                ((r.TopicId.HasValue && topicIds.Contains(r.TopicId.Value)) ||
                 (r.ConceptGraphSnapshotId.HasValue && conceptGraphSnapshotIds.Contains(r.ConceptGraphSnapshotId.Value))))
            .Select(r => r.Id)
            .ToListAsync(ct);
        AddRunScopes(scope, standardsValidationRunIds, "StandardsValidationRunId");

        scope.Add("EntityId", topicIds
            .Concat(sessionIds)
            .Concat(messageIds)
            .Concat(wikiPageIds)
            .Concat(learningSourceIds)
            .Concat(sourceChunkIds)
            .Concat(quizRunIds)
            .Concat(quizAttemptIds)
            .Concat(learningSignalIds)
            .Concat(conceptGraphSnapshotIds)
            .Concat(learningConceptIds)
            .Concat(learningOutcomeIds)
            .Concat(assessmentItemIds)
            .Concat(learningEventIds)
            .Concat(tutorTurnStateIds)
            .Concat(tutorActionTraceIds)
            .Concat(tutorToolCallIds)
            .Concat(ragEvaluationRunIds)
            .Concat(sourceRetrievalRunIds)
            .Concat(standardsExportRunIds)
            .Concat(standardsValidationRunIds));

        return scope;
    }

    private static void AddRunScopes(DataLifecycleScope scope, IEnumerable<Guid> ids, params string[] propertyNames)
    {
        var materialized = ids.ToList();
        foreach (var propertyName in propertyNames)
            scope.Add(propertyName, materialized);
    }

    private async Task AnonymizeOperationalRecordsAsync(Guid userId, DataLifecycleScope scope, bool accountDelete, CancellationToken ct)
    {
        var sessionIds = scope.Get("SessionId");
        var messageIds = scope.Get("MessageId");
        var topicIds = scope.Get("TopicId");

        var telemetry = await _db.ToolTelemetryEvents
            .Where(t =>
                (accountDelete && t.UserId == userId) ||
                (!accountDelete &&
                 (t.UserId == userId || t.UserId == null) &&
                 ((t.TopicId.HasValue && topicIds.Contains(t.TopicId.Value)) ||
                  (t.SessionId.HasValue && sessionIds.Contains(t.SessionId.Value)))))
            .ToListAsync(ct);

        foreach (var item in telemetry)
        {
            item.UserId = null;
            item.TopicId = null;
            item.SessionId = null;
            item.MetadataJson = null;
            item.CorrelationId = null;
        }

        var costs = await _db.CostRecords
            .Where(c =>
                (accountDelete && c.UserId == userId) ||
                (!accountDelete &&
                 (c.UserId == userId || c.UserId == null) &&
                 ((c.TopicId.HasValue && topicIds.Contains(c.TopicId.Value)) ||
                  (c.SessionId.HasValue && sessionIds.Contains(c.SessionId.Value)) ||
                  (c.MessageId.HasValue && messageIds.Contains(c.MessageId.Value)))))
            .ToListAsync(ct);

        foreach (var item in costs)
        {
            item.UserId = null;
            item.SessionId = null;
            item.TopicId = null;
            item.MessageId = null;
            item.MetadataJson = null;
        }

        if (!accountDelete)
        {
            await AnonymizeOperationalRecordsWithScopedMetadataAsync(userId, scope, ct);
        }
    }

    private async Task AnonymizeOperationalRecordsWithScopedMetadataAsync(Guid userId, DataLifecycleScope scope, CancellationToken ct)
    {
        var telemetry = await _db.ToolTelemetryEvents
            .Where(t => t.UserId == userId && t.MetadataJson != null)
            .ToListAsync(ct);

        foreach (var item in telemetry.Where(item => ContainsScopedIdentifier(item.MetadataJson, scope)))
        {
            item.UserId = null;
            item.TopicId = null;
            item.SessionId = null;
            item.MetadataJson = null;
            item.CorrelationId = null;
        }

        var costs = await _db.CostRecords
            .Where(c => c.UserId == userId && c.MetadataJson != null)
            .ToListAsync(ct);

        foreach (var item in costs.Where(item => ContainsScopedIdentifier(item.MetadataJson, scope)))
        {
            item.UserId = null;
            item.SessionId = null;
            item.TopicId = null;
            item.MessageId = null;
            item.MetadataJson = null;
        }
    }

    private static bool ContainsScopedIdentifier(string? metadataJson, DataLifecycleScope scope)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return false;

        foreach (var id in scope.AllIds)
        {
            if (metadataJson.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
                metadataJson.Contains(id.ToString("N"), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task RemoveEntitiesByScopeAsync(DataLifecycleScope scope, CancellationToken ct)
    {
        var entityTypes = _db.Model.GetEntityTypes()
            .Where(e => e.ClrType is not null && !ExcludedHardDeleteTypes.Contains(e.ClrType))
            .Distinct()
            .OrderByDescending(GetDeleteDependencyDepth)
            .ThenBy(e => e.ClrType.Name)
            .Select(e => e.ClrType)
            .ToList();

        foreach (var entityType in entityTypes)
        {
            var propertyNames = entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => scope.HasValues(p.Name) && (p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?)))
                .Select(p => p.Name)
                .Distinct()
                .ToArray();

            if (propertyNames.Length == 0)
                continue;

            var task = (Task)RemoveMatchingEntitiesMethod.MakeGenericMethod(entityType)
                .Invoke(this, [propertyNames, scope.Values, ct])!;
            await task;
        }
    }

    private static int GetDeleteDependencyDepth(Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType)
    {
        return GetDeleteDependencyDepth(entityType, []);
    }

    private static int GetDeleteDependencyDepth(
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType,
        HashSet<Microsoft.EntityFrameworkCore.Metadata.IEntityType> visiting)
    {
        if (!visiting.Add(entityType))
            return 0;

        var depth = 0;
        foreach (var foreignKey in entityType.GetForeignKeys())
        {
            if (foreignKey.PrincipalEntityType == entityType)
                continue;

            depth = Math.Max(depth, 1 + GetDeleteDependencyDepth(foreignKey.PrincipalEntityType, visiting));
        }

        visiting.Remove(entityType);
        return depth;
    }

    private async Task RemoveMatchingEntitiesAsync<TEntity>(
        string[] propertyNames,
        IReadOnlyDictionary<string, HashSet<Guid>> scope,
        CancellationToken ct)
        where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        Expression? body = null;
        var containsMethod = typeof(HashSet<Guid>).GetMethod(nameof(HashSet<Guid>.Contains), [typeof(Guid)])
            ?? throw new InvalidOperationException("HashSet.Contains missing.");

        foreach (var propertyName in propertyNames)
        {
            if (!scope.TryGetValue(propertyName, out var ids) || ids.Count == 0)
                continue;

            var property = Expression.Property(parameter, propertyName);
            var idsExpression = Expression.Constant(ids);
            Expression contains;

            if (property.Type == typeof(Guid))
            {
                contains = Expression.Call(idsExpression, containsMethod, property);
            }
            else if (property.Type == typeof(Guid?))
            {
                contains = Expression.AndAlso(
                    Expression.Property(property, nameof(Nullable<Guid>.HasValue)),
                    Expression.Call(idsExpression, containsMethod, Expression.Property(property, nameof(Nullable<Guid>.Value))));
            }
            else
            {
                continue;
            }

            body = body is null ? contains : Expression.OrElse(body, contains);
        }

        if (body is null)
            return;

        body = ApplyOwnerGuard<TEntity>(parameter, body, scope);
        var predicate = Expression.Lambda<Func<TEntity, bool>>(body, parameter);
        var matches = await _db.Set<TEntity>().Where(predicate).ToListAsync(ct);
        if (matches.Count > 0)
            _db.Set<TEntity>().RemoveRange(matches);
    }

    private static Expression ApplyOwnerGuard<TEntity>(
        ParameterExpression parameter,
        Expression body,
        IReadOnlyDictionary<string, HashSet<Guid>> scope)
    {
        if (!scope.TryGetValue(DataLifecycleScope.OwnerUserIdKey, out var ownerIds) || ownerIds.Count != 1)
            return body;

        var userIdProperty = typeof(TEntity).GetProperty("UserId", BindingFlags.Instance | BindingFlags.Public);
        if (userIdProperty is null ||
            (userIdProperty.PropertyType != typeof(Guid) && userIdProperty.PropertyType != typeof(Guid?)))
        {
            return body;
        }

        var ownerId = ownerIds.Single();
        var property = Expression.Property(parameter, userIdProperty.Name);
        Expression ownerMatch;

        if (property.Type == typeof(Guid))
        {
            ownerMatch = Expression.Equal(property, Expression.Constant(ownerId));
        }
        else
        {
            ownerMatch = Expression.AndAlso(
                Expression.Property(property, nameof(Nullable<Guid>.HasValue)),
                Expression.Equal(Expression.Property(property, nameof(Nullable<Guid>.Value)), Expression.Constant(ownerId)));
        }

        return Expression.AndAlso(ownerMatch, body);
    }

    private async Task DeleteTopicsLeafFirstAsync(IReadOnlyDictionary<Guid, int> topicDepths, CancellationToken ct)
    {
        foreach (var level in topicDepths.GroupBy(kvp => kvp.Value).OrderByDescending(g => g.Key))
        {
            var ids = level.Select(kvp => kvp.Key).ToHashSet();
            var topics = await _db.Topics.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
            if (topics.Count == 0)
                continue;

            _db.Topics.RemoveRange(topics);
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task InvalidateTopicCachesAsync(Guid userId, IEnumerable<Guid> topicIds, string reason)
    {
        var scopedTopicIds = topicIds.Distinct().ToArray();
        foreach (var topicId in scopedTopicIds)
        {
            try
            {
                await _redis.InvalidateLearningCachesAsync(userId, topicId, reason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Learning cache invalidation failed after data lifecycle operation. TopicId={TopicId}", topicId);
            }
        }

        try
        {
            await _redis.PurgeUserCachesAsync(userId, scopedTopicIds, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Broad Redis cache purge failed after data lifecycle operation. Reason={Reason}", reason);
        }
    }

    private static Dictionary<Guid, int> CollectTopicTree(IReadOnlyList<TopicTreeRow> allTopics, Guid rootTopicId)
    {
        var childrenByParent = allTopics
            .Where(t => t.ParentTopicId.HasValue)
            .GroupBy(t => t.ParentTopicId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Id).ToList());

        var depths = new Dictionary<Guid, int> { [rootTopicId] = 0 };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootTopicId);

        while (queue.TryDequeue(out var current))
        {
            if (!childrenByParent.TryGetValue(current, out var children))
                continue;

            foreach (var child in children)
            {
                depths[child] = depths[current] + 1;
                queue.Enqueue(child);
            }
        }

        return depths;
    }

    private static Dictionary<Guid, int> CollectAllTopics(IReadOnlyList<TopicTreeRow> allTopics)
    {
        var roots = allTopics
            .Where(t => !t.ParentTopicId.HasValue || allTopics.All(candidate => candidate.Id != t.ParentTopicId.Value))
            .Select(t => t.Id)
            .ToList();

        var depths = new Dictionary<Guid, int>();
        foreach (var root in roots)
        {
            foreach (var (id, depth) in CollectTopicTree(allTopics, root))
                depths[id] = Math.Max(depths.GetValueOrDefault(id), depth);
        }

        return depths;
    }

    private sealed record TopicTreeRow(Guid Id, Guid? ParentTopicId);

    private sealed class DataLifecycleScope
    {
        public const string OwnerUserIdKey = "__OwnerUserId";

        public IReadOnlyDictionary<string, HashSet<Guid>> Values => _values;
        public IEnumerable<Guid> AllIds => _values
            .Where(kvp => kvp.Key != OwnerUserIdKey)
            .SelectMany(kvp => kvp.Value)
            .Distinct();

        private readonly Dictionary<string, HashSet<Guid>> _values = new(StringComparer.Ordinal);

        public void Add(string propertyName, Guid value)
        {
            if (value == Guid.Empty)
                return;

            if (!_values.TryGetValue(propertyName, out var set))
            {
                set = [];
                _values[propertyName] = set;
            }

            set.Add(value);
        }

        public void Add(string propertyName, IEnumerable<Guid> values)
        {
            foreach (var value in values)
                Add(propertyName, value);
        }

        public bool HasValues(string propertyName) =>
            _values.TryGetValue(propertyName, out var set) && set.Count > 0;

        public bool Contains(string propertyName, Guid value) =>
            _values.TryGetValue(propertyName, out var set) && set.Contains(value);

        public HashSet<Guid> Get(string propertyName) =>
            _values.TryGetValue(propertyName, out var set) ? set : [];
    }
}

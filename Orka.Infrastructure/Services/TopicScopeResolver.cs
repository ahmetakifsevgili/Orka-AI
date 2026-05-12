using Microsoft.EntityFrameworkCore;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class TopicScopeResolver : ITopicScopeResolver
{
    private readonly OrkaDbContext _db;

    public TopicScopeResolver(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<TopicScope> ResolveAsync(Guid userId, Guid currentTopicId, CancellationToken ct = default)
    {
        var topics = await _db.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new ScopeTopic(
                t.Id,
                t.ParentTopicId,
                t.Order,
                t.ProgressPercentage,
                t.IsMastered,
                t.LastAccessedAt,
                t.CreatedAt,
                t.IsArchived))
            .ToListAsync(ct);

        var topicById = topics.ToDictionary(t => t.Id);
        if (!topicById.TryGetValue(currentTopicId, out var current))
            return TopicScope.Empty(currentTopicId);

        var ancestorIds = ResolveAncestors(current, topicById);
        var rootId = ancestorIds.Count == 0 ? current.Id : ancestorIds[^1];
        var childrenByParent = topics
            .Where(t => t.ParentTopicId.HasValue)
            .GroupBy(t => t.ParentTopicId!.Value)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(t => t.Order)
                .ThenBy(t => t.CreatedAt)
                .Select(t => t.Id)
                .ToList());

        var descendantIds = ResolveDescendants(current.Id, childrenByParent);
        var rootDescendantIds = ResolveDescendants(rootId, childrenByParent);
        var treeTopicIds = new[] { rootId }
            .Concat(rootDescendantIds)
            .Distinct()
            .ToArray();

        var activeLessonTopicId = ResolveActiveLesson(current, descendantIds, childrenByParent, topicById);

        return new TopicScope(
            current.Id,
            rootId,
            ancestorIds,
            descendantIds,
            treeTopicIds,
            activeLessonTopicId,
            true);
    }

    private static IReadOnlyList<Guid> ResolveAncestors(
        ScopeTopic current,
        IReadOnlyDictionary<Guid, ScopeTopic> topicById)
    {
        var ancestors = new List<Guid>();
        var seen = new HashSet<Guid> { current.Id };
        var cursor = current;

        while (cursor.ParentTopicId.HasValue &&
               topicById.TryGetValue(cursor.ParentTopicId.Value, out var parent) &&
               seen.Add(parent.Id))
        {
            ancestors.Add(parent.Id);
            cursor = parent;
        }

        return ancestors;
    }

    private static IReadOnlyList<Guid> ResolveDescendants(
        Guid topicId,
        IReadOnlyDictionary<Guid, List<Guid>> childrenByParent)
    {
        var descendants = new List<Guid>();
        var seen = new HashSet<Guid> { topicId };
        var queue = new Queue<Guid>();
        queue.Enqueue(topicId);

        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();
            if (!childrenByParent.TryGetValue(parentId, out var children))
                continue;

            foreach (var childId in children)
            {
                if (!seen.Add(childId))
                    continue;

                descendants.Add(childId);
                queue.Enqueue(childId);
            }
        }

        return descendants;
    }

    private static Guid? ResolveActiveLesson(
        ScopeTopic current,
        IReadOnlyList<Guid> descendantIds,
        IReadOnlyDictionary<Guid, List<Guid>> childrenByParent,
        IReadOnlyDictionary<Guid, ScopeTopic> topicById)
    {
        if (!childrenByParent.ContainsKey(current.Id))
            return current.Id;

        var lessonCandidates = descendantIds
            .Where(id => !childrenByParent.ContainsKey(id))
            .Select(id => topicById[id])
            .Where(t => !t.IsArchived)
            .ToList();

        var orderedCandidates = lessonCandidates
            .Select(t => new ActiveLessonCandidate(t, BuildPathSortKey(current.Id, t, topicById)))
            .ToList();

        var incomplete = orderedCandidates
            .Where(t => !t.IsMastered && t.ProgressPercentage < 100)
            .OrderBy(t => t.PathSortKey, StringComparer.Ordinal)
            .FirstOrDefault();
        if (incomplete is not null)
            return incomplete.Topic.Id;

        return orderedCandidates
            .OrderByDescending(t => t.Topic.LastAccessedAt)
            .ThenBy(t => t.PathSortKey, StringComparer.Ordinal)
            .Select(t => (Guid?)t.Topic.Id)
            .FirstOrDefault();
    }

    private static string BuildPathSortKey(
        Guid currentTopicId,
        ScopeTopic leaf,
        IReadOnlyDictionary<Guid, ScopeTopic> topicById)
    {
        var path = new List<ScopeTopic>();
        var seen = new HashSet<Guid>();
        var cursor = leaf;

        while (seen.Add(cursor.Id))
        {
            path.Add(cursor);
            if (cursor.Id == currentTopicId)
                break;
            if (!cursor.ParentTopicId.HasValue ||
                !topicById.TryGetValue(cursor.ParentTopicId.Value, out cursor))
            {
                break;
            }
        }

        path.Reverse();
        if (path.Count > 0 && path[0].Id == currentTopicId)
            path.RemoveAt(0);

        return string.Join("/", path.Select(t =>
            $"{(long)t.Order - int.MinValue:D10}:{t.CreatedAt.Ticks:D19}:{t.Id:N}"));
    }

    private sealed record ScopeTopic(
        Guid Id,
        Guid? ParentTopicId,
        int Order,
        double ProgressPercentage,
        bool IsMastered,
        DateTime LastAccessedAt,
        DateTime CreatedAt,
        bool IsArchived);

    private sealed record ActiveLessonCandidate(ScopeTopic Topic, string PathSortKey)
    {
        public bool IsMastered => Topic.IsMastered;
        public double ProgressPercentage => Topic.ProgressPercentage;
    }
}

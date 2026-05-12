using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class SessionService
{
    private readonly OrkaDbContext _dbContext;
    private readonly ITopicScopeResolver _topicScopeResolver;

    public SessionService(OrkaDbContext dbContext, ITopicScopeResolver topicScopeResolver)
    {
        _dbContext = dbContext;
        _topicScopeResolver = topicScopeResolver;
    }

    public async Task<IEnumerable<Session>> GetTopicSessionsAsync(Guid topicId, Guid userId)
    {
        return await _dbContext.Sessions
            .Where(s => s.TopicId == topicId && s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<object?> GetLatestSessionAsync(Guid topicId, Guid userId)
    {
        var topicScope = await _topicScopeResolver.ResolveAsync(userId, topicId);
        if (!topicScope.IsValid || topicScope.TreeTopicIds.Count == 0) return null;

        var primaryRestoreTopicIds = GetPrimaryRestoreTopicIds(topicScope);
        var session = await FindLatestSessionAsync(primaryRestoreTopicIds, userId);
        if (session is null && !topicScope.HasDescendants)
        {
            session = await FindLatestSessionAsync(topicScope.AncestorTopicIds, userId);
        }

        if (session == null) return null;

        var lastMessage = session.Messages
            .Where(m => m.Role == "assistant")
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        var nextPageTopicIds = GetNextPageTopicIds(topicScope);
        var nextPage = await _dbContext.WikiPages
            .Where(w => nextPageTopicIds.Contains(w.TopicId) && w.UserId == userId && (w.Status == "pending" || w.Status == "learning"))
            .OrderBy(w => w.OrderIndex)
            .FirstOrDefaultAsync();

        var mappedMessages = session.Messages.OrderBy(m => m.CreatedAt).Select(m => new {
            id = m.Id,
            role = m.Role,
            messageType = m.MessageType.ToString().ToLowerInvariant(),
            content = m.Content,
            createdAt = m.CreatedAt
        }).ToList();

        return new
        {
            sessionId = session.Id,
            sessionNumber = session.SessionNumber,
            summary = session.Summary,
            lastMessage = lastMessage?.Content?[..Math.Min(200, lastMessage.Content.Length)],
            nextSuggestedTopic = nextPage?.Title,
            totalTokensUsed = session.TotalTokensUsed,
            totalCostUsd = session.TotalCostUSD,
            messages = mappedMessages
        };
    }

    private async Task<Session?> FindLatestSessionAsync(IReadOnlyCollection<Guid> topicIds, Guid userId)
    {
        if (topicIds.Count == 0) return null;

        return await _dbContext.Sessions
            .Include(s => s.Messages)
            .Where(s => s.TopicId.HasValue && topicIds.Contains(s.TopicId.Value) && s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private static IReadOnlyList<Guid> GetPrimaryRestoreTopicIds(TopicScope topicScope)
    {
        if (!topicScope.HasDescendants)
            return [topicScope.CurrentTopicId];

        return new[] { topicScope.CurrentTopicId }
            .Concat(topicScope.DescendantTopicIds)
            .Distinct()
            .ToArray();
    }

    private static IReadOnlyList<Guid> GetNextPageTopicIds(TopicScope topicScope)
    {
        if (topicScope.HasDescendants)
            return GetPrimaryRestoreTopicIds(topicScope);

        return new[] { topicScope.CurrentTopicId }
            .Concat(topicScope.AncestorTopicIds)
            .Distinct()
            .ToArray();
    }
}

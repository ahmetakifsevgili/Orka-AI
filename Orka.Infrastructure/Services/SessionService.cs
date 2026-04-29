using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class SessionService
{
    private readonly OrkaDbContext _dbContext;

    public SessionService(OrkaDbContext dbContext)
    {
        _dbContext = dbContext;
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
        var treeTopicIds = await GetTopicTreeIdsAsync(topicId, userId);
        if (treeTopicIds.Count == 0) return null;

        var session = await _dbContext.Sessions
            .Include(s => s.Messages)
            .Where(s => s.TopicId.HasValue && treeTopicIds.Contains(s.TopicId.Value) && s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (session == null) return null;

        var lastMessage = session.Messages
            .Where(m => m.Role == "assistant")
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        var nextPage = await _dbContext.WikiPages
            .Where(w => w.TopicId == topicId && (w.Status == "pending" || w.Status == "learning"))
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

    private async Task<List<Guid>> GetTopicTreeIdsAsync(Guid topicId, Guid userId)
    {
        var topic = await _dbContext.Topics
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId);
        if (topic == null) return new List<Guid>();

        while (topic.ParentTopicId.HasValue)
        {
            var parent = await _dbContext.Topics
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == topic.ParentTopicId.Value && t.UserId == userId);
            if (parent == null) break;
            topic = parent;
        }

        var ids = new List<Guid> { topic.Id };
        var frontier = new List<Guid> { topic.Id };

        while (frontier.Count > 0)
        {
            var children = await _dbContext.Topics
                .AsNoTracking()
                .Where(t => t.ParentTopicId.HasValue && frontier.Contains(t.ParentTopicId.Value) && t.UserId == userId)
                .Select(t => t.Id)
                .ToListAsync();

            frontier = children.Except(ids).ToList();
            ids.AddRange(frontier);
        }

        return ids;
    }
}

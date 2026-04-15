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
        var session = await _dbContext.Sessions
            .Include(s => s.Messages)
            .Where(s => s.TopicId == topicId && s.UserId == userId)
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
}

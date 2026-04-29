using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class ContextBuilder : IContextBuilder
{
    private readonly OrkaDbContext _dbContext;
    private readonly IGroqService _groqService;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly ILogger<ContextBuilder> _logger;
    private readonly int _maxContextMessages;
    private const int SemanticTruncationThreshold = 50; // [HATA-7]: CLAUDE.md Madde 4

    public ContextBuilder(OrkaDbContext dbContext, IGroqService groqService,
        IBackgroundTaskQueue backgroundQueue,
        IConfiguration configuration, ILogger<ContextBuilder> logger)
    {
        _dbContext = dbContext;
        _groqService = groqService;
        _backgroundQueue = backgroundQueue;
        _logger = logger;
        _maxContextMessages = int.TryParse(configuration["Limits:MaxContextMessages"], out var limit) ? limit : 10;
    }

    public async Task<IEnumerable<Message>> BuildContextAsync(Guid topicId, Guid sessionId)
    {
        // [HATA-7]: Semantic truncation for long topic histories.
        // Topic bazinda toplam mesaj sayisini kontrol et.
        var totalMessageCount = await _dbContext.Messages
            .Join(_dbContext.Sessions, m => m.SessionId, s => s.Id, (m, s) => new { m, s })
            .Where(x => x.s.TopicId == topicId)
            .CountAsync();

        // 50 mesaji asiyorsa ozet olustur ve budanmis baglamla devam et.
        if (totalMessageCount > SemanticTruncationThreshold)
        {
            _logger.LogInformation("[SEMANTIC TRUNCATION] {Count} mesaj > 50 esigi. Baglam ozetleniyor.", totalMessageCount);
            return await BuildTruncatedContextAsync(topicId, sessionId);
        }

        // Normal akis: son N mesaji dondur.
        var messages = await _dbContext.Messages
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(_maxContextMessages)
            .ToListAsync();

        messages.Reverse();
        return messages;
    }

    private async Task<IEnumerable<Message>> BuildTruncatedContextAsync(Guid topicId, Guid sessionId)
    {
        try
        {
            // Eski mesajlari al (son 10 haric).
            var recentMessages = await _dbContext.Messages
                .Where(m => m.SessionId == sessionId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(_maxContextMessages)
                .ToListAsync();

            recentMessages.Reverse();

            // Onceki mesajlari queue uzerinden ozetle; kullaniciyi bekletmez.
            var oldMessages = await _dbContext.Messages
                .Join(_dbContext.Sessions, m => m.SessionId, s => s.Id, (m, s) => new { m, s })
                .Where(x => x.s.TopicId == topicId && !recentMessages.Select(r => r.Id).Contains(x.m.Id))
                .OrderBy(x => x.m.CreatedAt)
                .Take(30)
                .Select(x => x.m)
                .ToListAsync();

            if (oldMessages.Any())
            {
                _ = _backgroundQueue.QueueAsync(new BackgroundTaskItem(
                    "context-semantic-summary",
                    null,
                    null,
                    async ct =>
                    {
                        var summary = await _groqService.SummarizeSessionAsync(oldMessages);
                        _logger.LogInformation("[SEMANTIC TRUNCATION] Ozet olusturuldu: {Length} karakter", summary.Length);
                    },
                    MaxAttempts: 1,
                    Timeout: TimeSpan.FromSeconds(30)));
            }

            return recentMessages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SEMANTIC TRUNCATION] Budama basarisiz, normal baglama donuluyor.");

            // Guvenli fallback: sadece son mesajlari dondur.
            var fallback = await _dbContext.Messages
                .Where(m => m.SessionId == sessionId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(_maxContextMessages)
                .ToListAsync();
            fallback.Reverse();
            return fallback;
        }
    }

    public Task<IEnumerable<Message>> BuildConversationContextAsync(Session session, int maxMessages = 0)
    {
        // 0 ise config'den oku; caller override edebilir.
        var limit = maxMessages > 0 ? maxMessages : _maxContextMessages;

        var result = session.Messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .OrderBy(m => m.CreatedAt)
            .Cast<Message>()
            .ToList();

        return Task.FromResult<IEnumerable<Message>>(result);
    }
}

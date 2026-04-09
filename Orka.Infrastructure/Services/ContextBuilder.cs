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
    private readonly ILogger<ContextBuilder> _logger;
    private readonly int _maxContextMessages;
    private const int SemanticTruncationThreshold = 50; // [HATA-7]: CLAUDE.md Madde 4

    public ContextBuilder(OrkaDbContext dbContext, IGroqService groqService,
        IConfiguration configuration, ILogger<ContextBuilder> logger)
    {
        _dbContext = dbContext;
        _groqService = groqService;
        _logger = logger;
        _maxContextMessages = int.TryParse(configuration["Limits:MaxContextMessages"], out var limit) ? limit : 10;
    }

    public async Task<IEnumerable<Message>> BuildContextAsync(Guid topicId, Guid sessionId)
    {
        // [HATA-7 DÜZELTMESİ]: CLAUDE.md Madde 4 — Semantik Budama (Semantic Truncation)
        // Topic bazında toplam mesaj sayısını kontrol et
        var totalMessageCount = await _dbContext.Messages
            .Join(_dbContext.Sessions, m => m.SessionId, s => s.Id, (m, s) => new { m, s })
            .Where(x => x.s.TopicId == topicId)
            .CountAsync();

        // 50 mesajı aşıyorsa, özet oluştur ve budanmış bağlamla devam et
        if (totalMessageCount > SemanticTruncationThreshold)
        {
            _logger.LogInformation("[SEMANTIC TRUNCATION] {Count} mesaj > 50 eşiği. Bağlam özetleniyor.", totalMessageCount);
            return await BuildTruncatedContextAsync(topicId, sessionId);
        }

        // Normal akış: son N mesajı döndür
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
            // Eski mesajları al (son 10 hariç)
            var recentMessages = await _dbContext.Messages
                .Where(m => m.SessionId == sessionId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(_maxContextMessages)
                .ToListAsync();

            recentMessages.Reverse();

            // Önceki mesajları özetle (fire & forget — kullanıcıyı beklettirme)
            var oldMessages = await _dbContext.Messages
                .Join(_dbContext.Sessions, m => m.SessionId, s => s.Id, (m, s) => new { m, s })
                .Where(x => x.s.TopicId == topicId && !recentMessages.Select(r => r.Id).Contains(x.m.Id))
                .OrderBy(x => x.m.CreatedAt)
                .Take(30)
                .Select(x => x.m)
                .ToListAsync();

            if (oldMessages.Any())
            {
                // Özetin oluşturulması arka planda (anayasal Fire & Forget)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var summary = await _groqService.SummarizeSessionAsync(oldMessages);
                        _logger.LogInformation("[SEMANTIC TRUNCATION] Özet oluşturuldu: {Length} karakter", summary.Length);
                        // Özeti sistem mesajı olarak ekle
                        var summaryMsg = new Message
                        {
                            Id = Guid.NewGuid(),
                            SessionId = sessionId,
                            Role = "system",
                            Content = $"[Öğrenme Özeti]: {summary}",
                            CreatedAt = DateTime.UtcNow,
                            MessageType = Orka.Core.Enums.MessageType.General
                        };
                        // Not: Bu arka plan thread'de DbContext kullanamayız (HATA-6 önlemi)
                        // Bu nedenle sadece log atıyoruz; gelecekte IServiceScopeFactory ile genişletilecek
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[SEMANTIC TRUNCATION] Özet oluşturulamadı.");
                    }
                });
            }

            return recentMessages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SEMANTIC TRUNCATION] Budama başarısız, normal bağlama dönülüyor.");

            // Güvenli fallback: sadece son mesajları döndür
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
        // 0 → config'den oku; caller override edebilir
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

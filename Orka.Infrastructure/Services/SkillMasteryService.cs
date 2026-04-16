using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class SkillMasteryService : ISkillMasteryService
{
    private readonly OrkaDbContext _db;
    private readonly ILogger<SkillMasteryService> _logger;

    public SkillMasteryService(OrkaDbContext db, ILogger<SkillMasteryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RecordMasteryAsync(Guid userId, Guid topicId, string subTopicTitle, int quizScore)
    {
        // Idempotent: aynı kullanıcı + konu + başlık kombinasyonu zaten varsa atla
        bool alreadyExists = await _db.SkillMasteries
            .AnyAsync(sm => sm.UserId == userId && sm.TopicId == topicId && sm.SubTopicTitle == subTopicTitle);

        if (alreadyExists)
        {
            _logger.LogInformation("[SkillMastery] Zaten mevcut, atlandı. UserId={UserId} TopicId={TopicId} Title={Title}",
                userId, topicId, subTopicTitle);
            return;
        }

        var mastery = new SkillMastery
        {
            Id            = Guid.NewGuid(),
            UserId        = userId,
            TopicId       = topicId,
            SubTopicTitle = subTopicTitle,
            MasteredAt    = DateTime.UtcNow,
            QuizScore     = quizScore
        };

        _db.SkillMasteries.Add(mastery);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[SkillMastery] Yeni mastery kaydedildi. UserId={UserId} TopicId={TopicId} Title={Title} Score={Score}",
            userId, topicId, subTopicTitle, quizScore);
    }

    public async Task<IEnumerable<SkillMastery>> GetMasteriesByTopicAsync(Guid userId, Guid topicId)
    {
        return await _db.SkillMasteries
            .Where(sm => sm.UserId == userId && sm.TopicId == topicId)
            .OrderByDescending(sm => sm.MasteredAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<SkillMastery>> GetAllMasteriesAsync(Guid userId)
    {
        return await _db.SkillMasteries
            .Where(sm => sm.UserId == userId)
            .OrderByDescending(sm => sm.MasteredAt)
            .ToListAsync();
    }
}

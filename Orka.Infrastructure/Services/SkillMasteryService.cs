using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

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
        // Upsert: aynı kullanıcı + konu + başlık zaten varsa ve yeni puan daha yüksekse güncelle
        var existing = await _db.SkillMasteries
            .FirstOrDefaultAsync(sm => sm.UserId == userId && sm.TopicId == topicId && sm.SubTopicTitle == subTopicTitle);

        if (existing != null)
        {
            if (quizScore > existing.QuizScore)
            {
                var oldScore = existing.QuizScore;
                existing.QuizScore = quizScore;
                existing.MasteredAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                _logger.LogInformation("[SkillMastery] Puan guncellendi. UserRef={UserRef} TopicRef={TopicRef} SkillRef={SkillRef} EskiScore={Old} YeniScore={New}",
                    LogPrivacyGuard.SafeId(userId, "usr"), LogPrivacyGuard.SafeId(topicId, "topic"), LogPrivacyGuard.SafeTextRef(subTopicTitle, "skill"), oldScore, quizScore);
            }
            else
            {
                _logger.LogInformation("[SkillMastery] Mevcut puan ({Existing}) daha yuksek/esit, guncelleme gerekmedi. UserRef={UserRef} TopicRef={TopicRef} SkillRef={SkillRef}",
                    existing.QuizScore, LogPrivacyGuard.SafeId(userId, "usr"), LogPrivacyGuard.SafeId(topicId, "topic"), LogPrivacyGuard.SafeTextRef(subTopicTitle, "skill"));
            }
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

        _logger.LogInformation("[SkillMastery] Yeni mastery kaydedildi. UserRef={UserRef} TopicRef={TopicRef} SkillRef={SkillRef} Score={Score}",
            LogPrivacyGuard.SafeId(userId, "usr"), LogPrivacyGuard.SafeId(topicId, "topic"), LogPrivacyGuard.SafeTextRef(subTopicTitle, "skill"), quizScore);
    }

    public async Task<IEnumerable<SkillMastery>> GetMasteriesByTopicAsync(Guid userId, Guid topicId)
    {
        var conceptMasteries = await _db.ConceptMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.TopicId == topicId)
            .OrderByDescending(m => m.UpdatedAt)
            .ToListAsync();

        var tracingStates = await _db.KnowledgeTracingStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();

        var canonical = ToLegacyMasteries(userId, conceptMasteries, tracingStates).ToList();
        if (canonical.Count > 0)
        {
            return canonical;
        }

        return await _db.SkillMasteries
            .AsNoTracking()
            .Where(sm => sm.UserId == userId && sm.TopicId == topicId)
            .OrderByDescending(sm => sm.MasteredAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<SkillMastery>> GetAllMasteriesAsync(Guid userId)
    {
        var conceptMasteries = await _db.ConceptMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.TopicId.HasValue)
            .OrderByDescending(m => m.UpdatedAt)
            .ToListAsync();

        var tracingStates = await _db.KnowledgeTracingStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId.HasValue)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();

        var canonical = ToLegacyMasteries(userId, conceptMasteries, tracingStates).ToList();
        if (canonical.Count > 0)
        {
            return canonical;
        }

        return await _db.SkillMasteries
            .AsNoTracking()
            .Where(sm => sm.UserId == userId)
            .OrderByDescending(sm => sm.MasteredAt)
            .ToListAsync();
    }

    private static IEnumerable<SkillMastery> ToLegacyMasteries(
        Guid userId,
        IEnumerable<ConceptMastery> conceptMasteries,
        IEnumerable<KnowledgeTracingState> tracingStates)
    {
        var byKey = conceptMasteries
            .Where(m => m.TopicId.HasValue)
            .GroupBy(m => (m.TopicId!.Value, Key: m.ConceptKey), StringComparerTuple.Instance)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.UpdatedAt).First(), StringComparerTuple.Instance);

        foreach (var state in tracingStates.Where(s => s.TopicId.HasValue))
        {
            var key = (state.TopicId!.Value, Key: state.ConceptKey);
            if (!byKey.ContainsKey(key))
            {
                byKey[key] = new ConceptMastery
                {
                    Id = state.Id,
                    UserId = userId,
                    TopicId = state.TopicId,
                    ConceptKey = state.ConceptKey,
                    Label = state.Label,
                    MasteryScore = Math.Round(state.MasteryProbability * 100m, 0),
                    Confidence = state.Confidence,
                    UpdatedAt = state.UpdatedAt,
                    LastEvidenceAt = state.LastEvidenceAt
                };
            }
        }

        return byKey.Values
            .OrderByDescending(m => m.UpdatedAt)
            .Select(m => new SkillMastery
            {
                Id = m.Id,
                UserId = userId,
                TopicId = m.TopicId!.Value,
                SubTopicTitle = string.IsNullOrWhiteSpace(m.Label) ? m.ConceptKey : m.Label,
                MasteredAt = m.LastEvidenceAt ?? m.UpdatedAt,
                QuizScore = (int)Math.Clamp(Math.Round(m.MasteryScore, 0), 0, 100)
            });
    }

    private sealed class StringComparerTuple : IEqualityComparer<(Guid TopicId, string Key)>
    {
        public static readonly StringComparerTuple Instance = new();

        public bool Equals((Guid TopicId, string Key) x, (Guid TopicId, string Key) y) =>
            x.TopicId == y.TopicId && string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid TopicId, string Key) obj) =>
            HashCode.Combine(obj.TopicId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key ?? string.Empty));
    }
}

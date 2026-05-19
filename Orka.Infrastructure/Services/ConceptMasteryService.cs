using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class ConceptMasteryService : IConceptMasteryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly ILogger<ConceptMasteryService> _logger;

    public ConceptMasteryService(OrkaDbContext db, ILogger<ConceptMasteryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ConceptMasteryDto>> UpdateFromDiagnosticProfileAsync(
        DiagnosticProfileDto profile,
        CancellationToken ct = default)
    {
        var updated = new List<ConceptMasteryDto>();
        foreach (var item in profile.ConceptMasteries.Where(m => !string.IsNullOrWhiteSpace(m.ConceptKey)))
        {
            var entity = await _db.ConceptMasteries
                .FirstOrDefaultAsync(m =>
                    m.UserId == profile.UserId &&
                    m.TopicId == profile.TopicId &&
                    m.ConceptKey == item.ConceptKey,
                    ct);

            if (entity == null)
            {
                entity = new ConceptMastery
                {
                    Id = Guid.NewGuid(),
                    UserId = profile.UserId,
                    TopicId = profile.TopicId,
                    ConceptGraphSnapshotId = profile.ConceptGraphSnapshotId,
                    ConceptKey = item.ConceptKey,
                    CreatedAt = DateTime.UtcNow
                };
                _db.ConceptMasteries.Add(entity);
            }

            entity.Label = string.IsNullOrWhiteSpace(item.Label) ? item.ConceptKey : item.Label;
            entity.MasteryScore = item.MasteryScore;
            entity.Confidence = item.Confidence;
            entity.RemediationNeed = item.RemediationNeed;
            entity.PracticeReadiness = item.PracticeReadiness;
            entity.MisconceptionEvidenceJson = JsonSerializer.Serialize(item.MisconceptionEvidence, JsonOptions);
            entity.Attempts = item.Attempts;
            entity.Correct = item.Correct;
            entity.LastEvidenceAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;

            item.Id = entity.Id;
            updated.Add(item);
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "[ConceptMastery] Updated {Count} concept mastery rows. UserRef={UserRef} TopicRef={TopicRef}",
            updated.Count,
            LogPrivacyGuard.SafeId(profile.UserId, "usr"),
            LogPrivacyGuard.SafeId(profile.TopicId, "topic"));
        return updated;
    }

    public async Task<IReadOnlyList<ConceptMasteryDto>> GetRecentMasteryAsync(
        Guid userId,
        Guid? topicId,
        int take = 12,
        CancellationToken ct = default)
    {
        var query = _db.ConceptMasteries.AsNoTracking().Where(m => m.UserId == userId);
        if (topicId.HasValue)
        {
            query = query.Where(m => m.TopicId == topicId.Value);
        }

        var rows = await query
            .OrderByDescending(m => m.UpdatedAt)
            .Take(take)
            .ToListAsync(ct);

        return rows.Select(m => new ConceptMasteryDto
        {
            Id = m.Id,
            ConceptKey = m.ConceptKey,
            Label = m.Label,
            MasteryScore = m.MasteryScore,
            Confidence = m.Confidence,
            RemediationNeed = m.RemediationNeed,
            PracticeReadiness = m.PracticeReadiness,
            MisconceptionEvidence = DeserializeList(m.MisconceptionEvidenceJson),
            Attempts = m.Attempts,
            Correct = m.Correct
        }).ToList();
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

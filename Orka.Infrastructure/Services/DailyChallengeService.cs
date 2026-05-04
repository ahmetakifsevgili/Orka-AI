using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class DailyChallengeService : IDailyChallengeService
{
    private readonly OrkaDbContext _db;
    private readonly IXpEventService _xp;

    public DailyChallengeService(OrkaDbContext db, IXpEventService xp)
    {
        _db = db;
        _xp = xp;
    }

    public async Task<DailyChallengeDto> GetTodayAsync(Guid userId, Guid? topicId, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var existing = await _db.DailyChallenges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.TopicId == topicId && c.Date == today, ct);
        if (existing != null) return ToDto(existing);

        var review = await _db.ReviewItems.AsNoTracking()
            .Where(r => r.UserId == userId && r.Status == "active" && r.DueAt <= DateTime.UtcNow)
            .Where(r => !topicId.HasValue || r.TopicId == topicId.Value)
            .OrderBy(r => r.DueAt)
            .FirstOrDefaultAsync(ct);

        var skill = review?.ConceptTag ?? review?.SkillTag ?? "genel tekrar";
        var prompt = $"{skill} icin 2 dakikalik retrieval practice yap: once tanimla, sonra mini ornek coz.";
        var challenge = new DailyChallenge
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            Date = today,
            SourceType = review == null ? "fallback" : "review-item",
            SourceSkillTag = review?.SkillTag ?? skill,
            SourceConceptTag = review?.ConceptTag,
            ReviewItemId = review?.Id,
            QuestionsJson = JsonSerializer.Serialize(new[] { new { prompt } }),
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        _db.DailyChallenges.Add(challenge);
        await _db.SaveChangesAsync(ct);
        return ToDto(challenge);
    }

    public async Task<DailyChallengeSubmissionDto?> SubmitAsync(Guid userId, Guid challengeId, string answer, int quality, CancellationToken ct = default)
    {
        var challenge = await _db.DailyChallenges.FirstOrDefaultAsync(c => c.Id == challengeId && c.UserId == userId, ct);
        if (challenge == null) return null;

        var existing = await _db.DailyChallengeSubmissions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.DailyChallengeId == challengeId, ct);
        var totalBefore = await _db.Users.Where(u => u.Id == userId).Select(u => u.TotalXP).FirstOrDefaultAsync(ct);
        if (existing != null)
            return new DailyChallengeSubmissionDto(existing.Id, challengeId, Duplicate: true, existing.XpAwarded, totalBefore, existing.Quality, existing.CreatedAt);

        quality = Math.Clamp(quality, 0, 5);
        var xp = await _xp.AwardAsync(userId, $"daily:{challenge.Date:yyyyMMdd}:{challenge.Id:N}", "daily_challenge_completed", Math.Max(5, quality * 5), "DailyChallenge", challenge.Id, ct);
        var submission = new DailyChallengeSubmission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DailyChallengeId = challengeId,
            Answer = answer,
            Quality = quality,
            XpAwarded = xp.XpAwarded,
            CreatedAt = DateTime.UtcNow
        };
        _db.DailyChallengeSubmissions.Add(submission);
        challenge.Status = "completed";
        challenge.Score = quality * 20;
        challenge.CorrectCount = quality >= 3 ? 1 : 0;
        challenge.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new DailyChallengeSubmissionDto(submission.Id, challengeId, Duplicate: false, submission.XpAwarded, xp.TotalXP, quality, submission.CreatedAt);
    }

    private static DailyChallengeDto ToDto(DailyChallenge c)
    {
        var prompt = "Kisa bir tekrar yap.";
        try
        {
            using var doc = JsonDocument.Parse(c.QuestionsJson);
            prompt = doc.RootElement.EnumerateArray().FirstOrDefault().GetProperty("prompt").GetString() ?? prompt;
        }
        catch
        {
            // Durable fallback DTO should stay stable even if old JSON is malformed.
        }

        return new DailyChallengeDto(c.Id, c.TopicId, c.Date, c.SourceType, c.SourceSkillTag, c.SourceConceptTag, prompt, c.Status, c.Score, c.CreatedAt);
    }
}

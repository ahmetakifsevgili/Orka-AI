using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class AdaptiveLearningContextBuilder : IAdaptiveLearningContextBuilder
{
    private const int RecentAttemptLimit = 100;
    private readonly OrkaDbContext _db;
    private readonly ILogger<AdaptiveLearningContextBuilder> _logger;

    public AdaptiveLearningContextBuilder(
        OrkaDbContext db,
        ILogger<AdaptiveLearningContextBuilder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AdaptiveLearningContext> BuildAsync(
        Guid userId,
        Guid? topicId,
        string topicTitle,
        string userLevel = "Bilinmiyor",
        CancellationToken ct = default)
    {
        try
        {
            var attempts = await LoadAttemptsAsync(userId, topicId, ct);
            return BuildContext(topicId, topicTitle, userLevel, attempts);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdaptiveLearningContext] Failed to build context. UserId={UserId} TopicId={TopicId}", userId, topicId);
            return Empty(topicId, topicTitle, userLevel);
        }
    }

    private async Task<List<AttemptSnapshot>> LoadAttemptsAsync(Guid userId, Guid? topicId, CancellationToken ct)
    {
        var query = _db.QuizAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId);

        if (topicId.HasValue)
        {
            query = query.Where(a => a.TopicId == topicId.Value);
        }

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(RecentAttemptLimit)
            .Select(a => new AttemptSnapshot(
                a.IsCorrect,
                a.SkillTag,
                a.TopicPath,
                a.CognitiveType,
                a.CreatedAt))
            .ToListAsync(ct);
    }

    private static AdaptiveLearningContext BuildContext(
        Guid? topicId,
        string topicTitle,
        string userLevel,
        List<AttemptSnapshot> attempts)
    {
        if (attempts.Count == 0)
        {
            return Empty(topicId, topicTitle, userLevel);
        }

        var weakSkills = attempts
            .Where(a => !string.IsNullOrWhiteSpace(a.SkillTag))
            .GroupBy(a => Normalize(a.SkillTag!))
            .Select(g =>
            {
                var total = g.Count();
                var wrong = g.Count(a => !a.IsCorrect);
                var accuracy = total == 0 ? 0 : (double)(total - wrong) / total;
                var topicPath = g.Select(a => a.TopicPath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
                return new WeakSkillEntry(g.Key, topicPath, wrong, total, accuracy);
            })
            .Where(s => s.WrongCount > 0)
            .OrderBy(s => s.Accuracy)
            .ThenByDescending(s => s.WrongCount)
            .ThenBy(s => s.Skill, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        var weakConcepts = attempts
            .Where(a => !a.IsCorrect && !string.IsNullOrWhiteSpace(a.SkillTag))
            .GroupBy(a => Normalize(a.SkillTag!))
            .Select(g => new WeakConceptEntry(g.Key, g.Count()))
            .OrderByDescending(c => c.Frequency)
            .ThenBy(c => c.Concept, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        var mistakePatterns = attempts
            .Where(a => !a.IsCorrect && !string.IsNullOrWhiteSpace(a.CognitiveType))
            .GroupBy(a => Normalize(a.CognitiveType!))
            .Select(g => new MistakePatternEntry(g.Key, g.Count(), $"Cognitive type: {g.Key}"))
            .OrderByDescending(p => p.Frequency)
            .ThenBy(p => p.Category, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        var averageAccuracy = attempts.Count == 0
            ? 0
            : attempts.Count(a => a.IsCorrect) / (double)attempts.Count;

        return new AdaptiveLearningContext(
            topicId,
            string.IsNullOrWhiteSpace(topicTitle) ? "Bilinmeyen konu" : topicTitle,
            string.IsNullOrWhiteSpace(userLevel) ? "Bilinmiyor" : userLevel,
            weakSkills,
            weakConcepts,
            mistakePatterns,
            new RecentQuizStats(averageAccuracy, attempts.Count),
            [],
            null,
            null,
            DateTime.UtcNow);
    }

    private static AdaptiveLearningContext Empty(Guid? topicId, string topicTitle, string userLevel) =>
        new(
            topicId,
            string.IsNullOrWhiteSpace(topicTitle) ? "Bilinmeyen konu" : topicTitle,
            string.IsNullOrWhiteSpace(userLevel) ? "Bilinmiyor" : userLevel,
            [],
            [],
            [],
            null,
            [],
            null,
            null,
            DateTime.UtcNow);

    private static string Normalize(string value) => value.Trim();

    private sealed record AttemptSnapshot(
        bool IsCorrect,
        string? SkillTag,
        string? TopicPath,
        string? CognitiveType,
        DateTime CreatedAt);
}

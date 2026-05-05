using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class ReviewSrsService : IReviewSrsService
{
    private readonly OrkaDbContext _db;
    private readonly IXpEventService _xp;
    private readonly INotificationService _notifications;

    public ReviewSrsService(OrkaDbContext db, IXpEventService xp, INotificationService notifications)
    {
        _db = db;
        _xp = xp;
        _notifications = notifications;
    }

    public string BuildReviewKey(Guid? topicId, string? conceptTag, string? skillTag, string? learningObjective, string? topicPath)
    {
        var topic = topicId?.ToString("N") ?? "global";
        var (prefix, value) = FirstNonBlank(
            ("concept", conceptTag),
            ("skill", skillTag),
            ("objective", learningObjective),
            ("topic", topicPath));
        return $"{prefix}:{topic}:{NormalizeKey(value)}";
    }

    public async Task<ReviewItem> EnsureReviewItemAsync(
        Guid userId,
        Guid? topicId,
        string? conceptTag,
        string? skillTag,
        string? learningObjective,
        string? mistakeCategory,
        string? topicPath,
        string? sourceType,
        Guid? sourceId,
        Guid? quizAttemptId,
        Guid? learningSignalId,
        Guid? flashcardId,
        Guid? remediationPlanId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var key = BuildReviewKey(topicId, conceptTag, skillTag, learningObjective, topicPath);
        var existing = await _db.ReviewItems
            .FirstOrDefaultAsync(r => r.UserId == userId && r.ReviewKey == key && r.Status == "active", ct);

        if (existing != null)
        {
            existing.SkillTag = FirstValue(existing.SkillTag, skillTag);
            existing.ConceptTag = FirstValue(existing.ConceptTag, conceptTag);
            existing.LearningObjective = FirstValue(existing.LearningObjective, learningObjective);
            existing.MistakeCategory = FirstValue(existing.MistakeCategory, mistakeCategory);
            existing.SourceType = FirstValue(existing.SourceType, sourceType);
            existing.SourceId ??= sourceId;
            existing.QuizAttemptId ??= quizAttemptId;
            existing.LearningSignalId ??= learningSignalId;
            existing.FlashcardId ??= flashcardId;
            existing.RemediationPlanId ??= remediationPlanId;
            existing.DueAt = existing.DueAt > now ? now : existing.DueAt;
            existing.UpdatedAt = now;
            return existing;
        }

        var item = new ReviewItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ReviewKey = key,
            SkillTag = Clean(skillTag) ?? Clean(topicPath) ?? Clean(learningObjective),
            ConceptTag = Clean(conceptTag),
            LearningObjective = Clean(learningObjective),
            MistakeCategory = Clean(mistakeCategory),
            SourceType = Clean(sourceType) ?? "quiz",
            SourceId = sourceId,
            DueAt = now,
            IntervalDays = 0,
            EaseFactor = 2.5,
            RepetitionCount = 0,
            LapseCount = 0,
            SuccessStreak = 0,
            Status = "active",
            QuizAttemptId = quizAttemptId,
            LearningSignalId = learningSignalId,
            FlashcardId = flashcardId,
            RemediationPlanId = remediationPlanId,
            CreatedAt = now,
            UpdatedAt = now,
            MetadataJson = JsonSerializer.Serialize(new { topicPath })
        };
        _db.ReviewItems.Add(item);
        return item;
    }

    public async Task<IReadOnlyList<DurableReviewItemDto>> GetDueAsync(Guid userId, Guid? topicId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var query = _db.ReviewItems
            .AsNoTracking()
            .Include(r => r.Flashcard)
            .Where(r => r.UserId == userId && r.Status == "active" && r.DueAt <= now);
        if (topicId.HasValue) query = query.Where(r => r.TopicId == topicId.Value);

        return await query
            .OrderBy(r => r.DueAt)
            .ThenByDescending(r => r.LapseCount)
            .Take(50)
            .Select(r => ToDto(r))
            .ToListAsync(ct);
    }

    public async Task<DurableReviewItemDto?> CompleteAsync(
        Guid userId,
        Guid reviewItemId,
        int quality,
        string? responseMode,
        string? notes,
        CancellationToken ct = default)
    {
        var item = await _db.ReviewItems
            .Include(r => r.Flashcard)
            .FirstOrDefaultAsync(r => r.Id == reviewItemId && r.UserId == userId, ct);
        if (item == null) return null;

        ApplyReview(item, quality);
        item.MetadataJson = MergeMetadata(item.MetadataJson, new { responseMode, notes, quality });
        await _xp.AwardAsync(userId, $"review:{reviewItemId}:{item.RepetitionCount}", "review_completed", Math.Clamp(quality, 1, 5) * 3, "ReviewItem", reviewItemId, ct);
        await _notifications.CreateAsync(userId, new CreateNotificationRequest(
            "review_due",
            "Review updated",
            $"{DisplayTitle(item)} review schedule updated.",
            RelatedEntityType: "ReviewItem",
            RelatedEntityId: item.Id), ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    public static void ApplyReview(ReviewItem item, int quality)
    {
        quality = Math.Clamp(quality, 0, 5);
        var now = DateTime.UtcNow;
        item.LastReviewedAt = now;
        item.UpdatedAt = now;
        if (quality < 3)
        {
            item.LapseCount += 1;
            item.SuccessStreak = 0;
            item.RepetitionCount = 0;
            item.IntervalDays = 1;
            item.EaseFactor = Math.Max(1.3, item.EaseFactor - 0.2);
        }
        else
        {
            item.SuccessStreak += 1;
            item.RepetitionCount += 1;
            item.EaseFactor = Math.Max(1.3, item.EaseFactor + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02)));
            item.IntervalDays = item.RepetitionCount switch
            {
                1 => 1,
                2 => 3,
                _ => Math.Max(4, (int)Math.Round(Math.Max(1, item.IntervalDays) * item.EaseFactor))
            };
        }
        item.DueAt = now.Date.AddDays(item.IntervalDays);
    }

    public static DurableReviewItemDto ToDto(ReviewItem r) =>
        new(
            r.Id,
            r.TopicId,
            r.ReviewKey,
            DisplayTitle(r),
            r.SkillTag,
            r.ConceptTag,
            r.LearningObjective,
            r.MistakeCategory,
            r.SourceType,
            r.SourceId,
            r.DueAt,
            r.LastReviewedAt,
            r.IntervalDays,
            r.EaseFactor,
            r.RepetitionCount,
            r.LapseCount,
            r.SuccessStreak,
            r.Status,
            r.FlashcardId,
            r.Flashcard?.Front,
            r.Flashcard?.Back);

    private static string DisplayTitle(ReviewItem r) =>
        FirstNonBlank(("concept", r.ConceptTag), ("skill", r.SkillTag), ("objective", r.LearningObjective), ("review", r.ReviewKey)).Value;

    private static (string Prefix, string Value) FirstNonBlank(params (string Prefix, string? Value)[] values)
    {
        foreach (var item in values)
        {
            if (!string.IsNullOrWhiteSpace(item.Value))
                return (item.Prefix, item.Value.Trim());
        }
        return ("topic", "general");
    }

    private static string NormalizeKey(string value)
    {
        var cleaned = new string(value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        while (cleaned.Contains("--", StringComparison.Ordinal)) cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        return cleaned.Trim('-').Length == 0 ? "general" : cleaned.Trim('-')[..Math.Min(180, cleaned.Trim('-').Length)];
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? FirstValue(string? current, string? next) => string.IsNullOrWhiteSpace(current) ? Clean(next) : current;

    private static string? MergeMetadata(string? existingJson, object addition)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(existingJson);
                foreach (var p in doc.RootElement.EnumerateObject())
                    dict[p.Name] = p.Value.ToString();
            }
            catch (JsonException)
            {
                dict["previous"] = existingJson;
            }
        }
        foreach (var prop in addition.GetType().GetProperties())
        {
            var value = prop.GetValue(addition);
            if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                dict[prop.Name] = value;
        }
        return JsonSerializer.Serialize(dict);
    }
}

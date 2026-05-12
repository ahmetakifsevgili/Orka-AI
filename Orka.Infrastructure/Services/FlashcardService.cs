using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class FlashcardService : IFlashcardService
{
    private readonly OrkaDbContext _db;
    private readonly IReviewSrsService _reviews;

    public FlashcardService(OrkaDbContext db, IReviewSrsService reviews)
    {
        _db = db;
        _reviews = reviews;
    }

    public async Task<FlashcardDto> CreateAsync(Guid userId, CreateFlashcardRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Front) || string.IsNullOrWhiteSpace(request.Back))
            throw new InvalidOperationException("Flashcard front/back zorunlu.");

        if (request.TopicId.HasValue && !await _db.Topics.AnyAsync(t => t.Id == request.TopicId && t.UserId == userId, ct))
            throw new KeyNotFoundException("Topic bulunamadı.");

        if (request.LearningSourceId.HasValue && !await _db.LearningSources.AnyAsync(s => s.Id == request.LearningSourceId && s.UserId == userId && !s.IsDeleted, ct))
            throw new KeyNotFoundException("Source bulunamadı.");

        var now = DateTime.UtcNow;
        var flashcard = new Flashcard
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = request.TopicId,
            LearningSourceId = request.LearningSourceId,
            WikiPageId = request.WikiPageId,
            Front = request.Front.Trim(),
            Back = request.Back.Trim(),
            Hint = Clean(request.Hint),
            SkillTag = Clean(request.SkillTag),
            ConceptTag = Clean(request.ConceptTag),
            LearningObjective = Clean(request.LearningObjective),
            Difficulty = Clean(request.Difficulty),
            CreatedFrom = Clean(request.CreatedFrom) ?? "manual",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Flashcards.Add(flashcard);
        var review = await _reviews.EnsureReviewItemAsync(
            userId,
            request.TopicId,
            request.ConceptTag,
            request.SkillTag,
            request.LearningObjective,
            request.Front,
            request.CreatedFrom,
            "flashcard",
            flashcard.Id,
            null,
            null,
            flashcard.Id,
            null,
            ct);
        review.FlashcardId = flashcard.Id;
        await _db.SaveChangesAsync(ct);
        return ToDto(flashcard, review.Id);
    }

    public async Task<IReadOnlyList<FlashcardDto>> ListAsync(Guid userId, Guid? topicId, CancellationToken ct = default)
    {
        var query = _db.Flashcards.AsNoTracking().Where(f => f.UserId == userId && f.Status != "deleted");
        if (topicId.HasValue) query = query.Where(f => f.TopicId == topicId.Value);
        var cards = await query.OrderByDescending(f => f.CreatedAt).Take(100).ToListAsync(ct);
        var ids = cards.Select(c => c.Id).ToList();
        var reviewByFlashcard = await _db.ReviewItems.AsNoTracking()
            .Where(r => r.FlashcardId.HasValue && ids.Contains(r.FlashcardId.Value) && r.UserId == userId && r.Status == "active")
            .ToDictionaryAsync(r => r.FlashcardId!.Value, r => r.Id, ct);
        return cards.Select(c => ToDto(c, reviewByFlashcard.TryGetValue(c.Id, out var rid) ? rid : null)).ToList();
    }

    public async Task<FlashcardDto?> ReviewAsync(Guid userId, Guid flashcardId, int quality, string? notes, CancellationToken ct = default)
    {
        var card = await _db.Flashcards.FirstOrDefaultAsync(f => f.Id == flashcardId && f.UserId == userId && f.Status != "deleted", ct);
        if (card == null) return null;
        var review = await _db.ReviewItems.FirstOrDefaultAsync(r => r.UserId == userId && r.FlashcardId == flashcardId && r.Status == "active", ct)
            ?? await _reviews.EnsureReviewItemAsync(userId, card.TopicId, card.ConceptTag, card.SkillTag, card.LearningObjective, null, card.Front, "flashcard", card.Id, null, null, card.Id, null, ct);
        ReviewSrsService.ApplyReview(review, quality);
        card.LastReviewedAt = DateTime.UtcNow;
        card.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(card, review.Id);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid flashcardId, CancellationToken ct = default)
    {
        var card = await _db.Flashcards.FirstOrDefaultAsync(f => f.Id == flashcardId && f.UserId == userId, ct);
        if (card == null) return false;
        card.Status = "deleted";
        card.UpdatedAt = DateTime.UtcNow;
        var reviews = await _db.ReviewItems.Where(r => r.UserId == userId && r.FlashcardId == flashcardId && r.Status == "active").ToListAsync(ct);
        foreach (var review in reviews)
        {
            review.Status = "archived";
            review.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static FlashcardDto ToDto(Flashcard f, Guid? reviewItemId) =>
        new(f.Id, f.TopicId, f.LearningSourceId, f.WikiPageId, f.Front, f.Back, f.Hint, f.SkillTag, f.ConceptTag, f.LearningObjective, f.Difficulty, f.Status, f.CreatedFrom, reviewItemId, f.CreatedAt, f.UpdatedAt);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

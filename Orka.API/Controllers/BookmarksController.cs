using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/bookmarks")]
public class BookmarksController : ControllerBase
{
    private readonly OrkaDbContext _db;

    public BookmarksController(OrkaDbContext db)
    {
        _db = db;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? topicId, CancellationToken ct)
    {
        var userId = GetUserId();
        var query = _db.Bookmarks
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.Status == "active");

        if (topicId.HasValue)
            query = query.Where(b => b.TopicId == topicId.Value);

        var items = await query
            .OrderByDescending(b => b.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        return Ok(items.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBookmarkRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var resolved = await ResolveOwnedTargetAsync(userId, request, ct);
        if (!resolved.Success)
            return resolved.NotFound ? NotFound(new { message = resolved.Message }) : BadRequest(new { message = resolved.Message });

        if (request.MessageId.HasValue)
        {
            var existing = await _db.Bookmarks
                .AsNoTracking()
                .Where(b => b.UserId == userId && b.MessageId == request.MessageId && b.Status == "active")
                .FirstOrDefaultAsync(ct);

            if (existing != null)
                return Ok(ToDto(existing));
        }

        var now = DateTime.UtcNow;
        var bookmark = new Bookmark
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = resolved.TopicId,
            SessionId = resolved.SessionId,
            MessageId = request.MessageId,
            LearningSourceId = request.LearningSourceId,
            WikiPageId = request.WikiPageId,
            ReviewItemId = request.ReviewItemId,
            FlashcardId = request.FlashcardId,
            Title = NormalizeTitle(request.Title, resolved.DefaultTitle),
            Note = NormalizeOptional(request.Note),
            Quote = NormalizeOptional(request.Quote ?? resolved.DefaultQuote),
            TagsJson = SerializeTags(request.Tags),
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Bookmarks.Add(bookmark);
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(bookmark));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBookmarkRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var bookmark = await _db.Bookmarks
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId && b.Status == "active", ct);

        if (bookmark == null)
            return NotFound(new { message = "Bookmark bulunamadi." });

        if (request.Title != null)
            bookmark.Title = NormalizeTitle(request.Title, bookmark.Title);
        if (request.Note != null)
            bookmark.Note = NormalizeOptional(request.Note);
        if (request.Quote != null)
            bookmark.Quote = NormalizeOptional(request.Quote);
        if (request.Tags != null)
            bookmark.TagsJson = SerializeTags(request.Tags);

        bookmark.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(bookmark));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var bookmark = await _db.Bookmarks
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId && b.Status == "active", ct);

        if (bookmark == null)
            return NotFound(new { message = "Bookmark bulunamadi." });

        bookmark.Status = "deleted";
        bookmark.DeletedAt = DateTime.UtcNow;
        bookmark.UpdatedAt = bookmark.DeletedAt.Value;
        await _db.SaveChangesAsync(ct);
        return Ok(new { deleted = true, id });
    }

    private async Task<(bool Success, bool NotFound, string Message, Guid? TopicId, Guid? SessionId, string DefaultTitle, string? DefaultQuote)>
        ResolveOwnedTargetAsync(Guid userId, CreateBookmarkRequest request, CancellationToken ct)
    {
        if (!request.MessageId.HasValue &&
            !request.TopicId.HasValue &&
            !request.LearningSourceId.HasValue &&
            !request.WikiPageId.HasValue &&
            !request.ReviewItemId.HasValue &&
            !request.FlashcardId.HasValue)
        {
            return (false, false, "En az bir bookmark hedefi verilmelidir.", null, null, "Bookmark", null);
        }

        Guid? topicId = request.TopicId;
        Guid? sessionId = request.SessionId;
        string title = "Bookmark";
        string? quote = null;

        if (request.TopicId.HasValue)
        {
            var topic = await _db.Topics.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TopicId && t.UserId == userId, ct);
            if (topic == null) return (false, true, "Konu bulunamadi.", null, null, title, null);
            title = topic.Title;
        }

        if (request.MessageId.HasValue)
        {
            var message = await _db.Messages.AsNoTracking()
                .Include(m => m.Session)
                .FirstOrDefaultAsync(m => m.Id == request.MessageId && m.UserId == userId, ct);
            if (message == null) return (false, true, "Mesaj bulunamadi.", null, null, title, null);
            sessionId = message.SessionId;
            topicId ??= message.Session.TopicId;
            title = string.IsNullOrWhiteSpace(message.TopicTitle) ? "Chat bookmark" : message.TopicTitle;
            quote = Snippet(message.Content, 500);
        }

        if (request.LearningSourceId.HasValue)
        {
            var source = await _db.LearningSources.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.LearningSourceId && s.UserId == userId && !s.IsDeleted, ct);
            if (source == null) return (false, true, "Kaynak bulunamadi.", null, null, title, null);
            topicId ??= source.TopicId;
            sessionId ??= source.SessionId;
            title = source.Title;
        }

        if (request.WikiPageId.HasValue)
        {
            var wiki = await _db.WikiPages.AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == request.WikiPageId && w.UserId == userId, ct);
            if (wiki == null) return (false, true, "Wiki sayfasi bulunamadi.", null, null, title, null);
            topicId ??= wiki.TopicId;
            title = wiki.Title;
        }

        if (request.ReviewItemId.HasValue)
        {
            var review = await _db.ReviewItems.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request.ReviewItemId && r.UserId == userId, ct);
            if (review == null) return (false, true, "Review item bulunamadi.", null, null, title, null);
            topicId ??= review.TopicId;
            title = review.ConceptTag ?? review.SkillTag ?? review.LearningObjective ?? review.ReviewKey;
        }

        if (request.FlashcardId.HasValue)
        {
            var flashcard = await _db.Flashcards.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == request.FlashcardId && f.UserId == userId && f.Status != "deleted", ct);
            if (flashcard == null) return (false, true, "Flashcard bulunamadi.", null, null, title, null);
            topicId ??= flashcard.TopicId;
            title = Snippet(flashcard.Front, 80);
            quote ??= Snippet(flashcard.Back, 500);
        }

        return (true, false, string.Empty, topicId, sessionId, title, quote);
    }

    private static BookmarkDto ToDto(Bookmark b) => new(
        b.Id,
        b.TopicId,
        b.SessionId,
        b.MessageId,
        b.LearningSourceId,
        b.WikiPageId,
        b.ReviewItemId,
        b.FlashcardId,
        b.Title,
        b.Note,
        b.Quote,
        DeserializeTags(b.TagsJson),
        b.Status,
        b.CreatedAt,
        b.UpdatedAt);

    private static string NormalizeTitle(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.Length > 160 ? text[..160] : text;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > 4000 ? trimmed[..4000] : trimmed;
    }

    private static string? SerializeTags(IReadOnlyList<string>? tags)
    {
        var normalized = tags?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        return normalized is { Count: > 0 } ? JsonSerializer.Serialize(normalized) : null;
    }

    private static IReadOnlyList<string> DeserializeTags(string? tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(tagsJson) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string Snippet(string content, int max)
    {
        var trimmed = content.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}

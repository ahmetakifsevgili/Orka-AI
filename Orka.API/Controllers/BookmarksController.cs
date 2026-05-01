using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/bookmarks")]
public class BookmarksController : ControllerBase
{
    private readonly OrkaDbContext _dbContext;
    private readonly ILogger<BookmarksController> _logger;

    public BookmarksController(OrkaDbContext dbContext, ILogger<BookmarksController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        var bookmarks = await _dbContext.Bookmarks
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .Take(200)
            .Select(b => new
            {
                b.Id,
                b.MessageId,
                TopicId = (Guid?)b.Message.Session.TopicId,
                TopicTitle = b.Message.Session.Topic != null ? b.Message.Session.Topic.Title : null,
                b.Note,
                b.Tag,
                MessageRole = b.Message.Role,
                MessageContent = b.Message.Content,
                MessageCreatedAt = b.Message.CreatedAt,
                b.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var result = bookmarks.Select(b => new BookmarkDto(
            b.Id,
            b.MessageId,
            b.TopicId,
            b.TopicTitle,
            b.Note,
            b.Tag,
            b.MessageRole,
            Snippet(b.MessageContent),
            b.MessageCreatedAt,
            b.CreatedAt)).ToList();

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBookmarkRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        var message = await _dbContext.Messages
            .AsNoTracking()
            .Where(m => m.Id == request.MessageId && m.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Mesaj bulunamadı.");

        // Idempotent: aynı kullanıcı + mesaj çifti varsa eski kaydı döndür.
        var existing = await _dbContext.Bookmarks
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.UserId == userId && b.MessageId == request.MessageId, cancellationToken);
        if (existing is not null)
        {
            return Ok(new { id = existing.Id, alreadyExisted = true });
        }

        var bookmark = new Bookmark
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MessageId = request.MessageId,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            Tag = string.IsNullOrWhiteSpace(request.Tag) ? null : request.Tag.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Bookmarks.Add(bookmark);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[Bookmarks] Created BookmarkId={Id} MessageId={MessageId} UserId={UserId}",
            bookmark.Id, request.MessageId, userId);

        return Ok(new { id = bookmark.Id, alreadyExisted = false });
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBookmarkRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        var bookmark = await _dbContext.Bookmarks
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Bookmark bulunamadı.");

        if (request.Note is not null)
            bookmark.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (request.Tag is not null)
            bookmark.Tag = string.IsNullOrWhiteSpace(request.Tag) ? null : request.Tag.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { id = bookmark.Id });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        var bookmark = await _dbContext.Bookmarks
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, cancellationToken);

        if (bookmark is null) return NoContent();

        _dbContext.Bookmarks.Remove(bookmark);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static string Snippet(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        var trimmed = content.Trim();
        return trimmed.Length > 240 ? trimmed[..240] + "…" : trimmed;
    }
}

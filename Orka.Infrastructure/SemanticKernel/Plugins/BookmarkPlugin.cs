using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class BookmarkPlugin
{
    private readonly OrkaDbContext _db;

    public BookmarkPlugin(OrkaDbContext db)
    {
        _db = db;
    }

    [KernelFunction, Description("List a user's latest bookmarks, optionally scoped to a topic.")]
    public async Task<string> ListBookmarks(Guid userId, Guid? topicId = null)
    {
        var query = _db.Bookmarks.AsNoTracking()
            .Where(b => b.UserId == userId && b.Status == "active");
        if (topicId.HasValue) query = query.Where(b => b.TopicId == topicId);

        var bookmarks = await query
            .OrderByDescending(b => b.CreatedAt)
            .Take(10)
            .Select(b => new { b.Id, b.Title, b.Note })
            .ToListAsync();

        if (bookmarks.Count == 0) return "Aktif bookmark yok.";
        return string.Join("\n", bookmarks.Select(b => $"- {b.Id}: {b.Title} {b.Note}".Trim()));
    }

    [KernelFunction, Description("Create a lightweight text bookmark for a user.")]
    public async Task<string> CreateBookmark(Guid userId, string title, string? note = null, Guid? topicId = null)
    {
        if (topicId.HasValue)
        {
            var ownsTopic = await _db.Topics.AnyAsync(t => t.Id == topicId && t.UserId == userId);
            if (!ownsTopic) return "Konu bulunamadı.";
        }

        var now = DateTime.UtcNow;
        var bookmark = new Bookmark
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            Title = string.IsNullOrWhiteSpace(title) ? "Tutor bookmark" : title.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Bookmarks.Add(bookmark);
        await _db.SaveChangesAsync();
        return $"Bookmark oluşturuldu: {bookmark.Id}";
    }
}

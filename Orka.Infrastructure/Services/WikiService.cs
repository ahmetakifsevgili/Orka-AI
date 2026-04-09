using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Core.DTOs;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class WikiService : IWikiService
{
    // [HATA-6 DÜZELTMESİ]: IServiceScopeFactory — her metot kendi scope'unu açar (thread-safe)
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Orka.Core.Interfaces.IMistralService _mistralService;
    private readonly ILogger<WikiService> _logger;

    public WikiService(IServiceScopeFactory scopeFactory,
        Orka.Core.Interfaces.IMistralService mistralService,
        ILogger<WikiService> logger)
    {
        _scopeFactory = scopeFactory;
        _mistralService = mistralService;
        _logger = logger;
    }

    public async Task<IEnumerable<WikiPage>> GetTopicWikiPagesAsync(Guid topicId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        return await db.WikiPages
            .Where(w => w.TopicId == topicId && w.UserId == userId)
            .OrderBy(w => w.OrderIndex)
            .ToListAsync();
    }

    public async Task<WikiPage?> GetWikiPageAsync(Guid pageId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        return await db.WikiPages
            .Include(w => w.Blocks)
            .Include(w => w.Sources)
            .FirstOrDefaultAsync(w => w.Id == pageId && w.UserId == userId);
    }

    public async Task<WikiBlock> AddUserNoteAsync(Guid pageId, Guid userId, string content)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var page = await db.WikiPages.FirstOrDefaultAsync(w => w.Id == pageId && w.UserId == userId)
            ?? throw new Exception("Wiki sayfası bulunamadı.");

        var maxOrder = await db.WikiBlocks
            .Where(b => b.WikiPageId == pageId)
            .MaxAsync(b => (int?)b.OrderIndex) ?? 0;

        var block = new WikiBlock
        {
            Id = Guid.NewGuid(),
            WikiPageId = pageId,
            BlockType = WikiBlockType.UserNote,
            Title = "Notum",
            Content = content,
            Source = "user",
            OrderIndex = maxOrder + 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.WikiBlocks.Add(block);
        await db.SaveChangesAsync();
        return block;
    }

    public async Task UpdateWikiBlockAsync(Guid blockId, Guid userId, string? title, string? content)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var block = await db.WikiBlocks
            .Include(b => b.WikiPage)
            .FirstOrDefaultAsync(b => b.Id == blockId && b.WikiPage.UserId == userId);

        if (block == null) return;

        if (title != null) block.Title = title;
        if (content != null) block.Content = content;
        block.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task DeleteWikiBlockAsync(Guid blockId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var block = await db.WikiBlocks
            .Include(b => b.WikiPage)
            .FirstOrDefaultAsync(b => b.Id == blockId && b.WikiPage.UserId == userId);

        if (block == null) return;

        if (block.BlockType != WikiBlockType.UserNote)
            throw new Exception("Sadece kullanıcı notları silinebilir.");

        db.WikiBlocks.Remove(block);
        await db.SaveChangesAsync();
    }

    public async Task AutoUpdateWikiAsync(Guid topicId, string aiContent, string userQuestion, string modelUsed)
    {
        // [HATA-6 DÜZELTMESİ]: Her çağrı kendi scope'unu açarak thread-safe çalışır
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var page = await db.WikiPages
            .Where(w => w.TopicId == topicId && (w.Status == "pending" || w.Status == "learning"))
            .OrderBy(w => w.OrderIndex)
            .FirstOrDefaultAsync();

        if (page == null)
        {
            _logger.LogDebug("[WIKI] Aktif wiki sayfası bulunamadı, topicId={TopicId}", topicId);
            return;
        }

        if (page.Status == "pending")
        {
            page.Status = "learning";
            page.UpdatedAt = DateTime.UtcNow;
        }

        var maxOrder = await db.WikiBlocks
            .Where(b => b.WikiPageId == page.Id)
            .MaxAsync(b => (int?)b.OrderIndex) ?? 0;

        var block = new WikiBlock
        {
            Id = Guid.NewGuid(),
            WikiPageId = page.Id,
            BlockType = WikiBlockType.Concept,
            Title = TruncateTitle(userQuestion, 60),
            Content = aiContent,
            Source = modelUsed,
            OrderIndex = maxOrder + 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.WikiBlocks.Add(block);

        // [ANAYASA MANDATE 3]: Her ders sonu QuizBlock eklenir — Mistral (Curator) ajanı
        try
        {
            var questions = await _mistralService.GenerateReinforcementQuestionsAsync(aiContent);
            var quizBlock = new WikiBlock
            {
                Id = Guid.NewGuid(),
                WikiPageId = page.Id,
                BlockType = WikiBlockType.Quiz,
                Title = "💡 Pekiştirme Soruları",
                Content = questions,
                Source = "mistral-curator",
                OrderIndex = maxOrder + 2,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.WikiBlocks.Add(quizBlock);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WIKI CURATOR] Quiz üretimi başarısız — ders akışı engellenmedi.");
        }

        await db.SaveChangesAsync();
    }

    private static string TruncateTitle(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "Başlıksız";
        if (text.Length <= maxLength) return text;
        return text[..maxLength].TrimEnd() + "...";
    }
}

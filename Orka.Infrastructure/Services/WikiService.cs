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
    private readonly IGroqService _groq;
    private readonly ILogger<WikiService> _logger;

    public WikiService(IServiceScopeFactory scopeFactory,
        IGroqService groq,
        ILogger<WikiService> logger)
    {
        _scopeFactory = scopeFactory;
        _groq = groq;
        _logger = logger;
    }

    public async Task<IEnumerable<WikiPage>> GetTopicWikiPagesAsync(Guid topicId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        // Önce kullanıcının kendi sayfalarını bul
        var pages = await db.WikiPages
            .Include(w => w.Blocks)
            .Where(w => w.TopicId == topicId && w.UserId == userId)
            .OrderBy(w => w.OrderIndex)
            .ToListAsync();

        if (pages.Count > 0) return pages;

        // Fallback: Sayfa Topic'e ait ama UserId farklı kaydedilmişse,
        // Topic sahibini kontrol et ve doğruysa döndür
        var topic = await db.Topics.FindAsync(topicId);
        if (topic != null && topic.UserId == userId)
        {
            // UserId eşleşmemiş olabilir (eski kayıtlar / Albert flow) — erişim ver
            pages = await db.WikiPages
                .Include(w => w.Blocks)
                .Where(w => w.TopicId == topicId)
                .OrderBy(w => w.OrderIndex)
                .ToListAsync();
        }

        return pages;
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
            var topic = await db.Topics.FindAsync(topicId);

            if (topic == null) return;

            // UserId: topic'ten al (en güvenilir kaynak)
            var resolvedUserId = topic.UserId;

            _logger.LogDebug("[WIKI] Aktif wiki sayfası bulunamadı, topicId={TopicId}. Yeni wiki sayfası oluşturuluyor.", topicId);

            page = new WikiPage
            {
                Id = Guid.NewGuid(),
                TopicId = topicId,
                UserId = resolvedUserId,
                Title = topic.Title,
                Status = "learning",
                OrderIndex = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.WikiPages.Add(page);
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
            Content = EnsureMarkdownIntegrity(aiContent), // Format onarıcı eklendi
            Source = modelUsed,
            OrderIndex = maxOrder + 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.WikiBlocks.Add(block);

        // QuizBlock: Her wiki güncellenmesinde Groq ile pekiştirme soruları üretilir
        try
        {
            var prompt = $@"Aşağıdaki ders içeriğine dayanarak 3-5 adet pekiştirme sorusu hazırla.
Sorular kısa ve net cevaplı olmalı. Sadece soruları maddeler halinde dön.

Ders İçeriği:
{aiContent}";

            var questions = await _groq.GenerateResponseAsync(
                "Sen bir eğitim küratörüsün. Sadece pekiştirme soruları hazırlarsın.",
                prompt);

            var quizBlock = new WikiBlock
            {
                Id = Guid.NewGuid(),
                WikiPageId = page.Id,
                BlockType = WikiBlockType.Quiz,
                Title = "💡 Pekiştirme Soruları",
                Content = questions,
                Source = "groq-curator",
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

    public async Task<string> GetWikiFullContentAsync(Guid topicId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var pages = await db.WikiPages
            .Include(p => p.Blocks)
            .Where(p => p.TopicId == topicId && p.UserId == userId)
            .OrderBy(p => p.OrderIndex)
            .ToListAsync();

        if (!pages.Any()) return string.Empty;

        var fullText = new System.Text.StringBuilder();
        foreach (var page in pages)
        {
            fullText.AppendLine($"# {page.Title}");
            foreach (var block in page.Blocks.OrderBy(b => b.OrderIndex))
            {
                if (!string.IsNullOrEmpty(block.Title)) fullText.AppendLine($"## {block.Title}");
                fullText.AppendLine(block.Content);
                fullText.AppendLine();
            }
        }

        return fullText.ToString();
    }

    private static string EnsureMarkdownIntegrity(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;

        // 1. Açık kalan kod bloklarını (```) kapat
        int codeBlockCount = 0;
        int lastIdx = 0;
        while ((lastIdx = content.IndexOf("```", lastIdx)) != -1)
        {
            codeBlockCount++;
            lastIdx += 3;
        }

        if (codeBlockCount % 2 != 0)
        {
            content += "\n```"; // Eksik bloğu kapat
        }

        // 2. Basit HTML/Tablo tag koruması (isteğe bağlı genişletilebilir)
        // Şimdilik sadece Markdown hiyerarşisine odaklanıyoruz.

        return content;
    }

    private static string TruncateTitle(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "Başlıksız";
        if (text.Length <= maxLength) return text;
        return text[..maxLength].TrimEnd() + "...";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class TopicService : ITopicService
{
    private readonly OrkaDbContext _dbContext;
    private readonly IGroqService _groqService;

    public TopicService(OrkaDbContext dbContext, IGroqService groqService)
    {
        _dbContext = dbContext;
        _groqService = groqService;
    }

    public async Task<IEnumerable<Topic>> GetUserTopicsAsync(Guid userId)
    {
        return await _dbContext.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsArchived)
            .OrderByDescending(t => t.LastAccessedAt)
            .ToListAsync();
    }

    public async Task<(Topic Topic, Session Session)> CreateDiscoveryTopicAsync(Guid userId, string title, string? routeCategory = null)
    {
        var emoji = GuessEmoji(title);
        // Fallback: Eğer routeCategory Chat ise Chat olarak kalır, değilse eski GuessCategory mantığına devredilmez, Plan olur.
        var category = !string.IsNullOrEmpty(routeCategory) ? routeCategory : "Plan";

        var topic = new Topic
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Emoji = emoji,
            Category = category,
            CurrentPhase = TopicPhase.Discovery,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };
        _dbContext.Topics.Add(topic);

        var session = new Session
        {
            Id = Guid.NewGuid(),
            TopicId = topic.Id,
            UserId = userId,
            SessionNumber = 1,
            CurrentState = SessionState.Learning,
            TotalTokensUsed = 0,
            TotalCostUSD = 0m,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Sessions.Add(session);

        await _dbContext.SaveChangesAsync();
        return (topic, session);
    }

    public async Task<List<WikiPage>> GenerateAndApplyPlanAsync(Guid topicId, string intent = "genel öğrenme", string level = "Beginner")
    {
        var topic = await _dbContext.Topics.FindAsync(topicId)
            ?? throw new Exception("Konu bulunamadı.");

        topic.LanguageLevel = level;
        topic.CurrentPhase = TopicPhase.Planning;

        var wikiPages = new List<WikiPage>();
        try
        {
            var planJson = await _groqService.GeneratePlanAsync(topic.Title, intent, level);
            var subTopics = ParsePlanJson(planJson);

            for (int i = 0; i < subTopics.Count; i++)
            {
                var page = new WikiPage
                {
                    Id = Guid.NewGuid(),
                    TopicId = topic.Id,
                    UserId = topic.UserId,
                    Title = subTopics[i],
                    OrderIndex = i + 1,
                    Status = "pending",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.WikiPages.Add(page);
                wikiPages.Add(page);
            }

            topic.TotalSections = subTopics.Count;
            topic.CurrentPhase = TopicPhase.ActiveStudy;
        }
        catch (Exception)
        {
            var defaultPage = new WikiPage { Id = Guid.NewGuid(), TopicId = topic.Id, UserId = topic.UserId, Title = "Genel Bilgiler", OrderIndex = 1, Status = "pending", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            _dbContext.WikiPages.Add(defaultPage);
            wikiPages.Add(defaultPage);
            topic.TotalSections = 1;
            topic.CurrentPhase = TopicPhase.ActiveStudy;
        }

        await _dbContext.SaveChangesAsync();
        return wikiPages;
    }

    public async Task<(Topic Topic, Session Session, List<WikiPage> WikiPages)> CreateTopicWithPlanAsync(Guid userId, string title, string intent = "genel öğrenme", string level = "orta")
    {
        var (topic, session) = await CreateDiscoveryTopicAsync(userId, title);
        var wikiPages = await GenerateAndApplyPlanAsync(topic.Id, level);
        
        // Hızlı akışta direkt ders moduna geçilebilir
        topic.CurrentPhase = TopicPhase.ActiveStudy;
        await _dbContext.SaveChangesAsync();

        return (topic, session, wikiPages);
    }

    public async Task<Topic?> GetTopicByIdAsync(Guid topicId, Guid userId)
    {
        return await _dbContext.Topics
            .Include(t => t.Sessions)
            .Include(t => t.WikiPages)
            .FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId);
    }

    public async Task UpdateTopicAsync(Guid topicId, Guid userId, string? title, string? emoji, bool? isArchived)
    {
        var topic = await _dbContext.Topics.FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId);
        if (topic == null) return;

        if (title != null) topic.Title = title;
        if (emoji != null) topic.Emoji = emoji;
        if (isArchived.HasValue) topic.IsArchived = isArchived.Value;

        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteTopicAsync(Guid topicId, Guid userId)
    {
        var topic = await _dbContext.Topics.FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId);
        if (topic == null) return;

        // 1. Alt konuları sil (self-referencing, NO_ACTION FK)
        var subTopics = await _dbContext.Topics.Where(t => t.ParentTopicId == topicId).ToListAsync();
        _dbContext.Topics.RemoveRange(subTopics);

        // 2. Session'ların mesajlarını sil (NO_ACTION FK — manuel cascade)
        var sessions = await _dbContext.Sessions
            .Include(s => s.Messages)
            .Where(s => s.TopicId == topicId)
            .ToListAsync();
        foreach (var session in sessions)
            _dbContext.Messages.RemoveRange(session.Messages);
        _dbContext.Sessions.RemoveRange(sessions);

        // 3. QuizAttempts sil (NO_ACTION FK — manuel cascade)
        var quizAttempts = await _dbContext.QuizAttempts.Where(q => q.TopicId == topicId).ToListAsync();
        _dbContext.QuizAttempts.RemoveRange(quizAttempts);

        // 4. Konuyu sil (WikiPages + WikiBlocks DB CASCADE ile silinir)
        _dbContext.Topics.Remove(topic);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateLastAccessedAsync(Guid topicId)
    {
        var topic = await _dbContext.Topics.FindAsync(topicId);
        if (topic != null)
        {
            topic.LastAccessedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }

    // ITopicService uyumluluğu için
    public async Task<Topic> CreateTopicAsync(Guid userId, string title, string? emoji, string? category)
    {
        var (topic, _) = await CreateDiscoveryTopicAsync(userId, title, category);
        return topic;
    }

    public async Task<Topic?> GetTopicByIdAsync(Guid topicId)
    {
        return await _dbContext.Topics
            .Include(t => t.Sessions)
            .Include(t => t.WikiPages)
            .FirstOrDefaultAsync(t => t.Id == topicId);
    }

    public async Task UpdateTopicAsync(Guid topicId, string? title, string? emoji, bool? isArchived)
    {
        var topic = await _dbContext.Topics.FindAsync(topicId);
        if (topic == null) return;
        if (title != null) topic.Title = title;
        if (emoji != null) topic.Emoji = emoji;
        if (isArchived.HasValue) topic.IsArchived = isArchived.Value;
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteTopicAsync(Guid topicId)
    {
        var topic = await _dbContext.Topics.FindAsync(topicId);
        if (topic == null) return;
        _dbContext.Topics.Remove(topic);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<List<Topic>> GetSubTopicsAsync(Guid parentTopicId)
    {
        return await _dbContext.Topics
            .AsNoTracking()
            .Where(t => t.ParentTopicId == parentTopicId)
            .OrderBy(t => t.Order)
            .ToListAsync();
    }

    public async Task<List<Topic>> GetOrderedLessonsAsync(Guid rootTopicId, Guid userId)
    {
        var children = await _dbContext.Topics
            .AsNoTracking()
            .Where(t => t.ParentTopicId == rootTopicId && t.UserId == userId)
            .OrderBy(t => t.Order)
            .ToListAsync();

        if (children.Count == 0) return new List<Topic>();

        var childIds = children.Select(c => c.Id).ToList();

        var grandchildren = await _dbContext.Topics
            .AsNoTracking()
            .Where(t => t.ParentTopicId.HasValue && childIds.Contains(t.ParentTopicId.Value) && t.UserId == userId)
            .ToListAsync();

        if (grandchildren.Count == 0) return children;

        var ordered = new List<Topic>();
        foreach (var module in children)
        {
            var lessons = grandchildren
                .Where(g => g.ParentTopicId == module.Id)
                .OrderBy(g => g.Order)
                .ToList();
            ordered.AddRange(lessons);
        }
        return ordered;
    }

    private static List<string> ParsePlanJson(string json)
    {
        // JSON bloğunu temizle (```json ... ``` gibi markdown varsa)
        var cleaned = json.Trim();
        if (cleaned.StartsWith("```"))
            cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();

        var startIdx = cleaned.IndexOf('[');
        var endIdx = cleaned.LastIndexOf(']');
        if (startIdx >= 0 && endIdx > startIdx)
            cleaned = cleaned[startIdx..(endIdx + 1)];

        try
        {
            // Yeni format: [{title, orderIndex, estimatedMinutes}, ...]
            using var doc = JsonDocument.Parse(cleaned);
            var array = doc.RootElement.EnumerateArray();
            var titles = new List<string>();

            foreach (var item in array)
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("title", out var titleProp))
                {
                    var title = titleProp.GetString();
                    if (!string.IsNullOrWhiteSpace(title))
                        titles.Add(title);
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    // Eski format uyumluluğu: ["Konu 1", "Konu 2"]
                    var title = item.GetString();
                    if (!string.IsNullOrWhiteSpace(title))
                        titles.Add(title);
                }
            }

            return titles.Count > 0 ? titles : DefaultPlan();
        }
        catch
        {
            return DefaultPlan();
        }
    }

    private static List<string> DefaultPlan() =>
    [
        "Temel Kavramlar",
        "Temel Prensipler",
        "Pratik Uygulamalar",
        "İleri Konular",
        "Proje ve Örnek"
    ];

    private static string GuessEmoji(string title)
    {
        var lower = title.ToLowerInvariant();
        if (lower.Contains("python") || lower.Contains("java") || lower.Contains(".net") || lower.Contains("c#") || lower.Contains("yazılım") || lower.Contains("kod") || lower.Contains("algoritma") || lower.Contains("programlama"))
            return "💻";
        if (lower.Contains("tarih") || lower.Contains("osmanlı") || lower.Contains("selçuklu") || lower.Contains("history"))
            return "📚";
        if (lower.Contains("matematik") || lower.Contains("math") || lower.Contains("istatistik") || lower.Contains("calculus"))
            return "📐";
        if (lower.Contains("fizik") || lower.Contains("kimya") || lower.Contains("biyoloji") || lower.Contains("science"))
            return "🔬";
        if (lower.Contains("ingilizce") || lower.Contains("english") || lower.Contains("fransızca") || lower.Contains("dil") || lower.Contains("language"))
            return "🌐";
        if (lower.Contains("müzik") || lower.Contains("music") || lower.Contains("gitar") || lower.Contains("piyano"))
            return "🎵";
        if (lower.Contains("yapay zeka") || lower.Contains("ai") || lower.Contains("machine learning") || lower.Contains("ml"))
            return "🤖";
        if (lower.Contains("sql") || lower.Contains("database") || lower.Contains("veritabanı"))
            return "🗄️";
        return "📖";
    }

    private static string GuessCategory(string title)
    {
        var lower = title.ToLowerInvariant();
        if (lower.Contains("python") || lower.Contains("java") || lower.Contains(".net") || lower.Contains("c#") ||
            lower.Contains("yazılım") || lower.Contains("kod") || lower.Contains("algoritma") || lower.Contains("sql") ||
            lower.Contains("yapay zeka") || lower.Contains("machine learning"))
            return "Yazılım";
        if (lower.Contains("tarih") || lower.Contains("osmanlı") || lower.Contains("selçuklu"))
            return "Tarih";
        if (lower.Contains("ingilizce") || lower.Contains("english") || lower.Contains("fransızca") || lower.Contains("dil"))
            return "Dil";
        if (lower.Contains("matematik") || lower.Contains("fizik") || lower.Contains("kimya") || lower.Contains("biyoloji"))
            return "Bilim";
        return "Diğer";
    }
}

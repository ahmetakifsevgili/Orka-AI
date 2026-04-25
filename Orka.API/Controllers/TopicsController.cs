using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/topics")]
public class TopicsController : ControllerBase
{
    private readonly ITopicService _topicService;
    private readonly SessionService _sessionService;
    private readonly OrkaDbContext _db;

    public TopicsController(ITopicService topicService, SessionService sessionService, OrkaDbContext db)
    {
        _topicService = topicService;
        _sessionService = sessionService;
        _db = db;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetTopics()
    {
        var userId = GetUserId();
        var topics = await _topicService.GetUserTopicsAsync(userId);
        var result = topics.Select(t => new
        {
            id                = t.Id,
            title             = t.Title,
            emoji             = t.Emoji ?? "📚",
            category          = t.Category ?? "Genel",
            parentTopicId     = t.ParentTopicId,
            order             = t.Order,
            currentPhase      = t.CurrentPhase.ToString(),
            progressPercentage = t.ProgressPercentage,
            successScore      = t.SuccessScore,
            isMastered        = t.IsMastered,
            totalSections     = t.TotalSections,
            completedSections = t.CompletedSections,
            lastAccessedAt    = t.LastAccessedAt
        });
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetTopic(Guid id)
    {
        var userId = GetUserId();
        var topic = await _topicService.GetTopicByIdAsync(id, userId);
        if (topic == null) return NotFound(new { message = "Konu bulunamadı." });

        return Ok(new
        {
            topic = new { 
                topic.Id, 
                topic.Title, 
                topic.Emoji, 
                topic.Category, 
                topic.ParentTopicId, 
                topic.Order, 
                topic.CurrentPhase, 
                topic.CreatedAt, 
                topic.LastAccessedAt,
                progressPercentage = topic.ProgressPercentage,
                successScore = topic.SuccessScore,
                isMastered = topic.IsMastered
            },
            sessions = topic.Sessions?.Select(s => new { s.Id, s.SessionNumber, s.Summary, s.CreatedAt, s.EndedAt }),
            wikiPages = topic.WikiPages?.Select(w => new { w.Id, w.Title, w.Status, w.OrderIndex })
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateTopic([FromBody] CreateTopicBody request)
    {
        var userId = GetUserId();
        var result = await _topicService.CreateDiscoveryTopicAsync(userId, request.Title);
        
        // Emoji and Category if provided (discovery creates with defaults)
        if (!string.IsNullOrEmpty(request.Emoji)) result.Topic.Emoji = request.Emoji;
        if (!string.IsNullOrEmpty(request.Category)) result.Topic.Category = request.Category;
        
        return Ok(new
        {
            id = result.Topic.Id,
            title = result.Topic.Title,
            emoji = result.Topic.Emoji,
            category = result.Topic.Category,
            createdAt = result.Topic.CreatedAt
        });
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateTopic(Guid id, [FromBody] UpdateTopicBody request)
    {
        var userId = GetUserId();
        await _topicService.UpdateTopicAsync(id, userId, request.Title, request.Emoji, request.IsArchived);
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTopic(Guid id)
    {
        var userId = GetUserId();
        var topic = await _topicService.GetTopicByIdAsync(id, userId);
        if (topic == null) return NotFound(new { message = "Konu bulunamadı." });
        await _topicService.DeleteTopicAsync(id, userId);
        return Ok();
    }

    [HttpGet("{id}/sessions")]
    public async Task<IActionResult> GetSessions(Guid id)
    {
        var userId = GetUserId();
        var sessions = await _sessionService.GetTopicSessionsAsync(id, userId);
        return Ok(sessions.Select(s => new { s.Id, s.SessionNumber, s.Summary, s.CreatedAt, s.EndedAt, s.TotalTokensUsed, s.TotalCostUSD }));
    }

    [HttpGet("{id}/sessions/latest")]
    public async Task<IActionResult> GetLatestSession(Guid id)
    {
        var userId = GetUserId();
        var result = await _sessionService.GetLatestSessionAsync(id, userId);
        if (result == null) return Ok(null);
        return Ok(result);
    }

    /// <summary>
    /// Alt konuları (müfredat konu listesi) döner — DeepPlan ile oluşturulan alt başlıklar.
    /// </summary>
    [HttpGet("{id}/subtopics")]
    public async Task<IActionResult> GetSubtopics(Guid id)
    {
        var userId = GetUserId();
        var parent = await _topicService.GetTopicByIdAsync(id, userId);
        if (parent == null) return NotFound(new { message = "Ana konu bulunamadı." });

        var subtopics = await _db.Topics
            .Where(t => t.ParentTopicId == id && t.UserId == userId)
            .OrderBy(t => t.Order)
            .Select(t => new
            {
                id                 = t.Id,
                title              = t.Title,
                order              = t.Order,
                progressPercentage = t.ProgressPercentage,
                successScore       = t.SuccessScore,
                isMastered         = t.IsMastered,
                completedSections  = t.CompletedSections,
                totalSections      = t.TotalSections
            })
            .ToListAsync();

        return Ok(new
        {
            parentId    = id,
            parentTitle = parent.Title,
            count       = subtopics.Count,
            subtopics
        });
    }

    /// <summary>
    /// Konunun ilerleme özetini döner — XP, tamamlanma yüzdesi, quiz başarı oranı.
    /// </summary>
    [HttpGet("{id}/progress")]
    public async Task<IActionResult> GetProgress(Guid id)
    {
        var userId = GetUserId();
        var topic = await _topicService.GetTopicByIdAsync(id, userId);
        if (topic == null) return NotFound(new { message = "Konu bulunamadı." });

        var quizAttempts = await _db.QuizAttempts
            .Where(qa => qa.TopicId == id && qa.UserId == userId)
            .ToListAsync();

        var totalAttempts   = quizAttempts.Count;
        var correctAttempts = quizAttempts.Count(qa => qa.IsCorrect);
        var quizAccuracy    = totalAttempts > 0 ? Math.Round((double)correctAttempts / totalAttempts * 100, 1) : 0.0;

        return Ok(new
        {
            topicId            = id,
            title              = topic.Title,
            progressPercentage = topic.ProgressPercentage,
            successScore       = topic.SuccessScore,
            isMastered         = topic.IsMastered,
            completedSections  = topic.CompletedSections,
            totalSections      = topic.TotalSections,
            quizAttempts       = totalAttempts,
            quizCorrect        = correctAttempts,
            quizAccuracy
        });
    }
}

public class CreateTopicBody
{
    public string Title { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public string? Category { get; set; }
}

public class UpdateTopicBody
{
    public string? Title { get; set; }
    public string? Emoji { get; set; }
    public bool? IsArchived { get; set; }
}

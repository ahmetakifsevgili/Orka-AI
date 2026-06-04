using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Orka.Infrastructure.Utilities;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/topics")]
public class TopicsController : ControllerBase
{
    private const int MaxTopicTitleLength = 200;
    private const int MaxTopicMetadataLength = 200;

    private readonly ITopicService _topicService;
    private readonly IDataLifecycleService _dataLifecycle;
    private readonly SessionService _sessionService;
    private readonly OrkaDbContext _db;

    public TopicsController(ITopicService topicService, IDataLifecycleService dataLifecycle, SessionService sessionService, OrkaDbContext db)
    {
        _topicService = topicService;
        _dataLifecycle = dataLifecycle;
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
            planIntent        = ResolvePlanIntent(t.PlanIntent, t.Category),
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

        var resolvedPlanIntent = ResolvePlanIntent(topic.PlanIntent, topic.Category);

        return Ok(new
        {
            id = topic.Id,
            title = topic.Title,
            emoji = topic.Emoji,
            category = topic.Category,
            planIntent = resolvedPlanIntent,
            parentTopicId = topic.ParentTopicId,
            order = topic.Order,
            currentPhase = topic.CurrentPhase.ToString(),
            createdAt = topic.CreatedAt,
            lastAccessedAt = topic.LastAccessedAt,
            progressPercentage = topic.ProgressPercentage,
            successScore = topic.SuccessScore,
            isMastered = topic.IsMastered,
            topic = new { 
                topic.Id, 
                topic.Title, 
                topic.Emoji, 
                topic.Category,
                PlanIntent = resolvedPlanIntent,
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
    public async Task<IActionResult> CreateTopic([FromBody] CreateTopicBody? request)
    {
        if (request == null)
            return BadRequest(new { message = "Konu istegi zorunlu." });
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Baslik zorunlu." });
        if (request.Title.Length > MaxTopicTitleLength ||
            request.Emoji is { Length: > MaxTopicMetadataLength } ||
            request.Category is { Length: > MaxTopicMetadataLength } ||
            request.PlanIntent is { Length: > MaxTopicMetadataLength })
        {
            return BadRequest(new { message = "Konu alani cok uzun." });
        }

        var userId = GetUserId();
        var result = await _topicService.CreateDiscoveryTopicAsync(userId, request.Title);
        
        // Emoji and Category if provided (discovery creates with defaults)
        if (!string.IsNullOrEmpty(request.Emoji)) result.Topic.Emoji = request.Emoji;
        if (!string.IsNullOrEmpty(request.Category)) result.Topic.Category = request.Category;
        if (!string.IsNullOrWhiteSpace(request.PlanIntent)) result.Topic.PlanIntent = request.PlanIntent.Trim();
        else result.Topic.PlanIntent = ResolvePlanIntent(result.Topic.PlanIntent, result.Topic.Category);
        await _db.SaveChangesAsync();
        
        return Ok(new
        {
            id = result.Topic.Id,
            title = result.Topic.Title,
            emoji = result.Topic.Emoji,
            category = result.Topic.Category,
            planIntent = ResolvePlanIntent(result.Topic.PlanIntent, result.Topic.Category),
            createdAt = result.Topic.CreatedAt
        });
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateTopic(Guid id, [FromBody] UpdateTopicBody? request)
    {
        if (request == null)
            return BadRequest(new { message = "Guncelleme istegi zorunlu." });
        if (request.Title is { Length: > MaxTopicTitleLength } ||
            request.Emoji is { Length: > MaxTopicMetadataLength })
        {
            return BadRequest(new { message = "Konu alani cok uzun." });
        }
        if (request.Title != null && string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Baslik bos olamaz." });

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
        await _dataLifecycle.DeleteTopicTreeAsync(userId, id, HttpContext.RequestAborted);
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
        if (result == null) return NotFound(new { message = "Oturum bulunamadı." });
        return Ok(result);
    }

    [HttpGet("{id}/curriculum")]
    public async Task<IActionResult> GetCurriculum(Guid id)
    {
        var userId = GetUserId();
        var parent = await _topicService.GetTopicByIdAsync(id, userId);
        if (parent == null) return NotFound(new { message = "Ana konu bulunamadi." });

        var modules = await _db.Topics
            .Where(t => t.ParentTopicId == id && t.UserId == userId && t.PlanIntent == "Module" && !t.IsArchived)
            .OrderBy(t => t.Order)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();
        var moduleIds = modules.Select(m => m.Id).ToList();
        var lessons = await _db.Topics
            .Where(t => t.ParentTopicId.HasValue && moduleIds.Contains(t.ParentTopicId.Value) && t.UserId == userId && !t.IsArchived)
            .OrderBy(t => t.ParentTopicId)
            .ThenBy(t => t.Order)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();

        var chapters = modules.Select(module =>
        {
            var moduleLessons = lessons
                .Where(lesson => lesson.ParentTopicId == module.Id)
                .OrderBy(lesson => lesson.Order)
                .Select(lesson =>
                {
                    var metadata = ParseLessonContractMetadata(lesson.MetadataJson);
                    return new
                    {
                        id = lesson.Id,
                        title = PublicTextNormalizer.RepairMojibake(lesson.Title),
                        order = lesson.Order,
                        planIntent = ResolvePlanIntent(lesson.PlanIntent, lesson.Category),
                        category = lesson.Category,
                        phaseMetadata = lesson.PhaseMetadata,
                        conceptKey = metadata.ConceptKey,
                        skillTag = metadata.SkillTag,
                        learningObjective = metadata.LearningObjective,
                        sequenceReason = metadata.SequenceReason,
                        prerequisiteConceptKeys = metadata.PrerequisiteConceptKeys,
                        quizHook = metadata.QuizHook,
                        tutorHook = metadata.TutorHook,
                        successCriteria = metadata.SuccessCriteria,
                        progressPercentage = lesson.ProgressPercentage,
                        successScore = lesson.SuccessScore,
                        isMastered = lesson.IsMastered
                    };
                })
                .ToList();

            return new
            {
                id = module.Id,
                title = PublicTextNormalizer.RepairMojibake(module.Title),
                order = module.Order,
                planIntent = ResolvePlanIntent(module.PlanIntent, module.Category),
                totalSections = module.TotalSections,
                lessonCount = moduleLessons.Count,
                lessons = moduleLessons
            };
        }).ToList();

        return Ok(new
        {
            rootTopicId = id,
            rootTitle = parent.Title,
            chapterCount = chapters.Count,
            lessonCount = lessons.Count,
            isMaterialized = chapters.Count >= 6 && lessons.Count >= 24 && chapters.All(c => c.lessonCount > 0),
            chapters = chapters,
            modules = chapters
        });
    }

    private static LessonContractMetadata ParseLessonContractMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return LessonContractMetadata.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new LessonContractMetadata(
                Repair(ReadString(root, "conceptKey")),
                Repair(ReadString(root, "skillTag")),
                Repair(ReadString(root, "learningObjective")),
                Repair(ReadString(root, "sequenceReason")),
                ReadStringArray(root, "prerequisiteConceptKeys"),
                CloneObject(root, "quizHook"),
                CloneObject(root, "tutorHook"),
                ReadStringArray(root, "successCriteria"));
        }
        catch (JsonException)
        {
            return LessonContractMetadata.Empty;
        }
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static string[] ReadStringArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => Repair(item.GetString()))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }

    private static object? CloneObject(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonSerializer.Deserialize<object>(value.GetRawText());
    }

    private static string? Repair(string? value) => string.IsNullOrWhiteSpace(value)
        ? value
        : PublicTextNormalizer.RepairMojibake(value);

    private sealed record LessonContractMetadata(
        string? ConceptKey,
        string? SkillTag,
        string? LearningObjective,
        string? SequenceReason,
        string[] PrerequisiteConceptKeys,
        object? QuizHook,
        object? TutorHook,
        string[] SuccessCriteria)
    {
        public static LessonContractMetadata Empty { get; } = new(null, null, null, null, [], null, null, []);
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
                planIntent         = ResolvePlanIntent(t.PlanIntent, t.Category),
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

    private static string? ResolvePlanIntent(string? planIntent, string? category)
    {
        if (!string.IsNullOrWhiteSpace(planIntent)) return planIntent.Trim();
        return !string.IsNullOrWhiteSpace(category) && category.StartsWith("Plan:", StringComparison.OrdinalIgnoreCase)
            ? category.Split(':', 2)[1]
            : null;
    }
}

public class CreateTopicBody
{
    public string Title { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public string? Category { get; set; }
    public string? PlanIntent { get; set; }
}

public class UpdateTopicBody
{
    public string? Title { get; set; }
    public string? Emoji { get; set; }
    public bool? IsArchived { get; set; }
}

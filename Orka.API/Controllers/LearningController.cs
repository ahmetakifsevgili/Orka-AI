using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/learning")]
public class LearningController : ControllerBase
{
    private readonly ILearningSignalService _signals;
    private readonly OrkaDbContext _db;
    private readonly IReviewSrsService _reviews;

    public LearningController(ILearningSignalService signals, OrkaDbContext db, IReviewSrsService reviews)
    {
        _signals = signals;
        _db = db;
        _reviews = reviews;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("topic/{topicId:guid}/summary")]
    public async Task<IActionResult> GetTopicSummary(Guid topicId)
    {
        var summary = await _signals.GetTopicSummaryAsync(GetUserId(), topicId, HttpContext.RequestAborted);
        return Ok(summary);
    }

    [HttpGet("topic/{topicId:guid}/recommendations")]
    [HttpGet("topic/{topicId:guid}/review/due")]
    public async Task<IActionResult> GetDueReview(Guid topicId)
    {
        var recommendations = await _signals.GetRecommendationsAsync(GetUserId(), topicId, HttpContext.RequestAborted);
        var durable = await _reviews.GetDueAsync(GetUserId(), topicId, HttpContext.RequestAborted);
        return Ok(new ReviewDueResponse(durable, recommendations));
    }

    [HttpPost("review/{recommendationId:guid}/complete")]
    public async Task<IActionResult> CompleteReview(Guid recommendationId, [FromBody] CompleteReviewRequest request)
    {
        var userId = GetUserId();
        var durable = await _reviews.CompleteAsync(
            userId,
            recommendationId,
            request.Quality,
            responseMode: "legacy-learning-endpoint",
            notes: null,
            HttpContext.RequestAborted);
        if (durable != null)
            return Ok(new { completed = true, recommendationId = durable.Id, durable.SkillTag, request.Quality, durable });

        var recommendation = await _db.StudyRecommendations
            .FirstOrDefaultAsync(r => r.Id == recommendationId && r.UserId == userId, HttpContext.RequestAborted);

        if (recommendation == null)
            return NotFound(new { message = "Review item bulunamadi." });

        if (!recommendation.IsDone)
        {
            recommendation.IsDone = true;
            recommendation.CompletedAt = DateTime.UtcNow;

            await _signals.RecordSignalAsync(
                userId,
                recommendation.TopicId,
                sessionId: null,
                LearningSignalTypes.ReviewCompleted,
                recommendation.SkillTag,
                recommendation.SkillTag,
                score: Math.Clamp(request.Quality, 0, 5) * 20,
                isPositive: request.Quality >= 3,
                payloadJson: System.Text.Json.JsonSerializer.Serialize(new { recommendationId, request.Quality }),
                HttpContext.RequestAborted);
        }

        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { completed = true, recommendationId, recommendation.SkillTag, request.Quality });
    }

    [HttpPost("signal")]
    public async Task<IActionResult> RecordSignal([FromBody] RecordLearningSignalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SignalType))
            return BadRequest(new { message = "SignalType zorunlu." });

        var signalType = LearningSignalTypes.Normalize(request.SignalType);

        await _signals.RecordSignalAsync(
            GetUserId(),
            request.TopicId,
            request.SessionId,
            signalType,
            request.SkillTag,
            request.TopicPath,
            request.Score,
            request.IsPositive,
            request.PayloadJson,
            HttpContext.RequestAborted);

        return Ok(new { recorded = true, signalType });
    }
}

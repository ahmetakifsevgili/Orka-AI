using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/learning-quality")]
public sealed class LearningQualityController : ControllerBase
{
    private readonly ILearningQualityReportService _reports;
    private readonly IRagEvaluationService _ragEvaluation;
    private readonly ILogger<LearningQualityController> _logger;

    public LearningQualityController(
        ILearningQualityReportService reports,
        IRagEvaluationService ragEvaluation,
        ILogger<LearningQualityController> logger)
    {
        _reports = reports;
        _ragEvaluation = ragEvaluation;
        _logger = logger;
    }

    [HttpGet("topic/{topicId:guid}")]
    public async Task<IActionResult> GetTopicQuality(Guid topicId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var report = await _reports.GetTopicReportAsync(userId, topicId, HttpContext.RequestAborted);
            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LearningQuality] Topic report failed. UserId={UserId} TopicId={TopicId}", userId, topicId);
            return StatusCode(500, new { error = "Learning quality report could not be generated." });
        }
    }

    [HttpPost("topic/{topicId:guid}/rag-evaluation/run")]
    public async Task<IActionResult> RunRagEvaluation(Guid topicId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _ragEvaluation.EvaluateTopicAsync(userId, topicId, null, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LearningQuality] RAG evaluation failed. UserId={UserId} TopicId={TopicId}", userId, topicId);
            return StatusCode(500, new { error = "RAG evaluation could not be generated." });
        }
    }
}

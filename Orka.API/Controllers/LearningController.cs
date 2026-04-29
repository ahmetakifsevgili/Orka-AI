using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/learning")]
public class LearningController : ControllerBase
{
    private readonly ILearningSignalService _signals;

    public LearningController(ILearningSignalService signals)
    {
        _signals = signals;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("topic/{topicId:guid}/summary")]
    public async Task<IActionResult> GetTopicSummary(Guid topicId)
    {
        var summary = await _signals.GetTopicSummaryAsync(GetUserId(), topicId, HttpContext.RequestAborted);
        return Ok(summary);
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

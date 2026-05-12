using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/daily-challenge")]
public class DailyChallengeController : ControllerBase
{
    private readonly IDailyChallengeService _dailyChallenges;

    public DailyChallengeController(IDailyChallengeService dailyChallenges)
    {
        _dailyChallenges = dailyChallenges;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid? topicId)
    {
        var challenge = await _dailyChallenges.GetTodayAsync(GetUserId(), topicId, HttpContext.RequestAborted);
        return Ok(challenge);
    }

    [HttpPost("{challengeId:guid}/submit")]
    public async Task<IActionResult> Submit(Guid challengeId, [FromBody] DailyChallengeSubmitRequest request)
    {
        var result = await _dailyChallenges.SubmitAsync(GetUserId(), challengeId, request.Answer, request.Quality, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Daily challenge bulunamadı." }) : Ok(result);
    }
}

public sealed class DailyChallengeSubmitRequest
{
    public Guid? TopicId { get; set; }
    public string? SkillTag { get; set; }
    public string Answer { get; set; } = string.Empty;
    public int Quality { get; set; } = 3;
}

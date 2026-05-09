using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/assessment")]
public sealed class AssessmentController : ControllerBase
{
    private readonly IAssessmentCalibrationService _calibration;

    public AssessmentController(IAssessmentCalibrationService calibration)
    {
        _calibration = calibration;
    }

    [HttpGet("topic/{topicId:guid}/calibration")]
    public async Task<IActionResult> GetCalibration(Guid topicId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _calibration.GetLatestAsync(userId, topicId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("topic/{topicId:guid}/calibration/run")]
    public async Task<IActionResult> RunCalibration(Guid topicId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _calibration.RunAsync(userId, topicId, ct);
        return Ok(result);
    }
}

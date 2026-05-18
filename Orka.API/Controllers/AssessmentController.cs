using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/assessment")]
public sealed class AssessmentController : ControllerBase
{
    private readonly IAssessmentCalibrationService _calibration;
    private readonly IAssessmentBlueprintService _blueprints;

    public AssessmentController(
        IAssessmentCalibrationService calibration,
        IAssessmentBlueprintService blueprints)
    {
        _calibration = calibration;
        _blueprints = blueprints;
    }

    [HttpGet("topic/{topicId:guid}/blueprint")]
    public async Task<IActionResult> GetDiagnosticBlueprint(Guid topicId, [FromQuery] Guid? sessionId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var result = await _blueprints.BuildDiagnosticBlueprintAsync(userId, topicId, sessionId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("blueprint/plan-step")]
    public async Task<IActionResult> BuildPlanStepBlueprint([FromBody] AssessmentBlueprintRequestDto request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var result = await _blueprints.BuildBlueprintForPlanStepAsync(userId, request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("blueprint/diagnostic")]
    public async Task<IActionResult> BuildDiagnosticBlueprint([FromBody] AssessmentBlueprintRequestDto request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!request.TopicId.HasValue)
        {
            return BadRequest(new { error = "topicId is required." });
        }

        try
        {
            var result = await _blueprints.BuildDiagnosticBlueprintAsync(userId, request.TopicId.Value, request.SessionId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("quality/evaluate")]
    public async Task<IActionResult> EvaluateQuality([FromBody] AssessmentQualityEvaluationRequestDto request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var result = await _blueprints.EvaluateAssessmentContractAsync(userId, request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpGet("quality/snapshots/{snapshotId:guid}")]
    public async Task<IActionResult> GetQualitySnapshot(Guid snapshotId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _blueprints.GetAssessmentQualitySnapshotAsync(userId, snapshotId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("topic/{topicId:guid}/quality/latest")]
    public async Task<IActionResult> GetLatestQualitySnapshot(Guid topicId, [FromQuery] Guid? sessionId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _blueprints.GetLatestAssessmentQualitySnapshotAsync(userId, topicId, sessionId, ct);
        return result == null ? NotFound() : Ok(result);
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

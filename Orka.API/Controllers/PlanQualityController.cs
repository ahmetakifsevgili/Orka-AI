using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[ApiController]
[Authorize]
[Route("api/plan-quality")]
public sealed class PlanQualityController : ControllerBase
{
    private readonly IPlanSequencingService _planSequencing;

    public PlanQualityController(IPlanSequencingService planSequencing)
    {
        _planSequencing = planSequencing;
    }

    [HttpGet("topic/{topicId:guid}/readiness")]
    public async Task<IActionResult> GetReadiness(
        Guid topicId,
        [FromQuery] Guid? sessionId = null,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _planSequencing.GetPlanReadinessAsync(GetUserId(), topicId, sessionId, ct));
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = "plan_topic_not_found" });
        }
    }

    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate([FromBody] PlanQualityEvaluationRequestDto request, CancellationToken ct = default)
    {
        if (request.TopicId == Guid.Empty)
        {
            return BadRequest(new { error = "topic_id_required" });
        }

        try
        {
            return Ok(await _planSequencing.EvaluatePlanSequenceAsync(GetUserId(), request, ct));
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = "plan_topic_not_found" });
        }
    }

    [HttpGet("snapshots/{id:guid}")]
    public async Task<IActionResult> GetSnapshot(Guid id, CancellationToken ct = default)
    {
        var snapshot = await _planSequencing.GetPlanQualitySnapshotAsync(GetUserId(), id, ct);
        return snapshot == null ? NotFound(new { error = "plan_quality_snapshot_not_found" }) : Ok(snapshot);
    }

    [HttpGet("topic/{topicId:guid}/latest")]
    public async Task<IActionResult> GetLatest(
        Guid topicId,
        [FromQuery] Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var snapshot = await _planSequencing.GetLatestPlanQualitySnapshotAsync(GetUserId(), topicId, sessionId, ct);
        return snapshot == null ? NotFound(new { error = "plan_quality_snapshot_not_found" }) : Ok(snapshot);
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

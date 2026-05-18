using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/learning-runtime")]
public sealed class LearningRuntimeController : ControllerBase
{
    private readonly ILearningRuntimeTelemetryService _runtime;

    public LearningRuntimeController(ILearningRuntimeTelemetryService runtime)
    {
        _runtime = runtime;
    }

    [HttpGet("traces")]
    public async Task<ActionResult<LearningRuntimeTracesResponseDto>> GetTraces(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        [FromQuery] int take = 25,
        CancellationToken ct = default)
    {
        var traces = await _runtime.GetRecentTracesAsync(GetUserId(), topicId, sessionId, take, ct);
        return Ok(new LearningRuntimeTracesResponseDto
        {
            Traces = traces,
            Count = traces.Count
        });
    }

    [HttpGet("traces/{id:guid}")]
    public async Task<ActionResult<LearningRuntimeTraceDto>> GetTrace(Guid id, CancellationToken ct)
    {
        var trace = await _runtime.GetTraceAsync(GetUserId(), id, ct);
        return trace is null ? NotFound(new { error = "learning_runtime_trace_not_found" }) : Ok(trace);
    }

    [HttpGet("correlation/{correlationId}")]
    public async Task<ActionResult<LearningRuntimeCorrelationDto>> GetCorrelation(string correlationId, CancellationToken ct)
    {
        return Ok(await _runtime.GetCorrelationSummaryAsync(GetUserId(), correlationId, ct));
    }

    [HttpGet("health")]
    public async Task<ActionResult<LearningRuntimeHealthDto>> GetHealth(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        CancellationToken ct = default)
    {
        return Ok(await _runtime.GetLearningRuntimeHealthAsync(GetUserId(), topicId, sessionId, ct));
    }

    [HttpGet("topic/{topicId:guid}/summary")]
    public async Task<ActionResult<LearningRuntimeFlowSummaryDto>> GetTopicSummary(
        Guid topicId,
        [FromQuery] Guid? sessionId = null,
        CancellationToken ct = default)
    {
        return Ok(await _runtime.GetTopicSummaryAsync(GetUserId(), topicId, sessionId, ct));
    }

    [HttpPost("privacy-check")]
    public ActionResult<LearningRuntimePrivacyCheckDto> PrivacyCheck([FromBody] LearningRuntimePrivacyCheckRequestDto request)
    {
        return Ok(_runtime.ValidateTelemetryPrivacy(request));
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

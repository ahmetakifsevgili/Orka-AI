using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/agentic-trust")]
public sealed class AgenticTrustController : ControllerBase
{
    private readonly IAgenticTrustPolicyService _trust;

    public AgenticTrustController(IAgenticTrustPolicyService trust)
    {
        _trust = trust;
    }

    [HttpPost("check/user-message")]
    public async Task<ActionResult<AgenticTrustCheckResultDto>> CheckUserMessage(
        [FromBody] AgenticTrustCheckRequestDto request,
        CancellationToken ct)
    {
        return Ok(await _trust.CheckUserMessageAsync(GetUserId(), request, ct));
    }

    [HttpPost("check/source-content")]
    public async Task<ActionResult<AgenticTrustCheckResultDto>> CheckSourceContent(
        [FromBody] AgenticTrustCheckRequestDto request,
        CancellationToken ct)
    {
        return Ok(await _trust.CheckSourceContentAsync(GetUserId(), request, ct));
    }

    [HttpPost("check/tool-request")]
    public async Task<ActionResult<AgenticTrustCheckResultDto>> CheckToolRequest(
        [FromBody] AgenticTrustCheckRequestDto request,
        CancellationToken ct)
    {
        return Ok(await _trust.CheckToolRequestAsync(GetUserId(), request, ct));
    }

    [HttpPost("check/tutor-response")]
    public async Task<ActionResult<AgenticTrustCheckResultDto>> CheckTutorResponse(
        [FromBody] AgenticTrustCheckRequestDto request,
        CancellationToken ct)
    {
        return Ok(await _trust.CheckTutorResponseAsync(GetUserId(), request, ct));
    }

    [HttpPost("check/memory-write")]
    public async Task<ActionResult<AgenticTrustCheckResultDto>> CheckMemoryWrite(
        [FromBody] AgenticTrustCheckRequestDto request,
        CancellationToken ct)
    {
        return Ok(await _trust.CheckMemoryWriteAsync(GetUserId(), request, ct));
    }

    [HttpPost("check/citation-set")]
    public async Task<ActionResult<AgenticTrustCheckResultDto>> CheckCitationSet(
        [FromBody] AgenticTrustCheckRequestDto request,
        CancellationToken ct)
    {
        return Ok(await _trust.CheckCitationSetAsync(GetUserId(), request, ct));
    }

    [HttpPost("check/public-payload")]
    public async Task<ActionResult<AgenticTrustCheckResultDto>> CheckPublicPayload(
        [FromBody] AgenticTrustCheckRequestDto request,
        CancellationToken ct)
    {
        return Ok(await _trust.CheckPublicPayloadAsync(GetUserId(), request, ct));
    }

    [HttpGet("summary")]
    public async Task<ActionResult<AgenticTrustRuntimeSummaryDto>> GetSummary(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        CancellationToken ct = default)
    {
        return Ok(await _trust.GetTrustRuntimeSummaryAsync(GetUserId(), topicId, sessionId, ct));
    }

    [HttpPost("fixtures/evaluate")]
    public async Task<ActionResult<AgenticTrustRuntimeSummaryDto>> EvaluateFixtures(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        CancellationToken ct = default)
    {
        return Ok(await _trust.EvaluateKnownFixturesAsync(GetUserId(), topicId, sessionId, ct));
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

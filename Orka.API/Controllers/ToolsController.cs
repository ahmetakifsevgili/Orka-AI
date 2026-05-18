using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/tools")]
public sealed class ToolsController : ControllerBase
{
    private readonly IToolCapabilityService _capabilities;
    private readonly IUnifiedToolRuntimeService _runtime;
    private readonly OrkaDbContext _db;

    public ToolsController(
        IToolCapabilityService capabilities,
        IUnifiedToolRuntimeService runtime,
        OrkaDbContext db)
    {
        _capabilities = capabilities;
        _runtime = runtime;
        _db = db;
    }

    [AllowAnonymous]
    [HttpGet("capabilities")]
    public async Task<IActionResult> GetCapabilities([FromQuery] bool includeInternal = false)
    {
        var canSeeInternal = includeInternal && await IsAdminAsync();
        var result = _capabilities.GetCapabilities(canSeeInternal);
        return Ok(new
        {
            tools = result,
            count = result.Count,
            includeInternal = canSeeInternal,
            contract = "tool_capability_v1"
        });
    }

    [AllowAnonymous]
    [HttpGet("capabilities/{toolId}")]
    public async Task<IActionResult> GetCapability(string toolId, [FromQuery] bool includeInternal = false)
    {
        var canSeeInternal = includeInternal && await IsAdminAsync();
        var tool = _capabilities.GetCapability(toolId, canSeeInternal);
        return tool is null ? NotFound(new { error = "tool_not_found" }) : Ok(tool);
    }

    [HttpGet("runtime/traces")]
    public async Task<IActionResult> GetRuntimeTraces(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var traces = await _runtime.GetRecentToolRuntimeTracesAsync(userId, topicId, sessionId, take, ct);
        return Ok(new
        {
            traces,
            count = traces.Count,
            contract = "tool_runtime_trace_v1"
        });
    }

    [HttpGet("runtime/traces/{id:guid}")]
    public async Task<IActionResult> GetRuntimeTrace(Guid id, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var trace = await _runtime.GetToolRuntimeTraceAsync(userId, id, ct);
        return trace is null ? NotFound(new { error = "tool_trace_not_found" }) : Ok(trace);
    }

    [HttpGet("runtime/governance-summary")]
    public async Task<IActionResult> GetGovernanceSummary(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        return Ok(await _runtime.GetToolGovernanceSummaryAsync(userId, topicId, sessionId, ct));
    }

    [HttpPost("runtime/decide")]
    public async Task<IActionResult> Decide([FromBody] ToolRuntimeRequestDto request, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        return Ok(await _runtime.DecideAsync(userId, request, ct));
    }

    private async Task<bool> IsAdminAsync()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return false;

        return await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.IsAdmin)
            .FirstOrDefaultAsync();
    }

    private bool TryGetUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out userId);
    }
}

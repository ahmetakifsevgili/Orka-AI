using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/tools")]
public sealed class ToolsController : ControllerBase
{
    private readonly IToolCapabilityService _capabilities;
    private readonly OrkaDbContext _db;

    public ToolsController(IToolCapabilityService capabilities, OrkaDbContext db)
    {
        _capabilities = capabilities;
        _db = db;
    }

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

    [HttpGet("capabilities/{toolId}")]
    public async Task<IActionResult> GetCapability(string toolId, [FromQuery] bool includeInternal = false)
    {
        var canSeeInternal = includeInternal && await IsAdminAsync();
        var tool = _capabilities.GetCapability(toolId, canSeeInternal);
        return tool is null ? NotFound(new { error = "tool_not_found" }) : Ok(tool);
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
}

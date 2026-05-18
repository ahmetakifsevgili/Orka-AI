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
[Route("api/learning-artifacts")]
public sealed class LearningArtifactsController : ControllerBase
{
    private readonly OrkaDbContext _db;
    private readonly ILearningArtifactService _artifacts;

    public LearningArtifactsController(OrkaDbContext db, ILearningArtifactService artifacts)
    {
        _db = db;
        _artifacts = artifacts;
    }

    [HttpGet("{artifactId:guid}")]
    public async Task<IActionResult> GetArtifact(Guid artifactId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        var artifact = await _artifacts.GetArtifactAsync(userId, artifactId, ct);
        return artifact == null ? NotFound() : Ok(artifact);
    }

    [HttpGet]
    public async Task<IActionResult> ListArtifacts(
        [FromQuery] Guid? topicId,
        [FromQuery] Guid? sessionId,
        [FromQuery] string? conceptKey,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (!await OwnsScopeAsync(userId, topicId, sessionId, ct)) return NotFound();

        var artifacts = await _artifacts.ListArtifactsAsync(userId, topicId, sessionId, conceptKey, ct);
        return Ok(artifacts);
    }

    [HttpPost]
    public async Task<IActionResult> CreateArtifact([FromBody] LearningArtifactRequestDto request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (!await OwnsScopeAsync(userId, request.TopicId, request.SessionId, ct)) return NotFound();

        try
        {
            var artifact = await _artifacts.CreateArtifactAsync(userId, request, ct);
            return Ok(artifact);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateArtifact([FromBody] LearningArtifactRequestDto request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (!await OwnsScopeAsync(userId, request.TopicId, request.SessionId, ct)) return NotFound();

        var safety = await _artifacts.ValidateArtifactAsync(userId, request, ct);
        return Ok(safety);
    }

    [HttpPost("{artifactId:guid}/refresh-status")]
    public async Task<IActionResult> RefreshArtifactStatus(
        Guid artifactId,
        [FromBody] LearningArtifactRefreshRequestDto? request,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        var artifact = await _artifacts.RefreshArtifactStatusAsync(userId, artifactId, request?.Reason, ct);
        return artifact == null ? NotFound() : Ok(artifact);
    }

    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<bool> OwnsScopeAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
        if (topicId.HasValue)
        {
            var ownsTopic = await _db.Topics.AsNoTracking().AnyAsync(t => t.Id == topicId && t.UserId == userId, ct);
            if (!ownsTopic) return false;
        }

        if (sessionId.HasValue)
        {
            var ownsSession = await _db.Sessions.AsNoTracking().AnyAsync(s => s.Id == sessionId && s.UserId == userId, ct);
            if (!ownsSession) return false;
        }

        return true;
    }
}

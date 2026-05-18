using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[ApiController]
[Authorize]
[Route("api/learning-snapshots")]
public sealed class LearningSnapshotsController : ControllerBase
{
    private readonly IActiveLessonSnapshotService _snapshots;

    public LearningSnapshotsController(IActiveLessonSnapshotService snapshots)
    {
        _snapshots = snapshots;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("active-lesson")]
    public async Task<ActionResult<ActiveLessonSnapshotDto>> GetActiveLesson(
        [FromQuery] Guid? topicId,
        [FromQuery] Guid? sessionId,
        CancellationToken ct)
    {
        var snapshot = await _snapshots.GetActiveLessonSnapshotAsync(GetUserId(), topicId, sessionId, ct);
        return snapshot == null ? NotFound() : Ok(snapshot);
    }

    [HttpPost("active-lesson/refresh")]
    public async Task<ActionResult<ActiveLessonSnapshotDto>> RefreshActiveLesson(
        [FromBody] ActiveLessonSnapshotRequestDto request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _snapshots.BuildOrRefreshActiveLessonSnapshotAsync(GetUserId(), request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("active-lesson/stale")]
    public async Task<IActionResult> MarkActiveLessonStale(
        [FromBody] ActiveLessonSnapshotRequestDto request,
        [FromQuery] string reason = "context_changed",
        CancellationToken ct = default)
    {
        await _snapshots.MarkActiveLessonSnapshotStaleAsync(GetUserId(), request.TopicId, request.SessionId, reason, ct);
        return NoContent();
    }

    [HttpGet("student-context")]
    public async Task<ActionResult<StudentContextSnapshotDto>> GetStudentContext(
        [FromQuery] Guid? topicId,
        [FromQuery] Guid? sessionId,
        CancellationToken ct)
    {
        var snapshot = await _snapshots.GetStudentContextSnapshotAsync(GetUserId(), topicId, sessionId, ct);
        return snapshot == null ? NotFound() : Ok(snapshot);
    }

    [HttpPost("student-context/refresh")]
    public async Task<ActionResult<StudentContextSnapshotDto>> RefreshStudentContext(
        [FromBody] StudentContextSnapshotRequestDto request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _snapshots.BuildOrRefreshStudentContextSnapshotAsync(GetUserId(), request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

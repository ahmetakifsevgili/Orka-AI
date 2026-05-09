using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/tutor")]
public sealed class TutorController : ControllerBase
{
    private readonly OrkaDbContext _db;
    private readonly ILearningStyleSignalService _styleSignals;
    private readonly IRedisMemoryService _redis;
    private readonly ITeachingArtifactService _artifacts;
    private readonly ITutorPedagogyEvaluationService _pedagogy;
    private readonly ITutorTraceProjectionService _traceProjection;

    public TutorController(
        OrkaDbContext db,
        ILearningStyleSignalService styleSignals,
        IRedisMemoryService redis,
        ITeachingArtifactService artifacts,
        ITutorPedagogyEvaluationService pedagogy,
        ITutorTraceProjectionService traceProjection)
    {
        _db = db;
        _styleSignals = styleSignals;
        _redis = redis;
        _artifacts = artifacts;
        _pedagogy = pedagogy;
        _traceProjection = traceProjection;
    }

    [HttpGet("state/topic/{topicId:guid}")]
    public async Task<IActionResult> GetTopicState(Guid topicId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var profile = await _db.LearnerProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.TopicId == topicId)
            .OrderByDescending(p => p.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var memory = await _db.TutorWorkingMemorySnapshots
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var latestTurn = await _db.TutorTurnStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            topicId,
            learnerProfile = profile,
            workingMemory = memory,
            latestTurnState = latestTurn
        });
    }

    [HttpGet("trace/{traceId:guid}")]
    public async Task<IActionResult> GetTrace(Guid traceId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var trace = await _db.TutorActionTraces
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == traceId && t.UserId == userId, ct);
        if (trace == null) return NotFound();

        var tools = await _db.TutorToolCalls
            .AsNoTracking()
            .Where(t => t.TutorActionTraceId == traceId && t.UserId == userId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

        var artifacts = await _db.TeachingArtifacts
            .AsNoTracking()
            .Where(a => a.TutorActionTraceId == traceId && a.UserId == userId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        var evidence = await _db.TeachingEvidenceItems
            .AsNoTracking()
            .Where(e => e.TutorActionTraceId == traceId && e.UserId == userId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        var reflections = await _db.TutorReflectionUpdates
            .AsNoTracking()
            .Where(r => r.TutorActionTraceId == traceId && r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(3)
            .ToListAsync(ct);
        var pedagogyRuns = await _db.TutorPedagogyEvaluationRuns
            .AsNoTracking()
            .Where(r => r.TutorActionTraceId == traceId && r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(3)
            .ToListAsync(ct);
        var runIds = pedagogyRuns.Select(r => r.Id).ToArray();
        var pedagogyScores = await _db.TutorPedagogyRubricScores
            .AsNoTracking()
            .Where(s => runIds.Contains(s.EvaluationRunId) && s.UserId == userId)
            .OrderBy(s => s.RubricKey)
            .ToListAsync(ct);

        return Ok(new { trace, tools, artifacts, evidence, reflections, pedagogyRuns, pedagogyScores });
    }

    [HttpGet("pedagogy/topic/{topicId:guid}")]
    public async Task<IActionResult> GetPedagogyTopic(Guid topicId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var summary = await _pedagogy.GetTopicSummaryAsync(userId, topicId, ct);
        return Ok(summary);
    }

    [HttpGet("pedagogy/run/{runId:guid}")]
    public async Task<IActionResult> GetPedagogyRun(Guid runId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var run = await _pedagogy.GetRunAsync(userId, runId, ct);
        return run == null ? NotFound() : Ok(run);
    }

    [HttpPost("pedagogy/evaluate/recent")]
    public async Task<IActionResult> EvaluateRecentPedagogy([FromQuery] Guid? topicId, [FromQuery] Guid? sessionId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var run = await _pedagogy.EvaluateRecentAsync(userId, topicId, sessionId, ct);
        return run == null ? NotFound(new { error = "No recent tutor turn found for pedagogy evaluation." }) : Ok(run);
    }

    [HttpGet("artifacts/{artifactId:guid}")]
    public async Task<IActionResult> GetArtifact(Guid artifactId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var artifact = await _db.TeachingArtifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == artifactId && a.UserId == userId, ct);
        return artifact == null ? NotFound() : Ok(artifact);
    }

    [HttpPost("artifacts/{artifactId:guid}/rendered")]
    public async Task<IActionResult> MarkArtifactRendered(Guid artifactId, [FromBody] ArtifactRenderedRequest? request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _artifacts.MarkRenderedAsync(artifactId, userId, request?.RenderError, ct);
        return Ok(new { artifactId, rendered = true });
    }

    [HttpGet("events/session/{sessionId:guid}")]
    public async Task<IActionResult> GetSessionEvents(Guid sessionId, [FromQuery] string? after = "0-0", [FromQuery] int take = 50)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ownsSession = await _db.Sessions
            .AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.UserId == userId, HttpContext.RequestAborted);
        if (!ownsSession) return NotFound();

        var events = await _redis.ReadStreamEventsAsync($"orka:v3:tutor-events:{sessionId}", after ?? "0-0", take);
        return Ok(new
        {
            sessionId,
            after = after ?? "0-0",
            events
        });
    }

    [HttpGet("events/session/{sessionId:guid}/timeline")]
    public async Task<IActionResult> GetSessionTimeline(Guid sessionId, [FromQuery] string? after = "0-0", [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var result = await _traceProjection.GetTimelineAsync(userId, sessionId, after ?? "0-0", take, ct);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("style-signal")]
    public async Task<IActionResult> RecordStyleSignal([FromBody] TutorStyleSignalRequest request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var signal = await _styleSignals.DetectAndRecordAsync(
            userId,
            request.TopicId,
            request.SessionId,
            request.Message ?? string.Empty,
            ct);

        return Ok(signal);
    }
}

public sealed class TutorStyleSignalRequest
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string? Message { get; set; }
}

public sealed class ArtifactRenderedRequest
{
    public string? RenderError { get; set; }
}

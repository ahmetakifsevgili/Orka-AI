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
[Route("api/tutor")]
public sealed class TutorController : ControllerBase
{
    private readonly OrkaDbContext _db;
    private readonly ILearningStyleSignalService _styleSignals;
    private readonly IRedisMemoryService _redis;
    private readonly ITeachingArtifactService _artifacts;
    private readonly ITutorPedagogyEvaluationService _pedagogy;
    private readonly ITutorTraceProjectionService _traceProjection;
    private readonly ITutorResponsePolicyService _responsePolicy;

    public TutorController(
        OrkaDbContext db,
        ILearningStyleSignalService styleSignals,
        IRedisMemoryService redis,
        ITeachingArtifactService artifacts,
        ITutorPedagogyEvaluationService pedagogy,
        ITutorTraceProjectionService traceProjection,
        ITutorResponsePolicyService responsePolicy)
    {
        _db = db;
        _styleSignals = styleSignals;
        _redis = redis;
        _artifacts = artifacts;
        _pedagogy = pedagogy;
        _traceProjection = traceProjection;
        _responsePolicy = responsePolicy;
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
            learnerProfile = profile == null ? null : new
            {
                profile.Id,
                profile.TopicId,
                profile.PreferredStyleMode,
                profile.StyleConfidence,
                profile.AffectiveState,
                profile.CognitiveLoad,
                profile.EvidenceCount,
                profile.CreatedAt,
                profile.UpdatedAt
            },
            workingMemory = memory == null ? null : new
            {
                memory.Id,
                memory.TopicId,
                memory.SessionId,
                memory.WorkingMemoryVersion,
                memory.ActiveConceptKey,
                memory.TeachingMode,
                memory.StyleMode,
                memory.AffectiveState,
                memory.CognitiveLoad,
                memory.Source,
                memory.IsDegraded,
                memory.CreatedAt,
                memory.ExpiresAt
            },
            latestTurnState = latestTurn == null ? null : new
            {
                latestTurn.Id,
                latestTurn.TopicId,
                latestTurn.SessionId,
                latestTurn.WorkingMemorySnapshotId,
                latestTurn.ConceptGraphSnapshotId,
                latestTurn.ActiveConceptKey,
                latestTurn.TeachingMode,
                latestTurn.StyleMode,
                latestTurn.AffectiveState,
                latestTurn.CognitiveLoad,
                latestTurn.GroundingStatus,
                latestTurn.CreatedAt
            }
        });
    }

    [HttpGet("policy/session/{sessionId:guid}")]
    public async Task<IActionResult> GetSessionPolicy(Guid sessionId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ownsSession = await _db.Sessions
            .AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (!ownsSession) return NotFound();

        var policy = await _responsePolicy.BuildPolicyAsync(userId, new TutorResponsePolicyRequestDto { SessionId = sessionId }, ct);
        return Ok(policy);
    }

    [HttpGet("policy/topic/{topicId:guid}")]
    public async Task<IActionResult> GetTopicPolicy(Guid topicId, [FromQuery] Guid? sessionId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ownsTopic = await _db.Topics
            .AsNoTracking()
            .AnyAsync(t => t.Id == topicId && t.UserId == userId, ct);
        if (!ownsTopic) return NotFound();

        var policy = await _responsePolicy.BuildPolicyAsync(userId, new TutorResponsePolicyRequestDto { TopicId = topicId, SessionId = sessionId }, ct);
        return Ok(policy);
    }

    [HttpPost("policy/evaluate")]
    public async Task<IActionResult> EvaluateTutorResponsePolicy([FromBody] TutorResponseQualityEvaluationRequestDto request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (request.TopicId.HasValue)
        {
            var ownsTopic = await _db.Topics.AsNoTracking().AnyAsync(t => t.Id == request.TopicId && t.UserId == userId, ct);
            if (!ownsTopic) return NotFound();
        }

        var result = await _responsePolicy.EvaluateTutorResponseAsync(userId, request, ct);
        return Ok(result);
    }

    [HttpGet("response-quality/latest")]
    public async Task<IActionResult> GetLatestResponseQuality([FromQuery] Guid? topicId, [FromQuery] Guid? sessionId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (topicId.HasValue)
        {
            var ownsTopic = await _db.Topics.AsNoTracking().AnyAsync(t => t.Id == topicId && t.UserId == userId, ct);
            if (!ownsTopic) return NotFound();
        }

        var result = await _responsePolicy.GetLatestResponseQualityAsync(userId, topicId, sessionId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("next-actions")]
    public async Task<IActionResult> GetTutorNextActions([FromQuery] Guid? topicId, [FromQuery] Guid? sessionId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (topicId.HasValue)
        {
            var ownsTopic = await _db.Topics.AsNoTracking().AnyAsync(t => t.Id == topicId && t.UserId == userId, ct);
            if (!ownsTopic) return NotFound();
        }

        var result = await _responsePolicy.GetTutorNextLearningActionsAsync(userId, topicId, sessionId, ct);
        return Ok(result);
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

        return Ok(new
        {
            trace = new
            {
                trace.Id,
                trace.TopicId,
                trace.SessionId,
                trace.TutorTurnStateId,
                trace.TeachingMode,
                trace.ActiveConceptKey,
                trace.StyleMode,
                trace.DirectAnswerPolicy,
                trace.GroundingPolicy,
                trace.NextCheckPrompt,
                trace.CreatedAt
            },
            tools = tools.Select(tool => new
            {
                tool.Id,
                tool.TopicId,
                tool.SessionId,
                tool.TutorActionTraceId,
                tool.ToolId,
                tool.Provider,
                tool.Status,
                tool.Success,
                tool.RiskLevel,
                tool.Evidence,
                tool.FallbackReason,
                tool.ErrorCode,
                tool.SafeMessage,
                tool.Confidence,
                tool.SourceCount,
                tool.LatencyMs,
                tool.StartedAt,
                tool.FinishedAt
            }),
            artifacts = artifacts.Select(artifact => new
            {
                artifact.Id,
                artifact.TopicId,
                artifact.SessionId,
                artifact.TutorActionTraceId,
                artifact.ArtifactType,
                artifact.Title,
                artifact.Content,
                artifact.RenderFormat,
                artifact.Status,
                artifact.Provider,
                artifact.ExternalUrl,
                artifact.RenderError,
                artifact.RenderedAt,
                artifact.CreatedAt
            }),
            evidence = evidence.Select(item => new
            {
                item.Id,
                item.TopicId,
                item.SessionId,
                item.TutorTurnStateId,
                item.TutorActionTraceId,
                item.TutorToolCallId,
                item.EvidenceType,
                item.Provider,
                item.ConceptKey,
                item.Title,
                item.Summary,
                item.FactualClaim,
                item.AnalogyCandidate,
                item.CitationUrl,
                item.CitationLabel,
                item.Confidence,
                item.Freshness,
                item.RiskLevel,
                item.Status,
                item.CreatedAt,
                item.ExpiresAt
            }),
            reflections = reflections.Select(reflection => new
            {
                reflection.Id,
                reflection.TopicId,
                reflection.SessionId,
                reflection.TutorActionTraceId,
                reflection.TutorTurnStateId,
                reflection.PolicyApplied,
                reflection.SourceClaimWithoutSource,
                reflection.DirectAnswerRiskHandled,
                reflection.ArtifactRendered,
                reflection.MicroCheckAsked,
                reflection.CreatedAt
            }),
            pedagogyRuns = pedagogyRuns.Select(run => new
            {
                run.Id,
                run.TopicId,
                run.SessionId,
                run.TutorTurnStateId,
                run.TutorActionTraceId,
                run.TutorReflectionUpdateId,
                run.Status,
                run.OverallScore,
                run.HasCriticalViolation,
                run.WarningCount,
                run.CriticalViolationCount,
                run.LlmJudgeUsed,
                run.Summary,
                run.Recommendation,
                run.CreatedAt
            }),
            pedagogyScores = pedagogyScores.Select(score => new
            {
                score.Id,
                score.EvaluationRunId,
                score.TopicId,
                score.TutorActionTraceId,
                score.RubricKey,
                score.Score,
                score.Severity,
                score.IsCritical,
                score.Evidence,
                score.Recommendation,
                score.CreatedAt
            })
        });
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
        return artifact == null
            ? NotFound()
            : Ok(new
            {
                artifact.Id,
                artifact.TopicId,
                artifact.SessionId,
                artifact.TutorActionTraceId,
                artifact.ArtifactType,
                artifact.Title,
                artifact.Content,
                artifact.RenderFormat,
                artifact.Status,
                artifact.Provider,
                artifact.ExternalUrl,
                artifact.RenderError,
                artifact.RenderedAt,
                artifact.CreatedAt
            });
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

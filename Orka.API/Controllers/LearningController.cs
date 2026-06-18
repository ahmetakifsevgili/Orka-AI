using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.API.Services;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/learning")]
public class LearningController : ControllerBase
{
    private const int MaxSignalTypeLength = 80;
    private const int MaxSkillLength = 160;
    private const int MaxTopicPathLength = 500;
    private const int MaxPayloadJsonLength = 8_000;

    private readonly ILearningSignalService _signals;
    private readonly OrkaDbContext _db;
    private readonly IReviewSrsService _reviews;
    private readonly ResourceOwnershipGuard _ownership;
    private readonly ITopicScopeResolver _topicScopeResolver;
    private readonly ILongTermAdaptiveLearningService _longTermAdaptiveLearning;
    private readonly IOrkaLearningStateService _orkaLearningState;
    private readonly ILearningContextPackService _contextPack;
    private readonly IOrkaMissionControlService _missionControl;
    private readonly IOrkaStudyCoachService _studyCoach;

    public LearningController(
        ILearningSignalService signals,
        OrkaDbContext db,
        IReviewSrsService reviews,
        ResourceOwnershipGuard ownership,
        ITopicScopeResolver topicScopeResolver,
        ILongTermAdaptiveLearningService longTermAdaptiveLearning,
        IOrkaLearningStateService orkaLearningState,
        ILearningContextPackService contextPack,
        IOrkaMissionControlService missionControl,
        IOrkaStudyCoachService studyCoach)
    {
        _signals = signals;
        _db = db;
        _reviews = reviews;
        _ownership = ownership;
        _topicScopeResolver = topicScopeResolver;
        _longTermAdaptiveLearning = longTermAdaptiveLearning;
        _orkaLearningState = orkaLearningState;
        _contextPack = contextPack;
        _missionControl = missionControl;
        _studyCoach = studyCoach;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("topic/{topicId:guid}/summary")]
    public async Task<IActionResult> GetTopicSummary(Guid topicId)
    {
        var userId = GetUserId();
        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
            return NotFound(new { message = "Konu bulunamadi." });

        var summary = await _signals.GetTopicSummaryAsync(userId, topicId, HttpContext.RequestAborted);
        return Ok(summary);
    }

    [HttpGet("topic/{topicId:guid}/recommendations")]
    [HttpGet("topic/{topicId:guid}/review/due")]
    public async Task<IActionResult> GetDueReview(Guid topicId)
    {
        var userId = GetUserId();
        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
            return NotFound(new { message = "Konu bulunamadi." });

        var recommendations = await _signals.GetRecommendationsAsync(userId, topicId, HttpContext.RequestAborted);
        var durable = await _reviews.GetDueAsync(userId, topicId, HttpContext.RequestAborted);
        return Ok(new ReviewDueResponse(durable, recommendations));
    }

    [HttpGet("adaptive-profile")]
    public async Task<ActionResult<LongTermLearningProfileDto>> GetAdaptiveProfile([FromQuery] Guid? topicId = null)
    {
        var userId = GetUserId();
        var scope = await ResolveTopicScopeAsync(userId, topicId, HttpContext.RequestAborted);
        if (scope == null)
        {
            return NotFound(new { message = "Konu bulunamadi." });
        }

        var profile = await _longTermAdaptiveLearning.BuildProfileAsync(
            userId,
            scope,
            sourceHealth: null,
            ct: HttpContext.RequestAborted);
        return Ok(profile);
    }

    [HttpGet("topic/{topicId:guid}/adaptive-profile")]
    public Task<ActionResult<LongTermLearningProfileDto>> GetTopicAdaptiveProfile(Guid topicId) =>
        GetAdaptiveProfile(topicId);

    [HttpGet("orka-state")]
    public async Task<ActionResult<OrkaLearningStateDto>> GetOrkaState(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        [FromQuery] string? examCode = "KPSS",
        [FromQuery] string? variantCode = null)
    {
        var userId = GetUserId();
        var state = await _orkaLearningState.BuildStateAsync(
            userId,
            topicId,
            sessionId,
            examCode,
            variantCode,
            HttpContext.RequestAborted);

        return state == null
            ? NotFound(new { message = "Ogrenme durumu bulunamadi." })
            : Ok(state);
    }

    [HttpGet("context-pack")]
    public async Task<ActionResult<LearningContextPackDto>> GetContextPack(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        [FromQuery] string? examCode = "KPSS",
        [FromQuery] string? variantCode = null)
    {
        var userId = GetUserId();
        var pack = await _contextPack.BuildPackAsync(
            userId,
            topicId,
            sessionId,
            examCode,
            variantCode,
            HttpContext.RequestAborted);

        return pack == null
            ? NotFound(new { message = "Ogrenme context paketi bulunamadi." })
            : Ok(pack);
    }

    [HttpGet("mission-control")]
    public async Task<ActionResult<OrkaMissionControlDto>> GetMissionControl(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        [FromQuery] string? examCode = "KPSS",
        [FromQuery] string? variantCode = null)
    {
        var userId = GetUserId();
        var mission = await _missionControl.BuildMissionControlAsync(
            userId,
            topicId,
            sessionId,
            examCode,
            variantCode,
            HttpContext.RequestAborted);

        return mission == null
            ? NotFound(new { message = "Mission control durumu bulunamadi." })
            : Ok(mission);
    }

    [HttpGet("study-coach")]
    public async Task<ActionResult<OrkaStudyCoachDto>> GetStudyCoach(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        [FromQuery] string? examCode = "KPSS",
        [FromQuery] string? variantCode = null)
    {
        var userId = GetUserId();
        var coach = await _studyCoach.BuildStudyCoachAsync(
            userId,
            topicId,
            sessionId,
            examCode,
            variantCode,
            HttpContext.RequestAborted);

        return coach == null
            ? NotFound(new { message = "Study coach durumu bulunamadi." })
            : Ok(coach);
    }

    [HttpPost("review/{recommendationId:guid}/complete")]
    public async Task<IActionResult> CompleteReview(Guid recommendationId, [FromBody] CompleteReviewRequest? request)
    {
        if (request == null)
            return BadRequest(new { message = "Review istegi zorunlu." });

        if (request.Quality is < 0 or > 5)
            return BadRequest(new { message = "Quality 0 ile 5 arasinda olmalidir." });

        var userId = GetUserId();
        var durable = await _reviews.CompleteAsync(
            userId,
            recommendationId,
            request.Quality,
            responseMode: "legacy-learning-endpoint",
            notes: null,
            ct: HttpContext.RequestAborted);
        if (durable != null)
            return Ok(new { completed = true, recommendationId = durable.Id, durable.SkillTag, request.Quality, durable });

        var recommendation = await _db.StudyRecommendations
            .FirstOrDefaultAsync(r => r.Id == recommendationId && r.UserId == userId, HttpContext.RequestAborted);

        if (recommendation == null)
            return NotFound(new { message = "Review item bulunamadi." });

        if (!recommendation.IsDone)
        {
            recommendation.IsDone = true;
            recommendation.CompletedAt = DateTime.UtcNow;

            await _signals.RecordSignalAsync(
                userId,
                recommendation.TopicId,
                sessionId: null,
                LearningSignalTypes.ReviewCompleted,
                recommendation.SkillTag,
                recommendation.SkillTag,
                score: Math.Clamp(request.Quality, 0, 5) * 20,
                isPositive: request.Quality >= 3,
                payloadJson: JsonSerializer.Serialize(new { recommendationId, request.Quality }),
                ct: HttpContext.RequestAborted);
        }

        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { completed = true, recommendationId, recommendation.SkillTag, request.Quality });
    }

    [HttpPost("signal")]
    public async Task<IActionResult> RecordSignal([FromBody] RecordLearningSignalRequest? request)
    {
        if (request == null)
            return BadRequest(new { message = "Signal istegi zorunlu." });

        if (string.IsNullOrWhiteSpace(request.SignalType))
            return BadRequest(new { message = "SignalType zorunlu." });

        if (request.SignalType.Length > MaxSignalTypeLength)
            return BadRequest(new { message = "SignalType cok uzun." });

        if (request.SkillTag is { Length: > MaxSkillLength } ||
            request.TopicPath is { Length: > MaxTopicPathLength })
        {
            return BadRequest(new { message = "Signal alani cok uzun." });
        }

        if (request.Score is < 0 or > 100)
            return BadRequest(new { message = "Score 0 ile 100 arasinda olmalidir." });

        if (!IsValidPayloadJson(request.SignalType, request.PayloadJson, out var payloadError))
            return BadRequest(new { message = payloadError });

        var userId = GetUserId();
        if (!await _ownership.OptionalTopicBelongsToUserAsync(userId, request.TopicId, HttpContext.RequestAborted) ||
            !await _ownership.OptionalSessionBelongsToUserAsync(userId, request.SessionId, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Kaynak bulunamadi." });
        }

        var signalType = LearningSignalTypes.Normalize(request.SignalType);

        await _signals.RecordSignalAsync(
            userId,
            request.TopicId,
            request.SessionId,
            signalType,
            request.SkillTag,
            request.TopicPath,
            request.Score,
            request.IsPositive,
            request.PayloadJson,
            HttpContext.RequestAborted);

        return Ok(new { recorded = true, signalType });
    }

    private static bool IsValidPayloadJson(string signalType, string? payloadJson, out string message)
    {
        message = "PayloadJson gecersiz.";
        if (string.IsNullOrWhiteSpace(payloadJson))
            return true;

        if (payloadJson.Length > MaxPayloadJsonLength)
        {
            message = "PayloadJson cok uzun.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!LearningSignalTypes.IsKnown(signalType))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("schemaVersion", out var schemaVersion) ||
                    schemaVersion.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(schemaVersion.GetString()))
                {
                    message = "Custom signal PayloadJson schemaVersion icermelidir.";
                    return false;
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyCollection<Guid>?> ResolveTopicScopeAsync(Guid userId, Guid? topicId, CancellationToken ct)
    {
        if (!topicId.HasValue)
        {
            return Array.Empty<Guid>();
        }

        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId.Value, ct))
        {
            return null;
        }

        var scope = await _topicScopeResolver.ResolveAsync(userId, topicId.Value, ct);
        return scope.IsValid && scope.TreeTopicIds.Count > 0
            ? scope.TreeTopicIds.ToArray()
            : [topicId.Value];
    }
}

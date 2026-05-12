using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/production-readiness")]
public sealed class ProductionReadinessController : ControllerBase
{
    private readonly IProductionReadinessService _readiness;
    private readonly IRetentionCleanupService _retention;
    private readonly IRedisStreamMaintenanceService _redisStreams;

    public ProductionReadinessController(
        IProductionReadinessService readiness,
        IRetentionCleanupService retention,
        IRedisStreamMaintenanceService redisStreams)
    {
        _readiness = readiness;
        _retention = retention;
        _redisStreams = redisStreams;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("v1")]
    public async Task<ActionResult<ProductionReadinessDto>> GetV1(CancellationToken ct)
    {
        return Ok(await _readiness.GetV1ReadinessAsync(GetUserId(), ct));
    }

    [HttpPost("retention/audio/purge")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AudioRetentionSummaryDto>> PurgeAudio(CancellationToken ct)
    {
        return Ok(await _retention.PurgeExpiredAudioAsync(ct));
    }

    [HttpPost("redis/tutor-events/trim")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RedisStreamMaintenanceSummaryDto>> TrimTutorEvents(CancellationToken ct)
    {
        return Ok(await _redisStreams.TrimTutorEventStreamsAsync(ct));
    }
}

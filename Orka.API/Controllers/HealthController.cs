using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orka.API.Controllers;

/// <summary>
/// Sistem sağlık durumu endpoint'leri.
/// /health/live  — process ayakta mı? (Kubernetes liveness probe)
/// /health/ready — dış bağımlılıklar (Redis + SQL) hazır mı? (readiness probe)
/// /health       — tüm check'lerin detaylı özeti
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    public HealthController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    /// <summary>
    /// Liveness probe — process ayakta olduğu sürece 200 döner.
    /// GET /health/live
    /// </summary>
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "alive", timestamp = DateTime.UtcNow });

    /// <summary>
    /// Readiness probe — Redis ve SQL bağlantılarını kontrol eder.
    /// GET /health/ready
    /// </summary>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        HealthReport result;
        try
        {
            result = await _healthCheckService.CheckHealthAsync(
                r => r.Tags.Contains("ready"),
                timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(503, new
            {
                status = "Unhealthy",
                duration = 2000,
                timedOut = true,
                checks = new[]
                {
                    new
                    {
                        name = "readiness",
                        status = "Unhealthy",
                        description = "Readiness checks exceeded 2 seconds.",
                        duration = 2000.0
                    }
                }
            });
        }

        return result.Status == HealthStatus.Healthy
            ? Ok(BuildResponse(result))
            : StatusCode(503, BuildResponse(result));
    }

    /// <summary>
    /// Tüm health check'lerin detaylı özeti.
    /// GET /health
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Health()
    {
        var result = await _healthCheckService.CheckHealthAsync(HttpContext.RequestAborted);

        return result.Status == HealthStatus.Healthy
            ? Ok(BuildResponse(result))
            : StatusCode(503, BuildResponse(result));
    }

    private static object BuildResponse(HealthReport report) => new
    {
        status = report.Status.ToString(),
        duration = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            duration = e.Value.Duration.TotalMilliseconds
        })
    };
}

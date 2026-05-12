using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orka.API.Controllers;

/// <summary>
/// System health endpoints.
/// /health/live  - process liveness
/// /health/ready - dependency readiness
/// /health       - detailed only outside protected environments
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("health")]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;
    private readonly IWebHostEnvironment _environment;

    public HealthController(
        HealthCheckService healthCheckService,
        IWebHostEnvironment environment)
    {
        _healthCheckService = healthCheckService;
        _environment = environment;
    }

    /// <summary>
    /// Liveness probe.
    /// GET /health/live
    /// </summary>
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "alive", timestamp = DateTime.UtcNow });

    /// <summary>
    /// Readiness probe.
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
                timedOut = true
            });
        }

        var response = UsePublicSafeHealth()
            ? BuildPublicResponse(result)
            : BuildDetailedResponse(result);

        return result.Status == HealthStatus.Healthy
            ? Ok(response)
            : StatusCode(503, response);
    }

    /// <summary>
    /// Aggregate health. Detailed check data is hidden in protected environments.
    /// GET /health
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Health()
    {
        var result = await _healthCheckService.CheckHealthAsync(HttpContext.RequestAborted);
        var response = UsePublicSafeHealth()
            ? BuildPublicResponse(result)
            : BuildDetailedResponse(result);

        return result.Status == HealthStatus.Healthy
            ? Ok(response)
            : StatusCode(503, response);
    }

    private bool UsePublicSafeHealth() =>
        _environment.IsProduction() || _environment.IsStaging();

    private static object BuildPublicResponse(HealthReport report) => new
    {
        status = report.Status.ToString(),
        duration = report.TotalDuration.TotalMilliseconds
    };

    private static object BuildDetailedResponse(HealthReport report) => new
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

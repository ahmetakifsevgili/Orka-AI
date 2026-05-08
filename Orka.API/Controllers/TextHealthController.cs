using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/dev/text-health")]
public sealed class TextHealthController : ControllerBase
{
    private readonly ITextHealthService _textHealth;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public TextHealthController(
        ITextHealthService textHealth,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _textHealth = textHealth;
        _environment = environment;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> DryRun(CancellationToken ct)
    {
        if (!CanUseTextHealth())
            return NotFound();

        return Ok(await _textHealth.DryRunAsync(ct));
    }

    [HttpPost("repair")]
    public async Task<IActionResult> Repair(CancellationToken ct)
    {
        if (!CanUseTextHealth())
            return NotFound();

        if (!_configuration.GetValue("TextHealth:RepairEnabled", false))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Text repair is disabled. Set TextHealth:RepairEnabled=true in a controlled dev/admin environment."
            });
        }

        return Ok(await _textHealth.RepairAsync(ct));
    }

    private bool CanUseTextHealth() =>
        _environment.IsDevelopment() || User.IsInRole("Admin");
}

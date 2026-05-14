using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/central-exams")]
public sealed class CentralExamDenemeController : ControllerBase
{
    private readonly ICentralExamDenemeService _denemeler;

    public CentralExamDenemeController(ICentralExamDenemeService denemeler)
    {
        _denemeler = denemeler;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("kpss/denemeler")]
    public async Task<ActionResult<IReadOnlyList<CentralExamDenemeBlueprintDto>>> GetKpssDenemeler(
        [FromQuery] string? variantCode,
        CancellationToken ct)
    {
        return Ok(await _denemeler.GetDenemeBlueprintsAsync(GetUserId(), "KPSS", variantCode, ct));
    }

    [HttpGet("kpss/denemeler/{blueprintCode}")]
    public async Task<ActionResult<CentralExamDenemeBlueprintDto>> GetKpssDeneme(
        string blueprintCode,
        [FromQuery] string? variantCode,
        CancellationToken ct)
    {
        var result = await _denemeler.GetDenemeBlueprintAsync(GetUserId(), blueprintCode, variantCode, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("kpss/denemeler/{blueprintCode}/start")]
    public async Task<ActionResult<CentralExamDenemeSessionDto>> StartKpssDeneme(
        string blueprintCode,
        [FromBody] CentralExamDenemeStartRequestDto request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _denemeler.StartDenemeAsync(GetUserId(), blueprintCode, request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("kpss/denemeler/submit")]
    public async Task<ActionResult<CentralExamDenemeResultDto>> SubmitKpssDeneme(
        [FromBody] CentralExamDenemeSubmitRequestDto request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _denemeler.SubmitDenemeAsync(GetUserId(), request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("deneme-attempts/{id:guid}")]
    public async Task<ActionResult<CentralExamDenemeResultDto>> GetDenemeAttempt(Guid id, CancellationToken ct)
    {
        var result = await _denemeler.GetDenemeAttemptAsync(GetUserId(), id, ct);
        return result is null ? NotFound() : Ok(result);
    }
}

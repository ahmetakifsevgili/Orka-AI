using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/curriculum")]
public sealed class CurriculumController : ControllerBase
{
    private readonly ICurriculumSourceRegistryService _curriculum;

    public CurriculumController(ICurriculumSourceRegistryService curriculum)
    {
        _curriculum = curriculum;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("sources")]
    public async Task<ActionResult<IReadOnlyList<SourceRegistryItemDto>>> GetSources(CancellationToken ct)
    {
        return Ok(await _curriculum.GetSourcesAsync(GetUserId(), ct));
    }

    [HttpGet("sources/{id:guid}")]
    public async Task<ActionResult<SourceRegistryItemDto>> GetSource(Guid id, CancellationToken ct)
    {
        var result = await _curriculum.GetSourceAsync(GetUserId(), id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("sources")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SourceRegistryItemDto>> RegisterSource(
        [FromBody] RegisterSourceRegistryItemDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _curriculum.RegisterSourceAsync(GetUserId(), request, ct);
            return CreatedAtAction(nameof(GetSource), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("sources/{id:guid}/verify")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SourceRegistryItemDto>> VerifySource(
        Guid id,
        [FromBody] VerifySourceRegistryItemDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _curriculum.VerifySourceAsync(GetUserId(), id, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("sources/{id:guid}/license-review")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ContentLicenseReviewDto>> ReviewSourceLicense(
        Guid id,
        [FromBody] ReviewSourceLicenseDto request,
        CancellationToken ct)
    {
        var result = await _curriculum.ReviewSourceLicenseAsync(GetUserId(), id, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("versions")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CurriculumVersionDto>> CreateVersion(
        [FromBody] CreateCurriculumVersionDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _curriculum.CreateCurriculumVersionAsync(GetUserId(), request, ct);
            return CreatedAtAction(nameof(GetVersion), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("versions/{id:guid}")]
    public async Task<ActionResult<CurriculumVersionDto>> GetVersion(Guid id, CancellationToken ct)
    {
        var result = await _curriculum.GetCurriculumVersionAsync(GetUserId(), id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("exams/{examCode}/versions")]
    public async Task<ActionResult<IReadOnlyList<CurriculumVersionDto>>> GetExamVersions(
        string examCode,
        CancellationToken ct)
    {
        return Ok(await _curriculum.GetCurriculumVersionsForExamAsync(GetUserId(), examCode, ct));
    }

    [HttpPost("versions/{id:guid}/deprecate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CurriculumVersionDto>> DeprecateVersion(
        Guid id,
        [FromBody] DeprecateCurriculumVersionDto request,
        CancellationToken ct)
    {
        var result = await _curriculum.DeprecateCurriculumVersionAsync(GetUserId(), id, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("versions/{id:guid}/supersede")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CurriculumVersionDto>> SupersedeVersion(
        Guid id,
        [FromBody] SupersedeCurriculumVersionDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _curriculum.SupersedeCurriculumVersionAsync(GetUserId(), id, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("versions/{id:guid}/nodes")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CurriculumNodeDto>> AddNode(
        Guid id,
        [FromBody] CreateCurriculumNodeDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _curriculum.AddCurriculumNodeAsync(GetUserId(), id, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("versions/{id:guid}/outcome-mappings")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CurriculumOutcomeMappingDto>> MapOutcome(
        Guid id,
        [FromBody] CreateCurriculumOutcomeMappingDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _curriculum.MapOutcomeAsync(GetUserId(), id, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("outcomes/{examOutcomeId:guid}/sources")]
    public async Task<ActionResult<CurriculumOutcomeSourceDto>> GetOutcomeSources(
        Guid examOutcomeId,
        CancellationToken ct)
    {
        return Ok(await _curriculum.GetOutcomeSourcesAsync(GetUserId(), examOutcomeId, ct));
    }
}

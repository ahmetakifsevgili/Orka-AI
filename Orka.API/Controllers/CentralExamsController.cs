using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/central-exams")]
public sealed class CentralExamsController : ControllerBase
{
    private readonly ICentralExamStudyService _centralExams;

    public CentralExamsController(ICentralExamStudyService centralExams)
    {
        _centralExams = centralExams;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CentralExamDto>>> GetCentralExams(CancellationToken ct)
    {
        return Ok(await _centralExams.GetCentralExamsAsync(GetUserId(), ct));
    }

    [HttpGet("kpss")]
    public async Task<ActionResult<CentralExamStudyHomeDto>> GetKpssStudyHome([FromQuery] string? variantCode, CancellationToken ct)
    {
        return Ok(await _centralExams.GetKpssStudyHomeAsync(GetUserId(), variantCode, ct));
    }

    [HttpGet("yks")]
    public async Task<ActionResult<CentralExamStudyHomeDto>> GetYksStudyHome([FromQuery] string? variantCode, CancellationToken ct)
    {
        var home = await _centralExams.GetStudyHomeAsync(GetUserId(), "YKS", variantCode, ct);
        return home is null ? NotFound() : Ok(home);
    }

    [HttpGet("lgs")]
    public async Task<ActionResult<CentralExamStudyHomeDto>> GetLgsStudyHome([FromQuery] string? variantCode, CancellationToken ct)
    {
        var home = await _centralExams.GetStudyHomeAsync(GetUserId(), "LGS", variantCode, ct);
        return home is null ? NotFound() : Ok(home);
    }

    [HttpGet("yds")]
    public async Task<ActionResult<CentralExamStudyHomeDto>> GetYdsStudyHome([FromQuery] string? variantCode, CancellationToken ct)
    {
        var home = await _centralExams.GetStudyHomeAsync(GetUserId(), "YDS", variantCode, ct);
        return home is null ? NotFound() : Ok(home);
    }

    [HttpGet("kpss/countdown")]
    public async Task<ActionResult<CentralExamCountdownDto>> GetKpssCountdown([FromQuery] string? variantCode, CancellationToken ct)
    {
        return Ok(await _centralExams.GetKpssCountdownAsync(GetUserId(), variantCode, ct));
    }

    [HttpGet("kpss/turkce-paragraf")]
    public async Task<ActionResult<CentralExamPracticeEntryDto>> GetKpssTurkceParagrafEntry([FromQuery] string? variantCode, CancellationToken ct)
    {
        return Ok(await _centralExams.GetKpssTurkceParagrafEntryAsync(GetUserId(), variantCode, ct));
    }

    [HttpPost("kpss/turkce-paragraf/start")]
    public async Task<ActionResult<PracticeSessionDto>> StartKpssTurkceParagrafPractice(
        [FromBody] PracticeStartRequestDto request,
        CancellationToken ct)
    {
        return Ok(await _centralExams.StartKpssTurkceParagrafPracticeAsync(GetUserId(), request, ct));
    }

    [HttpPost("kpss/turkce-paragraf/submit")]
    public async Task<ActionResult<PracticeResultDto>> SubmitKpssTurkceParagrafPractice(
        [FromBody] PracticeSubmitRequestDto request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _centralExams.SubmitKpssTurkceParagrafPracticeAsync(GetUserId(), request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("practice-attempts/{id:guid}")]
    public async Task<ActionResult<PracticeResultDto>> GetPracticeAttempt(Guid id, CancellationToken ct)
    {
        var result = await _centralExams.GetPracticeAttemptAsync(GetUserId(), id, ct);
        return result is null ? NotFound() : Ok(result);
    }
}

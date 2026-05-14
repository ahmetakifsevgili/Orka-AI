using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/exams")]
public sealed class ExamsController : ControllerBase
{
    private readonly IExamFrameworkService _examFramework;

    public ExamsController(IExamFrameworkService examFramework)
    {
        _examFramework = examFramework;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExamDefinitionDto>>> GetDefinitions(CancellationToken ct)
    {
        return Ok(await _examFramework.GetDefinitionsAsync(GetUserId(), ct));
    }

    [HttpGet("{examCode}")]
    public async Task<ActionResult<ExamDefinitionDto>> GetTree(string examCode, CancellationToken ct)
    {
        var result = await _examFramework.GetTreeAsync(GetUserId(), examCode, null, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{examCode}/variants/{variantCode}")]
    public async Task<ActionResult<ExamDefinitionDto>> GetVariantTree(string examCode, string variantCode, CancellationToken ct)
    {
        var result = await _examFramework.GetTreeAsync(GetUserId(), examCode, variantCode, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("import-tree")]
    public async Task<ActionResult<ExamDefinitionDto>> ImportTree([FromBody] ExamTreeImportDto request, CancellationToken ct)
    {
        try
        {
            var result = await _examFramework.ImportTreeAsync(GetUserId(), request, ct);
            return CreatedAtAction(nameof(GetTree), new { examCode = result.Code }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/question-imports")]
public sealed class QuestionImportsController : ControllerBase
{
    private readonly IQuestionImportService _questionImports;

    public QuestionImportsController(IQuestionImportService questionImports)
    {
        _questionImports = questionImports;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("preview")]
    public async Task<ActionResult<QuestionImportPreviewDto>> PreviewImport(
        [FromBody] QuestionImportRequestDto request,
        CancellationToken ct)
    {
        var result = await _questionImports.PreviewImportAsync(GetUserId(), request, ct);
        return CreatedAtAction(nameof(GetImportPreview), new { id = result.Id }, result);
    }

    [HttpPost("approve")]
    public async Task<ActionResult<QuestionImportResultDto>> ApproveImport(
        [FromBody] QuestionImportApprovalDto request,
        CancellationToken ct)
    {
        var result = await _questionImports.ApproveImportAsync(GetUserId(), request, ct);
        return result.Issues.Any(i => i.Severity == "error") ? BadRequest(result) : Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuestionImportPreviewDto>> GetImportPreview(Guid id, CancellationToken ct)
    {
        var result = await _questionImports.GetImportPreviewAsync(GetUserId(), id, ct);
        return result is null ? NotFound() : Ok(result);
    }
}

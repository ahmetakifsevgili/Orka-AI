using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[EnableRateLimiting("QuestionImportLimiter")]
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

    [HttpPost("preview-package")]
    public async Task<ActionResult<QuestionImportPreviewDto>> PreviewPackageImport(
        [FromBody] QuestionImportPackageDto request,
        CancellationToken ct)
    {
        var result = await _questionImports.PreviewPackageImportAsync(GetUserId(), request, ct);
        return CreatedAtAction(nameof(GetImportPreview), new { id = result.Id }, result);
    }

    [HttpPost("preview-aiken")]
    public async Task<ActionResult<QuestionImportPreviewDto>> PreviewAikenImport(
        [FromBody] QuestionImportTextAdapterRequestDto request,
        CancellationToken ct)
    {
        var result = await _questionImports.PreviewAikenImportAsync(GetUserId(), request, ct);
        return CreatedAtAction(nameof(GetImportPreview), new { id = result.Id }, result);
    }

    [HttpPost("preview-gift")]
    public async Task<ActionResult<QuestionImportPreviewDto>> PreviewGiftImport(
        [FromBody] QuestionImportTextAdapterRequestDto request,
        CancellationToken ct)
    {
        var result = await _questionImports.PreviewGiftImportAsync(GetUserId(), request, ct);
        return CreatedAtAction(nameof(GetImportPreview), new { id = result.Id }, result);
    }

    [HttpPost("preview-qti")]
    public async Task<ActionResult<QuestionImportPreviewDto>> PreviewQtiImport(
        [FromBody] QuestionImportTextAdapterRequestDto request,
        CancellationToken ct)
    {
        var result = await _questionImports.PreviewQtiImportAsync(GetUserId(), request, ct);
        return CreatedAtAction(nameof(GetImportPreview), new { id = result.Id }, result);
    }

    [HttpPost("preview-moodle")]
    public async Task<ActionResult<QuestionImportPreviewDto>> PreviewMoodleImport(
        [FromBody] QuestionImportTextAdapterRequestDto request,
        CancellationToken ct)
    {
        var result = await _questionImports.PreviewMoodleImportAsync(GetUserId(), request, ct);
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

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/question-drafts")]
public sealed class QuestionDraftGenerationController : ControllerBase
{
    private readonly IQuestionDraftGenerationService _draftGeneration;

    public QuestionDraftGenerationController(IQuestionDraftGenerationService draftGeneration)
    {
        _draftGeneration = draftGeneration;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("preview")]
    public async Task<ActionResult<QuestionDraftPreviewDto>> PreviewDraftGeneration(
        [FromBody] QuestionDraftGenerationRequestDto request,
        CancellationToken ct)
    {
        var result = await _draftGeneration.PreviewDraftGenerationAsync(GetUserId(), request, ct);
        return result.Issues.Any(i => i.Severity == "error")
            ? BadRequest(result)
            : CreatedAtAction(nameof(GetDraftGenerationPreview), new { id = result.Id }, result);
    }

    [HttpPost("approve")]
    public async Task<ActionResult<QuestionDraftApprovalResultDto>> ApproveDrafts(
        [FromBody] QuestionDraftApprovalDto request,
        CancellationToken ct)
    {
        var result = await _draftGeneration.ApproveDraftsToQuestionBankAsync(GetUserId(), request, ct);
        return result.Issues.Any(i => i.Severity == "error") ? BadRequest(result) : Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuestionDraftPreviewDto>> GetDraftGenerationPreview(Guid id, CancellationToken ct)
    {
        var result = await _draftGeneration.GetDraftGenerationPreviewAsync(GetUserId(), id, ct);
        return result is null ? NotFound() : Ok(result);
    }
}

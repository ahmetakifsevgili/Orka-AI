using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/content-ops")]
public sealed class ContentOperationsController : ControllerBase
{
    private readonly IContentOperationsService _contentOperations;

    public ContentOperationsController(IContentOperationsService contentOperations)
    {
        _contentOperations = contentOperations;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("questions/{questionId:guid}/workflow")]
    public async Task<ActionResult<QuestionReviewWorkflowDto>> GetWorkflow(Guid questionId, CancellationToken ct)
    {
        var result = await _contentOperations.GetWorkflowAsync(GetUserId(), questionId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("questions/{questionId:guid}/submit-review")]
    public async Task<ActionResult<QuestionReviewWorkflowDto>> SubmitReview(
        Guid questionId,
        [FromBody] SubmitQuestionReviewDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _contentOperations.SubmitQuestionForReviewAsync(GetUserId(), questionId, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Invalid(ex.Message));
        }
    }

    [HttpPost("questions/{questionId:guid}/assign-reviewer")]
    public async Task<ActionResult<QuestionReviewWorkflowDto>> AssignReviewer(
        Guid questionId,
        [FromBody] AssignQuestionReviewerDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _contentOperations.AssignReviewerAsync(GetUserId(), questionId, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Invalid(ex.Message));
        }
    }

    [HttpPost("questions/{questionId:guid}/advance-stage")]
    public async Task<ActionResult<QuestionReviewWorkflowDto>> AdvanceStage(
        Guid questionId,
        [FromBody] AdvanceQuestionReviewStageDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _contentOperations.AdvanceReviewStageAsync(GetUserId(), questionId, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Invalid(ex.Message));
        }
    }

    [HttpPost("questions/{questionId:guid}/reject")]
    public async Task<ActionResult<QuestionReviewWorkflowDto>> Reject(
        Guid questionId,
        [FromBody] RejectQuestionReviewDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _contentOperations.RejectQuestionAsync(GetUserId(), questionId, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Invalid(ex.Message));
        }
    }

    [HttpPost("questions/{questionId:guid}/retire")]
    public async Task<ActionResult<QuestionReviewWorkflowDto>> Retire(
        Guid questionId,
        [FromBody] RetireQuestionDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _contentOperations.RetireQuestionAsync(GetUserId(), questionId, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Invalid(ex.Message));
        }
    }

    [HttpGet("questions/{questionId:guid}/publish-readiness")]
    public async Task<ActionResult<QuestionPublishReadinessDto>> GetPublishReadiness(Guid questionId, CancellationToken ct)
    {
        var result = await _contentOperations.GetPublishReadinessAsync(GetUserId(), questionId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("questions/{questionId:guid}/publish")]
    public async Task<ActionResult<QuestionItemDto>> Publish(
        Guid questionId,
        [FromBody] PublishQuestionContentDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _contentOperations.PublishApprovedQuestionAsync(GetUserId(), questionId, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Invalid(ex.Message));
        }
    }

    [HttpGet("questions/{questionId:guid}/versions")]
    public async Task<ActionResult<IReadOnlyList<QuestionContentVersionDto>>> GetVersions(Guid questionId, CancellationToken ct)
    {
        var result = await _contentOperations.GetQuestionVersionsAsync(GetUserId(), questionId, ct);
        return Ok(result);
    }

    private static QuestionValidationResultDto Invalid(string message) => new()
    {
        IsValid = false,
        Errors = [message]
    };
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/questions")]
public sealed class QuestionsController : ControllerBase
{
    private readonly IQuestionBankService _questionBank;

    public QuestionsController(IQuestionBankService questionBank)
    {
        _questionBank = questionBank;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<QuestionItemDto>>> GetQuestions(
        [FromQuery] QuestionBankFilterDto filters,
        CancellationToken ct)
    {
        return Ok(await _questionBank.GetQuestionsAsync(GetUserId(), filters, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuestionItemDto>> GetQuestion(Guid id, CancellationToken ct)
    {
        var result = await _questionBank.GetQuestionAsync(GetUserId(), id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<QuestionItemDto>> CreateQuestion([FromBody] CreateQuestionDto request, CancellationToken ct)
    {
        try
        {
            var result = await _questionBank.CreateQuestionAsync(GetUserId(), request, ct);
            return CreatedAtAction(nameof(GetQuestion), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Invalid(ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<QuestionItemDto>> UpdateQuestion(Guid id, [FromBody] UpdateQuestionDto request, CancellationToken ct)
    {
        try
        {
            var result = await _questionBank.UpdateQuestionAsync(GetUserId(), id, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Invalid(ex.Message));
        }
    }

    [HttpPost("{id:guid}/submit-review")]
    public async Task<ActionResult<QuestionItemDto>> SubmitForReview(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await _questionBank.SubmitForReviewAsync(GetUserId(), id, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Invalid(ex.Message));
        }
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<QuestionItemDto>> PublishQuestion(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await _questionBank.PublishQuestionAsync(GetUserId(), id, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Invalid(ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDeleteQuestion(Guid id, CancellationToken ct)
    {
        var deleted = await _questionBank.SoftDeleteQuestionAsync(GetUserId(), id, ct);
        return deleted ? NoContent() : NotFound();
    }

    private static QuestionValidationResultDto Invalid(string message) => new()
    {
        IsValid = false,
        Errors = [message]
    };
}

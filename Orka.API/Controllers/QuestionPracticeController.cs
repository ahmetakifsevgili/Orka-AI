using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/question-practice")]
public sealed class QuestionPracticeController : ControllerBase
{
    private readonly IQuestionPracticeService _questionPractice;

    public QuestionPracticeController(IQuestionPracticeService questionPractice)
    {
        _questionPractice = questionPractice;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("start")]
    public async Task<ActionResult<QuestionPracticeSessionDto>> Start(
        [FromBody] QuestionPracticeStartRequestDto request,
        CancellationToken ct)
    {
        return Ok(await _questionPractice.StartAsync(GetUserId(), request, ct));
    }

    [HttpPost("submit")]
    public async Task<ActionResult<QuestionPracticeSubmitResponseDto>> Submit(
        [FromBody] QuestionPracticeSubmitRequestDto request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _questionPractice.SubmitAsync(GetUserId(), request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

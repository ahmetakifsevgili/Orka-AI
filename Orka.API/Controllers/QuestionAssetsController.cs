using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/question-assets")]
public sealed class QuestionAssetsController : ControllerBase
{
    private readonly IQuestionBankService _questionBank;

    public QuestionAssetsController(IQuestionBankService questionBank)
    {
        _questionBank = questionBank;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuestionAssetDto>> GetAsset(Guid id, CancellationToken ct)
    {
        var result = await _questionBank.GetAssetAsync(GetUserId(), id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<QuestionAssetDto>> CreateAsset(
        [FromBody] CreateQuestionAssetDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _questionBank.CreateAssetAsync(GetUserId(), request, ct);
            return CreatedAtAction(nameof(GetAsset), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new QuestionValidationResultDto
            {
                IsValid = false,
                Errors = [ex.Message]
            });
        }
    }
}

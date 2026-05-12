using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/flashcards")]
public class FlashcardsController : ControllerBase
{
    private readonly IFlashcardService _flashcards;

    public FlashcardsController(IFlashcardService flashcards)
    {
        _flashcards = flashcards;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? topicId)
    {
        var items = await _flashcards.ListAsync(GetUserId(), topicId, HttpContext.RequestAborted);
        return Ok(items);
    }

    [HttpGet("topic/{topicId:guid}")]
    public async Task<IActionResult> ListByTopic(Guid topicId)
    {
        var items = await _flashcards.ListAsync(GetUserId(), topicId, HttpContext.RequestAborted);
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFlashcardRequest request)
    {
        try
        {
            var item = await _flashcards.CreateAsync(GetUserId(), request, HttpContext.RequestAborted);
            return Ok(item);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Flashcard bağlamı bulunamadı." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("generate")]
    public IActionResult Generate([FromBody] CreateFlashcardRequest request)
    {
        return Ok(new
        {
            proposals = Array.Empty<FlashcardDto>(),
            fallbackReason = "provider_generation_not_required_for_deterministic_contract",
            canCreateManually = true
        });
    }

    [HttpGet("proposals/{topicId:guid}")]
    public IActionResult GetProposals(Guid topicId)
    {
        return Ok(new
        {
            topicId,
            proposals = Array.Empty<FlashcardDto>(),
            fallbackReason = "provider_generation_not_required_for_deterministic_contract",
            canCreateManually = true
        });
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> CreateBulk([FromBody] IReadOnlyList<CreateFlashcardRequest> requests)
    {
        if (requests.Count == 0)
            return BadRequest(new { message = "En az bir flashcard gerekli." });

        if (requests.Count > 50)
            return BadRequest(new { message = "Tek istekte en fazla 50 flashcard olusturulabilir." });

        var created = new List<FlashcardDto>();
        foreach (var request in requests)
        {
            try
            {
                created.Add(await _flashcards.CreateAsync(GetUserId(), request, HttpContext.RequestAborted));
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = "Flashcard bağlamı bulunamadı." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        return Ok(new { items = created, count = created.Count });
    }

    [HttpPost("{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, [FromBody] ReviewFlashcardRequest request)
    {
        var result = await _flashcards.ReviewAsync(GetUserId(), id, request.Quality, request.Notes, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Flashcard bulunamadı." }) : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _flashcards.DeleteAsync(GetUserId(), id, HttpContext.RequestAborted);
        return deleted ? Ok(new { deleted = true, id }) : NotFound(new { message = "Flashcard bulunamadı." });
    }
}

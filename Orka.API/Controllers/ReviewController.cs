using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/review")]
public class ReviewController : ControllerBase
{
    private readonly IReviewSrsService _reviews;

    public ReviewController(IReviewSrsService reviews)
    {
        _reviews = reviews;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("due")]
    public async Task<IActionResult> GetDue([FromQuery] Guid? topicId)
    {
        var items = await _reviews.GetDueAsync(GetUserId(), topicId, HttpContext.RequestAborted);
        return Ok(items);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteDurableReviewRequest request)
    {
        var result = await _reviews.CompleteAsync(
            GetUserId(),
            id,
            request.Quality,
            request.ResponseMode,
            request.Notes,
            HttpContext.RequestAborted);

        return result == null ? NotFound(new { message = "Review item bulunamadı." }) : Ok(result);
    }
}

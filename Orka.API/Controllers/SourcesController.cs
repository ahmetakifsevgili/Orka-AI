using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Exceptions;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/sources")]
public class SourcesController : ControllerBase
{
    private readonly ILearningSourceService _sources;

    public SourcesController(ILearningSourceService sources)
    {
        _sources = sources;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("upload")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] SourceUploadRequest request)
    {
        if (request.File == null || request.File.Length == 0)
            return BadRequest(new { message = "Dosya zorunlu." });

        var ext = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (ext is not ".pdf" and not ".txt" and not ".md")
            return BadRequest(new { message = "Sadece PDF, TXT veya MD destekleniyor." });

        try
        {
            await using var stream = request.File.OpenReadStream();
            var result = await _sources.UploadAsync(
                GetUserId(),
                request.TopicId,
                request.SessionId,
                request.File.FileName,
                request.File.ContentType,
                stream,
                HttpContext.RequestAborted);

            return Ok(result);
        }
        catch (StorageQuotaExceededException)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = "Depolama limitine ulasildi." });
        }
    }

    [HttpGet("topic/{topicId:guid}")]
    public async Task<IActionResult> GetTopicSources(Guid topicId)
    {
        var result = await _sources.GetTopicSourcesAsync(GetUserId(), topicId, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpPost("{sourceId:guid}/ask")]
    public async Task<IActionResult> Ask(Guid sourceId, [FromBody] SourceAskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "Soru boş olamaz." });

        var result = await _sources.AskAsync(GetUserId(), sourceId, request.Question, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpGet("{sourceId:guid}/pages/{page:int}")]
    public async Task<IActionResult> GetPage(Guid sourceId, int page)
    {
        var result = await _sources.GetPageAsync(GetUserId(), sourceId, page, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Sayfa veya kaynak bulunamadı." }) : Ok(result);
    }
}

public class SourceUploadRequest
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public IFormFile? File { get; set; }
}

public class SourceAskRequest
{
    public string Question { get; set; } = string.Empty;
}

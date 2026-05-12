using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.API.Services;
using Orka.Core.Exceptions;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/sources")]
public class SourcesController : ControllerBase
{
    private const int MaxQuestionLength = 4_000;
    private const int MaxTitleLength = 200;
    private const int MaxFileNameLength = 240;

    private readonly ILearningSourceService _sources;
    private readonly ResourceOwnershipGuard _ownership;
    private readonly UploadContentSafetyGuard _contentSafety;

    public SourcesController(
        ILearningSourceService sources,
        ResourceOwnershipGuard ownership,
        UploadContentSafetyGuard contentSafety)
    {
        _sources = sources;
        _ownership = ownership;
        _contentSafety = contentSafety;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("upload")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] SourceUploadRequest? request)
    {
        if (request == null)
            return BadRequest(new { message = "Kaynak yukleme istegi zorunlu." });

        if (request.File == null || request.File.Length == 0)
            return BadRequest(new { message = "Dosya zorunlu." });

        if (request.File.FileName.Length > MaxFileNameLength)
            return BadRequest(new { message = "Dosya adi cok uzun." });

        var ext = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (ext is not ".pdf" and not ".txt" and not ".md")
            return BadRequest(new { message = "Sadece PDF, TXT veya MD destekleniyor." });
        try
        {
            _contentSafety.ValidateMetadata(request.File.FileName, request.File.ContentType, request.File.Length);
        }
        catch (ContentSafetyException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.PublicMessage });
        }

        var userId = GetUserId();
        if (!await _ownership.OptionalTopicBelongsToUserAsync(userId, request.TopicId, HttpContext.RequestAborted) ||
            !await _ownership.OptionalSessionBelongsToUserAsync(userId, request.SessionId, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Kaynak bulunamadi." });
        }

        try
        {
            await using var stream = request.File.OpenReadStream();
            var result = await _sources.UploadAsync(
                userId,
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
        catch (ContentSafetyException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.PublicMessage });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "Kaynak yukleme istegi gecersiz." });
        }
    }

    [HttpGet("topic/{topicId:guid}")]
    public async Task<IActionResult> GetTopicSources(Guid topicId)
    {
        var userId = GetUserId();
        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
            return NotFound(new { message = "Konu bulunamadi." });

        var result = await _sources.GetTopicSourcesAsync(userId, topicId, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpGet("topic/{topicId:guid}/quality")]
    public async Task<IActionResult> GetTopicQuality(Guid topicId)
    {
        var userId = GetUserId();
        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
            return NotFound(new { message = "Konu bulunamadi." });

        var result = await _sources.GetTopicQualityAsync(userId, topicId, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpPost("{sourceId:guid}/ask")]
    public async Task<IActionResult> Ask(Guid sourceId, [FromBody] SourceAskRequest? request)
    {
        if (request == null)
            return BadRequest(new { message = "Soru zorunlu." });

        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "Soru bos olamaz." });

        if (request.Question.Length > MaxQuestionLength)
            return BadRequest(new { message = "Soru cok uzun." });

        var userId = GetUserId();
        if (!await _ownership.SourceBelongsToUserAsync(userId, sourceId, HttpContext.RequestAborted))
            return NotFound(new { message = "Kaynak bulunamadi." });

        try
        {
            var result = await _sources.AskAsync(userId, sourceId, request.Question, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { message = "Kaynak bulunamadi." });
        }
    }

    [HttpGet("{sourceId:guid}/pages/{page:int}")]
    public async Task<IActionResult> GetPage(Guid sourceId, int page)
    {
        if (page < 1)
            return BadRequest(new { message = "Sayfa numarasi gecersiz." });

        var userId = GetUserId();
        if (!await _ownership.SourceBelongsToUserAsync(userId, sourceId, HttpContext.RequestAborted))
            return NotFound(new { message = "Sayfa veya kaynak bulunamadi." });

        var result = await _sources.GetPageAsync(userId, sourceId, page, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Sayfa veya kaynak bulunamadi." }) : Ok(result);
    }

    [HttpPatch("{sourceId:guid}")]
    public async Task<IActionResult> Update(Guid sourceId, [FromBody] SourceUpdateRequest? request)
    {
        if (request == null)
            return BadRequest(new { message = "Guncelleme istegi zorunlu." });

        if (request.Title is { Length: > MaxTitleLength })
            return BadRequest(new { message = "Baslik cok uzun." });

        var userId = GetUserId();
        if (!await _ownership.SourceBelongsToUserAsync(userId, sourceId, HttpContext.RequestAborted))
            return NotFound(new { message = "Kaynak bulunamadi." });

        var result = await _sources.UpdateSourceAsync(userId, sourceId, request.Title, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Kaynak bulunamadi." }) : Ok(result);
    }

    [HttpDelete("{sourceId:guid}")]
    public async Task<IActionResult> Delete(Guid sourceId)
    {
        var userId = GetUserId();
        if (!await _ownership.SourceBelongsToUserAsync(userId, sourceId, HttpContext.RequestAborted))
            return NotFound(new { message = "Kaynak bulunamadi." });

        var deleted = await _sources.DeleteSourceAsync(userId, sourceId, HttpContext.RequestAborted);
        return deleted ? Ok(new { deleted = true, sourceId }) : NotFound(new { message = "Kaynak bulunamadi." });
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

public class SourceUpdateRequest
{
    public string? Title { get; set; }
}

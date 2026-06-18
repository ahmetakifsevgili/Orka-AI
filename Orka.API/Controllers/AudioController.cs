using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[EnableRateLimiting("AudioLimiter")]
[ApiController]
[Route("api/audio")]
public class AudioController : ControllerBase
{
    private readonly IAudioOverviewService _audio;

    public AudioController(IAudioOverviewService audio)
    {
        _audio = audio;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("overview")]
    public async Task<IActionResult> CreateOverview([FromBody] AudioOverviewRequest request)
    {
        var surface = NormalizeSurface(request.Surface);
        if (surface == "wiki" && request.SourceId.HasValue)
        {
            return BadRequest(new { message = "Wiki audio sourceId ile baslatilamaz; kaynak sesleri OrkaLM yuzeyinde calisir." });
        }

        if (surface == "orkalm" && request.WikiPageId.HasValue)
        {
            return BadRequest(new { message = "OrkaLM audio wikiPageId ile baslatilamaz; Wiki ders sesleri Wiki yuzeyinde calisir." });
        }

        if (!request.TopicId.HasValue && !request.SessionId.HasValue && !request.WikiPageId.HasValue && !request.SourceId.HasValue)
        {
            return BadRequest(new { message = "Audio Overview icin topicId, sessionId, wikiPageId veya sourceId zorunlu." });
        }

        AudioOverviewJobDto job;
        try
        {
            job = await _audio.CreateOverviewAsync(
                GetUserId(),
                request.TopicId,
                request.SessionId,
                surface,
                request.WikiPageId,
                request.SourceId,
                request.AudioMode,
                request.TtsQuality,
                HttpContext.RequestAborted);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        return Accepted(job);
    }

    [HttpGet("overview/{jobId:guid}")]
    public async Task<IActionResult> GetOverview(Guid jobId)
    {
        var job = await _audio.GetOverviewAsync(GetUserId(), jobId, HttpContext.RequestAborted);
        if (job == null) return NotFound(new { message = "Audio overview job bulunamadı." });

        return Ok(job);
    }

    [HttpGet("overview/{jobId:guid}/stream")]
    public async Task<IActionResult> Stream(Guid jobId)
    {
        var audio = await _audio.GetAudioAsync(GetUserId(), jobId, HttpContext.RequestAborted);
        if (audio == null) return NotFound(new { message = "Ses dosyası hazır değil." });

        return File(audio.Value.Bytes, audio.Value.ContentType, audio.Value.FileName, enableRangeProcessing: true);
    }

    private static string NormalizeSurface(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "wiki" : value.Trim().ToLowerInvariant();
        return key is "orkalm" or "source" or "source_notebook" ? "orkalm" : "wiki";
    }
}

public class AudioOverviewRequest
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string? Surface { get; set; }
    public Guid? WikiPageId { get; set; }
    public Guid? SourceId { get; set; }
    public string? AudioMode { get; set; }
    public string? TtsQuality { get; set; }
}

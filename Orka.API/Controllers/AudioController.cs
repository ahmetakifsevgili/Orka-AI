using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
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
        var job = await _audio.CreateOverviewAsync(
            GetUserId(),
            request.TopicId,
            request.SessionId,
            HttpContext.RequestAborted);

        return Ok(job);
    }

    [HttpGet("overview/{jobId:guid}/stream")]
    public async Task<IActionResult> Stream(Guid jobId)
    {
        var audio = await _audio.GetAudioAsync(GetUserId(), jobId, HttpContext.RequestAborted);
        if (audio == null) return NotFound(new { message = "Ses dosyası hazır değil." });

        return File(audio.Value.Bytes, audio.Value.ContentType, audio.Value.FileName, enableRangeProcessing: true);
    }
}

public class AudioOverviewRequest
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
}

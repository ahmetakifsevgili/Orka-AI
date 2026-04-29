using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/classroom")]
public class ClassroomController : ControllerBase
{
    private readonly IClassroomService _classroom;

    public ClassroomController(IClassroomService classroom)
    {
        _classroom = classroom;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("session")]
    public async Task<IActionResult> Start([FromBody] ClassroomStartRequest request)
    {
        var session = await _classroom.StartSessionAsync(
            GetUserId(),
            request.TopicId,
            request.SessionId,
            request.AudioOverviewJobId,
            request.Transcript ?? string.Empty,
            HttpContext.RequestAborted);

        return Ok(session);
    }

    [HttpPost("{id:guid}/ask")]
    public async Task<IActionResult> Ask(Guid id, [FromBody] ClassroomAskRequest request)
    {
        var result = await _classroom.AskAsync(
            GetUserId(),
            id,
            request.Question,
            request.ActiveSegment,
            HttpContext.RequestAborted);

        return Ok(result);
    }

    [HttpGet("interaction/{interactionId:guid}/audio")]
    public async Task<IActionResult> GetInteractionAudio(Guid interactionId)
    {
        var audio = await _classroom.GetInteractionAudioAsync(GetUserId(), interactionId, HttpContext.RequestAborted);
        if (audio == null) return NotFound("Ses dosyası henüz hazır değil veya bulunamadı.");

        return File(audio.Value.Bytes, audio.Value.ContentType, $"interaction-{interactionId}.mp3");
    }
}

public class ClassroomStartRequest
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? AudioOverviewJobId { get; set; }
    public string? Transcript { get; set; }
}

public class ClassroomAskRequest
{
    public string Question { get; set; } = string.Empty;
    public string? ActiveSegment { get; set; }
}

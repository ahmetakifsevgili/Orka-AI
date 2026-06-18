using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/classroom")]
public class ClassroomController : ControllerBase
{
    private readonly IClassroomService _classroom;
    private readonly IOrkaStudyRoomService _studyRoom;

    public ClassroomController(IClassroomService classroom, IOrkaStudyRoomService studyRoom)
    {
        _classroom = classroom;
        _studyRoom = studyRoom;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("study-room")]
    public async Task<ActionResult<OrkaStudyRoomDto>> GetStudyRoom(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        [FromQuery] string? examCode = "KPSS",
        [FromQuery] string? variantCode = null,
        [FromQuery] Guid? sourceId = null,
        [FromQuery] Guid? wikiPageId = null,
        [FromQuery] string? mode = null)
    {
        var result = await _studyRoom.BuildStudyRoomAsync(
            GetUserId(),
            topicId,
            sessionId,
            examCode,
            variantCode,
            sourceId,
            wikiPageId,
            mode,
            HttpContext.RequestAborted);

        return result == null
            ? NotFound(new { message = "Study Room durumu bulunamadi." })
            : Ok(result);
    }

    [HttpPost("study-room/start")]
    public async Task<ActionResult<OrkaStudyRoomDto>> StartStudyRoom([FromBody] OrkaStudyRoomStartRequestDto request)
    {
        var result = await _studyRoom.StartStudyRoomAsync(GetUserId(), request, HttpContext.RequestAborted);
        return result == null
            ? NotFound(new { message = "Study Room baslatilamadi." })
            : Ok(result);
    }

    [HttpPost("study-room/checkpoint")]
    public async Task<ActionResult<OrkaStudyRoomDto>> SubmitStudyRoomCheckpoint([FromBody] OrkaStudyRoomCheckpointRequestDto request)
    {
        var result = await _studyRoom.SubmitCheckpointAsync(GetUserId(), request, HttpContext.RequestAborted);
        return result == null
            ? NotFound(new { message = "Study Room checkpoint bulunamadi." })
            : Ok(result);
    }

    [HttpPost("session")]
    public async Task<IActionResult> Start([FromBody] ClassroomStartRequest request)
    {
        var surface = NormalizeSurface(request.Surface);
        if (surface == "wiki" && request.SourceId.HasValue)
        {
            return BadRequest(new { message = "Wiki classroom sourceId ile baslatilamaz; kaynak canli sinifi OrkaLM yuzeyinde calisir." });
        }

        if (surface == "orkalm" && request.WikiPageId.HasValue)
        {
            return BadRequest(new { message = "OrkaLM classroom wikiPageId ile baslatilamaz; Wiki ders sinifi Wiki yuzeyinde calisir." });
        }

        ClassroomSessionDto session;
        try
        {
            session = await _classroom.StartSessionAsync(
                GetUserId(),
                request.TopicId,
                request.SessionId,
                request.AudioOverviewJobId,
                request.Transcript ?? string.Empty,
                surface,
                request.WikiPageId,
                request.SourceId,
                request.AudioMode,
                HttpContext.RequestAborted);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

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

    private static string NormalizeSurface(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "wiki" : value.Trim().ToLowerInvariant();
        return key is "orkalm" or "source" or "source_notebook" ? "orkalm" : "wiki";
    }
}

public class ClassroomStartRequest
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? AudioOverviewJobId { get; set; }
    public string? Transcript { get; set; }
    public string? Surface { get; set; }
    public Guid? WikiPageId { get; set; }
    public Guid? SourceId { get; set; }
    public string? AudioMode { get; set; }
}

public class ClassroomAskRequest
{
    public string Question { get; set; } = string.Empty;
    public string? ActiveSegment { get; set; }
}

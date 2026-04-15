using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/wiki")]
public class WikiController : ControllerBase
{
    private readonly IWikiService _wikiService;

    public WikiController(IWikiService wikiService)
    {
        _wikiService = wikiService;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{topicId}")]
    public async Task<IActionResult> GetTopicWiki(Guid topicId)
    {
        var userId = GetUserId();
        var pages = await _wikiService.GetTopicWikiPagesAsync(topicId, userId);
        return Ok(pages.Select(p => new
        {
            id = p.Id,
            title = p.Title,
            status = p.Status,
            orderIndex = p.OrderIndex,
            blockCount = p.Blocks?.Count ?? 0
        }));
    }

    [HttpGet("page/{pageId}")]
    public async Task<IActionResult> GetWikiPage(Guid pageId)
    {
        var userId = GetUserId();
        var page = await _wikiService.GetWikiPageAsync(pageId, userId);
        if (page == null) return NotFound(new { message = "Sayfa bulunamadı." });

        return Ok(new
        {
            page = new { page.Id, page.Title, page.Status, page.OrderIndex, page.CreatedAt, page.UpdatedAt },
            blocks = page.Blocks?.OrderBy(b => b.OrderIndex).Select(b => new
            {
                b.Id, type = b.BlockType, b.Title, b.Content, b.Source, b.OrderIndex, b.CreatedAt
            }),
            sources = page.Sources?.Select(s => new
            {
                s.Id, s.Type, s.Title, s.Url, s.IsWatched
            })
        });
    }

    [HttpPost("page/{pageId}/note")]
    public async Task<IActionResult> AddNote(Guid pageId, [FromBody] AddNoteRequest request)
    {
        var userId = GetUserId();
        var block = await _wikiService.AddUserNoteAsync(pageId, userId, request.Content);
        return Ok(new { blockId = block.Id, message = "Not eklendi." });
    }

    [HttpPut("block/{blockId}")]
    public async Task<IActionResult> UpdateBlock(Guid blockId, [FromBody] UpdateBlockRequest request)
    {
        var userId = GetUserId();
        await _wikiService.UpdateWikiBlockAsync(blockId, userId, request.Title, request.Content);
        return Ok();
    }

    [HttpDelete("block/{blockId}")]
    public async Task<IActionResult> DeleteBlock(Guid blockId)
    {
        var userId = GetUserId();
        try
        {
            await _wikiService.DeleteWikiBlockAsync(blockId, userId);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Konunun tüm Wiki içeriğini tek bir Markdown string olarak döner (export/print için).
    /// </summary>
    [HttpGet("{topicId}/export")]
    public async Task<IActionResult> ExportWiki(Guid topicId)
    {
        var userId = GetUserId();
        try
        {
            var content = await _wikiService.GetWikiFullContentAsync(topicId, userId);
            if (string.IsNullOrWhiteSpace(content))
                return NotFound(new { message = "Bu konu için henüz wiki içeriği oluşturulmamış." });

            return Ok(new
            {
                topicId,
                exportedAt = DateTime.UtcNow,
                format     = "markdown",
                length     = content.Length,
                content
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Wiki içeriğinden soru cevaplama (mevcut ajan).
    /// </summary>
    [HttpPost("{topicId}/chat")]
    public async Task AskWikiQuestion(Guid topicId, [FromBody] WikiChatRequest request, [FromServices] IWikiAgent wikiAgent)
    {
        var userId = GetUserId();
        var wikiContent = await _wikiService.GetWikiFullContentAsync(topicId, userId);

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        await foreach (var chunk in wikiAgent.AskQuestionStreamAsync(wikiContent, request.Question))
        {
            var data = System.Text.Json.JsonSerializer.Serialize(new { content = chunk });
            await Response.WriteAsync($"data: {data}\n\n");
            await Response.Body.FlushAsync();
        }
    }

    /// <summary>
    /// Korteks ile derin araştırma — Wiki Copilot.
    /// Wiki belgesi yetersizse, Korteks internetten araştırma yapar.
    /// Frontend'e SSE stream olarak adım adım bilgi akar.
    /// </summary>
    [HttpPost("{topicId}/research")]
    public async Task KorteksResearch(Guid topicId, [FromBody] WikiChatRequest request, [FromServices] IKorteksAgent korteks)
    {
        var userId = GetUserId();

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var ct = HttpContext.RequestAborted;

        await foreach (var chunk in korteks.RunResearchAsync(request.Question, userId, topicId, null, ct))
        {
            var data = System.Text.Json.JsonSerializer.Serialize(new { content = chunk });
            await Response.WriteAsync($"data: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        await Response.WriteAsync("data: [DONE]\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}

public class WikiChatRequest
{
    public string Question { get; set; } = string.Empty;
}

public class AddNoteRequest
{
    public string Content { get; set; } = string.Empty;
}

public class UpdateBlockRequest
{
    public string? Title { get; set; }
    public string? Content { get; set; }
}


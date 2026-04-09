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
            pageId = p.Id,
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
                b.Id, b.BlockType, b.Title, b.Content, b.Source, b.OrderIndex, b.CreatedAt
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

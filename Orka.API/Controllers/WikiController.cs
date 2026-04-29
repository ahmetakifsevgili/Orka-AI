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
        if (page == null) return NotFound(new { message = "Sayfa bulunamadÄ±." });

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
        catch (Exception)
        {
            return BadRequest(new { message = "Istek islenemedi." });
        }
    }

    /// <summary>
    /// Konunun tÃ¼m Wiki iÃ§eriÄŸini tek bir Markdown string olarak dÃ¶ner (export/print iÃ§in).
    /// </summary>
    [HttpGet("{topicId}/export")]
    public async Task<IActionResult> ExportWiki(Guid topicId)
    {
        var userId = GetUserId();
        try
        {
            var content = await _wikiService.GetWikiFullContentAsync(topicId, userId);
            if (string.IsNullOrWhiteSpace(content))
                return NotFound(new { message = "Bu konu iÃ§in henÃ¼z wiki iÃ§eriÄŸi oluÅŸturulmamÄ±ÅŸ." });

            return Ok(new
            {
                topicId,
                exportedAt = DateTime.UtcNow,
                format     = "markdown",
                length     = content.Length,
                content
            });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Islem su an tamamlanamadi. Lutfen tekrar deneyin." });
        }
    }

    /// <summary>
    /// NotebookLM-tarzÄ± "Briefing Document" â€” okumadan Ã¶nce hÄ±zlÄ± bakÄ±ÅŸ.
    /// Wiki + Korteks raporundan TL;DR + 5 anahtar Ã§Ä±karÄ±m + 3 Ã¶neri soru.
    /// 1 saatlik in-memory cache.
    /// </summary>
    [HttpGet("{topicId}/briefing")]
    public async Task<IActionResult> GetBriefing(Guid topicId, [FromServices] ISummarizerAgent summarizer)
    {
        var userId = GetUserId();
        try
        {
            var briefing = await summarizer.GenerateBriefingAsync(topicId, userId, HttpContext.RequestAborted);
            return Ok(new
            {
                topicId,
                topicTitle         = briefing.TopicTitle,
                tldr               = briefing.TLDR,
                keyTakeaways       = briefing.KeyTakeaways,
                suggestedQuestions = briefing.SuggestedQuestions,
                generatedAt        = briefing.GeneratedAt
            });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Islem su an tamamlanamadi. Lutfen tekrar deneyin." });
        }
    }

    [HttpGet("{topicId}/glossary")]
    public async Task<IActionResult> GetGlossary(Guid topicId, [FromServices] ISummarizerAgent summarizer)
    {
        var userId = GetUserId();
        try
        {
            var items = await summarizer.GenerateGlossaryAsync(topicId, userId, HttpContext.RequestAborted);
            return Ok(new { topicId, items, generatedAt = DateTime.UtcNow });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Islem su an tamamlanamadi. Lutfen tekrar deneyin." });
        }
    }

    [HttpGet("{topicId}/timeline")]
    public async Task<IActionResult> GetTimeline(Guid topicId, [FromServices] ISummarizerAgent summarizer)
    {
        var userId = GetUserId();
        try
        {
            var items = await summarizer.GenerateTimelineAsync(topicId, userId, HttpContext.RequestAborted);
            return Ok(new { topicId, items, generatedAt = DateTime.UtcNow });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Islem su an tamamlanamadi. Lutfen tekrar deneyin." });
        }
    }

    [HttpGet("{topicId}/mindmap")]
    public async Task<IActionResult> GetMindMap(Guid topicId, [FromServices] ISummarizerAgent summarizer)
    {
        var userId = GetUserId();
        try
        {
            var map = await summarizer.GenerateMindMapAsync(topicId, userId, HttpContext.RequestAborted);
            return Ok(new { topicId, mermaid = map.Mermaid, nodes = map.Nodes, generatedAt = DateTime.UtcNow });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Islem su an tamamlanamadi. Lutfen tekrar deneyin." });
        }
    }

    [HttpGet("{topicId}/study-cards")]
    public async Task<IActionResult> GetStudyCards(Guid topicId, [FromServices] ISummarizerAgent summarizer)
    {
        var userId = GetUserId();
        try
        {
            var cards = await summarizer.GenerateStudyCardsAsync(topicId, userId, HttpContext.RequestAborted);
            return Ok(new { topicId, cards, generatedAt = DateTime.UtcNow });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Islem su an tamamlanamadi. Lutfen tekrar deneyin." });
        }
    }

    [HttpGet("{topicId}/recommendations")]
    public async Task<IActionResult> GetRecommendations(Guid topicId, [FromServices] ILearningSignalService signals)
    {
        var userId = GetUserId();
        try
        {
            var items = await signals.GetRecommendationsAsync(userId, topicId, HttpContext.RequestAborted);
            return Ok(new { topicId, items, generatedAt = DateTime.UtcNow });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Islem su an tamamlanamadi. Lutfen tekrar deneyin." });
        }
    }

    /// <summary>
    /// Wiki iÃ§eriÄŸinden soru cevaplama (mevcut ajan).
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
    /// Korteks ile derin araÅŸtÄ±rma â€” Wiki Copilot.
    /// Wiki belgesi yetersizse, Korteks internetten araÅŸtÄ±rma yapar.
    /// Frontend'e SSE stream olarak adÄ±m adÄ±m bilgi akar.
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

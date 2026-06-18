using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/wiki")]
public class WikiController : ControllerBase
{
    private readonly IWikiService _wikiService;
    private readonly IWikiLearningAssistant _wikiLearningAssistant;
    private readonly IWikiEvidenceService _wikiEvidenceService;
    private readonly ISourceEvidenceLifecycleService _sourceLifecycle;
    private readonly ISourceConceptLinkingService _sourceConceptLinks;
    private readonly IWikiAutoCurationService _wikiCuration;
    private readonly IWikiCopilotService _wikiCopilot;
    private readonly IQuestionPracticeService _questionPractice;
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    public WikiController(
        IWikiService wikiService,
        IWikiLearningAssistant wikiLearningAssistant,
        IWikiEvidenceService wikiEvidenceService,
        ISourceEvidenceLifecycleService sourceLifecycle,
        ISourceConceptLinkingService sourceConceptLinks,
        IWikiAutoCurationService wikiCuration,
        IWikiCopilotService wikiCopilot,
        IQuestionPracticeService questionPractice)
    {
        _wikiService = wikiService;
        _wikiLearningAssistant = wikiLearningAssistant;
        _wikiEvidenceService = wikiEvidenceService;
        _sourceLifecycle = sourceLifecycle;
        _sourceConceptLinks = sourceConceptLinks;
        _wikiCuration = wikiCuration;
        _wikiCopilot = wikiCopilot;
        _questionPractice = questionPractice;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{topicId}")]
    public async Task<IActionResult> GetTopicWiki(Guid topicId)
    {
        var userId = GetUserId();
        var pages = await _wikiService.GetTopicWikiPagesAsync(topicId, userId);
        var pageDtos = new List<object>();
        foreach (var p in pages)
        {
            var visibleBlocks = p.Blocks?.Where(b => !b.IsDeleted).ToArray() ?? [];
            var requiredBlockTypesPresent = HasRequiredWikiBlockType(visibleBlocks);
            var hasLearningContent = !string.IsNullOrWhiteSpace(p.SafeSummary) && visibleBlocks.Length > 0;
            pageDtos.Add(new
            {
                id = p.Id,
                title = p.Title,
                status = p.Status,
                pageKey = string.IsNullOrWhiteSpace(p.PageKey) ? p.Id.ToString("N") : p.PageKey,
                pageType = string.IsNullOrWhiteSpace(p.PageType) ? "concept" : p.PageType,
                planStepId = p.PlanStepId,
                conceptKey = p.ConceptKey,
                parentConceptKey = p.ParentConceptKey,
                parentWikiPageId = p.ParentWikiPageId,
                sourceReadiness = p.SourceReadiness,
                evidenceStatus = p.EvidenceStatus,
                safeSummary = p.SafeSummary,
                curation = await _wikiCuration.BuildPageSummaryAsync(userId, p.Id, HttpContext.RequestAborted),
                orderIndex = p.OrderIndex,
                blockCount = visibleBlocks.Length,
                visibleBlockCount = visibleBlocks.Length,
                requiredBlockTypesPresent,
                hasLearningContent,
                contentReadiness = hasLearningContent && requiredBlockTypesPresent ? "ready" : visibleBlocks.Length == 0 ? "skeleton" : "degraded",
                learningSystemBinding = WikiLearningSystemBindingFactory.From(p, visibleBlocks)
            });
        }
        return Ok(pageDtos);
    }

    [HttpGet("page/{pageId}")]
    public async Task<IActionResult> GetWikiPage(Guid pageId)
    {
        var userId = GetUserId();
        var page = await _wikiService.GetWikiPageAsync(pageId, userId);
        var curation = await _wikiCuration.BuildPageSummaryAsync(userId, pageId, HttpContext.RequestAborted);
        if (page == null) return NotFound(new { message = "Sayfa bulunamadı." });

        var visibleBlocks = page.Blocks?.Where(b => !b.IsDeleted).OrderBy(b => b.OrderIndex).ToArray() ?? [];
        var requiredBlockTypesPresent = HasRequiredWikiBlockType(visibleBlocks);
        var hasLearningContent = !string.IsNullOrWhiteSpace(page.SafeSummary) && visibleBlocks.Length > 0;

        return Ok(new
        {
            page = new
            {
                page.Id,
                page.Title,
                page.Status,
                page.PageKey,
                page.PageType,
                page.PlanStepId,
                page.ConceptKey,
                page.ParentConceptKey,
                page.ParentWikiPageId,
                page.SourceReadiness,
                page.EvidenceStatus,
                page.SafeSummary,
                contentReadiness = hasLearningContent && requiredBlockTypesPresent ? "ready" : visibleBlocks.Length == 0 ? "skeleton" : "degraded",
                hasLearningContent,
                visibleBlockCount = visibleBlocks.Length,
                requiredBlockTypesPresent,
                Curation = curation,
                learningSystemBinding = WikiLearningSystemBindingFactory.From(page, visibleBlocks),
                page.OrderIndex,
                page.CreatedAt,
                page.UpdatedAt
            },
            blocks = visibleBlocks.Select(b => new
            {
                b.Id,
                type = b.BlockType,
                b.Title,
                b.Content,
                b.Source,
                b.SourceBasis,
                b.ConceptKey,
                b.MisconceptionKey,
                b.QuizAttemptId,
                b.SourceEvidenceBundleId,
                b.LearningArtifactId,
                b.TutorTurnStateId,
                b.Visibility,
                b.OrderIndex,
                b.CreatedAt
            }),
            sources = page.Sources?.Select(s => new
            {
                s.Id, s.Type, s.Title, s.Url, s.IsWatched
            })
        });
    }

    private static bool HasRequiredWikiBlockType(IReadOnlyCollection<WikiBlock> blocks) =>
        blocks.Any(b => b.BlockType is WikiBlockType.Summary or WikiBlockType.Concept or WikiBlockType.SourceExcerptSummary or WikiBlockType.TutorExplanation or WikiBlockType.RepairNote or WikiBlockType.MisconceptionNote);

    private static List<string> PageConceptKeys(WikiPage page) =>
        string.IsNullOrWhiteSpace(page.ConceptKey) ? [] : [page.ConceptKey.Trim()];

    private static WikiPageQuestionSetDto ToWikiPageQuestionSet(WikiPage page, QuestionPracticeSessionDto session) => new()
    {
        PageId = page.Id,
        TopicId = page.TopicId,
        ConceptKey = page.ConceptKey,
        PracticeSetId = session.PracticeSetId,
        Status = session.Status,
        EmptyState = session.EmptyState,
        Mode = session.Mode,
        TotalQuestions = session.TotalQuestions,
        Questions = session.Questions
    };

    [HttpGet("page/{pageId}/curation")]
    public async Task<IActionResult> GetWikiPageCuration(Guid pageId)
    {
        var userId = GetUserId();
        var curation = await _wikiCuration.BuildPageSummaryAsync(userId, pageId, HttpContext.RequestAborted);
        return curation == null ? NotFound(new { message = "Sayfa bulunamadi." }) : Ok(curation);
    }

    [HttpGet("page/{pageId}/copilot")]
    public async Task<IActionResult> GetWikiPageCopilot(Guid pageId)
    {
        var userId = GetUserId();
        var context = await _wikiCopilot.BuildPageContextAsync(userId, pageId, HttpContext.RequestAborted);
        return context == null ? NotFound(new { message = "Sayfa bulunamadi." }) : Ok(context);
    }

    [HttpGet("page/{pageId}/questions")]
    public async Task<ActionResult<WikiPageQuestionSetDto>> GetWikiPageQuestions(
        Guid pageId,
        [FromQuery] int count = 8,
        [FromQuery] string? questionBankSource = null,
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        var page = await _wikiService.GetWikiPageAsync(pageId, userId);
        if (page == null) return NotFound(new { message = "Sayfa bulunamadi." });

        var session = await _questionPractice.StartAsync(userId, new QuestionPracticeStartRequestDto
        {
            TopicId = page.TopicId,
            ConceptKeys = PageConceptKeys(page),
            QuestionBankSource = questionBankSource,
            Mode = "wiki_page_questions",
            Count = count
        }, ct);

        return Ok(ToWikiPageQuestionSet(page, session));
    }

    [HttpPost("page/{pageId}/practice/start")]
    public async Task<ActionResult<QuestionPracticeSessionDto>> StartWikiPagePractice(
        Guid pageId,
        [FromBody] WikiPagePracticeStartRequestDto? request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var page = await _wikiService.GetWikiPageAsync(pageId, userId);
        if (page == null) return NotFound(new { message = "Sayfa bulunamadi." });

        request ??= new WikiPagePracticeStartRequestDto();
        var startRequest = new QuestionPracticeStartRequestDto
        {
            TopicId = page.TopicId,
            SessionId = request.SessionId,
            ConceptKeys = PageConceptKeys(page),
            QuestionBankSource = request.QuestionBankSource,
            Mode = string.IsNullOrWhiteSpace(request.Mode) ? "wiki_page_practice" : request.Mode,
            Count = request.Count <= 0 ? 8 : request.Count
        };

        return Ok(await _questionPractice.StartAsync(userId, startRequest, ct));
    }

    [HttpPost("page/{pageId}/blocks")]
    public async Task<IActionResult> AddBlock(Guid pageId, [FromBody] CreateWikiBlockRequestDto request)
    {
        var userId = GetUserId();
        var block = await _wikiService.AddWikiBlockAsync(pageId, userId, request);
        return block == null
            ? BadRequest(new { message = "Wiki blogu olusturulamadi." })
            : Ok(block);
    }

    [HttpGet("{topicId}/graph")]
    public async Task<IActionResult> GetWikiGraph(Guid topicId)
    {
        var userId = GetUserId();
        var graph = await _wikiService.GetWikiGraphAsync(topicId, userId);
        return Ok(graph);
    }

    [HttpGet("page/{pageId}/graph")]
    public async Task<IActionResult> GetLocalWikiGraph(Guid pageId)
    {
        var userId = GetUserId();
        var graph = await _wikiService.GetLocalWikiGraphAsync(pageId, userId);
        return graph == null ? NotFound(new { message = "Sayfa bulunamadi." }) : Ok(graph);
    }

    [HttpGet("pages/{pageId:guid}/source-links")]
    public async Task<IActionResult> GetWikiPageSourceLinks(Guid pageId)
    {
        var userId = GetUserId();
        var links = await _sourceConceptLinks.GetWikiPageSourceLinksAsync(userId, pageId, HttpContext.RequestAborted);
        return links == null ? NotFound(new { message = "Sayfa kaynak linkleri bulunamadi." }) : Ok(links);
    }

    [HttpPost("links")]
    public async Task<IActionResult> CreateWikiLink([FromBody] CreateWikiLinkRequestDto request)
    {
        var userId = GetUserId();
        var link = await _wikiService.LinkWikiPagesAsync(userId, request);
        return link == null ? BadRequest(new { message = "Wiki link olusturulamadi." }) : Ok(link);
    }

    [HttpPost("{topicId}/sync-graph")]
    public async Task<IActionResult> SyncWikiGraph(Guid topicId, [FromBody] WikiGraphSyncRequestDto? request)
    {
        var userId = GetUserId();
        var result = await _wikiService.SyncWikiGraphAsync(topicId, userId, request ?? new WikiGraphSyncRequestDto());
        return result.SyncStatus == "not_found"
            ? NotFound(new { message = "Konu bulunamadi veya kullanici kapsaminda degil.", result })
            : Ok(result);
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
        await _wikiService.DeleteWikiBlockAsync(blockId, userId);
        return Ok();
    }

    /// <summary>
    /// Konunun tüm Wiki içeriğini tek bir Markdown string olarak döner (export/print için).
    /// </summary>
    [HttpGet("{topicId}/export")]
    public async Task<IActionResult> ExportWiki(Guid topicId)
    {
        var userId = GetUserId();
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

    /// <summary>
    /// NotebookLM-tarzı "Briefing Document" — okumadan önce hızlı bakış.
    /// Wiki + Korteks raporundan TL;DR + 5 anahtar çıkarım + 3 öneri soru.
    /// 1 saatlik in-memory cache.
    /// </summary>
    [HttpGet("{topicId}/briefing")]
    public async Task<IActionResult> GetBriefing(Guid topicId, [FromServices] ISummarizerAgent summarizer)
    {
        var userId = GetUserId();
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

    [HttpGet("{topicId}/glossary")]
    public async Task<IActionResult> GetGlossary(Guid topicId, [FromServices] ISummarizerAgent summarizer)
    {
        var userId = GetUserId();
        var items = await summarizer.GenerateGlossaryAsync(topicId, userId, HttpContext.RequestAborted);
        return Ok(new { topicId, items, generatedAt = DateTime.UtcNow });
    }

    [HttpGet("{topicId}/timeline")]
    public async Task<IActionResult> GetTimeline(Guid topicId, [FromServices] ISummarizerAgent summarizer)
    {
        var userId = GetUserId();
        var items = await summarizer.GenerateTimelineAsync(topicId, userId, HttpContext.RequestAborted);
        return Ok(new { topicId, items, generatedAt = DateTime.UtcNow });
    }

    [HttpGet("{topicId}/mindmap")]
    public async Task<IActionResult> GetMindMap(Guid topicId, [FromServices] ISummarizerAgent summarizer)
    {
        var userId = GetUserId();
        var map = await summarizer.GenerateMindMapAsync(topicId, userId, HttpContext.RequestAborted);
        return Ok(new { topicId, mermaid = map.Mermaid, nodes = map.Nodes, generatedAt = DateTime.UtcNow });
    }

    [HttpGet("{topicId}/study-cards")]
    public async Task<IActionResult> GetStudyCards(Guid topicId, [FromServices] ISummarizerAgent summarizer)
    {
        var userId = GetUserId();
        var cards = await summarizer.GenerateStudyCardsAsync(topicId, userId, HttpContext.RequestAborted);
        return Ok(new { topicId, cards, generatedAt = DateTime.UtcNow });
    }

    [HttpGet("{topicId}/recommendations")]
    public async Task<IActionResult> GetRecommendations(Guid topicId, [FromServices] ILearningSignalService signals)
    {
        var userId = GetUserId();
        var items = await signals.GetRecommendationsAsync(userId, topicId, HttpContext.RequestAborted);
        return Ok(new { topicId, items, generatedAt = DateTime.UtcNow });
    }

    /// <summary>
    /// Wiki içeriğinden soru cevaplama (mevcut ajan).
    /// </summary>
    [HttpPost("{topicId}/chat")]
    public async Task AskWikiQuestion(Guid topicId, [FromBody] WikiChatRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Question))
        {
            Response.StatusCode = 400;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new { message = "Soru boş olamaz." });
            return;
        }

        var userId = GetUserId();
        var ct = HttpContext.RequestAborted;

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var learningRequest = new WikiLearningRequestDto
        {
            UserId = userId,
            TopicId = topicId,
            Question = request.Question,
            Mode = string.IsNullOrWhiteSpace(request.Mode) ? "wiki" : request.Mode.Trim(),
            SourceId = request.SourceId,
            ActivePageId = request.ActivePageId,
            SessionId = request.SessionId
        };

        await foreach (var evt in _wikiLearningAssistant.StreamAsync(learningRequest, ct))
        {
            var data = JsonSerializer.Serialize(evt, SseJsonOptions);
            await Response.WriteAsync($"data: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        await Response.WriteAsync("data: [DONE]\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    [HttpGet("{topicId}/workspace-state")]
    public async Task<IActionResult> GetWorkspaceState(Guid topicId)
    {
        var userId = GetUserId();
        var state = await _wikiEvidenceService.GetWorkspaceStateAsync(userId, topicId, HttpContext.RequestAborted);
        return Ok(state);
    }

    [HttpGet("{topicId}/knowledge-notebook")]
    public async Task<IActionResult> GetKnowledgeNotebook(Guid topicId)
    {
        var userId = GetUserId();
        var notebook = await _sourceLifecycle.GetLatestWikiKnowledgeNotebookAsync(userId, topicId, HttpContext.RequestAborted)
                       ?? await _sourceLifecycle.BuildWikiKnowledgeNotebookAsync(userId, topicId, HttpContext.RequestAborted);
        return Ok(notebook);
    }

    [HttpPost("{topicId}/knowledge-notebook/refresh")]
    public async Task<IActionResult> RefreshKnowledgeNotebook(Guid topicId)
    {
        var userId = GetUserId();
        var notebook = await _sourceLifecycle.BuildWikiKnowledgeNotebookAsync(userId, topicId, HttpContext.RequestAborted);
        return Ok(notebook);
    }

    /// <summary>
    /// Korteks ile derin araştırma — Wiki Copilot.
    /// Wiki belgesi yetersizse, Korteks internetten araştırma yapar.
    /// Frontend'e SSE stream olarak adım adım bilgi akar.
    /// </summary>
    [HttpPost("{topicId}/research")]
    public async Task KorteksResearch(Guid topicId, [FromBody] WikiChatRequest? request, [FromServices] IKorteksAgent korteks)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Question))
        {
            Response.StatusCode = 400;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new { message = "Araştırma sorusu boş olamaz." });
            return;
        }

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
    public string? Mode { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? ActivePageId { get; set; }
    public Guid? SessionId { get; set; }
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

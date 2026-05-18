using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.API.Services;
using Orka.Core.DTOs;
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
    private readonly ISourceEvidenceLifecycleService _sourceLifecycle;
    private readonly ISourceConceptLinkingService _sourceConceptLinks;
    private readonly ISourceQuestionService _sourceQuestions;
    private readonly ISourceCompareService _sourceCompare;
    private readonly ISourceQuestionThreadService _sourceQuestionThreads;
    private readonly ResourceOwnershipGuard _ownership;
    private readonly UploadContentSafetyGuard _contentSafety;

    public SourcesController(
        ILearningSourceService sources,
        ISourceEvidenceLifecycleService sourceLifecycle,
        ISourceConceptLinkingService sourceConceptLinks,
        ISourceQuestionService sourceQuestions,
        ISourceCompareService sourceCompare,
        ISourceQuestionThreadService sourceQuestionThreads,
        ResourceOwnershipGuard ownership,
        UploadContentSafetyGuard contentSafety)
    {
        _sources = sources;
        _sourceLifecycle = sourceLifecycle;
        _sourceConceptLinks = sourceConceptLinks;
        _sourceQuestions = sourceQuestions;
        _sourceCompare = sourceCompare;
        _sourceQuestionThreads = sourceQuestionThreads;
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

    [HttpGet("topic/{topicId:guid}/notebook")]
    public async Task<IActionResult> GetTopicSourceNotebook(Guid topicId)
    {
        var userId = GetUserId();
        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
            return NotFound(new { message = "Konu bulunamadi." });

        var result = await _sources.GetTopicSourceNotebookAsync(userId, topicId, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Kaynak defteri bulunamadi." }) : Ok(result);
    }

    [HttpGet("{sourceId:guid}/notebook")]
    public async Task<IActionResult> GetSourceNotebook(Guid sourceId)
    {
        var userId = GetUserId();
        if (!await _ownership.SourceBelongsToUserAsync(userId, sourceId, HttpContext.RequestAborted))
            return NotFound(new { message = "Kaynak bulunamadi." });

        var result = await _sources.GetSourceNotebookAsync(userId, sourceId, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Kaynak defteri bulunamadi." }) : Ok(result);
    }

    [HttpGet("{sourceId:guid}/concept-links")]
    public async Task<IActionResult> GetSourceConceptLinks(Guid sourceId)
    {
        var userId = GetUserId();
        if (!await _ownership.SourceBelongsToUserAsync(userId, sourceId, HttpContext.RequestAborted))
            return NotFound(new { message = "Kaynak bulunamadi." });

        var result = await _sourceConceptLinks.GetSourceConceptLinksAsync(userId, sourceId, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Kaynak-kavram linkleri bulunamadi." }) : Ok(result);
    }

    [HttpPost("{sourceId:guid}/concept-links/sync")]
    public async Task<IActionResult> SyncSourceConceptLinks(Guid sourceId)
    {
        var userId = GetUserId();
        if (!await _ownership.SourceBelongsToUserAsync(userId, sourceId, HttpContext.RequestAborted))
            return NotFound(new { message = "Kaynak bulunamadi." });

        var result = await _sourceConceptLinks.SyncSourceConceptLinksAsync(userId, sourceId, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Kaynak-kavram linkleri bulunamadi." }) : Ok(result);
    }

    [HttpGet("topic/{topicId:guid}/concept-graph")]
    public async Task<IActionResult> GetTopicSourceConceptGraph(Guid topicId)
    {
        var userId = GetUserId();
        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
            return NotFound(new { message = "Konu bulunamadi." });

        var result = await _sourceConceptLinks.GetTopicSourceConceptGraphAsync(userId, topicId, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Kaynak-kavram grafigi bulunamadi." }) : Ok(result);
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

    [HttpGet("topic/{topicId:guid}/evidence-bundle")]
    public async Task<IActionResult> GetSourceEvidenceBundle(Guid topicId, [FromQuery] Guid? sessionId)
    {
        var userId = GetUserId();
        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
            return NotFound(new { message = "Konu bulunamadi." });

        var result = await _sourceLifecycle.GetLatestSourceEvidenceBundleAsync(userId, topicId, sessionId, HttpContext.RequestAborted)
                     ?? await _sourceLifecycle.BuildSourceEvidenceBundleAsync(userId, topicId, sessionId, null, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpPost("topic/{topicId:guid}/evidence-bundle/refresh")]
    public async Task<IActionResult> RefreshSourceEvidenceBundle(Guid topicId, [FromBody] SourceEvidenceBundleRequestDto? request)
    {
        var userId = GetUserId();
        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
            return NotFound(new { message = "Konu bulunamadi." });

        var result = await _sourceLifecycle.BuildSourceEvidenceBundleAsync(
            userId,
            topicId,
            request?.SessionId,
            request?.Question,
            HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpGet("topic/{topicId:guid}/lifecycle-summary")]
    public async Task<IActionResult> GetSourceLifecycleSummary(Guid topicId)
    {
        var userId = GetUserId();
        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
            return NotFound(new { message = "Konu bulunamadi." });

        var result = await _sourceLifecycle.GetSourceLifecycleSummaryAsync(userId, topicId, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpPost("{sourceId:guid}/stale")]
    public async Task<IActionResult> MarkSourceStale(Guid sourceId, [FromBody] MarkSourceStaleRequestDto? request)
    {
        var userId = GetUserId();
        if (!await _ownership.SourceBelongsToUserAsync(userId, sourceId, HttpContext.RequestAborted))
            return NotFound(new { message = "Kaynak bulunamadi." });

        var updated = await _sourceLifecycle.MarkSourceStaleAsync(
            userId,
            sourceId,
            request?.Reason ?? "source_stale",
            HttpContext.RequestAborted);
        return updated ? Ok(new { sourceId, status = "stale" }) : NotFound(new { message = "Kaynak bulunamadi." });
    }

    [HttpPost("{sourceId:guid}/invalidate-evidence")]
    public async Task<IActionResult> InvalidateSourceEvidence(Guid sourceId, [FromBody] MarkSourceStaleRequestDto? request)
    {
        var userId = GetUserId();
        if (!await _ownership.SourceBelongsToUserAsync(userId, sourceId, HttpContext.RequestAborted))
            return NotFound(new { message = "Kaynak bulunamadi." });

        var updated = await _sourceLifecycle.InvalidateEvidenceForSourceAsync(
            userId,
            sourceId,
            request?.Reason ?? "source_evidence_invalidated",
            HttpContext.RequestAborted);
        return updated ? Ok(new { sourceId, status = "evidence_invalidated" }) : NotFound(new { message = "Kaynak bulunamadi." });
    }

    [HttpPost("citations/validate")]
    public async Task<IActionResult> ValidateCitations([FromBody] ValidateSourceCitationSetRequestDto? request)
    {
        if (request == null)
            return BadRequest(new { message = "Citation validation istegi zorunlu." });

        var userId = GetUserId();
        if (request.TopicId.HasValue &&
            !await _ownership.TopicBelongsToUserAsync(userId, request.TopicId.Value, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Konu bulunamadi." });
        }

        var result = await _sourceLifecycle.ValidateCitationSetAsync(userId, request, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpPost("compare")]
    public async Task<IActionResult> CompareSources([FromBody] MultiSourceCompareRequestDto? request)
    {
        if (request == null)
            return BadRequest(new { message = "Kaynak karsilastirma istegi zorunlu." });

        var userId = GetUserId();
        if (request.TopicId.HasValue &&
            !await _ownership.TopicBelongsToUserAsync(userId, request.TopicId.Value, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Konu bulunamadi." });
        }

        var result = await _sourceCompare.CompareAsync(userId, request, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Kaynaklar bulunamadi." }) : Ok(result);
    }

    [HttpPost("topic/{topicId:guid}/compare")]
    public async Task<IActionResult> CompareTopicSources(Guid topicId, [FromBody] MultiSourceCompareRequestDto? request)
    {
        if (request == null)
            return BadRequest(new { message = "Kaynak karsilastirma istegi zorunlu." });

        var userId = GetUserId();
        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
            return NotFound(new { message = "Konu bulunamadi." });

        var result = await _sourceCompare.CompareTopicAsync(userId, topicId, request, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Kaynaklar bulunamadi." }) : Ok(result);
    }

    [HttpGet("{sourceId:guid}/citation-review")]
    public async Task<IActionResult> GetSourceCitationReview(Guid sourceId)
    {
        var userId = GetUserId();
        if (!await _ownership.SourceBelongsToUserAsync(userId, sourceId, HttpContext.RequestAborted))
            return NotFound(new { message = "Kaynak bulunamadi." });

        var result = await _sourceCompare.GetSourceCitationReviewAsync(userId, sourceId, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Citation review bulunamadi." }) : Ok(result);
    }

    [HttpGet("topic/{topicId:guid}/citation-review")]
    public async Task<IActionResult> GetTopicCitationReview(Guid topicId)
    {
        var userId = GetUserId();
        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
            return NotFound(new { message = "Konu bulunamadi." });

        var result = await _sourceCompare.GetTopicCitationReviewAsync(userId, topicId, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Citation review bulunamadi." }) : Ok(result);
    }

    [HttpGet("study-summary")]
    public async Task<IActionResult> GetSourceStudySummary([FromQuery] Guid? topicId, [FromQuery] Guid? sourceId, [FromQuery] Guid? wikiPageId)
    {
        var userId = GetUserId();
        if (topicId.HasValue &&
            !await _ownership.TopicBelongsToUserAsync(userId, topicId.Value, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Konu bulunamadi." });
        }

        if (sourceId.HasValue &&
            !await _ownership.SourceBelongsToUserAsync(userId, sourceId.Value, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Kaynak bulunamadi." });
        }

        var result = await _sourceQuestionThreads.GetStudySummaryAsync(userId, topicId, sourceId, wikiPageId, HttpContext.RequestAborted);
        return result.StudyStatus == "not_found" ? NotFound(new { message = "Source study summary bulunamadi." }) : Ok(result);
    }

    [HttpGet("question-threads")]
    public async Task<IActionResult> ListQuestionThreads([FromQuery] Guid? topicId, [FromQuery] Guid? sourceId, [FromQuery] Guid? wikiPageId)
    {
        var userId = GetUserId();
        if (topicId.HasValue &&
            !await _ownership.TopicBelongsToUserAsync(userId, topicId.Value, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Konu bulunamadi." });
        }

        if (sourceId.HasValue &&
            !await _ownership.SourceBelongsToUserAsync(userId, sourceId.Value, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Kaynak bulunamadi." });
        }

        var result = await _sourceQuestionThreads.ListThreadsAsync(userId, topicId, sourceId, wikiPageId, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpGet("question-threads/{threadId:guid}")]
    public async Task<IActionResult> GetQuestionThread(Guid threadId)
    {
        var userId = GetUserId();
        var result = await _sourceQuestionThreads.GetThreadAsync(userId, threadId, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Source Q&A thread bulunamadi." }) : Ok(result);
    }

    [HttpPost("question-threads")]
    public async Task<IActionResult> CreateQuestionThread([FromBody] SourceQuestionThreadRequestDto? request)
    {
        if (request == null)
            return BadRequest(new { message = "Source Q&A thread istegi zorunlu." });

        if (request.InitialQuestion is { Length: > MaxQuestionLength })
            return BadRequest(new { message = "Soru cok uzun." });

        var userId = GetUserId();
        if (request.TopicId.HasValue &&
            !await _ownership.TopicBelongsToUserAsync(userId, request.TopicId.Value, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Konu bulunamadi." });
        }

        if (request.SourceId.HasValue &&
            !await _ownership.SourceBelongsToUserAsync(userId, request.SourceId.Value, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Kaynak bulunamadi." });
        }

        var result = await _sourceQuestionThreads.CreateThreadAsync(userId, request, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Source Q&A thread olusturulamadi." }) : Ok(result);
    }

    [HttpPost("question-threads/{threadId:guid}/ask")]
    public async Task<IActionResult> AskQuestionThread(Guid threadId, [FromBody] SourceQuestionFollowUpRequestDto? request)
    {
        if (request == null)
            return BadRequest(new { message = "Soru zorunlu." });

        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "Soru bos olamaz." });

        if (request.Question.Length > MaxQuestionLength)
            return BadRequest(new { message = "Soru cok uzun." });

        var userId = GetUserId();
        var result = await _sourceQuestionThreads.AskFollowUpAsync(userId, threadId, request, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Source Q&A thread bulunamadi." }) : Ok(result);
    }

    [HttpPatch("question-threads/{threadId:guid}/review")]
    public async Task<IActionResult> ReviewQuestionThread(Guid threadId, [FromBody] SourceQuestionReviewStateDto? request)
    {
        if (request == null)
            return BadRequest(new { message = "Review istegi zorunlu." });

        var userId = GetUserId();
        var result = await _sourceQuestionThreads.UpdateReviewAsync(userId, threadId, request, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Source Q&A thread bulunamadi." }) : Ok(result);
    }

    [HttpPost("question-threads/{threadId:guid}/wiki-trace")]
    public async Task<IActionResult> WriteQuestionThreadWikiTrace(Guid threadId)
    {
        var userId = GetUserId();
        var block = await _sourceQuestionThreads.WriteWikiTraceAsync(userId, threadId, HttpContext.RequestAborted);
        return block == null ? NotFound(new { message = "Wiki trace yazilamadi." }) : Ok(block);
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] SourceQuestionRequestDto? request)
    {
        if (request == null)
            return BadRequest(new { message = "Soru zorunlu." });

        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "Soru bos olamaz." });

        if (request.Question.Length > MaxQuestionLength)
            return BadRequest(new { message = "Soru cok uzun." });

        var userId = GetUserId();
        if (request.TopicId.HasValue &&
            !await _ownership.TopicBelongsToUserAsync(userId, request.TopicId.Value, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Konu bulunamadi." });
        }

        if (request.SourceId.HasValue &&
            !await _ownership.SourceBelongsToUserAsync(userId, request.SourceId.Value, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Kaynak bulunamadi." });
        }

        var result = await _sourceQuestions.AskAsync(userId, request, HttpContext.RequestAborted);
        return result == null ? BadRequest(new { message = "Kaynak veya konu baglami zorunlu." }) : Ok(result);
    }

    [HttpPost("{sourceId:guid}/ask")]
    public async Task<IActionResult> Ask(Guid sourceId, [FromBody] SourceQuestionRequestDto? request)
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

        var result = await _sourceQuestions.AskSourceAsync(userId, sourceId, request, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Kaynak bulunamadi." }) : Ok(result);
    }

    [HttpPost("topic/{topicId:guid}/ask")]
    public async Task<IActionResult> AskTopicSources(Guid topicId, [FromBody] SourceQuestionRequestDto? request)
    {
        if (request == null)
            return BadRequest(new { message = "Soru zorunlu." });

        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "Soru bos olamaz." });

        if (request.Question.Length > MaxQuestionLength)
            return BadRequest(new { message = "Soru cok uzun." });

        var userId = GetUserId();
        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
            return NotFound(new { message = "Konu bulunamadi." });

        var result = await _sourceQuestions.AskTopicSourcesAsync(userId, topicId, request, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Kaynak defteri bulunamadi." }) : Ok(result);
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

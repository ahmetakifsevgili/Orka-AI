using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.API.Services;
using Orka.Core.DTOs.Korteks;
using Orka.Core.Exceptions;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;
using Orka.Infrastructure.Utilities;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/korteks")]
public class KorteksController : ControllerBase
{
    private readonly IKorteksAgent _korteks;
    private readonly IKorteksSynthesisService _synthesis;
    private readonly FileExtractionService _fileExtractor;
    private readonly UploadContentSafetyGuard _contentSafety;
    private readonly IAuthAttemptLimiter _rateLimiter;
    private readonly ILogger<KorteksController> _logger;

    public KorteksController(
        IKorteksAgent korteks,
        IKorteksSynthesisService synthesis,
        FileExtractionService fileExtractor,
        UploadContentSafetyGuard contentSafety,
        IAuthAttemptLimiter rateLimiter,
        ILogger<KorteksController> logger)
    {
        _korteks = korteks;
        _synthesis = synthesis;
        _fileExtractor = fileExtractor;
        _contentSafety = contentSafety;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("research-stream")]
    public async Task Research([FromBody] KorteksResearchRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Topic))
        {
            Response.StatusCode = 400;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new { message = "Araştırma konusu boş olamaz." });
            return;
        }

        var userId = GetUserId();
        _logger.LogInformation("[KorteksController] Arastirma. UserRef={UserRef} TopicRef={TopicRef} UrlRef={UrlRef}",
            LogPrivacyGuard.SafeId(userId, "usr"),
            LogPrivacyGuard.SafeTextRef(request.Topic, "topic"),
            LogPrivacyGuard.SafeTextRef(request.SourceUrl, "url"));

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var ct = HttpContext.RequestAborted;
        var fileContext = BuildUrlContext(request.SourceUrl);

        await StreamResearchAsync(request.Topic, userId, request.TopicId, fileContext, ct);
    }

    [HttpPost("research-file")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task ResearchFile([FromForm] KorteksFileRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Topic))
        {
            Response.StatusCode = 400;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new { message = "Araştırma konusu boş olamaz." });
            return;
        }

        var userId = GetUserId();
        _logger.LogInformation("[KorteksController] Dosyali arastirma. UserRef={UserRef} TopicRef={TopicRef} FileRef={FileRef}",
            LogPrivacyGuard.SafeId(userId, "usr"),
            LogPrivacyGuard.SafeTextRef(request.Topic, "topic"),
            LogPrivacyGuard.SafeTextRef(request.File?.FileName, "file"));

        var ct = HttpContext.RequestAborted;
        string? fileContext;
        try
        {
            await EnforceFileResearchBackpressureAsync(userId, ct);
            fileContext = await ExtractFileContextAsync(request.File, ct);
        }
        catch (ContentSafetyException ex)
        {
            Response.StatusCode = ex.StatusCode;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new { message = ex.PublicMessage }, ct);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        await StreamResearchAsync(request.Topic, userId, request.TopicId, fileContext, ct);
    }

    [HttpPost("research")]
    [HttpPost("research-sync")]
    public async Task<IActionResult> ResearchSync([FromBody] KorteksResearchRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Topic))
            return BadRequest(new { message = "Araştırma konusu boş olamaz." });

        var userId = GetUserId();
        _logger.LogInformation("[KorteksController] Senkron arastirma. UserRef={UserRef} TopicRef={TopicRef}",
            LogPrivacyGuard.SafeId(userId, "usr"),
            LogPrivacyGuard.SafeTextRef(request.Topic, "topic"));

        var fileContext = BuildUrlContext(request.SourceUrl);
        var ct = HttpContext.RequestAborted;

        try
        {
            var researchResult = await _korteks.RunResearchWithEvidenceAsync(
                request.Topic, userId, request.TopicId, fileContext, ct);
            var synthesis = await _synthesis.BuildAndSaveAsync(
                userId,
                researchResult,
                new KorteksResearchSynthesisContextDto
                {
                    TopicId = request.TopicId,
                    Purpose = "korteks_sync"
                },
                ct);
            var text = researchResult.Report;
            var providerWarnings = researchResult.ProviderFailures
                .Concat(researchResult.Warnings)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Ok(new
            {
                success = true,
                topic = request.Topic,
                report = text,
                answer = text,
                length = text.Length,
                hasFile = fileContext != null,
                research = text,
                groundingMode = researchResult.GroundingMode.ToString(),
                sourceCount = researchResult.SourceCount,
                sources = researchResult.Sources,
                providerWarnings,
                providerCalls = researchResult.ProviderCalls,
                isFallback = researchResult.IsFallback,
                legacySources = researchResult.Sources.Select(s => s.Url).ToList(),
                synthesisWorkflowId = synthesis.Id,
                synthesisStatus = synthesis.Status,
                synthesis = synthesis.Synthesis,
                consumerContexts = synthesis.ConsumerContexts,
                evidenceSummary = synthesis.EvidenceSummary,
                safetyIssues = synthesis.SafetyIssues
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("[KorteksController] Senkron arastirma hatasi. UserRef={UserRef} TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeTextRef(request.Topic, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { success = false, error = "Korteks arastirmasi su an tamamlanamadi." });
        }
    }

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { status = "Korteks online", timestamp = DateTime.UtcNow });

    [HttpGet("synthesis/{id:guid}")]
    public async Task<IActionResult> GetSynthesis(Guid id, CancellationToken ct = default)
    {
        var workflow = await _synthesis.GetWorkflowAsync(GetUserId(), id, ct);
        return workflow == null ? NotFound(new { error = "korteks_synthesis_not_found" }) : Ok(workflow);
    }

    [HttpGet("synthesis/latest")]
    public async Task<IActionResult> GetLatestSynthesis(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var workflow = await _synthesis.GetLatestWorkflowAsync(GetUserId(), topicId, sessionId, ct);
        return workflow == null ? NotFound(new { error = "korteks_synthesis_not_found" }) : Ok(workflow);
    }

    private async Task StreamResearchAsync(
        string topic, Guid userId, Guid? topicId, string? fileContext, CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in _korteks.RunResearchAsync(topic, userId, topicId, fileContext, ct))
            {
                await Response.WriteAsync($"data: {chunk}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            await Response.WriteAsync("data: [DONE]\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError("[KorteksController] Arastirma akisi hatasi. UserRef={UserRef} TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeTextRef(topic, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
            try
            {
                await Response.WriteAsync("data: [ERROR]: Korteks arastirmasi su an tamamlanamadi.\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            catch (Exception flushEx)
            {
                _logger.LogDebug("[KorteksController] SSE error flush failed. ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeExceptionType(flushEx));
            }
        }
    }

    private async Task<string?> ExtractFileContextAsync(IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return null;

        _contentSafety.ValidateMetadata(file.FileName, file.ContentType, file.Length);

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        _contentSafety.ValidateBytes(file.FileName, file.ContentType, bytes);
        return _fileExtractor.Extract(file.FileName, bytes);
    }

    private async Task EnforceFileResearchBackpressureAsync(Guid userId, CancellationToken ct)
    {
        var limit = _contentSafety.Options.MaxKorteksFileResearchPerUserPerHour;
        var result = await _rateLimiter.TryConsumeAsync(
            $"korteks:file:{HashPartition(userId.ToString("N"))}",
            limit,
            TimeSpan.FromHours(1),
            ct);

        if (!result.Allowed)
        {
            var message = result.LimiterUnavailable
                ? "Korteks dosya arastirma korumasi gecici olarak kullanilamiyor."
                : "Korteks dosya arastirma limiti asildi.";
            throw ContentSafetyException.TooManyRequests(message);
        }
    }

    private static string HashPartition(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? BuildUrlContext(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        return $"[KAYNAK_URL]: {url}\nBu URL'yi WebSearch-SearchWeb ile cek ve icerigini arastirmana ekle.";
    }
}

public class KorteksResearchRequest
{
    private string _topic = "";

    [JsonPropertyName("topic")]
    public string Topic
    {
        get => _topic;
        set => _topic = value;
    }

    [JsonPropertyName("concept")]
    public string Concept
    {
        get => _topic;
        set => _topic = value;
    }

    [JsonPropertyName("topicId")]
    public Guid? TopicId { get; set; }

    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; set; }
}

public class KorteksFileRequest
{
    public string Topic { get; set; } = "";
    public Guid? TopicId { get; set; }
    public IFormFile? File { get; set; }
}

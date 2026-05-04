using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;
using System.Security.Claims;
using System.Text.Json.Serialization;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/korteks")]
public class KorteksController : ControllerBase
{
    private readonly IKorteksAgent _korteks;
    private readonly FileExtractionService _fileExtractor;
    private readonly ILogger<KorteksController> _logger;

    private const int MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public KorteksController(
        IKorteksAgent korteks,
        FileExtractionService fileExtractor,
        ILogger<KorteksController> logger)
    {
        _korteks = korteks;
        _fileExtractor = fileExtractor;
        _logger = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("research-stream")]
    public async Task Research([FromBody] KorteksResearchRequest request)
    {
        var userId = GetUserId();
        _logger.LogInformation("[KorteksController] Arastirma: {Topic} | URL: {Url}", request.Topic, request.SourceUrl);

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var ct = HttpContext.RequestAborted;
        var fileContext = BuildUrlContext(request.SourceUrl);

        await StreamResearchAsync(request.Topic, userId, request.TopicId, fileContext, ct);
    }

    [HttpPost("research-file")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task ResearchFile([FromForm] KorteksFileRequest request)
    {
        var userId = GetUserId();
        _logger.LogInformation("[KorteksController] Dosyali arastirma: {Topic} | Dosya: {File}",
            request.Topic, request.File?.FileName);

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var ct = HttpContext.RequestAborted;
        var fileContext = await ExtractFileContextAsync(request.File);

        await StreamResearchAsync(request.Topic, userId, request.TopicId, fileContext, ct);
    }

    [HttpPost("research")]
    [HttpPost("research-sync")]
    public async Task<IActionResult> ResearchSync([FromBody] KorteksResearchRequest request)
    {
        var userId = GetUserId();
        _logger.LogInformation("[KorteksController] Senkron arastirma: {Topic}", request.Topic);

        var fileContext = BuildUrlContext(request.SourceUrl);
        var ct = HttpContext.RequestAborted;

        try
        {
            var researchResult = await _korteks.RunResearchWithEvidenceAsync(
                request.Topic, userId, request.TopicId, fileContext, ct);
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
                legacySources = researchResult.Sources.Select(s => s.Url).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KorteksController] Senkron arastirma hatasi.");
            return StatusCode(500, new { success = false, error = "Korteks arastirmasi su an tamamlanamadi." });
        }
    }

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { status = "Korteks online", timestamp = DateTime.UtcNow });

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
            _logger.LogError(ex, "[KorteksController] Arastirma akisi hatasi.");
            try
            {
                await Response.WriteAsync("data: [ERROR]: Korteks arastirmasi su an tamamlanamadi.\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            catch (Exception flushEx)
            {
                _logger.LogDebug(flushEx, "[KorteksController] SSE error flush failed.");
            }
        }
    }

    private async Task<string?> ExtractFileContextAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0) return null;

        if (file.Length > MaxFileSizeBytes)
            return $"[Dosya cok buyuk: {file.Length / 1024 / 1024} MB - maksimum 10 MB]";

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return _fileExtractor.Extract(file.FileName, ms.ToArray());
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

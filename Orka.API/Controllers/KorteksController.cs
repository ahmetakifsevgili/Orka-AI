using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;
using System.Security.Claims;

namespace Orka.API.Controllers;

/// <summary>
/// Korteks — Orka AI'nın otonom derin araştırma motoru.
/// Semantic Kernel + Function Calling ile web'de arama yapar,
/// sonuçları sentezler ve Wiki'ye kaydeder.
///
/// Dosya/URL desteği:
///   POST /api/korteks/research        — JSON body (konu + opsiyonel URL)
///   POST /api/korteks/research-file   — multipart/form-data (konu + PDF/TXT/MD)
///   POST /api/korteks/research-sync   — JSON, tam sonucu döner (test/Swagger)
/// </summary>
[Authorize]
[ApiController]
[Route("api/korteks")]
public class KorteksController : ControllerBase
{
    private readonly IKorteksAgent         _korteks;
    private readonly FileExtractionService _fileExtractor;
    private readonly ILogger<KorteksController> _logger;

    private const int MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public KorteksController(
        IKorteksAgent         korteks,
        FileExtractionService fileExtractor,
        ILogger<KorteksController> logger)
    {
        _korteks       = korteks;
        _fileExtractor = fileExtractor;
        _logger        = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ── SSE stream — JSON body + opsiyonel URL ────────────────────────────────

    /// <summary>
    /// Derin araştırma — SSE stream formatında yanıt döner.
    /// SourceUrl verilirse Tavily o URL'yi de çeker ve konuya ekler.
    /// </summary>
    [HttpPost("research")]
    public async Task Research([FromBody] KorteksResearchRequest request)
    {
        var userId = GetUserId();
        _logger.LogInformation("[KorteksController] Araştırma: {Topic} | URL: {Url}", request.Topic, request.SourceUrl);

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var ct          = HttpContext.RequestAborted;
        var fileContext = BuildUrlContext(request.SourceUrl);

        await StreamResearchAsync(request.Topic, userId, request.TopicId, fileContext, ct);
    }

    // ── SSE stream — multipart/form-data (dosya yükleme) ─────────────────────

    /// <summary>
    /// Dosya yükleme ile araştırma — PDF, TXT veya MD dosyası kabul eder.
    /// Dosya içeriği Korteks'in context'ine enjekte edilir, web aramasıyla zenginleştirilir.
    /// </summary>
    [HttpPost("research-file")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task ResearchFile([FromForm] KorteksFileRequest request)
    {
        var userId = GetUserId();
        _logger.LogInformation("[KorteksController] Dosyalı araştırma: {Topic} | Dosya: {File}",
            request.Topic, request.File?.FileName);

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var ct          = HttpContext.RequestAborted;
        var fileContext = await ExtractFileContextAsync(request.File);

        await StreamResearchAsync(request.Topic, userId, request.TopicId, fileContext, ct);
    }

    // ── Senkron — test/Swagger için ───────────────────────────────────────────

    /// <summary>
    /// Senkron araştırma — tam sonucu JSON olarak döner (test / Swagger için).
    /// </summary>
    [HttpPost("research-sync")]
    public async Task<IActionResult> ResearchSync([FromBody] KorteksResearchRequest request)
    {
        var userId = GetUserId();
        _logger.LogInformation("[KorteksController] Senkron araştırma: {Topic}", request.Topic);

        var fileContext = BuildUrlContext(request.SourceUrl);
        var result      = new System.Text.StringBuilder();
        var ct          = HttpContext.RequestAborted;

        try
        {
            await foreach (var chunk in _korteks.RunResearchAsync(
                request.Topic, userId, request.TopicId, fileContext, ct))
            {
                result.Append(chunk);
            }
            var text = result.ToString();
            return Ok(new
            {
                success     = true,
                topic       = request.Topic,
                length      = text.Length,
                hasFile     = fileContext != null,
                research    = text
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KorteksController] Senkron araştırma hatası.");
            return StatusCode(500, new { success = false, error = "Korteks arastirmasi su an tamamlanamadi." });
        }
    }

    // ── Ping ─────────────────────────────────────────────────────────────────

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { status = "Korteks online", timestamp = DateTime.UtcNow });

    // ── Yardımcılar ──────────────────────────────────────────────────────────

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
            _logger.LogError(ex, "[KorteksController] Araştırma akışı hatası.");
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
            return $"[Dosya çok büyük: {file.Length / 1024 / 1024} MB — maksimum 10 MB]";

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return _fileExtractor.Extract(file.FileName, ms.ToArray());
    }

    private static string? BuildUrlContext(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        // URL validation — sadece http/https kabul et
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        // URL Tavily'ye SearchWeb sorgusu olarak geçirilecek şekilde işaretlenir.
        // KorteksAgent sistem promptu bu metni görünce Tavily'ye URL araması yaptırır.
        return $"[KAYNAK_URL]: {url}\nBu URL'yi WebSearch-SearchWeb ile çek ve içeriğini araştırmana ekle.";
    }
}

// ── Request modelleri ─────────────────────────────────────────────────────────

public class KorteksResearchRequest
{
    public string  Topic     { get; set; } = "";
    public Guid?   TopicId   { get; set; }
    public string? SourceUrl { get; set; }
}

public class KorteksFileRequest
{
    public string    Topic   { get; set; } = "";
    public Guid?     TopicId { get; set; }
    public IFormFile? File   { get; set; }
}

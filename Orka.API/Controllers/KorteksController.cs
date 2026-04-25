using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Markdig;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class KorteksController : ControllerBase
{
    private readonly IKorteksSwarmOrchestrator _swarm;
    private readonly IDocumentExtractorService _extractor;

    public KorteksController(IKorteksSwarmOrchestrator swarm, IDocumentExtractorService extractor)
    {
        _swarm = swarm;
        _extractor = extractor;
    }

    public class StartResearchRequest
    {
        public string Query { get; set; } = string.Empty;
        public Guid? TopicId { get; set; }
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartResearch([FromBody] StartResearchRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest("Arama sorgusu boş olamaz.");

        var jobId = await _swarm.EnqueueResearchJobAsync(userId, request.Query, request.TopicId);

        return Ok(new { JobId = jobId });
    }

    /// <summary>
    /// Dosya yükleyerek araştırma başlat.
    /// requireWebSearch=false → Sadece belge analizi (RAG).
    /// requireWebSearch=true  → Belge + İnternet teyidi (Hybrid).
    /// Desteklenen formatlar: application/pdf, text/plain, text/markdown
    /// </summary>
    [HttpPost("start-file")]
    [RequestSizeLimit(25 * 1024 * 1024)] // 25 MB
    public async Task<IActionResult> StartFileResearch(
        [FromForm] string query,
        [FromForm] Guid? topicId,
        [FromForm] bool requireWebSearch,
        IFormFile file)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Araştırma sorusu boş olamaz.");

        if (file == null || file.Length == 0)
            return BadRequest("Dosya boş.");

        if (!_extractor.IsSupported(file.ContentType))
            return BadRequest($"Desteklenmeyen dosya türü: {file.ContentType}. Kabul edilenler: PDF, TXT, Markdown.");

        // Metin çıkar — in-memory, disk'e yazılmaz
        string documentContext;
        await using (var stream = file.OpenReadStream())
        {
            documentContext = await _extractor.ExtractTextAsync(stream, file.ContentType);
        }

        if (string.IsNullOrWhiteSpace(documentContext))
            return BadRequest("Belgeden metin çıkarılamadı. Metin tabanlı bir PDF veya TXT dosyası olduğundan emin olun.");

        var jobId = await _swarm.EnqueueResearchJobAsync(
            userId,
            query,
            topicId,
            documentContext,
            requireWebSearch);

        return Ok(new { JobId = jobId, Mode = requireWebSearch ? "hybrid" : "rag" });
    }

    [HttpGet("status/{jobId}")]
    public async Task<IActionResult> GetStatus(Guid jobId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var job = await _swarm.GetJobStatusAsync(jobId);
        if (job == null) return NotFound();

        // Security check
        if (job.UserId != userId) return Forbid();

        return Ok(new
        {
            Phase = job.Phase.ToString(),
            Logs = job.Logs,
            Result = job.FinalReport,
            Error = job.ErrorMessage
        });
    }

    [HttpGet("library")]
    public async Task<IActionResult> GetLibrary()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var jobs = await _swarm.GetUserLibraryAsync(userId);

        return Ok(jobs.Select(j => new
        {
            j.Id,
            j.Query,
            j.Phase,
            CompletedAt = j.CompletedAt,
            HasReport = !string.IsNullOrEmpty(j.FinalReport),
            j.TopicId
        }));
    }

    [HttpGet("library/{jobId}/report")]
    public async Task<IActionResult> GetReport(Guid jobId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var job = await _swarm.GetJobStatusAsync(jobId);
        if (job == null) return NotFound();
        if (job.UserId != userId) return Forbid();

        return Ok(new { job.Query, Report = job.FinalReport, job.CompletedAt });
    }

    public class ExportHtmlRequest
    {
        public string Topic { get; set; } = string.Empty;
        public string Markdown { get; set; } = string.Empty;
    }

    [HttpPost("export-html")]
    public IActionResult ExportHtml([FromBody] ExportHtmlRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Markdown))
            return BadRequest("Markdown içerik boş olamaz.");

        var pipeline = new Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var htmlContent = Markdig.Markdown.ToHtml(request.Markdown, pipeline);

        var title = string.IsNullOrWhiteSpace(request.Topic) ? "Korteks Araştırması" : request.Topic;

        var fullHtml = $@"<!DOCTYPE html>
<html lang=""tr"">
<head>
    <meta charset=""UTF-8"">
    <title>{System.Net.WebUtility.HtmlEncode(title)}</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 800px;
            margin: 0 auto;
            padding: 2rem;
        }}
        h1, h2, h3 {{ border-bottom: 1px solid #eaecef; padding-bottom: 0.3em; }}
        pre {{ background-color: #f6f8fa; padding: 16px; overflow: auto; border-radius: 6px; }}
        code {{ background-color: #f6f8fa; padding: 0.2em 0.4em; border-radius: 6px; font-family: monospace; }}
        blockquote {{ padding: 0 1em; color: #6a737d; border-left: 0.25em solid #dfe2e5; margin: 0; }}
        table {{ border-collapse: collapse; width: 100%; margin-bottom: 1rem; }}
        th, td {{ border: 1px solid #dfe2e5; padding: 6px 13px; }}
        tr:nth-child(2n) {{ background-color: #f6f8fa; }}
    </style>
</head>
<body onload=""window.print()"">
    <h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>
    {htmlContent}
</body>
</html>";

        return Content(fullHtml, "text/html", System.Text.Encoding.UTF8);
    }
}

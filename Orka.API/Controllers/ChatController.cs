using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs.Chat;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly IAgentOrchestrator _agentOrchestrator;
    private readonly IGroqService _groqService;
    private readonly IChaosContext _chaos;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IAgentOrchestrator agentOrchestrator,
        IGroqService groqService,
        IChaosContext chaos,
        ILogger<ChatController> logger)
    {
        _agentOrchestrator = agentOrchestrator;
        _groqService = groqService;
        _chaos = chaos;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("test-ai")]
    public async Task<IActionResult> TestAI()
    {
        try
        {
            var groqResponse = await _groqService.GetResponseAsync(
                new List<Core.Entities.Message>(),
                "Sen bir test asistanısın. Sadece 'OK' de.");

            return Ok(new { status = "Complete", results = new[] { new { Service = "Groq", Status = groqResponse.Trim() } } });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { status = "Error", message = ex.Message });
        }
    }

    [HttpPost("message")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ── CHAOS MONKEY header injection ──────────────────────────────────────
        var chaosHeader = Request.Headers["X-Chaos-Fail"].ToString();
        if (!string.IsNullOrWhiteSpace(chaosHeader))
            _chaos.SetFailingProvider(chaosHeader);
        // ───────────────────────────────────────────────────────────────────────

        try
        {
            var response = await _agentOrchestrator.ProcessMessageAsync(
                userId,
                request.Content,
                request.TopicId,
                request.SessionId,
                request.IsPlanMode);

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("stream")]
    public async Task StreamMessage([FromBody] SendMessageRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        
        Guid sid;
        Guid? createdTopicId = null;
        try
        {
            var session = await _agentOrchestrator.GetOrCreateSessionAsync(userId, request.TopicId, request.SessionId, request.Content);
            if (session == null) throw new Exception("Oturum başlatılamadı.");
            
            sid = session.Id;
            createdTopicId = session.TopicId;

            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Orka-SessionId", sid.ToString());
            if (createdTopicId.HasValue) {
                Response.Headers.Append("X-Orka-TopicId", createdTopicId.Value.ToString());
            }

            // CORS için header'ları dışarı aç
            Response.Headers.Append("Access-Control-Expose-Headers", "X-Orka-SessionId, X-Orka-TopicId");

            // Initial heartbeat — forces headers to be flushed to client immediately.
            // Without this, GetResponse() blocks until the first AI chunk (up to 90s if primary model retries).
            await Response.WriteAsync(": connected\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stream initialization failed");
            Response.StatusCode = 500;
            return;
        }

        try
        {
            await foreach (var chunk in _agentOrchestrator.ProcessMessageStreamAsync(
                userId,
                request.Content,
                request.TopicId,
                sid,
                request.IsPlanMode))
            {
                var safeChunk = chunk.Replace("\n", "[NEWLINE]").Replace("\r", "");
                await Response.WriteAsync($"data: {safeChunk}\n\n");
                await Response.Body.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StreamMessage akışı kesildi.");
            try
            {
                var cleanSuffix = ex.Message.Replace("\n", " ").Replace("\r", " ");
                await Response.WriteAsync($"data: [ERROR]: AI Bağlantı Kesintisi: {cleanSuffix}\n\n");
                await Response.Body.FlushAsync();
            }
            catch { /* Response zaten kapatılmış olabilir */ }
        }
    }

    [HttpPost("session/end")]
    public async Task<IActionResult> EndSession([FromBody] EndSessionRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        try
        {
            await _agentOrchestrator.EndSessionAsync(request.SessionId, userId);
            return Ok(new { message = "Oturum kapatıldı." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

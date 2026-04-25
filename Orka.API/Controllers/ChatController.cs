using System;
using System.Security.Claims;
using System.Threading;
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
                request.IsPlanMode,
                request.IsVoiceMode))
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

    // ─── FAZ 1: Barge-In (Metin Modu Kesintisi) ────────────────────────────────

    /// <summary>
    /// Kullanıcı LLM stream'ını keserken bu endpoint'i çağırır (Cancel-then-Send pattern).
    /// Backend devam eden LLM stream'ini anında iptal eder.
    /// Kullanıcının yeni mesajı bağlama eklenerek bir sonraki stream başlatılır.
    /// </summary>
    [HttpPost("interrupt/{sessionId:guid}")]
    public async Task<IActionResult> InterruptStream([FromRoute] Guid sessionId, [FromBody] InterruptRequest request)
    {
        try
        {
            var interrupted = await _agentOrchestrator.InterruptStreamAsync(sessionId, request.UserMessage);
            return Ok(new { interrupted, message = interrupted ? "Akış kesildi." : "Aktif akış bulunamadı." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Interrupt isteği başarısız. SessionId={SessionId}", sessionId);
            return BadRequest(new { message = ex.Message });
        }
    }

    // ─── FAZ 2: AgentGroupChat (Otonom Sınıf Simülasyonu) ─────────────────────

    /// <summary>
    /// Tutor (Hoca) ve Peer (Öğrenci) ajanlarının otonom podcast/sınıf simülasyonunu başlatır.
    /// SSE formatında akış: [TUTOR]: ve [PEER]: etiketleri UI'da ayrı balonlara dönüştürülebilir.
    /// Kullanıcı araya girmek için /api/chat/interrupt/{sessionId} kullanır.
    /// </summary>
    [HttpPost("classroom/start")]
    public async Task StartClassroomSession([FromBody] ClassroomStartRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        Guid sid;
        try
        {
            var session = await _agentOrchestrator.GetOrCreateSessionAsync(
                userId, request.TopicId, request.SessionId, request.Topic);
            if (session == null) throw new Exception("Sınıf oturumu başlatılamadı.");
            sid = session.Id;

            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Orka-SessionId", sid.ToString());
            Response.Headers.Append("Access-Control-Expose-Headers", "X-Orka-SessionId");

            await Response.WriteAsync(": classroom-connected\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Classroom session başlanamadı.");
            Response.StatusCode = 500;
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);

        try
        {
            await foreach (var chunk in _agentOrchestrator.StartClassroomSessionAsync(
                userId, sid, request.Topic, request.IsVoiceMode, cts.Token))
            {
                var safeChunk = chunk.Replace("\n", "[NEWLINE]").Replace("\r", "");
                await Response.WriteAsync($"data: {safeChunk}\n\n");
                await Response.Body.FlushAsync();
            }

            // Sınıf bitti event'i
            await Response.WriteAsync("event: classroom-ended\ndata: {\"reason\":\"completed\"}\n\n");
            await Response.Body.FlushAsync();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Classroom session kullanıcı tarafından kesildi. SessionId={Sid}", sid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Classroom session hatası. SessionId={Sid}", sid);
            try
            {
                await Response.WriteAsync($"event: error\ndata: {{\"message\":\"{ex.Message}\"}}\n\n");
                await Response.Body.FlushAsync();
            }
            catch { }
        }
    }

    // ─── FAZ 3: Çok Modlu (Multimodal) ───────────────────────────────────────

    /// <summary>
    /// Metin + Görsel (URL) içeren çok modlu mesajları işler.
    /// Görseller önce /api/upload/image ile yüklenmeli, dönen URL bu endpoint'e gönderilmelidir.
    /// LOH (Large Object Heap) şişmesini önlemek için Base64 KULLANILMAZ.
    /// </summary>
    [HttpPost("multimodal")]
    public async Task StreamMultimodalMessage([FromBody] MultimodalSendMessageRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // İlk metin öğesini session bulmak için al
        var textContent = request.ContentItems
            .FirstOrDefault(x => x.Type == ContentType.Text)?.Text ?? "Görsel mesaj";

        Guid sid;
        try
        {
            var session = await _agentOrchestrator.GetOrCreateSessionAsync(
                userId, request.TopicId, request.SessionId, textContent);
            if (session == null) throw new Exception("Oturum başlatılamadı.");
            sid = session.Id;

            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Orka-SessionId", sid.ToString());
            Response.Headers.Append("Access-Control-Expose-Headers", "X-Orka-SessionId");

            await Response.WriteAsync(": multimodal-connected\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multimodal stream başlanamadı.");
            Response.StatusCode = 500;
            return;
        }

        try
        {
            await foreach (var chunk in _agentOrchestrator.ProcessMultimodalMessageStreamAsync(
                userId, request.ContentItems, request.TopicId, sid, request.IsPlanMode))
            {
                var safeChunk = chunk.Replace("\n", "[NEWLINE]").Replace("\r", "");
                await Response.WriteAsync($"data: {safeChunk}\n\n");
                await Response.Body.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multimodal stream hatası.");
            try
            {
                await Response.WriteAsync($"data: [ERROR]: {ex.Message}\n\n");
                await Response.Body.FlushAsync();
            }
            catch { }
        }
    }

    // ─── Mevcut (Korunuyor) ────────────────────────────────────────────────────

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

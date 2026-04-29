using System;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Orka.Core.DTOs.Chat;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> DailyLimitLocks = new();

    private readonly IAgentOrchestrator _agentOrchestrator;
    private readonly IGroqService _groqService;
    private readonly IChaosContext _chaos;
    private readonly OrkaDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IAgentOrchestrator agentOrchestrator,
        IGroqService groqService,
        IChaosContext chaos,
        OrkaDbContext dbContext,
        IConfiguration configuration,
        ILogger<ChatController> logger)
    {
        _agentOrchestrator = agentOrchestrator;
        _groqService = groqService;
        _chaos = chaos;
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("test-ai")]
    public async Task<IActionResult> TestAI()
    {
        try
        {
            var groqResponse = await _groqService.GetResponseAsync(
                new List<Core.Entities.Message>(),
                "Sen bir test asistanisin. Sadece 'OK' de.");

            return Ok(new { status = "Complete", results = new[] { new { Service = "Groq", Status = groqResponse.Trim() } } });
        }
        catch (Exception)
        {
            return StatusCode(500, new { status = "Error", message = "AI servis testi tamamlanamadi." });
        }
    }

    [HttpPost("message")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var limit = await TryConsumeDailyMessageAsync(userId);
        if (!limit.Allowed)
            return StatusCode(429, new { message = "Gunluk mesaj limitine ulasildi.", dailyLimit = limit.Limit });

        // CHAOS MONKEY header injection for provider-failover smoke tests.
        var chaosHeader = Request.Headers["X-Chaos-Fail"].ToString();
        if (!string.IsNullOrWhiteSpace(chaosHeader))
            _chaos.SetFailingProvider(chaosHeader);
        // End chaos header injection.

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
        catch (Exception)
        {
            return BadRequest(new { message = "Istek islenemedi." });
        }
    }

    [HttpPost("stream")]
    public async Task StreamMessage([FromBody] SendMessageRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var limit = await TryConsumeDailyMessageAsync(userId);
        if (!limit.Allowed)
        {
            Response.StatusCode = 429;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new { message = "Gunluk mesaj limitine ulasildi.", dailyLimit = limit.Limit });
            return;
        }
        
        Guid sid;
        Guid? createdTopicId = null;
        try
        {
            var session = await _agentOrchestrator.GetOrCreateSessionAsync(userId, request.TopicId, request.SessionId, request.Content);
            if (session == null) throw new Exception("Oturum baslatilamadi.");
            
            sid = session.Id;
            createdTopicId = session.TopicId;

            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Orka-SessionId", sid.ToString());
            if (createdTopicId.HasValue) {
                Response.Headers.Append("X-Orka-TopicId", createdTopicId.Value.ToString());
            }

            // Expose session/topic ids to the frontend client.
            Response.Headers.Append("Access-Control-Expose-Headers", "X-Orka-SessionId, X-Orka-TopicId");

            // Initial heartbeat forces headers to be flushed to client immediately.
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
            _logger.LogError(ex, "StreamMessage akisi kesildi.");
            try
            {
                await Response.WriteAsync("data: [ERROR]: AI baglantisi su an tamamlanamadi.\n\n");
                await Response.Body.FlushAsync();
            }
            catch (Exception flushEx)
            {
                _logger.LogDebug(flushEx, "Stream error suffix could not be flushed.");
            }
        }
    }

    [HttpPost("session/end")]
    public async Task<IActionResult> EndSession([FromBody] EndSessionRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        try
        {
            await _agentOrchestrator.EndSessionAsync(request.SessionId, userId);
            return Ok(new { message = "Oturum kapatildi." });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Istek islenemedi." });
        }
    }

    private async Task<(bool Allowed, int Limit)> TryConsumeDailyMessageAsync(Guid userId)
    {
        var gate = DailyLimitLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return (false, 0);

            var limit = user.Plan == UserPlan.Pro
                ? _configuration.GetValue("Limits:ProUserDailyMessages", 500)
                : _configuration.GetValue("Limits:FreeUserDailyMessages", 50);

            var now = DateTime.UtcNow;
            if (user.DailyMessageResetAt <= now)
            {
                user.DailyMessageCount = 0;
                user.DailyMessageResetAt = now.Date.AddDays(1);
            }

            if (user.DailyMessageCount >= limit)
            {
                return (false, limit);
            }

            user.DailyMessageCount++;
            await _dbContext.SaveChangesAsync();
            return (true, limit);
        }
        finally
        {
            gate.Release();
        }
    }
}

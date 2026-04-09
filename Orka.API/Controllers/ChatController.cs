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
    private readonly IOpenRouterService _openRouterService;
    private readonly IMistralService _mistralService;
    private readonly IChaosContext _chaos;

    public ChatController(
        IAgentOrchestrator agentOrchestrator,
        IGroqService groqService,
        IOpenRouterService openRouterService,
        IMistralService mistralService,
        IChaosContext chaos)
    {
        _agentOrchestrator = agentOrchestrator;
        _groqService = groqService;
        _openRouterService = openRouterService;
        _mistralService = mistralService;
        _chaos = chaos;
    }

    [AllowAnonymous]
    [HttpGet("test-ai")]
    public async Task<IActionResult> TestAI()
    {
        try
        {
            var results = new List<object>();

            // Test Groq
            try
            {
                var groqResponse = await _groqService.GetResponseAsync(new List<Core.Entities.Message>(), "Sen bir test asistanısın. Sadece 'OK' de.");
                results.Add(new { Service = "Groq", Status = groqResponse.Trim() });
            }
            catch (Exception ex)
            {
                results.Add(new { Service = "Groq", Status = $"HATA: {ex.Message}" });
            }

            // Test OpenRouter
            try
            {
                var orResponse = await _openRouterService.ChatCompletionAsync("Sen bir test asistanısın.", "BAĞLANTI TESTİ. Bana sadece 'OK' de.");
                results.Add(new { Service = "OpenRouter", Status = orResponse.Trim() });
            }
            catch (Exception ex)
            {
                results.Add(new { Service = "OpenRouter", Status = $"HATA: {ex.Message}" });
            }

            // Test Mistral
            try
            {
                // MistralService normally implements GetChatCompletionAsync
                var mistralResponse = await _mistralService.GetResponseAsync(new List<Core.Entities.Message>(), "Sen bir test asistanısın. Sadece 'OK' de.");
                results.Add(new { Service = "Mistral", Status = mistralResponse.Trim() });
            }
            catch (Exception ex)
            {
                results.Add(new { Service = "Mistral", Status = $"HATA: {ex.Message}" });
            }

            return Ok(new { status = "Complete", results = results });
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
                request.SessionId);

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
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

using System;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orka.API.Services;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Chat;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Orka.Infrastructure.Utilities;

namespace Orka.API.Controllers;

[Authorize]
[EnableRateLimiting("AiLimiter")]
[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> DailyLimitLocks = new();
    private const int MaxMessageLength = 20_000;
    private const int MaxFocusPathLength = 500;
    private const int MaxFocusSourceRefLength = 500;

    private readonly IAgentOrchestrator _agentOrchestrator;
    private readonly IGroqService _groqService;
    private readonly IChaosContext _chaos;
    private readonly OrkaDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ResourceOwnershipGuard _ownership;
    private readonly IQuizAttemptRecorder _quizAttemptRecorder;
    private readonly ILearningSignalService _learningSignals;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IAgentOrchestrator agentOrchestrator,
        IGroqService groqService,
        IChaosContext chaos,
        OrkaDbContext dbContext,
        IConfiguration configuration,
        IHostEnvironment environment,
        ResourceOwnershipGuard ownership,
        IQuizAttemptRecorder quizAttemptRecorder,
        ILearningSignalService learningSignals,
        ILogger<ChatController> logger)
    {
        _agentOrchestrator = agentOrchestrator;
        _groqService = groqService;
        _chaos = chaos;
        _dbContext = dbContext;
        _configuration = configuration;
        _environment = environment;
        _ownership = ownership;
        _quizAttemptRecorder = quizAttemptRecorder;
        _learningSignals = learningSignals;
        _logger = logger;
    }

    [Authorize]
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
        catch (Exception)
        {
            return StatusCode(500, new { status = "Error", message = "AI servis testi tamamlanamadı." });
        }
    }

    [HttpPost("message")]
    [HttpPost("send")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { message = "Mesaj boş olamaz." });

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var boundaryError = await ValidateMessageBoundaryAsync(userId, request);
        if (boundaryError != null) return boundaryError;

        var limit = await TryConsumeDailyMessageAsync(userId);
        if (!limit.Allowed)
            return StatusCode(429, new { message = "Günlük mesaj limitine ulaşıldı.", dailyLimit = limit.Limit });

        // Development-only provider-failover smoke hook. In Staging/Production the
        // header is deliberately ignored, including for admin users.
        var chaosHeader = Request.Headers["X-Chaos-Fail"].ToString();
        if (_environment.IsDevelopment())
        {
            if (!string.IsNullOrWhiteSpace(chaosHeader))
                _chaos.SetFailingProvider(chaosHeader);
        }

        try
        {
            var response = await _agentOrchestrator.ProcessMessageAsync(
                userId,
                request.Content,
                request.TopicId,
                request.SessionId,
                request.IsPlanMode,
                request.FocusTopicId,
                request.FocusTopicPath,
                request.FocusSourceRef);

            await TryRecordObservedChatQuizEvidenceAsync(userId, response, request.Content, HttpContext.RequestAborted);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat message processing failed for user {UserId}", userId);
            return BadRequest(new { message = "İstek işlenemedi." });
        }
    }

    [HttpPost("stream")]
    public async Task StreamMessage([FromBody] SendMessageRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Content))
        {
            Response.StatusCode = 400;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new { message = "Mesaj boş olamaz." });
            return;
        }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await ValidateStreamBoundaryAsync(userId, request))
            return;

        var limit = await TryConsumeDailyMessageAsync(userId);
        if (!limit.Allowed)
        {
            Response.StatusCode = 429;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new { message = "Günlük mesaj limitine ulaşıldı.", dailyLimit = limit.Limit });
            return;
        }

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

            // Expose session/topic ids to the frontend client.
            Response.Headers.Append("Access-Control-Expose-Headers", "X-Orka-SessionId, X-Orka-TopicId");

            // Initial heartbeat forces headers to be flushed to client immediately.
            // Without this, GetResponse() blocks until the first AI chunk (up to 90s if primary model retries).
            await Response.WriteAsync(": connected\n\n");
            await Response.Body.FlushAsync();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Stream initialization rejected. UserRef={UserRef} TopicRef={TopicRef} SessionRef={SessionRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(request.TopicId, "topic"),
                LogPrivacyGuard.SafeId(request.SessionId, "session"),
                LogPrivacyGuard.SafeExceptionType(ex));
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new { message = "Yanit akisi baslatilamadi. Secili konu veya oturum durumunu kontrol edip tekrar deneyin." });
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError("Stream initialization failed. UserRef={UserRef} TopicRef={TopicRef} SessionRef={SessionRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(request.TopicId, "topic"),
                LogPrivacyGuard.SafeId(request.SessionId, "session"),
                LogPrivacyGuard.SafeExceptionType(ex));
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
                request.FocusTopicId,
                request.FocusTopicPath,
                request.FocusSourceRef,
                HttpContext.RequestAborted))
            {
                if (chunk.StartsWith("{\"type\":\"") || chunk.StartsWith("{\"type\": \""))
                {
                    string typeStr = "metadata";
                    var match = System.Text.RegularExpressions.Regex.Match(chunk, "\"type\"\\s*:\\s*\"([^\"]+)\"");
                    if (match.Success)
                    {
                        typeStr = match.Groups[1].Value;
                    }
                    var safeChunk = chunk.Replace("\n", "[NEWLINE]").Replace("\r", "");
                    await Response.WriteAsync($"event: {typeStr}\ndata: {safeChunk}\n\n");
                }
                else
                {
                    var safeChunk = chunk.Replace("\n", "[NEWLINE]").Replace("\r", "");
                    await Response.WriteAsync($"event: token\ndata: {safeChunk}\n\n");
                }
                await Response.Body.FlushAsync();
            }
            
            await Response.WriteAsync("event: done\ndata: [DONE]\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("StreamMessage akisi kesildi. UserRef={UserRef} TopicRef={TopicRef} SessionRef={SessionRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(request.TopicId, "topic"),
                LogPrivacyGuard.SafeId(sid, "session"),
                LogPrivacyGuard.SafeExceptionType(ex));
            try
            {
                await Response.WriteAsync("data: [ERROR]: AI bağlantısı şu an tamamlanamadı.\n\n");
                await Response.Body.FlushAsync();
            }
            catch (Exception flushEx)
            {
                _logger.LogDebug("Stream error suffix could not be flushed. SessionRef={SessionRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeId(sid, "session"),
                    LogPrivacyGuard.SafeExceptionType(flushEx));
            }
        }
    }

    [HttpPost("session/end")]
    public async Task<IActionResult> EndSession([FromBody] EndSessionRequest? request)
    {
        if (request == null || request.SessionId == Guid.Empty)
            return BadRequest(new { message = "sessionId zorunlu." });

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await _ownership.SessionBelongsToUserAsync(userId, request.SessionId, HttpContext.RequestAborted))
            return NotFound(new { message = "Oturum bulunamadı." });

        try
        {
            await _agentOrchestrator.EndSessionAsync(request.SessionId, userId);
            return Ok(new { message = "Oturum kapatıldı." });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "İstek işlenemedi." });
        }
    }

    private async Task<IActionResult?> ValidateMessageBoundaryAsync(Guid userId, SendMessageRequest request)
    {
        if (request.Content.Length > MaxMessageLength)
            return BadRequest(new { message = "Mesaj cok uzun." });

        if (request.FocusTopicPath is { Length: > MaxFocusPathLength } ||
            request.FocusSourceRef is { Length: > MaxFocusSourceRefLength })
        {
            return BadRequest(new { message = "Odak baglami cok uzun." });
        }

        if (!await _ownership.OptionalTopicBelongsToUserAsync(userId, request.TopicId, HttpContext.RequestAborted) ||
            !await _ownership.OptionalSessionBelongsToUserAsync(userId, request.SessionId, HttpContext.RequestAborted) ||
            !await _ownership.OptionalTopicBelongsToUserAsync(userId, request.FocusTopicId, HttpContext.RequestAborted))
        {
            return NotFound(new { message = "Kaynak bulunamadi." });
        }

        return null;
    }

    private async Task<bool> ValidateStreamBoundaryAsync(Guid userId, SendMessageRequest request)
    {
        var error = await ValidateMessageBoundaryAsync(userId, request);
        if (error == null) return true;

        Response.StatusCode = error switch
        {
            BadRequestObjectResult => StatusCodes.Status400BadRequest,
            NotFoundObjectResult => StatusCodes.Status404NotFound,
            ObjectResult objectResult when objectResult.StatusCode.HasValue => objectResult.StatusCode.Value,
            _ => StatusCodes.Status400BadRequest
        };
        Response.ContentType = "application/json";

        var value = error switch
        {
            ObjectResult objectResult => objectResult.Value,
            _ => new { message = "Istek gecersiz." }
        };

        await Response.WriteAsJsonAsync(value);
        return false;
    }

    private async Task TryRecordObservedChatQuizEvidenceAsync(
        Guid userId,
        ChatMessageResponse response,
        string userContent,
        CancellationToken ct)
    {
        if (!TryParseObservedQuizScore(userContent, out var correct, out var total))
            return;

        var sessionId = response.SessionId == Guid.Empty ? (Guid?)null : response.SessionId;
        var topicId = response.TopicId == Guid.Empty ? (Guid?)null : response.TopicId;
        if (!topicId.HasValue && sessionId.HasValue)
        {
            topicId = await _dbContext.Sessions
                .AsNoTracking()
                .Where(s => s.Id == sessionId.Value && s.UserId == userId)
                .Select(s => s.TopicId)
                .FirstOrDefaultAsync(ct);
        }

        if (!topicId.HasValue)
            return;

        var score = total <= 0 ? 0 : (int)Math.Round((double)correct / total * 100d);
        var isPositive = correct > 0 && correct >= total;
        var skillTag = "chat-quiz-evidence";
        var topicPath = "chat/quiz-evidence";
        var questionText = $"Chat observed quiz result {correct}/{total}";
        var questionHash = RedisMemoryService.ComputeQuestionHash(questionText, skillTag, topicPath, "chat-observed", "observed");

        try
        {
            var alreadyRecorded = await _dbContext.QuizAttempts
                .AsNoTracking()
                .AnyAsync(a =>
                    a.UserId == userId &&
                    a.TopicId == topicId.Value &&
                    a.QuestionHash == questionHash,
                    ct);

            await _quizAttemptRecorder.RecordAsync(userId, new RecordQuizAttemptRequest
            {
                TopicId = topicId,
                SessionId = sessionId,
                QuestionId = "chat-observed-quiz-result",
                Question = questionText,
                SelectedOptionId = isPositive ? "observed_correct" : "observed_incomplete",
                IsCorrect = isPositive,
                SkillTag = skillTag,
                ConceptTag = skillTag,
                LearningObjective = "Chat icindeki quiz sonucunu dashboard coordination kaniti olarak kaydetmek.",
                QuestionType = "observed_chat_quiz_result",
                AssessmentMode = "chat_observed",
                TopicPath = topicPath,
                Difficulty = "observed",
                QuestionHash = questionHash,
                SourceRefsJson = JsonSerializer.Serialize(new
                {
                    origin = "chat_message",
                    correctnessVerified = false,
                    confidence = "observed_only",
                    correct,
                    total
                })
            }, ct);

            if (!alreadyRecorded)
            {
                await _learningSignals.RecordSignalAsync(
                    userId,
                    topicId,
                    sessionId,
                    LearningSignalTypes.QuizAnswered,
                    skillTag,
                    topicPath,
                    score,
                    isPositive,
                    JsonSerializer.Serialize(new
                    {
                        origin = "chat_message",
                        confidence = "observed_only",
                        correctnessVerified = false,
                        correct,
                        total
                    }),
                    ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[Chat] Observed quiz evidence record failed. UserRef={UserRef} TopicRef={TopicRef} SessionRef={SessionRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(topicId, "topic"),
                LogPrivacyGuard.SafeId(sessionId, "session"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    private static bool TryParseObservedQuizScore(string content, out int correct, out int total)
    {
        correct = 0;
        total = 0;
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var normalized = content
            .Replace("*", " ", StringComparison.Ordinal)
            .Replace("_", " ", StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
        if (!normalized.Contains("quiz", StringComparison.OrdinalIgnoreCase) ||
            !(normalized.Contains("cevab", StringComparison.OrdinalIgnoreCase) ||
              normalized.Contains("answer", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var match = Regex.Match(normalized, @"(?<correct>\d{1,3})\s*/\s*(?<total>\d{1,3})");
        if (!match.Success ||
            !int.TryParse(match.Groups["correct"].Value, out correct) ||
            !int.TryParse(match.Groups["total"].Value, out total) ||
            total <= 0 ||
            correct < 0 ||
            correct > total)
        {
            correct = 0;
            total = 0;
            return false;
        }

        return true;
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

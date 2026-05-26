using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Orka.API.Services;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Code;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;
using System.Text.RegularExpressions;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/code")]
[EnableRateLimiting("CodeLimiter")]
public class CodeController : ControllerBase
{
    private readonly IPistonService _piston;
    private readonly IRedisMemoryService _redis;
    private readonly ILearningSignalService _signals;
    private readonly IMistakeClassifierService? _mistakeClassifier;
    private readonly ResourceOwnershipGuard? _ownership;
    private readonly IOrkaCodeLearningIdeService? _codeLearningIde;
    private readonly ILogger<CodeController> _logger;

    public CodeController(
        IPistonService piston,
        IRedisMemoryService redis,
        ILearningSignalService signals,
        ILogger<CodeController> logger,
        ResourceOwnershipGuard? ownership = null,
        IMistakeClassifierService? mistakeClassifier = null,
        IOrkaCodeLearningIdeService? codeLearningIde = null)
    {
        _piston = piston;
        _redis = redis;
        _signals = signals;
        _logger = logger;
        _ownership = ownership;
        _mistakeClassifier = mistakeClassifier;
        _codeLearningIde = codeLearningIde;
    }

    [HttpGet("languages")]
    public async Task<IActionResult> GetLanguages()
    {
        var runtimes = await _piston.GetRuntimesAsync();
        var result = runtimes
            .Select(r => new { language = r.Language, version = r.Version, aliases = r.Aliases })
            .OrderBy(r => r.language)
            .ToList();
        return Ok(result);
    }

    [HttpGet("learning-ide")]
    public async Task<ActionResult<OrkaCodeLearningIdeDto>> GetLearningIde(
        [FromQuery] Guid? topicId = null,
        [FromQuery] Guid? sessionId = null,
        [FromQuery] string? language = null,
        [FromQuery] string? exerciseId = null,
        [FromQuery] string? mode = null)
    {
        if (_codeLearningIde == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Code Learning IDE hazir degil." });
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var ide = await _codeLearningIde.BuildIdeAsync(
            userId,
            topicId,
            sessionId,
            language,
            exerciseId,
            mode,
            HttpContext.RequestAborted);

        return ide == null
            ? NotFound(new { message = "Code Learning IDE durumu bulunamadi." })
            : Ok(ide);
    }

    [HttpPost("run")]
    [HttpPost("execute")]
    public async Task<IActionResult> RunCode([FromBody] CodeRunRequest? request)
    {
        if (request == null)
            return BadRequest(new { error = "Kod calistirma istegi zorunlu." });

        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { error = "Kod boş olamaz." });

        if (request.Code.Length > 50_000)
            return BadRequest(new { error = "Kod 50.000 karakteri gecemez." });

        if ((request.Stdin?.Length ?? 0) > 10_000)
            return BadRequest(new { error = "stdin 10.000 karakteri gecemez." });

        var language = string.IsNullOrWhiteSpace(request.Language)
            ? "csharp"
            : request.Language.Trim().ToLowerInvariant();
        if (!IsSafeLanguage(language))
            return BadRequest(new { error = "Dil degeri gecersiz." });

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        if (_ownership != null &&
            (!await _ownership.OptionalTopicBelongsToUserAsync(userId, request.TopicId, HttpContext.RequestAborted) ||
             !await _ownership.OptionalSessionBelongsToUserAsync(userId, request.SessionId, HttpContext.RequestAborted)))
        {
            return NotFound(new { message = "Kaynak bulunamadi." });
        }

        _logger.LogInformation(
            "Kod calistirma istegi - dil: {Language}, boyut: {Size} karakter, stdin: {HasStdin}",
            language,
            request.Code.Length,
            request.Stdin is not null);

        var result = await _piston.ExecuteAsync(
            request.Code,
            language,
            request.Stdin);
        var publicResult = SanitizePublicResult(result);

        if (request.SessionId.HasValue)
        {
            var redisRuntimeContext = BuildRedisRuntimeContext(request.Code, publicResult);
            await _redis.SetLastPistonResultAsync(
                request.SessionId.Value,
                redisRuntimeContext.Code,
                redisRuntimeContext.Stdout,
                redisRuntimeContext.Stderr,
                language,
                publicResult.Phase,
                publicResult.CompileError,
                publicResult.RuntimeError,
                publicResult.Success,
                publicResult.SafeTutorSummary);

            _logger.LogInformation(
                "Piston sonucu Redis'e yazildi. SessionRef={SessionRef} Dil={Language} Phase={Phase} Success={Success}",
                LogPrivacyGuard.SafeId(request.SessionId.Value, "session"),
                language,
                publicResult.Phase,
                publicResult.Success);
        }

        await _signals.RecordSignalAsync(
            userId,
            request.TopicId,
            request.SessionId,
            ResolveIdeSignalType(publicResult),
            skillTag: language,
            topicPath: request.TopicId.HasValue ? "IDE > Kod calistirma" : null,
            score: publicResult.Success ? 100 : 0,
            isPositive: publicResult.Success,
            payloadJson: JsonSerializer.Serialize(new
            {
                language,
                success = publicResult.Success,
                phase = publicResult.Phase,
                compileError = publicResult.CompileError,
                runtimeError = publicResult.RuntimeError,
                exitCode = publicResult.ExitCode,
                durationMs = publicResult.DurationMs,
                truncated = publicResult.Truncated,
                safeTutorSummary = publicResult.SafeTutorSummary,
                stdoutLength = publicResult.Stdout?.Length ?? 0,
                stderrLength = publicResult.Stderr?.Length ?? 0
            }),
            ct: HttpContext.RequestAborted);

        if (!publicResult.Success && _mistakeClassifier != null)
        {
            await _mistakeClassifier.ClassifyAndRecordAsync(
                userId,
                request.TopicId,
                request.SessionId,
                new Orka.Core.DTOs.MistakeClassificationRequest(
                    Question: "IDE code execution",
                    ExpectedAnswer: "Code compiles and runs successfully",
                    StudentAnswer: request.Code,
                    Explanation: result.SafeTutorSummary,
                    TopicId: request.TopicId,
                    SkillTag: language,
                    ConceptTag: publicResult.Phase,
                    CodePhase: publicResult.Phase,
                    CompileError: publicResult.CompileError,
                    RuntimeError: publicResult.RuntimeError),
                HttpContext.RequestAborted);
        }

        return Ok(new CodeRunResponse(
            publicResult.Stdout ?? string.Empty,
            publicResult.Stderr ?? string.Empty,
            publicResult.Success,
            publicResult.Phase,
            publicResult.CompileError,
            publicResult.RuntimeError,
            publicResult.ExitCode,
            publicResult.DurationMs,
            publicResult.Truncated,
            publicResult.SafeTutorSummary,
            publicResult.Runtime));
    }

    private static string ResolveIdeSignalType(PistonResult result)
    {
        if (result.Success) return LearningSignalTypes.IdeRunCompleted;
        return result.Phase switch
        {
            "compile" => LearningSignalTypes.IdeCompileError,
            "timeout" => LearningSignalTypes.IdeExecutionTimeout,
            "provider_missing" => LearningSignalTypes.IdeProviderUnavailable,
            _ => LearningSignalTypes.IdeRuntimeError
        };
    }

    private sealed record RedisRuntimeContext(string Code, string Stdout, string Stderr);

    private static RedisRuntimeContext BuildRedisRuntimeContext(string originalCode, PistonResult publicResult) => new(
        $"[student_code_redacted length={originalCode.Length}]",
        BuildRedisChannelSummary("stdout", publicResult.Stdout),
        BuildRedisChannelSummary("stderr", publicResult.Stderr));

    private static string BuildRedisChannelSummary(string channel, string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var status = value.Length >= 12_000 ? "truncated" : "present";
        return $"[{channel}_redacted length={value.Length} status={status}]";
    }

    private static bool IsSafeLanguage(string language)
    {
        if (language.Length is < 1 or > 40) return false;
        return language.All(c => char.IsLetterOrDigit(c) || c is '#' or '+' or '-' or '_' or '.');
    }

    private static PistonResult SanitizePublicResult(PistonResult result) => new(
        SanitizePublicRuntimeText(result.Stdout, 12_000) ?? string.Empty,
        SanitizePublicRuntimeText(result.Stderr, 12_000) ?? string.Empty,
        result.Success,
        result.Phase,
        SanitizePublicRuntimeText(result.CompileError, 4_000),
        SanitizePublicRuntimeText(result.RuntimeError, 4_000),
        result.ExitCode,
        result.DurationMs,
        result.Truncated,
        SanitizePublicRuntimeText(result.SafeTutorSummary, 600),
        SanitizePublicRuntimeText(result.Runtime, 80));

    private static string? SanitizePublicRuntimeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var text = value.Trim();
        string[] markers =
        [
            "rawPrompt", "hiddenPrompt", "systemPrompt", "developerPrompt", "rawProviderPayload",
            "rawSourceChunk", "rawToolPayload", "debugTrace", "localPath", "apiKey", "secret",
            "token", "answerKey", "correctAnswer", "stackTrace", "ownerId", "userId", "rawTranscript"
        ];

        foreach (var marker in markers)
        {
            text = Regex.Replace(text, Regex.Escape(marker), "[redacted]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        }

        text = Regex.Replace(text, @"[A-Za-z]:\\[^\s,;]+", "[redacted_path]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        text = Regex.Replace(text, @"/(?:home|users|var|tmp|workspace|app)/[^\s,;]+", "[redacted_path]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        text = Regex.Replace(text, @"(?i)(api[_-]?key|secret|token)\s*[:=]\s*['""]?[^'""\s,;]+", "[redacted_credential]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        text = Regex.Replace(text, @"(?im)^\s*at\s+.+$", "[redacted_trace]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        text = Regex.Replace(text, @"(?is)traceback\s*\(most recent call last\):.*", "[redacted_trace]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}

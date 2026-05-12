using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.API.Services;
using Orka.Core.Constants;
using Orka.Core.DTOs.Code;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/code")]
public class CodeController : ControllerBase
{
    private readonly IPistonService _piston;
    private readonly IRedisMemoryService _redis;
    private readonly ILearningSignalService _signals;
    private readonly IMistakeClassifierService? _mistakeClassifier;
    private readonly ResourceOwnershipGuard? _ownership;
    private readonly ILogger<CodeController> _logger;

    public CodeController(
        IPistonService piston,
        IRedisMemoryService redis,
        ILearningSignalService signals,
        ILogger<CodeController> logger,
        ResourceOwnershipGuard? ownership = null,
        IMistakeClassifierService? mistakeClassifier = null)
    {
        _piston = piston;
        _redis = redis;
        _signals = signals;
        _logger = logger;
        _ownership = ownership;
        _mistakeClassifier = mistakeClassifier;
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

        if (request.SessionId.HasValue)
        {
            await _redis.SetLastPistonResultAsync(
                request.SessionId.Value,
                request.Code,
                result.Stdout,
                result.Stderr,
                language,
                result.Phase,
                result.CompileError,
                result.RuntimeError,
                result.Success,
                result.SafeTutorSummary);

            _logger.LogInformation(
                "Piston sonucu Redis'e yazildi. Session={SessionId} Dil={Language} Phase={Phase} Success={Success}",
                request.SessionId.Value,
                language,
                result.Phase,
                result.Success);
        }

        await _signals.RecordSignalAsync(
            userId,
            request.TopicId,
            request.SessionId,
            ResolveIdeSignalType(result),
            skillTag: language,
            topicPath: request.TopicId.HasValue ? "IDE > Kod calistirma" : null,
            score: result.Success ? 100 : 0,
            isPositive: result.Success,
            payloadJson: JsonSerializer.Serialize(new
            {
                language,
                success = result.Success,
                phase = result.Phase,
                compileError = result.CompileError,
                runtimeError = result.RuntimeError,
                exitCode = result.ExitCode,
                durationMs = result.DurationMs,
                truncated = result.Truncated,
                safeTutorSummary = result.SafeTutorSummary,
                stdoutLength = result.Stdout?.Length ?? 0,
                stderrLength = result.Stderr?.Length ?? 0
            }),
            ct: HttpContext.RequestAborted);

        if (!result.Success && _mistakeClassifier != null)
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
                    ConceptTag: result.Phase,
                    CodePhase: result.Phase,
                    CompileError: result.CompileError,
                    RuntimeError: result.RuntimeError),
                HttpContext.RequestAborted);
        }

        return Ok(new CodeRunResponse(
            result.Stdout ?? string.Empty,
            result.Stderr ?? string.Empty,
            result.Success,
            result.Phase,
            result.CompileError,
            result.RuntimeError,
            result.ExitCode,
            result.DurationMs,
            result.Truncated,
            result.SafeTutorSummary,
            result.Runtime));
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

    private static bool IsSafeLanguage(string language)
    {
        if (language.Length is < 1 or > 40) return false;
        return language.All(c => char.IsLetterOrDigit(c) || c is '#' or '+' or '-' or '_' or '.');
    }
}

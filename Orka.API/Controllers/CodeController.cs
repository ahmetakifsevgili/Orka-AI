using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    private readonly ILogger<CodeController> _logger;

    public CodeController(IPistonService piston, IRedisMemoryService redis, ILogger<CodeController> logger)
    {
        _piston = piston;
        _redis  = redis;
        _logger = logger;
    }

    /// <summary>
    /// Piston API'nin desteklediği dil/runtime listesini döner.
    /// GET /api/code/languages
    /// </summary>
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

    /// <summary>
    /// Gönderilen kodu Piston sandbox'ında çalıştırır ve stdout/stderr döner.
    /// POST /api/code/run
    /// Body: { "code": "...", "language": "csharp", "stdin": "optional input" }
    /// </summary>
    [HttpPost("run")]
    public async Task<IActionResult> RunCode([FromBody] CodeRunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { error = "Kod boş olamaz." });

        if (request.Code.Length > 50_000)
            return BadRequest(new { error = "Kod 50.000 karakteri geçemez." });

        _logger.LogInformation("Kod çalıştırma isteği — dil: {Language}, boyut: {Size} karakter, stdin: {HasStdin}",
            request.Language, request.Code.Length, request.Stdin is not null);

        var result = await _piston.ExecuteAsync(
            request.Code,
            request.Language ?? "csharp",
            request.Stdin);

        // SessionId sağlandıysa sonucu Redis'e yaz — TutorAgent bir sonraki mesajda okur
        if (request.SessionId.HasValue && result.Success)
        {
            await _redis.SetLastPistonResultAsync(
                request.SessionId.Value,
                request.Code,
                result.Stdout,
                result.Stderr,
                request.Language ?? "csharp");

            _logger.LogInformation(
                "Piston sonucu Redis'e yazıldı. Session={SessionId} Dil={Language}",
                request.SessionId.Value, request.Language);
        }

        return Ok(new CodeRunResponse(result.Stdout, result.Stderr, result.Success));
    }
}

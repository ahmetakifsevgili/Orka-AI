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
    private readonly ILogger<CodeController> _logger;

    public CodeController(IPistonService piston, ILogger<CodeController> logger)
    {
        _piston = piston;
        _logger = logger;
    }

    /// <summary>
    /// Gönderilen kodu Piston sandbox'ında çalıştırır ve stdout/stderr döner.
    /// POST /api/code/run
    /// Body: { "code": "...", "language": "csharp" }
    /// </summary>
    [HttpPost("run")]
    public async Task<IActionResult> RunCode([FromBody] CodeRunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { error = "Kod boş olamaz." });

        if (request.Code.Length > 50_000)
            return BadRequest(new { error = "Kod 50.000 karakteri geçemez." });

        _logger.LogInformation("Kod çalıştırma isteği — dil: {Language}, boyut: {Size} karakter",
            request.Language, request.Code.Length);

        var result = await _piston.ExecuteAsync(request.Code, request.Language ?? "csharp");

        return Ok(new CodeRunResponse(result.Stdout, result.Stderr, result.Success));
    }
}

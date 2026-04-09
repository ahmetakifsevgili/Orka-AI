using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

/// <summary>
/// Beşli Çete AI sağlayıcıları için sağlık kontrolü (health check) endpoint'leri.
/// Tüm endpoint'ler [AllowAnonymous] — auth token gerektirmez.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    private readonly IGroqService       _groq;
    private readonly ISambaNovaService  _sambaNova;
    private readonly ICerebrasService   _cerebras;
    private readonly IOpenRouterService _openRouter;
    private readonly IMistralService    _mistral;
    private readonly IAIServiceChain    _chain;

    public TestController(
        IGroqService       groq,
        ISambaNovaService  sambaNova,
        ICerebrasService   cerebras,
        IOpenRouterService openRouter,
        IMistralService    mistral,
        IAIServiceChain    chain)
    {
        _groq       = groq;
        _sambaNova  = sambaNova;
        _cerebras   = cerebras;
        _openRouter = openRouter;
        _mistral    = mistral;
        _chain      = chain;
    }

    private const string PingSystem = "Sen bir test botusun. Sadece 'PONG' yaz.";
    private const string PingUser   = "PING";

    /// <summary>Tüm 5 sağlayıcıyı paralel test eder.</summary>
    [HttpGet("ping-all")]
    public async Task<IActionResult> PingAll()
    {
        var tasks = new[]
        {
            Ping("Groq",       () => _groq.GenerateResponseAsync(PingSystem, PingUser)),
            Ping("SambaNova",  () => _sambaNova.GenerateResponseAsync(PingSystem, PingUser)),
            Ping("Cerebras",   () => _cerebras.GenerateResponseAsync(PingSystem, PingUser)),
            Ping("OpenRouter", () => _openRouter.GenerateResponseAsync(PingSystem, PingUser)),
            Ping("Mistral",    () => _mistral.GenerateResponseAsync(PingSystem, PingUser)),
        };

        var results = await Task.WhenAll(tasks);
        return Ok(new { status = "COMPLETE", results });
    }

    [HttpGet("ping-groq")]
    public async Task<IActionResult> PingGroq()
        => Ok(await Ping("Groq", () => _groq.GenerateResponseAsync(PingSystem, PingUser)));

    [HttpGet("ping-sambanova")]
    public async Task<IActionResult> PingSambaNova()
        => Ok(await Ping("SambaNova", () => _sambaNova.GenerateResponseAsync(PingSystem, PingUser)));

    [HttpGet("ping-cerebras")]
    public async Task<IActionResult> PingCerebras()
        => Ok(await Ping("Cerebras", () => _cerebras.GenerateResponseAsync(PingSystem, PingUser)));

    [HttpGet("ping-openrouter")]
    public async Task<IActionResult> PingOpenRouter()
        => Ok(await Ping("OpenRouter", () => _openRouter.GenerateResponseAsync(PingSystem, PingUser)));

    [HttpGet("ping-mistral")]
    public async Task<IActionResult> PingMistral()
        => Ok(await Ping("Mistral", () => _mistral.GenerateResponseAsync(PingSystem, PingUser)));

    /// <summary>
    /// AIServiceChain failover zincirini test eder.
    /// Gerçek bir soru gönderir; hangi provider yanıt verdiyse gösterir.
    /// </summary>
    [HttpGet("chain-test")]
    public async Task<IActionResult> ChainTest([FromQuery] string q = "Merhaba, nasılsın?")
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await _chain.GenerateWithFallbackAsync("Sen yardımcı bir asistansın.", q);
            sw.Stop();
            return Ok(new { ok = true, response = result.Trim(), latencyMs = sw.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return StatusCode(503, new { ok = false, error = ex.Message, latencyMs = sw.ElapsedMilliseconds });
        }
    }

    /// <summary>Cerebras ile 4 adımlı plan üretir (Deep Plan testi).</summary>
    [HttpGet("cerebras-plan")]
    public async Task<IActionResult> CerebrasDeepPlan([FromQuery] string topic = "C# ile SQL Veritabani")
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var systemPrompt = "Sen bir eğitim planlayıcısısın. Kullanıcının verdiği konuyu 4 mantıksal alt başlığa böl. Her başlık bir satır, numaralı liste formatında yaz.";
            var result = await _cerebras.GenerateResponseAsync(systemPrompt, $"Konu: {topic}");
            sw.Stop();
            return Ok(new
            {
                provider  = "Cerebras/llama3.1-8b",
                topic,
                plan      = result.Trim(),
                latencyMs = sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return StatusCode(503, new { ok = false, provider = "Cerebras", error = ex.Message, latencyMs = sw.ElapsedMilliseconds });
        }
    }

    private static async Task<object> Ping(string provider, Func<Task<string>> call)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var resp = await call();
            sw.Stop();
            return new { provider, ok = true, response = resp.Trim(), latencyMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new { provider, ok = false, error = ex.Message, latencyMs = sw.ElapsedMilliseconds };
        }
    }
}

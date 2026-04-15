using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

/// <summary>
/// AI sağlayıcıları için sağlık kontrolü (health check) endpoint'leri.
/// Tüm endpoint'ler [AllowAnonymous] — auth token gerektirmez.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    private readonly IGroqService          _groq;
    private readonly IAIServiceChain       _chain;
    private readonly IGitHubModelsService  _github;
    private readonly IEmbeddingService     _embedding;
    private readonly IAIAgentFactory       _factory;

    public TestController(
        IGroqService          groq,
        IAIServiceChain       chain,
        IGitHubModelsService  github,
        IEmbeddingService     embedding,
        IAIAgentFactory       factory)
    {
        _groq      = groq;
        _chain     = chain;
        _github    = github;
        _embedding = embedding;
        _factory   = factory;
    }

    private const string PingSystem = "Sen bir test botusun. Sadece 'PONG' yaz.";
    private const string PingUser   = "PING";

    /// <summary>Groq primary provider ping.</summary>
    [HttpGet("ping-groq")]
    public async Task<IActionResult> PingGroq()
        => Ok(await Ping("Groq", () => _groq.GenerateResponseAsync(PingSystem, PingUser)));

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

    /// <summary>GitHub Models + AIAgentFactory + Cohere Embed sağlık kontrolü.</summary>
    [HttpGet("ping-github")]
    public async Task<IActionResult> PingGitHub()
    {
        var tasks = new[]
        {
            Ping("GitHub/gpt-4o",      () => _github.ChatAsync(PingSystem, PingUser, "gpt-4o")),
            Ping("GitHub/gpt-4o-mini", () => _github.ChatAsync(PingSystem, PingUser, "gpt-4o-mini")),
            Ping("GitHub/Llama-405B",  () => _github.ChatAsync(PingSystem, PingUser, "Meta-Llama-3.1-405B-Instruct")),
        };
        var results = await Task.WhenAll(tasks);
        return Ok(new { status = "COMPLETE", results });
    }

    /// <summary>AIAgentFactory failover zincirini test eder (GitHub → Groq → Gemini).</summary>
    [HttpGet("ping-factory")]
    public async Task<IActionResult> PingFactory([FromQuery] string role = "Tutor")
    {
        if (!Enum.TryParse<AgentRole>(role, true, out var agentRole))
            return BadRequest(new { error = "Geçersiz role. Değerler: Tutor, DeepPlan, Analyzer, Summarizer, Korteks" });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var model    = _factory.GetModel(agentRole);
            var response = await _factory.CompleteChatAsync(agentRole, PingSystem, PingUser);
            sw.Stop();
            return Ok(new { ok = true, role = agentRole.ToString(), model, response = response.Trim(), latencyMs = sw.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return StatusCode(503, new { ok = false, error = ex.Message, latencyMs = sw.ElapsedMilliseconds });
        }
    }

    /// <summary>Cohere Embed ping — 1024 boyutlu vektör üretimini doğrular.</summary>
    [HttpGet("ping-embed")]
    public async Task<IActionResult> PingEmbed([FromQuery] string text = "Python programlama dili")
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var vector = await _embedding.EmbedAsync(text, "search_query");
            sw.Stop();
            return Ok(new { ok = true, model = "embed-multilingual-v3.0", dimensions = vector.Length, sampleValues = vector.Take(5), latencyMs = sw.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return StatusCode(503, new { ok = false, error = ex.Message, latencyMs = sw.ElapsedMilliseconds });
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

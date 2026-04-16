using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// AIAgentFactory — Orka'nın ajan yönlendirme ve hata toleransı merkezi.
///
/// Her ajan rolü için model eşleştirmesi appsettings.json'dan okunur:
///   AI:GitHubModels:Agents:{Role}:Model
///
/// Failover Zinciri (sıralı):
///   1. GitHub Models (Azure AI Inference)  — Primary
///   2. Groq (llama-3.3-70b-versatile)      — Fallback 1
///   3. Gemini 2.5 Flash                     — Fallback 2
/// </summary>
public class AIAgentFactory : IAIAgentFactory
{
    private readonly IGitHubModelsService _github;
    private readonly IGroqService _groq;
    private readonly IGeminiService _gemini;
    private readonly IRedisMemoryService _redis;
    private readonly ILogger<AIAgentFactory> _logger;
    private readonly Dictionary<AgentRole, string> _modelMap;

    public AIAgentFactory(
        IGitHubModelsService github,
        IGroqService groq,
        IGeminiService gemini,
        IRedisMemoryService redis,
        IConfiguration configuration,
        ILogger<AIAgentFactory> logger)
    {
        _github = github;
        _groq   = groq;
        _gemini = gemini;
        _redis  = redis;
        _logger = logger;

        _modelMap = new Dictionary<AgentRole, string>
        {
            [AgentRole.Tutor]      = configuration["AI:GitHubModels:Agents:Tutor:Model"]      ?? "gpt-4o",
            [AgentRole.DeepPlan]   = configuration["AI:GitHubModels:Agents:DeepPlan:Model"]   ?? "Meta-Llama-3.1-405B-Instruct",
            [AgentRole.Analyzer]   = configuration["AI:GitHubModels:Agents:Analyzer:Model"]   ?? "gpt-4o-mini",
            [AgentRole.Summarizer] = configuration["AI:GitHubModels:Agents:Summarizer:Model"] ?? "gpt-4o-mini",
            [AgentRole.Korteks]    = configuration["AI:GitHubModels:Agents:Korteks:Model"]    ?? "Meta-Llama-3.1-405B-Instruct",
            [AgentRole.Supervisor] = configuration["AI:GitHubModels:Agents:Supervisor:Model"]  ?? "gpt-4o-mini",
            [AgentRole.Grader]     = configuration["AI:GitHubModels:Agents:Grader:Model"]      ?? "gpt-4o-mini",
            [AgentRole.Evaluator]  = configuration["AI:GitHubModels:Agents:Evaluator:Model"]   ?? "gpt-4o-mini",
        };
    }

    /// <inheritdoc/>
    public string GetModel(AgentRole role) =>
        _modelMap.TryGetValue(role, out var m) ? m : "gpt-4o-mini";

    private static readonly TimeSpan AgentTimeout = TimeSpan.FromSeconds(20);

    /// <inheritdoc/>
    public async Task<string> CompleteChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        var model    = GetModel(role);
        var roleName = role.ToString();
        var sw       = Stopwatch.StartNew();

        // 1. GitHub Models — 10s timeout
        using (var cts1 = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts1.CancelAfter(AgentTimeout);
            try
            {
                _logger.LogDebug("[AIAgentFactory] {Role} → GitHub Models ({Model})", role, model);
                var result = await _github.ChatAsync(systemPrompt, userMessage, model, cts1.Token);
                sw.Stop();
                _ = _redis.RecordAgentMetricAsync(roleName, sw.ElapsedMilliseconds, true, "GitHub");
                return result;
            }
            catch (Exception ex) when (IsTransient(ex) || cts1.IsCancellationRequested)
            {
                _logger.LogWarning("[AIAgentFactory] {Role} GitHub başarısız/timeout ({Msg}), Groq fallback.", role, ex.Message);
                _ = _redis.RecordAgentMetricAsync(roleName, sw.ElapsedMilliseconds, false, "GitHub");
                sw.Restart();
            }
        }

        // 2. Groq fallback — 10s timeout
        using (var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts2.CancelAfter(AgentTimeout);
            try
            {
                var result = await _groq.GenerateResponseAsync(systemPrompt, userMessage, cts2.Token);
                sw.Stop();
                _ = _redis.RecordAgentMetricAsync(roleName, sw.ElapsedMilliseconds, true, "Groq");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[AIAgentFactory] {Role} Groq başarısız/timeout ({Msg}), Gemini fallback.", role, ex.Message);
                _ = _redis.RecordAgentMetricAsync(roleName, sw.ElapsedMilliseconds, false, "Groq");
                sw.Restart();
            }
        }

        // 3. Gemini fallback — 10s timeout
        using var cts3 = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts3.CancelAfter(AgentTimeout);
        var geminiResult = await _gemini.GenerateSmartAsync(systemPrompt, userMessage, cts3.Token);
        sw.Stop();
        _ = _redis.RecordAgentMetricAsync(roleName, sw.ElapsedMilliseconds, true, "Gemini");
        return geminiResult;
    }

    /// <inheritdoc/>
    /// C# kısıtı: yield return, catch yan tümcesi içeren try bloğunda kullanamaz.
    /// Bu nedenle "ilk token probe" pattern kullanılır:
    ///   - İlk token TRY/CATCH içinde alınır (yield yok)
    ///   - Başarılıysa, geri kalan stream TRY dışında yield edilir
    ///   - Başarısızsa fallback provider'a geçilir
    public async IAsyncEnumerable<string> StreamChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = GetModel(role);

        // ── 1. GitHub Models stream — 10s ilk token timeout ─────────────────
        IAsyncEnumerator<string>? githubEnum = null;
        string? firstChunk = null;
        bool githubOk = false;

        using (var probeCts1 = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            probeCts1.CancelAfter(AgentTimeout);
            try
            {
                githubEnum = _github.ChatStreamAsync(systemPrompt, userMessage, model, probeCts1.Token)
                                     .GetAsyncEnumerator(probeCts1.Token);
                githubOk   = await githubEnum.MoveNextAsync();
                if (githubOk) firstChunk = githubEnum.Current;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[AIAgentFactory] {Role} GitHub stream başarısız/timeout: {Msg}", role, ex.Message);
                githubOk = false;
            }
        }

        if (githubOk && githubEnum != null)
        {
            yield return firstChunk!;

            // Geri kalanı outer ct ile stream et
            while (await githubEnum.MoveNextAsync())
                yield return githubEnum.Current;

            await githubEnum.DisposeAsync();
            yield break;
        }

        if (githubEnum != null) await githubEnum.DisposeAsync();

        _logger.LogInformation("[AIAgentFactory] {Role} Groq stream fallback.", role);

        // ── 2. Groq stream — 10s ilk token timeout ──────────────────────────
        IAsyncEnumerator<string>? groqEnum = null;
        string? groqFirst = null;
        bool groqOk = false;

        using (var probeCts2 = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            probeCts2.CancelAfter(AgentTimeout);
            try
            {
                groqEnum = _groq.GenerateResponseStreamAsync(systemPrompt, userMessage, probeCts2.Token)
                                 .GetAsyncEnumerator(probeCts2.Token);
                groqOk   = await groqEnum.MoveNextAsync();
                if (groqOk) groqFirst = groqEnum.Current;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[AIAgentFactory] {Role} Groq stream başarısız/timeout: {Msg}", role, ex.Message);
                groqOk = false;
            }
        }

        if (groqOk && groqEnum != null)
        {
            yield return groqFirst!;

            while (await groqEnum.MoveNextAsync())
                yield return groqEnum.Current;

            await groqEnum.DisposeAsync();
            yield break;
        }

        if (groqEnum != null) await groqEnum.DisposeAsync();

        _logger.LogInformation("[AIAgentFactory] {Role} Gemini stream fallback.", role);

        // ── 3. Gemini stream (son çare) — 10s ilk token timeout ─────────────
        using var probeCts3 = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probeCts3.CancelAfter(AgentTimeout);
        await foreach (var chunk in _gemini.StreamSmartAsync(systemPrompt, userMessage, probeCts3.Token))
            yield return chunk;
    }

    /// <inheritdoc/>
    public async Task<string> CompleteChatWithHistoryAsync(
        AgentRole role,
        string systemPrompt,
        IEnumerable<(string Role, string Content)> messages,
        CancellationToken ct = default)
    {
        var history     = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
        var userMessage = $"[Sohbet Geçmişi]\n{history}";
        return await CompleteChatAsync(role, systemPrompt, userMessage, ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsTransient(Exception ex)
    {
        if (ex is Azure.RequestFailedException rfe)
            // 401/403: token hatası (geçici değil ama fallback açısından tolere edilir)
            // 429: rate limit, 503: servis dışı, 0: ağ hatası
            return rfe.Status is 401 or 403 or 429 or 503 or 0;

        if (ex is HttpRequestException) return true;
        if (ex is TaskCanceledException)  return true;
        if (ex is TimeoutException)       return true;

        if (ex.InnerException != null)
            return IsTransient(ex.InnerException);

        return false;
    }
}

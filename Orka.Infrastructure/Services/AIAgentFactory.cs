using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// AIAgentFactory â€” Orka'nÄ±n ajan yÃ¶nlendirme ve hata toleransÄ± merkezi.
///
/// KonfigÃ¼rasyon:
///   AI:AgentRouting:{Role}:Provider  â†’ "GitHubModels" | "Groq" | "Gemini" | "OpenRouter" | "Cerebras" | "Mistral"
///   AI:AgentRouting:{Role}:Model     â†’ role-spesifik model adÄ±
///
/// Geriye uyumluluk: AgentRouting tanÄ±mÄ± yoksa eski "AI:GitHubModels:Agents:{Role}:Model" yolu kullanÄ±lÄ±r.
///
/// Failover Zinciri (sÄ±ralÄ±, provider'a gÃ¶re dinamik):
///   Non-stream: Primary â†’ Groq â†’ Mistral
///   Stream:     Primary â†’ Gemini â†’ Mistral
/// </summary>
public class AIAgentFactory : IAIAgentFactory
{
    private readonly IGitHubModelsService _github;
    private readonly IGroqService _groq;
    private readonly IGeminiService _gemini;
    private readonly IOpenRouterService _openRouter;
    private readonly ICerebrasService _cerebras;
    private readonly IMistralService _mistral;
    private readonly ISambaNovaService _sambaNova;
    private readonly IRedisMemoryService _redis;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AIAgentFactory> _logger;
    private readonly Dictionary<AgentRole, RouteConfig> _routes;

    private record RouteConfig(string Provider, string Model);

    public AIAgentFactory(
        IGitHubModelsService github,
        IGroqService groq,
        IGeminiService gemini,
        IOpenRouterService openRouter,
        ICerebrasService cerebras,
        IMistralService mistral,
        ISambaNovaService sambaNova,
        IRedisMemoryService redis,
        IBackgroundTaskQueue backgroundQueue,
        IConfiguration configuration,
        ILogger<AIAgentFactory> logger)
    {
        _github     = github;
        _groq       = groq;
        _gemini     = gemini;
        _openRouter = openRouter;
        _cerebras   = cerebras;
        _mistral    = mistral;
        _sambaNova  = sambaNova;
        _redis      = redis;
        _backgroundQueue = backgroundQueue;
        _configuration = configuration;
        _logger     = logger;

        _routes = new Dictionary<AgentRole, RouteConfig>();
        foreach (AgentRole role in Enum.GetValues<AgentRole>())
        {
            var provider = configuration[$"AI:AgentRouting:{role}:Provider"]
                          ?? "GitHubModels";
            var model    = configuration[$"AI:AgentRouting:{role}:Model"]
                          ?? configuration[$"AI:GitHubModels:Agents:{role}:Model"]
                          ?? "gpt-4o-mini";
            _routes[role] = new RouteConfig(provider, model);
        }
    }

    /// <inheritdoc/>
    public string GetModel(AgentRole role) =>
        _routes.TryGetValue(role, out var r) ? r.Model : "gpt-4o-mini";

    public string GetProvider(AgentRole role) =>
        _routes.TryGetValue(role, out var r) ? r.Provider : "GitHubModels";

    private static readonly TimeSpan AgentTimeout = TimeSpan.FromSeconds(20);

    /// <inheritdoc/>
    public async Task<string> CompleteChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        var route    = _routes[role];
        var roleName = role.ToString();
        var sw       = Stopwatch.StartNew();

        // â”€â”€ 1. PRIMARY (rol iÃ§in belirlenmiÅŸ provider) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        using (var cts1 = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts1.CancelAfter(AgentTimeout);
            try
            {
                _logger.LogDebug("[AIAgentFactory] {Role} â†’ {Provider} ({Model})", role, route.Provider, route.Model);
                EnsureProviderConfigured(route.Provider);
                var result = await CallPrimaryProviderAsync(route, systemPrompt, userMessage, cts1.Token);
                sw.Stop();
                RecordMetricSafe(roleName, sw.ElapsedMilliseconds, true, route.Provider);
                return result;
            }
            catch (ProviderConfigurationException ex)
            {
                _logger.LogWarning("[AIAgentFactory] {Role} provider config eksik: {Msg}. Fallback deneniyor.", role, ex.Message);
                RecordMetricSafe(roleName, sw.ElapsedMilliseconds, false, route.Provider);
                sw.Restart();
            }
            catch (Exception ex) when (IsTransient(ex) || cts1.IsCancellationRequested)
            {
                _logger.LogWarning("[AIAgentFactory] {Role} {Provider} baÅŸarÄ±sÄ±z ({Msg}), Groq fallback.", role, route.Provider, ex.Message);
                RecordMetricSafe(roleName, sw.ElapsedMilliseconds, false, route.Provider);
                sw.Restart();
            }
        }

        // â”€â”€ 2. GROQ FALLBACK â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (!string.Equals(route.Provider, "Groq", StringComparison.OrdinalIgnoreCase))
        {
            using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts2.CancelAfter(AgentTimeout);
            try
            {
                EnsureProviderConfigured("Groq");
                var result = await _groq.GenerateResponseAsync(systemPrompt, userMessage, cts2.Token);
                sw.Stop();
                RecordMetricSafe(roleName, sw.ElapsedMilliseconds, true, "Groq");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[AIAgentFactory] {Role} Groq baÅŸarÄ±sÄ±z ({Msg}), Mistral fallback.", role, ex.Message);
                RecordMetricSafe(roleName, sw.ElapsedMilliseconds, false, "Groq");
                sw.Restart();
            }
        }

        // â”€â”€ 3. MISTRAL FALLBACK (son seviye) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        using var cts3 = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts3.CancelAfter(AgentTimeout);
        try
        {
            EnsureProviderConfigured("Mistral");
            var result = await _mistral.GenerateResponseAsync(systemPrompt, userMessage, cts3.Token);
            sw.Stop();
            RecordMetricSafe(roleName, sw.ElapsedMilliseconds, true, "Mistral");
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[AIAgentFactory] {Role} Mistral de baÅŸarÄ±sÄ±z. TÃ¼m provider'lar tÃ¼kendi.", role);
            RecordMetricSafe(roleName, sw.ElapsedMilliseconds, false, "Mistral");
            throw;
        }
    }

    /// <inheritdoc/>
    /// C# kÄ±sÄ±tÄ±: yield return, catch yan tÃ¼mcesi iÃ§eren try bloÄŸunda kullanamaz.
    /// Bu nedenle "ilk token probe" pattern kullanÄ±lÄ±r:
    ///   - Ä°lk token TRY/CATCH iÃ§inde alÄ±nÄ±r (yield yok) ve TTFT Ã¶lÃ§Ã¼lÃ¼r
    ///   - BaÅŸarÄ±lÄ±ysa, geri kalan stream TRY dÄ±ÅŸÄ±nda yield edilir
    ///   - BaÅŸarÄ±sÄ±zsa fallback provider'a geÃ§ilir
    public async IAsyncEnumerable<string> StreamChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var route    = _routes[role];
        var roleName = role.ToString();

        // â”€â”€ 1. PRIMARY stream â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        IAsyncEnumerator<string>? primaryEnum = null;
        string? firstChunk = null;
        bool primaryOk = false;
        var swPrimary = Stopwatch.StartNew();

        using (var probeCts1 = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            probeCts1.CancelAfter(AgentTimeout);
            try
            {
                EnsureProviderConfigured(route.Provider);
                primaryEnum = StreamFromPrimary(route, systemPrompt, userMessage, probeCts1.Token)
                                .GetAsyncEnumerator(probeCts1.Token);
                primaryOk = await primaryEnum.MoveNextAsync(); // TTFT
                swPrimary.Stop();
                if (primaryOk) firstChunk = primaryEnum.Current;
            }
            catch (Exception ex)
            {
                swPrimary.Stop();
                _logger.LogWarning("[AIAgentFactory] {Role} {Provider} stream baÅŸarÄ±sÄ±z: {Msg}", role, route.Provider, ex.Message);
                RecordMetricSafe(roleName, swPrimary.ElapsedMilliseconds, false, route.Provider);
                primaryOk = false;
            }
        }

        if (primaryOk && primaryEnum != null)
        {
            RecordMetricSafe(roleName, swPrimary.ElapsedMilliseconds, true, route.Provider);
            yield return firstChunk!;
            while (await primaryEnum.MoveNextAsync())
                yield return primaryEnum.Current;
            await primaryEnum.DisposeAsync();
            yield break;
        }
        if (primaryEnum != null) await primaryEnum.DisposeAsync();

        _logger.LogInformation("[AIAgentFactory] {Role} Gemini stream fallback.", role);

        // â”€â”€ 2. GEMINI FALLBACK stream â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        IAsyncEnumerator<string>? geminiEnum = null;
        string? geminiFirst = null;
        bool geminiOk = false;
        var swGemini = Stopwatch.StartNew();

        using (var probeCts2 = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            probeCts2.CancelAfter(AgentTimeout);
            try
            {
                EnsureProviderConfigured("Gemini");
                geminiEnum = _gemini.StreamSmartAsync(systemPrompt, userMessage, probeCts2.Token)
                                    .GetAsyncEnumerator(probeCts2.Token);
                geminiOk = await geminiEnum.MoveNextAsync();
                swGemini.Stop();
                if (geminiOk) geminiFirst = geminiEnum.Current;
            }
            catch (Exception ex)
            {
                swGemini.Stop();
                _logger.LogWarning("[AIAgentFactory] {Role} Gemini stream baÅŸarÄ±sÄ±z: {Msg}", role, ex.Message);
                RecordMetricSafe(roleName, swGemini.ElapsedMilliseconds, false, "Gemini");
                geminiOk = false;
            }
        }

        if (geminiOk && geminiEnum != null)
        {
            RecordMetricSafe(roleName, swGemini.ElapsedMilliseconds, true, "Gemini");
            yield return geminiFirst!;
            while (await geminiEnum.MoveNextAsync())
                yield return geminiEnum.Current;
            await geminiEnum.DisposeAsync();
            yield break;
        }
        if (geminiEnum != null) await geminiEnum.DisposeAsync();

        _logger.LogInformation("[AIAgentFactory] {Role} Mistral stream fallback (son seviye).", role);

        // â”€â”€ 3. MISTRAL FALLBACK stream â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var swMistral = Stopwatch.StartNew();
        bool mistralGotFirst = false;
        using var probeCts3 = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probeCts3.CancelAfter(AgentTimeout);
        EnsureProviderConfigured("Mistral");
        await foreach (var chunk in _mistral.GenerateResponseStreamAsync(systemPrompt, userMessage, probeCts3.Token))
        {
            if (!mistralGotFirst)
            {
                swMistral.Stop();
                RecordMetricSafe(roleName, swMistral.ElapsedMilliseconds, true, "Mistral");
                mistralGotFirst = true;
            }
            yield return chunk;
        }
    }

    /// <inheritdoc/>
    public async Task<string> CompleteChatWithHistoryAsync(
        AgentRole role,
        string systemPrompt,
        IEnumerable<(string Role, string Content)> messages,
        CancellationToken ct = default)
    {
        var history     = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
        var userMessage = $"[Sohbet GeÃ§miÅŸi]\n{history}";
        return await CompleteChatAsync(role, systemPrompt, userMessage, ct);
    }

    // â”€â”€ Provider Ã§aÄŸrÄ± dispatch'i â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private Task<string> CallPrimaryProviderAsync(
        RouteConfig route, string systemPrompt, string userMessage, CancellationToken ct)
    {
        return route.Provider.ToLowerInvariant() switch
        {
            "githubmodels" => _github.ChatAsync(systemPrompt, userMessage, route.Model, ct),
            "groq"         => _groq.GenerateResponseAsync(systemPrompt, userMessage, ct),
            "gemini"       => _gemini.GenerateSmartAsync(systemPrompt, userMessage, ct),
            "openrouter"   => _openRouter.ChatCompletionAsync(systemPrompt, userMessage, route.Model, ct),
            "cerebras"     => _cerebras.GenerateResponseAsync(systemPrompt, userMessage, ct),
            "mistral"      => _mistral.GenerateResponseAsync(systemPrompt, userMessage, ct),
            "sambanova"    => _sambaNova.GenerateResponseAsync(systemPrompt, userMessage, ct),
            _              => _github.ChatAsync(systemPrompt, userMessage, route.Model, ct)
        };
    }

    private IAsyncEnumerable<string> StreamFromPrimary(
        RouteConfig route, string systemPrompt, string userMessage, CancellationToken ct)
    {
        return route.Provider.ToLowerInvariant() switch
        {
            "githubmodels" => _github.ChatStreamAsync(systemPrompt, userMessage, route.Model, ct),
            "groq"         => _groq.GenerateResponseStreamAsync(systemPrompt, userMessage, ct),
            "gemini"       => _gemini.StreamSmartAsync(systemPrompt, userMessage, ct),
            "openrouter"   => _openRouter.GenerateResponseStreamAsync(systemPrompt, userMessage, ct),
            "cerebras"     => _cerebras.GenerateResponseStreamAsync(systemPrompt, userMessage, ct),
            "mistral"      => _mistral.GenerateResponseStreamAsync(systemPrompt, userMessage, ct),
            "sambanova"    => _sambaNova.GenerateResponseStreamAsync(systemPrompt, userMessage, ct),
            _              => _github.ChatStreamAsync(systemPrompt, userMessage, route.Model, ct)
        };
    }

    private void EnsureProviderConfigured(string provider)
    {
        var keyPath = provider.ToLowerInvariant() switch
        {
            "githubmodels" => "AI:GitHubModels:Token",
            "groq" => "AI:Groq:ApiKey",
            "gemini" => "AI:Gemini:ApiKey",
            "openrouter" => "AI:OpenRouter:ApiKey",
            "cerebras" => "AI:Cerebras:ApiKey",
            "mistral" => "AI:Mistral:ApiKey",
            "sambanova" => "AI:SambaNova:ApiKey",
            _ => null
        };

        if (keyPath is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_configuration[keyPath]))
        {
            throw new ProviderConfigurationException(provider, keyPath);
        }
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Redis metric kaydÄ±nÄ± gÃ¼venli ÅŸekilde fire-and-forget yapar.
    /// Hata durumunda log'lar, stream performansÄ±nÄ± etkilemez.
    /// </summary>
    private void RecordMetricSafe(string roleName, long latencyMs, bool isSuccess, string provider)
    {
        _ = _backgroundQueue.QueueAsync(new BackgroundTaskItem(
            "agent-metric",
            null,
            null,
            _ => _redis.RecordAgentMetricAsync(roleName, latencyMs, isSuccess, provider),
            MaxAttempts: 1,
            Timeout: TimeSpan.FromSeconds(5)));
    }

    private static bool IsTransient(Exception ex)
    {
        if (ex is Azure.RequestFailedException rfe)
            return rfe.Status is 401 or 403 or 429 or 503 or 0;

        if (ex is HttpRequestException) return true;
        if (ex is TaskCanceledException)  return true;
        if (ex is TimeoutException)       return true;

        if (ex.InnerException != null)
            return IsTransient(ex.InnerException);

        return false;
    }
}

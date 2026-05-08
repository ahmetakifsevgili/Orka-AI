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
/// Konfigürasyon:
///   AI:AgentRouting:{Role}:Provider  → "GitHubModels" | "Groq" | "Gemini" | "OpenRouter" | "Cerebras" | "Mistral"
///   AI:AgentRouting:{Role}:Model     → role-spesifik model adı
///
/// Geriye uyumluluk: AgentRouting tanımı yoksa eski "AI:GitHubModels:Agents:{Role}:Model" yolu kullanılır.
///
/// Failover Zinciri (sıralı, provider'a göre dinamik):
///   Non-stream: Primary → Groq → Mistral
///   Stream:     Primary → Gemini → Mistral
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

        // ── 1. PRIMARY (rol için belirlenmiş provider) ──────────────────────
        using (var cts1 = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts1.CancelAfter(AgentTimeout);
            try
            {
                _logger.LogDebug("[AIAgentFactory] {Role} → {Provider} ({Model})", role, route.Provider, route.Model);
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
                _logger.LogWarning("[AIAgentFactory] {Role} {Provider} başarısız ({Msg}), Groq fallback.", role, route.Provider, ex.Message);
                RecordMetricSafe(roleName, sw.ElapsedMilliseconds, false, route.Provider);
                sw.Restart();
            }
        }

        // ── 2. GROQ FALLBACK ────────────────────────────────────────────────
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
                _logger.LogWarning("[AIAgentFactory] {Role} Groq başarısız ({Msg}), Mistral fallback.", role, ex.Message);
                RecordMetricSafe(roleName, sw.ElapsedMilliseconds, false, "Groq");
                sw.Restart();
            }
        }

        // ── 3. MISTRAL FALLBACK (son seviye) ────────────────────────────────
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
            _logger.LogError(ex, "[AIAgentFactory] {Role} Mistral de başarısız. Tüm provider'lar tükendi.", role);
            RecordMetricSafe(roleName, sw.ElapsedMilliseconds, false, "Mistral");
            throw;
        }
    }

    /// <inheritdoc/>
    /// C# kısıtı: yield return, catch yan tümcesi içeren try bloğunda kullanamaz.
    /// Bu nedenle "ilk token probe" pattern kullanılır:
    ///   - İlk token TRY/CATCH içinde alınır (yield yok) ve TTFT ölçülür
    ///   - Başarılıysa, geri kalan stream TRY dışında yield edilir
    ///   - Başarısızsa fallback provider'a geçilir
    public async IAsyncEnumerable<string> StreamChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var route    = _routes[role];
        var roleName = role.ToString();

        // ── 1. PRIMARY stream ────────────────────────────────────────────────
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
                _logger.LogWarning("[AIAgentFactory] {Role} {Provider} stream başarısız: {Msg}", role, route.Provider, ex.Message);
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

        // ── 2. GEMINI FALLBACK stream ────────────────────────────────────────
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
                _logger.LogWarning("[AIAgentFactory] {Role} Gemini stream başarısız: {Msg}", role, ex.Message);
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

        // ── 3. MISTRAL FALLBACK stream ───────────────────────────────────────
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
        var userMessage = $"[Sohbet Geçmişi]\n{history}";
        return await CompleteChatAsync(role, systemPrompt, userMessage, ct);
    }

    // ── Provider çağrı dispatch'i ────────────────────────────────────────────

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

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Redis metric kaydını güvenli şekilde fire-and-forget yapar.
    /// Hata durumunda log'lar, stream performansını etkilemez.
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

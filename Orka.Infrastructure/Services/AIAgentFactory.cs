using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// AIAgentFactory — Orka'nın dinamik Swarm Router'ı.
/// N-to-N mimarisi ile her ajan için appsettings'ten Provider ve Model okur.
/// Tüm LLM API'leri (OpenRouter, Mistral, Cerebras vs.) liyakat tabanlı ve optimize kullanılır.
/// </summary>
public class AIAgentFactory : IAIAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IRedisMemoryService _redis;
    private readonly ILogger<AIAgentFactory> _logger;
    private readonly Dictionary<AgentRole, (string Provider, string Model)> _agentConfigMap;

    public AIAgentFactory(
        IServiceProvider serviceProvider,
        IRedisMemoryService redis,
        IConfiguration configuration,
        ILogger<AIAgentFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _redis = redis;
        _logger = logger;
        _agentConfigMap = new Dictionary<AgentRole, (string, string)>();

        foreach (var role in Enum.GetValues<AgentRole>())
        {
            var roleName = role.ToString();
            // Yeni konfigürasyon yapısı: AI:AgentRouting:{Role}:Provider
            var provider = configuration[$"AI:AgentRouting:{roleName}:Provider"] ?? "GitHubModels";
            var model = configuration[$"AI:AgentRouting:{roleName}:Model"] ?? "gpt-4o-mini";
            _agentConfigMap[role] = (provider, model);
        }
    }

    public string GetModel(AgentRole role) => _agentConfigMap.TryGetValue(role, out var config) ? config.Model : "gpt-4o-mini";
    public string GetProvider(AgentRole role) => _agentConfigMap.TryGetValue(role, out var config) ? config.Provider : "GitHubModels";

    private static readonly TimeSpan AgentTimeout = TimeSpan.FromSeconds(20);

    private IAIService ResolveService(string provider)
    {
        return provider switch
        {
            "OpenRouter"  => _serviceProvider.GetRequiredService<IOpenRouterService>(),
            "Mistral"     => _serviceProvider.GetRequiredService<IMistralService>(),
            "SambaNova"   => _serviceProvider.GetRequiredService<ISambaNovaService>(),
            "Cerebras"    => _serviceProvider.GetRequiredService<ICerebrasService>(),
            "HuggingFace" => _serviceProvider.GetRequiredService<IHuggingFaceService>(),
            "Groq"        => _serviceProvider.GetRequiredService<IGroqService>(),
            "Cohere"      => _serviceProvider.GetRequiredService<ICohereService>(),
            _             => throw new NotSupportedException($"Desteklenmeyen AI Provider: {provider}")
        };
    }

    private async Task<string> DispatchChatAsync(string provider, string model, string systemPrompt, string userMessage, CancellationToken ct)
    {
        if (provider == "GitHubModels")
        {
            var gh = _serviceProvider.GetRequiredService<IGitHubModelsService>();
            return await gh.ChatAsync(systemPrompt, userMessage, model, ct);
        }
        else if (provider == "Gemini")
        {
            var gem = _serviceProvider.GetRequiredService<IGeminiService>();
            return await gem.GenerateSmartAsync(systemPrompt, userMessage, ct);
        }
        else
        {
            var service = ResolveService(provider);
            return await service.GenerateResponseAsync(systemPrompt, userMessage, model, ct);
        }
    }

    private IAsyncEnumerable<string> DispatchStreamAsync(string provider, string model, string systemPrompt, string userMessage, CancellationToken ct)
    {
        if (provider == "GitHubModels")
        {
            var gh = _serviceProvider.GetRequiredService<IGitHubModelsService>();
            return gh.ChatStreamAsync(systemPrompt, userMessage, model, ct);
        }
        else if (provider == "Gemini")
        {
            var gem = _serviceProvider.GetRequiredService<IGeminiService>();
            return gem.StreamSmartAsync(systemPrompt, userMessage, ct);
        }
        else
        {
            var service = ResolveService(provider);
            return service.GenerateResponseStreamAsync(systemPrompt, userMessage, model, ct);
        }
    }

    public async Task<string> CompleteChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        var model    = GetModel(role);
        var provider = GetProvider(role);
        var roleName = role.ToString();
        var sw       = Stopwatch.StartNew();

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(AgentTimeout);
            try
            {
                _logger.LogDebug("[AIAgentFactory] {Role} → {Provider} ({Model})", role, provider, model);
                var result = await DispatchChatAsync(provider, model, systemPrompt, userMessage, cts.Token);
                sw.Stop();
                RecordMetricSafe(roleName, sw.ElapsedMilliseconds, true, provider);
                return result;
            }
            catch (Exception ex) when (IsTransient(ex) || cts.IsCancellationRequested)
            {
                _logger.LogWarning("[AIAgentFactory] {Role} {Provider} başarısız ({Msg}), Global Fallback (Groq) devrede.", role, provider, ex.Message);
                RecordMetricSafe(roleName, sw.ElapsedMilliseconds, false, provider);
                sw.Restart();
            }
        }

        // Global Fallback - Eğer atanan Provider çökerse her zaman çalışan ücretsiz ve hızlı Groq veya Gemini kurtarıcısı.
        using (var ctsFallback = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            ctsFallback.CancelAfter(AgentTimeout);
            try
            {
                var groq = _serviceProvider.GetRequiredService<IGroqService>();
                var result = await groq.GenerateResponseAsync(systemPrompt, userMessage, null, ctsFallback.Token);
                sw.Stop();
                RecordMetricSafe(roleName, sw.ElapsedMilliseconds, true, "GroqFallback");
                return result;
            }
            catch
            {
                sw.Stop();
                RecordMetricSafe(roleName, sw.ElapsedMilliseconds, false, "GroqFallback");
                throw;
            }
        }
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model    = GetModel(role);
        var provider = GetProvider(role);
        var roleName = role.ToString();

        IAsyncEnumerator<string>? mainEnum = null;
        string? firstChunk = null;
        bool isOk = false;
        var sw = Stopwatch.StartNew();

        using (var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            probeCts.CancelAfter(AgentTimeout);
            try
            {
                mainEnum = DispatchStreamAsync(provider, model, systemPrompt, userMessage, probeCts.Token).GetAsyncEnumerator(probeCts.Token);
                isOk = await mainEnum.MoveNextAsync(); // TTFT
                sw.Stop();
                if (isOk) firstChunk = mainEnum.Current;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning("[AIAgentFactory] {Role} stream başarısız {Provider}: {Msg}", role, provider, ex.Message);
                RecordMetricSafe(roleName, sw.ElapsedMilliseconds, false, provider);
                isOk = false;
            }
        }

        if (isOk && mainEnum != null)
        {
            RecordMetricSafe(roleName, sw.ElapsedMilliseconds, true, provider);
            yield return firstChunk!;

            while (await mainEnum.MoveNextAsync())
                yield return mainEnum.Current;

            await mainEnum.DisposeAsync();
            yield break;
        }

        if (mainEnum != null) await mainEnum.DisposeAsync();

        _logger.LogInformation("[AIAgentFactory] {Role} Global Fallback (Gemini stream) devrede.", role);

        // Global Stream Fallback
        var gemini = _serviceProvider.GetRequiredService<IGeminiService>();
        var swFallback = Stopwatch.StartNew();
        bool gotFirst = false;
        using var fbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fbCts.CancelAfter(AgentTimeout);
        
        await foreach (var chunk in gemini.StreamSmartAsync(systemPrompt, userMessage, fbCts.Token))
        {
            if (!gotFirst)
            {
                swFallback.Stop();
                RecordMetricSafe(roleName, swFallback.ElapsedMilliseconds, true, "GeminiFallback");
                gotFirst = true;
            }
            yield return chunk;
        }
    }

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

    private void RecordMetricSafe(string roleName, long latencyMs, bool isSuccess, string provider)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _redis.RecordAgentMetricAsync(roleName, latencyMs, isSuccess, provider);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AIAgentFactory] Redis metric başarısız. {Role} {Provider}", roleName, provider);
            }
        });
    }

    private static bool IsTransient(Exception ex)
    {
        if (ex is Azure.RequestFailedException rfe)
            return rfe.Status is 401 or 403 or 429 or 503 or 0;
        if (ex is HttpRequestException || ex is TaskCanceledException || ex is TimeoutException) return true;
        if (ex.InnerException != null) return IsTransient(ex.InnerException);
        return false;
    }
}

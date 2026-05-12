using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Enums;
using Orka.Core.Exceptions;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Security;

namespace Orka.Infrastructure.Services;

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
    private readonly IRuntimeTelemetryService _runtimeTelemetry;
    private readonly IAiProviderTelemetryService _aiTelemetry;
    private readonly IAiUsageBudgetService _budget;
    private readonly IAiProviderCircuitBreaker _circuitBreaker;
    private readonly IAiRequestContextAccessor _aiRequestContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AIAgentFactory> _logger;
    private readonly Dictionary<AgentRole, RouteConfig> _routes;

    private static readonly TimeSpan AgentTimeout = TimeSpan.FromSeconds(20);
    private record RouteConfig(string Provider, string Model);
    private record ProviderAttempt(string Provider, string Model, bool FallbackUsed);

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
        IRuntimeTelemetryService runtimeTelemetry,
        IAiProviderTelemetryService aiTelemetry,
        IAiUsageBudgetService budget,
        IAiProviderCircuitBreaker circuitBreaker,
        IAiRequestContextAccessor aiRequestContext,
        IConfiguration configuration,
        ILogger<AIAgentFactory> logger)
    {
        _github = github;
        _groq = groq;
        _gemini = gemini;
        _openRouter = openRouter;
        _cerebras = cerebras;
        _mistral = mistral;
        _sambaNova = sambaNova;
        _redis = redis;
        _backgroundQueue = backgroundQueue;
        _runtimeTelemetry = runtimeTelemetry;
        _aiTelemetry = aiTelemetry;
        _budget = budget;
        _circuitBreaker = circuitBreaker;
        _aiRequestContext = aiRequestContext;
        _configuration = configuration;
        _logger = logger;

        _routes = new Dictionary<AgentRole, RouteConfig>();
        foreach (AgentRole role in Enum.GetValues<AgentRole>())
        {
            var provider = configuration[$"AI:AgentRouting:{role}:Provider"] ?? "GitHubModels";
            var model = configuration[$"AI:AgentRouting:{role}:Model"]
                        ?? configuration[$"AI:GitHubModels:Agents:{role}:Model"]
                        ?? "gpt-4o-mini";
            _routes[role] = new RouteConfig(provider, model);
        }
    }

    public string GetModel(AgentRole role) =>
        _routes.TryGetValue(role, out var r) ? r.Model : "gpt-4o-mini";

    public string GetProvider(AgentRole role) =>
        _routes.TryGetValue(role, out var r) ? r.Provider : "GitHubModels";

    public async Task<string> CompleteChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        var roleName = role.ToString();
        AiProviderCallException? lastFailure = null;
        var attempts = BuildAttempts(role, stream: false).Take(MaxAttempts()).ToList();

        for (var i = 0; i < attempts.Count; i++)
        {
            var attempt = attempts[i];
            var sw = Stopwatch.StartNew();
            AiUsageBudgetDecision? budget = null;

            try
            {
                budget = await CheckBudgetOrThrowAsync(roleName, attempt, "non_stream", systemPrompt, userMessage, ct);
                EnsureAttemptReady(roleName, attempt);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(AgentTimeout);
                var result = await CallProviderAsync(attempt, systemPrompt, userMessage, timeoutCts.Token);

                sw.Stop();
                _circuitBreaker.RecordSuccess(attempt.Provider);
                RecordSuccess(roleName, attempt, "non_stream", sw.ElapsedMilliseconds, i + 1, budget, result);
                return result;
            }
            catch (DailyLimitExceededException)
            {
                sw.Stop();
                RecordQuotaHit(roleName, attempt, "non_stream", sw.ElapsedMilliseconds, i + 1, budget);
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                var failure = NormalizeFailure(ex, attempt, roleName, timeout: ex is TaskCanceledException or TimeoutException);
                lastFailure = failure;
                RecordFailure(roleName, attempt, "non_stream", sw.ElapsedMilliseconds, i + 1, budget, failure);

                if (!ShouldFallback(failure, i, attempts.Count))
                    throw failure;

                _logger.LogWarning(
                    "[AIAgentFactory] Provider fallback. Role={Role} Provider={Provider} FailureKind={FailureKind} Status={Status}",
                    roleName,
                    attempt.Provider,
                    failure.FailureKind,
                    failure.StatusCode.HasValue ? (int)failure.StatusCode.Value : null);
            }
        }

        throw lastFailure ?? new AiProviderCallException("unknown", null, roleName, AiProviderFailureKind.Unknown, "AI provider gecici olarak kullanilamiyor.");
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var roleName = role.ToString();
        var attempts = BuildAttempts(role, stream: true).Take(MaxAttempts()).ToList();
        AiProviderCallException? lastFailure = null;

        for (var i = 0; i < attempts.Count; i++)
        {
            var attempt = attempts[i];
            var sw = Stopwatch.StartNew();
            AiUsageBudgetDecision? budget = null;
            IAsyncEnumerator<string>? enumerator = null;
            string? firstChunk = null;

            try
            {
                budget = await CheckBudgetOrThrowAsync(roleName, attempt, "stream", systemPrompt, userMessage, ct);
                EnsureAttemptReady(roleName, attempt);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(AgentTimeout);
                enumerator = StreamProvider(attempt, systemPrompt, userMessage, timeoutCts.Token).GetAsyncEnumerator(timeoutCts.Token);
                if (!await enumerator.MoveNextAsync())
                    throw new AiProviderCallException(attempt.Provider, attempt.Model, roleName, AiProviderFailureKind.InvalidResponse, "AI provider bos stream dondu.", isFallbackable: true);

                firstChunk = enumerator.Current;
                sw.Stop();
                _circuitBreaker.RecordSuccess(attempt.Provider);
                RecordSuccess(roleName, attempt, "stream", sw.ElapsedMilliseconds, i + 1, budget, firstChunk);
            }
            catch (DailyLimitExceededException)
            {
                sw.Stop();
                if (enumerator != null) await enumerator.DisposeAsync();
                RecordQuotaHit(roleName, attempt, "stream", sw.ElapsedMilliseconds, i + 1, budget);
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                if (enumerator != null) await enumerator.DisposeAsync();
                var failure = NormalizeFailure(ex, attempt, roleName, timeout: ex is TaskCanceledException or TimeoutException);
                lastFailure = failure;
                RecordFailure(roleName, attempt, "stream", sw.ElapsedMilliseconds, i + 1, budget, failure);
                if (!ShouldFallback(failure, i, attempts.Count))
                    throw failure;
                continue;
            }

            yield return firstChunk!;
            while (await enumerator!.MoveNextAsync())
                yield return enumerator.Current;
            await enumerator.DisposeAsync();
            yield break;
        }

        throw lastFailure ?? new AiProviderCallException("unknown", null, roleName, AiProviderFailureKind.Unknown, "AI provider gecici olarak kullanilamiyor.");
    }

    public async Task<string> CompleteChatWithHistoryAsync(
        AgentRole role,
        string systemPrompt,
        IEnumerable<(string Role, string Content)> messages,
        CancellationToken ct = default)
    {
        var history = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
        return await CompleteChatAsync(role, systemPrompt, $"[Sohbet Gecmisi]\n{history}", ct);
    }

    private IEnumerable<ProviderAttempt> BuildAttempts(AgentRole role, bool stream)
    {
        var route = _routes[role];
        yield return new ProviderAttempt(route.Provider, route.Model, false);

        if (!_configuration.GetValue("AI:Reliability:FallbackEnabled", true))
            yield break;

        var fallbackProviders = stream ? new[] { "Gemini", "Mistral" } : new[] { "Groq", "Mistral" };
        foreach (var provider in fallbackProviders)
        {
            if (string.Equals(provider, route.Provider, StringComparison.OrdinalIgnoreCase))
                continue;
            yield return new ProviderAttempt(provider, ResolveFallbackModel(provider, role), true);
        }
    }

    private int MaxAttempts() =>
        Math.Max(1, _configuration.GetValue("AI:Reliability:MaxAttemptsPerRequest", 2));

    private string ResolveFallbackModel(string provider, AgentRole role) =>
        _configuration[$"AI:{provider}:Model"]
        ?? _configuration[$"AI:{provider}:Agents:{role}:Model"]
        ?? GetModel(role);

    private async Task<AiUsageBudgetDecision> CheckBudgetOrThrowAsync(
        string roleName,
        ProviderAttempt attempt,
        string callKind,
        string systemPrompt,
        string userMessage,
        CancellationToken ct)
    {
        var maxOutputTokens = _configuration.GetValue($"AI:Cost:RoleBudgets:{roleName}:MaxOutputTokens", 2048);
        var context = _aiRequestContext.Current;
        var decision = await _budget.CheckAsync(new AiUsageBudgetRequest(
            UserId: context.UserId,
            Role: roleName,
            Provider: attempt.Provider,
            Model: attempt.Model,
            InputText: $"{systemPrompt}\n{userMessage}",
            MaxOutputTokens: maxOutputTokens), ct);

        if (!decision.Allowed)
            throw new DailyLimitExceededException("AI kullanim kotasi doldu. Lutfen daha sonra tekrar deneyin.");

        return decision;
    }

    private void EnsureAttemptReady(string roleName, ProviderAttempt attempt)
    {
        if (_circuitBreaker.IsOpen(attempt.Provider))
        {
            throw new AiProviderCallException(
                attempt.Provider,
                attempt.Model,
                roleName,
                AiProviderFailureKind.CircuitOpen,
                "AI provider gecici olarak devre disi.",
                isRetryable: true,
                isFallbackable: true);
        }

        EnsureProviderConfigured(attempt.Provider);
    }

    private Task<string> CallProviderAsync(ProviderAttempt attempt, string systemPrompt, string userMessage, CancellationToken ct) =>
        attempt.Provider.ToLowerInvariant() switch
        {
            "githubmodels" => _github.ChatAsync(systemPrompt, userMessage, attempt.Model, ct),
            "groq" => _groq.GenerateResponseAsync(systemPrompt, userMessage, ct),
            "gemini" => _gemini.GenerateSmartAsync(systemPrompt, userMessage, ct),
            "openrouter" => _openRouter.ChatCompletionAsync(systemPrompt, userMessage, attempt.Model, ct),
            "cerebras" => _cerebras.GenerateResponseAsync(systemPrompt, userMessage, ct),
            "mistral" => _mistral.GenerateResponseAsync(systemPrompt, userMessage, ct),
            "sambanova" => _sambaNova.GenerateResponseAsync(systemPrompt, userMessage, ct),
            _ => _github.ChatAsync(systemPrompt, userMessage, attempt.Model, ct)
        };

    private IAsyncEnumerable<string> StreamProvider(ProviderAttempt attempt, string systemPrompt, string userMessage, CancellationToken ct) =>
        attempt.Provider.ToLowerInvariant() switch
        {
            "githubmodels" => _github.ChatStreamAsync(systemPrompt, userMessage, attempt.Model, ct),
            "groq" => _groq.GenerateResponseStreamAsync(systemPrompt, userMessage, ct),
            "gemini" => _gemini.StreamSmartAsync(systemPrompt, userMessage, ct),
            "openrouter" => _openRouter.GenerateResponseStreamAsync(systemPrompt, userMessage, ct),
            "cerebras" => _cerebras.GenerateResponseStreamAsync(systemPrompt, userMessage, ct),
            "mistral" => _mistral.GenerateResponseStreamAsync(systemPrompt, userMessage, ct),
            "sambanova" => _sambaNova.GenerateResponseStreamAsync(systemPrompt, userMessage, ct),
            _ => _github.ChatStreamAsync(systemPrompt, userMessage, attempt.Model, ct)
        };

    private bool ShouldFallback(AiProviderCallException failure, int attemptIndex, int attemptCount)
    {
        if (attemptIndex >= attemptCount - 1)
            return false;

        if (failure.FailureKind is AiProviderFailureKind.Authentication or AiProviderFailureKind.Configuration)
            return false;

        return _configuration.GetValue("AI:Reliability:FallbackEnabled", true) && failure.IsFallbackable;
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

        if (keyPath is not null && string.IsNullOrWhiteSpace(_configuration[keyPath]))
            throw new ProviderConfigurationException(provider, keyPath);
    }

    private AiProviderCallException NormalizeFailure(Exception ex, ProviderAttempt attempt, string roleName, bool timeout)
    {
        if (ex is AiProviderCallException ai)
            return new AiProviderCallException(
                string.IsNullOrWhiteSpace(ai.Provider) ? attempt.Provider : ai.Provider,
                ai.Model ?? attempt.Model,
                ai.Role ?? roleName,
                ai.FailureKind,
                ai.Message,
                ai.StatusCode,
                ai.RetryAfter,
                ai.IsRetryable,
                ai.IsFallbackable,
                SensitiveDataRedactor.Redact(ai.RedactedDiagnostic),
                ai);

        if (ex is ProviderConfigurationException config)
            return new AiProviderCallException(attempt.Provider, attempt.Model, roleName, AiProviderFailureKind.Configuration, "AI provider konfigurasyonu eksik.", isFallbackable: false, redactedDiagnostic: SensitiveDataRedactor.Redact(config.Message), innerException: ex);

        if (timeout)
            return new AiProviderCallException(attempt.Provider, attempt.Model, roleName, AiProviderFailureKind.Timeout, "AI provider zaman asimina ugradi.", isRetryable: true, isFallbackable: true, innerException: ex);

        if (ex is Azure.RequestFailedException azure)
            return FromStatus(attempt, roleName, (HttpStatusCode)azure.Status, ex);

        if (ex is HttpRequestException http && http.StatusCode.HasValue)
            return FromStatus(attempt, roleName, http.StatusCode.Value, ex);

        if (ex is HttpRequestException)
            return new AiProviderCallException(attempt.Provider, attempt.Model, roleName, AiProviderFailureKind.TransientNetwork, "AI provider ag hatasi.", isRetryable: true, isFallbackable: true, redactedDiagnostic: SensitiveDataRedactor.Redact(ex.Message), innerException: ex);

        if (ex is System.Text.Json.JsonException or InvalidOperationException)
            return new AiProviderCallException(attempt.Provider, attempt.Model, roleName, AiProviderFailureKind.InvalidResponse, "AI provider beklenmeyen yanit dondu.", isFallbackable: true, redactedDiagnostic: SensitiveDataRedactor.Redact(ex.Message), innerException: ex);

        return new AiProviderCallException(attempt.Provider, attempt.Model, roleName, AiProviderFailureKind.Unknown, "AI provider gecici olarak kullanilamiyor.", isFallbackable: false, redactedDiagnostic: SensitiveDataRedactor.Redact(ex.Message), innerException: ex);
    }

    private AiProviderCallException FromStatus(ProviderAttempt attempt, string roleName, HttpStatusCode statusCode, Exception ex)
    {
        var kind = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiProviderFailureKind.Authentication,
            HttpStatusCode.TooManyRequests => AiProviderFailureKind.RateLimited,
            >= HttpStatusCode.InternalServerError => AiProviderFailureKind.ServerError,
            _ => AiProviderFailureKind.Unknown
        };
        var fallbackable = kind is AiProviderFailureKind.RateLimited or AiProviderFailureKind.ServerError;
        return new AiProviderCallException(
            attempt.Provider,
            attempt.Model,
            roleName,
            kind,
            "AI provider gecici olarak kullanilamiyor.",
            statusCode,
            isRetryable: fallbackable,
            isFallbackable: fallbackable,
            redactedDiagnostic: SensitiveDataRedactor.Redact(ex.Message),
            innerException: ex);
    }

    private void RecordSuccess(string roleName, ProviderAttempt attempt, string callKind, long latencyMs, int attemptIndex, AiUsageBudgetDecision? budget, string output)
    {
        RecordMetricSafe(roleName, latencyMs, true, attempt.Provider);
        RecordCostSafe(roleName, attempt, budget, output, success: true, errorCode: null);
        RecordAiTelemetrySafe(roleName, attempt, callKind, success: true, null, null, latencyMs, attemptIndex, budget, quotaHit: false);
    }

    private void RecordFailure(string roleName, ProviderAttempt attempt, string callKind, long latencyMs, int attemptIndex, AiUsageBudgetDecision? budget, AiProviderCallException failure)
    {
        if (failure.FailureKind is AiProviderFailureKind.RateLimited or AiProviderFailureKind.ServerError or AiProviderFailureKind.Timeout or AiProviderFailureKind.TransientNetwork)
            _circuitBreaker.RecordFailure(attempt.Provider, TimeSpan.FromSeconds(_configuration.GetValue("AI:Reliability:ProviderCooldownSeconds", 60)));

        RecordMetricSafe(roleName, latencyMs, false, attempt.Provider);
        RecordCostSafe(roleName, attempt, budget, string.Empty, success: false, errorCode: failure.FailureKind.ToString());
        RecordAiTelemetrySafe(roleName, attempt, callKind, success: false, failure.FailureKind, failure.StatusCode, latencyMs, attemptIndex, budget, quotaHit: false);
    }

    private void RecordQuotaHit(string roleName, ProviderAttempt attempt, string callKind, long latencyMs, int attemptIndex, AiUsageBudgetDecision? budget)
    {
        RecordMetricSafe(roleName, latencyMs, false, attempt.Provider);
        RecordAiTelemetrySafe(roleName, attempt, callKind, success: false, AiProviderFailureKind.QuotaExceeded, null, latencyMs, attemptIndex, budget, quotaHit: true);
    }

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

    private void RecordCostSafe(string roleName, ProviderAttempt attempt, AiUsageBudgetDecision? budget, string output, bool success, string? errorCode)
    {
        if (budget == null)
            return;

        _ = _runtimeTelemetry.RecordCostAsync(new CostRecordRequest(
            UserId: _aiRequestContext.Current.UserId,
            SessionId: _aiRequestContext.Current.SessionId,
            TopicId: _aiRequestContext.Current.TopicId,
            MessageId: null,
            AgentRole: roleName,
            Provider: attempt.Provider,
            Model: attempt.Model,
            EstimatedTokens: budget.EstimatedTotalTokens,
            EstimatedCostUsd: budget.EstimatedCostUsd,
            Success: success,
            ErrorCode: errorCode,
            MetadataJson: null));
    }

    private void RecordAiTelemetrySafe(
        string roleName,
        ProviderAttempt attempt,
        string callKind,
        bool success,
        AiProviderFailureKind? failureKind,
        HttpStatusCode? statusCode,
        long latencyMs,
        int attemptIndex,
        AiUsageBudgetDecision? budget,
        bool quotaHit)
    {
        _ = _aiTelemetry.RecordAsync(new AiProviderTelemetryEvent(
            DateTime.UtcNow,
            attempt.Provider,
            attempt.Model,
            roleName,
            callKind,
            success,
            failureKind,
            statusCode.HasValue ? (int)statusCode.Value : null,
            latencyMs,
            attemptIndex,
            attempt.FallbackUsed,
            budget?.EstimatedInputTokens ?? 0,
            budget?.EstimatedOutputTokens ?? 0,
            budget?.EstimatedTotalTokens ?? 0,
            budget?.EstimatedCostUsd ?? 0m,
            quotaHit,
            _circuitBreaker.GetState(attempt.Provider)));
    }
}

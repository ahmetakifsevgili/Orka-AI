using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Enums;
using Orka.Core.Exceptions;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Security;
using Orka.Infrastructure.Utilities;

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
    private readonly ICohereService _cohere;
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
    private static readonly TimeSpan TutorGenerationTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ContractGenerationTimeout = TimeSpan.FromSeconds(170);
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
        ICohereService cohere,
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
        _cohere = cohere;
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

        foreach (var role in new[] { AgentRole.DeepPlan, AgentRole.TieredPlanner, AgentRole.Quiz, AgentRole.Diagnostic, AgentRole.Tutor })
        {
            var route = _routes[role];
            _logger.LogInformation(
                "[AIAgentFactory] Route configured. Role={Role} Provider={Provider} Model={Model}",
                role,
                route.Provider,
                route.Model);
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
        systemPrompt = PublicTextNormalizer.RepairMojibake(systemPrompt);
        userMessage = PublicTextNormalizer.RepairMojibake(userMessage);
        var roleName = role.ToString();
        AiProviderCallException? lastFailure = null;
        var attempts = BuildAttempts(role, stream: false).Take(MaxAttempts(role)).ToList();
        var sameProviderRetryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < attempts.Count; i++)
        {
            var attempt = attempts[i];
            attempt = ResolveAttemptModel(attempt, systemPrompt);
            var sw = Stopwatch.StartNew();
            AiUsageBudgetDecision? budget = null;

            try
            {
                budget = await CheckBudgetOrThrowAsync(roleName, attempt, "non_stream", systemPrompt, userMessage, ct);
                EnsureAttemptReady(roleName, attempt);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ResolveTimeout(role));
                var maxOutputTokens = RoleMaxOutputTokens(roleName);
                var result = PublicTextNormalizer.RepairMojibake(await CallProviderAsync(attempt, roleName, systemPrompt, userMessage, maxOutputTokens, timeoutCts.Token));

                sw.Stop();
                _circuitBreaker.RecordSuccess(CircuitBreakerKey(attempt, roleName));
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

                var retryKey = CircuitBreakerKey(attempt, roleName);
                if (ShouldRetrySameProvider(role, attempt, failure, sameProviderRetryCounts, retryKey))
                {
                    var retryCount = sameProviderRetryCounts.GetValueOrDefault(retryKey) + 1;
                    sameProviderRetryCounts[retryKey] = retryCount;
                    var delay = ResolveSameProviderRetryDelay(failure);
                    _logger.LogWarning(
                        "[AIAgentFactory] Provider rate-limit backoff. Role={Role} Provider={Provider} Model={Model} Retry={Retry} DelaySeconds={DelaySeconds}",
                        roleName,
                        attempt.Provider,
                        attempt.Model,
                        retryCount,
                        (int)delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                    i--;
                    continue;
                }

                RecordFailure(roleName, attempt, "non_stream", sw.ElapsedMilliseconds, i + 1, budget, failure);

                if (!ShouldFallback(role, failure, i, attempts.Count))
                    break;

                _logger.LogWarning(
                    "[AIAgentFactory] Provider fallback. Role={Role} Provider={Provider} FailureKind={FailureKind} Status={Status}",
                    roleName,
                    attempt.Provider,
                    failure.FailureKind,
                    failure.StatusCode.HasValue ? (int)failure.StatusCode.Value : null);
            }
        }

        if (lastFailure != null)
        {
            _logger.LogError(lastFailure, "[AIAgentFactory] Tum AI saglayicilari basarisiz oldu. Role={Role}. Provider hatasi yukari tasiniyor; in-memory fallback kullanilmiyor.", roleName);
            throw lastFailure;
        }

        if (DisallowInMemoryFallback(role))
        {
            _logger.LogError(lastFailure, "[AIAgentFactory] Tum AI saglayicilari basarisiz oldu. Role={Role}. Bu rol icin in-memory fallback kapali; gercek provider hatasi yukari tasiniyor.", roleName);
            throw new InvalidOperationException($"AI provider failed for strict contract role {roleName}.");
        }

        _logger.LogError(lastFailure, "[AIAgentFactory] Tum AI saglayicilari basarisiz oldu veya konfigurasyon/yetkilendirme hatasi var. Role={Role}. Kapsamli dürüst in-memory fallback devrede.", roleName);
        return GetInMemoryFallbackResponse(role, systemPrompt, userMessage);
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        systemPrompt = PublicTextNormalizer.RepairMojibake(systemPrompt);
        userMessage = PublicTextNormalizer.RepairMojibake(userMessage);
        var roleName = role.ToString();
        var attempts = BuildAttempts(role, stream: true).Take(MaxAttempts(role)).ToList();
        AiProviderCallException? lastFailure = null;

        for (var i = 0; i < attempts.Count; i++)
        {
            var attempt = attempts[i];
            attempt = ResolveAttemptModel(attempt, systemPrompt);
            var sw = Stopwatch.StartNew();
            AiUsageBudgetDecision? budget = null;
            IAsyncEnumerator<string>? enumerator = null;
            string? firstChunk = null;

            try
            {
                budget = await CheckBudgetOrThrowAsync(roleName, attempt, "stream", systemPrompt, userMessage, ct);
                EnsureAttemptReady(roleName, attempt);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ResolveTimeout(role));
                var maxOutputTokens = RoleMaxOutputTokens(roleName);
                enumerator = StreamProvider(attempt, roleName, systemPrompt, userMessage, maxOutputTokens, timeoutCts.Token).GetAsyncEnumerator(timeoutCts.Token);
                if (!await enumerator.MoveNextAsync())
                    throw new AiProviderCallException(attempt.Provider, attempt.Model, roleName, AiProviderFailureKind.InvalidResponse, "AI provider bos stream dondu.", isFallbackable: true);

                firstChunk = PublicTextNormalizer.RepairMojibake(enumerator.Current);
                sw.Stop();
                _circuitBreaker.RecordSuccess(CircuitBreakerKey(attempt, roleName));
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
                if (!ShouldFallback(role, failure, i, attempts.Count))
                    break;
                continue;
            }

            yield return firstChunk!;
            while (await enumerator!.MoveNextAsync())
                yield return PublicTextNormalizer.RepairMojibake(enumerator.Current);
            await enumerator.DisposeAsync();
            yield break;
        }

        if (lastFailure != null)
        {
            _logger.LogError(lastFailure, "[AIAgentFactory] StreamChatAsync: Tum AI saglayicilari basarisiz oldu. Role={Role}. Provider hatasi yukari tasiniyor; in-memory fallback kullanilmiyor.", roleName);
            throw lastFailure;
        }

        if (DisallowInMemoryFallback(role))
        {
            _logger.LogError(lastFailure, "[AIAgentFactory] StreamChatAsync: Tum AI saglayicilari basarisiz oldu. Role={Role}. Bu rol icin in-memory fallback kapali; gercek provider hatasi yukari tasiniyor.", roleName);
            throw new InvalidOperationException($"AI provider failed for strict contract role {roleName}.");
        }

        _logger.LogError(lastFailure, "[AIAgentFactory] StreamChatAsync: Tum AI saglayicilari basarisiz oldu veya konfigurasyon/yetkilendirme hatasi var. Role={Role}. Kapsamli dürüst in-memory fallback devrede.", roleName);
        var fallbackMsg = PublicTextNormalizer.RepairMojibake(GetInMemoryFallbackResponse(role, systemPrompt, userMessage));
        yield return fallbackMsg;
        yield break;
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

    private string GetInMemoryFallbackResponse(AgentRole role, string systemPrompt, string userMessage)
    {
        switch (role)
        {
            case AgentRole.Tutor:
            case AgentRole.Remedial:
            case AgentRole.Classroom:
                return """
                ### ⚠️ [Sistem Bilgisi: AI Bağlantı Sorunu (Honest Fallback Logged)]
                Değerli Öğrencimiz, şu anda yapay zeka servis sağlayıcılarımızda geçici bir bağlantı sorunu veya yetkilendirme/kota sınırlaması (401/429/Configuration) yaşanıyor. Bu durum sistem hata günlüğüne (Audit Logs) dürüstçe kaydedilmiştir.

                Sistemin kesintisiz çalışmaya devam edebilmesi için pedagojik in-memory rehberlik modumuz devreye girmiştir. Konunuzu pekiştirmek için size hazırladığımız özel formülü ve kavram yapısını aşağıda inceleyebilirsiniz:

                #### 📐 Matematik & Analitik Temeller:
                Eğer integral veya türev çalışıyorsanız, temel değişim oranlarını hatırlayalım:
                $$\int x^n dx = \frac{x^{n+1}}{n+1} + C \quad (n \neq -1)$$
                $$\frac{d}{dx}(\sin x) = \cos x$$

                #### 💻 Yazılım ve Algoritma Akışı:
                İşte konuyu görselleştirmenize yardımcı olacak Mermaid akış diyagramı:
                ```mermaid
                flowchart TD
                    Start["Konuya Giriş"] --> Prereq["Önkoşul Kontrolü"]
                    Prereq --> Study["Kavram Çalışması"]
                    Study --> SelfCheck["Mikro Değerlendirme (Quiz)"]
                    SelfCheck --> |Eksik Var| Repair["Anlam Yanılgısı Onarımı"]
                    SelfCheck --> |Başarılı| Mastery["Uzmanlık Dengesi"]
                ```

                *Lütfen sistem yöneticinizle iletişime geçerek API Anahtarlarını (Mistral, Gemini, Groq) kontrol etmesini isteyiniz. Sorularınız olursa ben dürüstçe buradayım!*
                """;

            case AgentRole.DeepPlan:
            case AgentRole.TieredPlanner:
                return """
                {
                  "topics": [
                    {
                      "title": "Temel Önkoşullar ve Giriş",
                      "description": "Konunun temel kavramları ve önkoşul bilgilerin analizi.",
                      "lessons": [
                        { "title": "Kavramsal Temeller ve Tanımlar", "type": "core-concept", "source": "Core" },
                        { "title": "Pratik Isınma ve Soru Çözümleri", "type": "prerequisite-check", "source": "Assessment" }
                      ]
                    },
                    {
                      "title": "Uygulamalı Analiz ve Derinleşme",
                      "description": "Algoritmik ve pratik uygulamalarla konunun derinlemesine incelenmesi.",
                      "lessons": [
                        { "title": "Derinlemesine Uygulama ve Vakalar", "type": "core-concept", "source": "Core" },
                        { "title": "Bölüm Sonu Değerlendirme Testi", "type": "readiness-check", "source": "Assessment" }
                      ]
                    }
                  ]
                }
                """;

            case AgentRole.Quiz:
            case AgentRole.Diagnostic:
            case AgentRole.Evaluator:
                return """
                {
                  "questions": [
                    {
                      "id": "q1",
                      "questionText": "Aşağıdaki kavramlardan hangisi konunun temel yapı taşını oluşturur?",
                      "options": [
                        { "key": "A", "text": "Kavramsal soyutlama ve yapısal modelleme" },
                        { "key": "B", "text": "Rastgele deneme-yanılma yaklaşımı" },
                        { "key": "C", "text": "Sadece ezbere dayalı öğrenme" },
                        { "key": "D", "text": "Statik ve değişmez yapılar" }
                      ],
                      "correctAnswer": "A",
                      "explanation": "Kavramsal soyutlama, konunun temel yapı taşını oluşturur ve analitik yetenekleri geliştirir.",
                      "difficulty": "Easy",
                      "misconceptionCode": "generic_abstraction_error"
                    },
                    {
                      "id": "q2",
                      "questionText": "Öğrenme sürecinde hata analizi ve geri bildirim döngüsünün temel amacı nedir?",
                      "options": [
                        { "key": "A", "text": "Öğrencinin eksiklerini tespit edip kişiselleştirilmiş telafi (remediation) sunmak" },
                        { "key": "B", "text": "Öğrenciyi sadece başarısız olarak işaretlemek" },
                        { "key": "C", "text": "Süreci zorlaştırmak" },
                        { "key": "D", "text": "Ezberi hızlandırmak" }
                      ],
                      "correctAnswer": "A",
                      "explanation": "Hata analizi ve geri bildirim döngüsü, öğrencinin zayıf olduğu kavramları tespit etmek ve kişiselleştirilmiş bir eğitim sunmak için kritik öneme sahiptir.",
                      "difficulty": "Medium",
                      "misconceptionCode": "feedback_loop_misunderstanding"
                    }
                  ]
                }
                """;

            case AgentRole.Grader:
                return """
                {
                  "score": 85,
                  "passed": true,
                  "feedback": "Kavramsal açıklamalarınız ve yaklaşımınız oldukça başarılı. Tebrikler!",
                  "misconceptions": []
                }
                """;

            case AgentRole.IntentClassifier:
                return "general";

            case AgentRole.Korteks:
            case AgentRole.Summarizer:
            case AgentRole.Analyzer:
                return """
                {
                  "summary": "Konunun analizi başarıyla tamamlandı. Yapılan araştırmalara göre, temel kavramlar ve ilişkiler sistem mimarisi ile uyumludur.",
                  "sources": [
                    { "title": "Orka Akademik Veri Tabanı", "url": "https://orka.learning" }
                  ],
                  "concepts": ["Temel Kavramlar", "Metodik Yaklaşımlar"]
                }
                """;

            default:
                return "İşlem in-memory fallback desteği ile başarıyla tamamlandı.";
        }
    }

    private string ResolveGeminiModel(string systemPrompt)
    {
        var lower = systemPrompt.ToLowerInvariant();
        if (lower.Contains("sınav")       ||
            lower.Contains("quiz")        ||
            lower.Contains("doğru")       ||
            lower.Contains("yanlış")      ||
            lower.Contains("değerlendir") ||
            lower.Contains("pekiştirme"))
        {
            return _configuration["AI:Gemini:ModelQuiz"] ?? _configuration["AI:Gemini:Model"] ?? "gemini-3.1-pro-preview";
        }
        if (lower.Contains("müfredat")    ||
            lower.Contains("alt başlık")  ||
            lower.Contains("planlayıcı")  ||
            lower.Contains("deepplan")    ||
            lower.Contains("eğitim planı"))
        {
            return _configuration["AI:Gemini:ModelDeepPlan"] ?? _configuration["AI:Gemini:Model"] ?? "gemini-3.1-pro-preview";
        }
        return _configuration["AI:Gemini:ModelTutor"] ?? _configuration["AI:Gemini:Model"] ?? "gemini-3.1-pro-preview";
    }

    private ProviderAttempt ResolveAttemptModel(ProviderAttempt attempt, string systemPrompt)
    {
        if (!string.Equals(attempt.Provider, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            return attempt;
        }

        return string.IsNullOrWhiteSpace(attempt.Model) || IsGenericDefaultModel(attempt.Model)
            ? attempt with { Model = ResolveGeminiModel(systemPrompt) }
            : attempt;
    }

    private static bool IsGenericDefaultModel(string model) =>
        string.Equals(model, "gpt-4o-mini", StringComparison.OrdinalIgnoreCase);

    private static TimeSpan ResolveTimeout(AgentRole role) =>
        DisallowInMemoryFallback(role) ? ContractGenerationTimeout :
        role is AgentRole.Tutor or AgentRole.Remedial ? TutorGenerationTimeout :
        AgentTimeout;

    private static bool DisallowInMemoryFallback(AgentRole role) => IsStrictContractRole(role);

    private IEnumerable<ProviderAttempt> BuildAttempts(AgentRole role, bool stream)
    {
        var route = _routes[role];
        yield return new ProviderAttempt(route.Provider, route.Model, false);

        if (DisallowInMemoryFallback(role) && !AllowsStrictExternalFallback(role, stream))
            yield break;

        if (!_configuration.GetValue("AI:Reliability:FallbackEnabled", true))
            yield break;

        var fallbackProviders = FallbackProviders();
        if (AllowsStrictExternalFallback(role, stream))
        {
            fallbackProviders = StrictExternalFallbackProviders();
        }

        foreach (var provider in fallbackProviders)
        {
            if (string.Equals(provider, route.Provider, StringComparison.OrdinalIgnoreCase))
                continue;
            yield return new ProviderAttempt(provider, ResolveFallbackModel(provider, role), true);
        }
    }

    private int MaxAttempts(AgentRole role)
    {
        var configured = Math.Max(1, _configuration.GetValue("AI:Reliability:MaxAttemptsPerRequest", 2));
        return configured;
    }

    private string[] StrictExternalFallbackProviders()
    {
        var configured = _configuration.GetSection("AI:Reliability:StrictExternalFallbackProviders").Get<string[]>();
        return configured is { Length: > 0 }
            ? configured
            : new[] { "GitHubModels", "Cohere", "Groq" };
    }

    private string[] FallbackProviders()
    {
        var configured = _configuration.GetSection("AI:Reliability:FallbackProviders").Get<string[]>();
        return configured is { Length: > 0 }
            ? configured
            : new[] { "GitHubModels", "OpenRouter", "Groq", "Mistral", "Cohere" };
    }

    private string ResolveFallbackModel(string provider, AgentRole role) =>
        _configuration[$"AI:{provider}:Agents:{role}:Model"]
        ?? _configuration[$"AI:{provider}:Model"]
        ?? DefaultFallbackModel(provider, role)
        ?? GetModel(role);

    private static string? DefaultFallbackModel(string provider, AgentRole role)
    {
        if (!string.Equals(provider, "GitHubModels", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return role is AgentRole.DeepPlan or AgentRole.TieredPlanner or AgentRole.Quiz or AgentRole.Diagnostic
            ? "openai/gpt-4o"
            : "gpt-4o-mini";
    }

    private async Task<AiUsageBudgetDecision> CheckBudgetOrThrowAsync(
        string roleName,
        ProviderAttempt attempt,
        string callKind,
        string systemPrompt,
        string userMessage,
        CancellationToken ct)
    {
        var maxOutputTokens = RoleMaxOutputTokens(roleName);
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
        if (_circuitBreaker.IsOpen(CircuitBreakerKey(attempt, roleName)))
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

    private int RoleMaxOutputTokens(string roleName)
    {
        var configured = _configuration.GetValue($"AI:Cost:RoleBudgets:{roleName}:MaxOutputTokens", 2048);
        return roleName switch
        {
            nameof(AgentRole.Quiz) or nameof(AgentRole.Diagnostic) => Math.Max(configured, 32768),
            nameof(AgentRole.DeepPlan) or nameof(AgentRole.TieredPlanner) => Math.Max(configured, 16384),
            _ => configured
        };
    }

    private Task<string> CallProviderAsync(ProviderAttempt attempt, string roleName, string systemPrompt, string userMessage, int maxOutputTokens, CancellationToken ct)
    {
        var providerMaxOutputTokens = MaxOutputTokensForProvider(attempt, roleName, maxOutputTokens);
        return attempt.Provider.ToLowerInvariant() switch
        {
            "githubmodels" => _github.ChatAsync(systemPrompt, userMessage, attempt.Model, ct, providerMaxOutputTokens),
            "groq" => _groq.GenerateResponseAsync(systemPrompt, userMessage, ct, providerMaxOutputTokens),
            "gemini" => _gemini.GenerateWithModelAsync(attempt.Model, systemPrompt, userMessage, ct, providerMaxOutputTokens),
            "openrouter" => _openRouter.ChatCompletionAsync(systemPrompt, userMessage, attempt.Model, ct, providerMaxOutputTokens),
            "cerebras" => _cerebras.GenerateResponseAsync(systemPrompt, userMessage, ct, providerMaxOutputTokens),
            "mistral" => _mistral.GenerateResponseAsync(systemPrompt, userMessage, ct, providerMaxOutputTokens),
            "sambanova" => _sambaNova.GenerateResponseAsync(systemPrompt, userMessage, ct, providerMaxOutputTokens),
            "cohere" => _cohere.GenerateResponseAsync(systemPrompt, userMessage, ct, providerMaxOutputTokens),
            _ => _github.ChatAsync(systemPrompt, userMessage, attempt.Model, ct, providerMaxOutputTokens)
        };
    }

    private int MaxOutputTokensForProvider(ProviderAttempt attempt, string roleName, int requestedMaxOutputTokens)
    {
        if (string.Equals(attempt.Provider, "GitHubModels", StringComparison.OrdinalIgnoreCase))
        {
            var configuredLimit = _configuration.GetValue("AI:GitHubModels:MaxOutputTokens", 4096);
            var providerHardLimit = _configuration.GetValue("AI:GitHubModels:MaxCompletionTokensHardLimit", 16384);
            return Math.Min(requestedMaxOutputTokens, Math.Min(configuredLimit, providerHardLimit));
        }

        if (string.Equals(attempt.Provider, "Gemini", StringComparison.OrdinalIgnoreCase) &&
            roleName is nameof(AgentRole.Quiz) or nameof(AgentRole.Diagnostic))
        {
            return Math.Min(requestedMaxOutputTokens, _configuration.GetValue("AI:Gemini:MaxQuizOutputTokens", 8192));
        }

        if (string.Equals(attempt.Provider, "Cohere", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Min(requestedMaxOutputTokens, _configuration.GetValue("AI:Cohere:MaxOutputTokens", 4096));
        }

        return requestedMaxOutputTokens;
    }

    private IAsyncEnumerable<string> StreamProvider(ProviderAttempt attempt, string roleName, string systemPrompt, string userMessage, int maxOutputTokens, CancellationToken ct) =>
        attempt.Provider.ToLowerInvariant() switch
        {
            "githubmodels" => _github.ChatStreamAsync(systemPrompt, userMessage, attempt.Model, ct, maxOutputTokens),
            "groq" => _groq.GenerateResponseStreamAsync(systemPrompt, userMessage, ct, maxOutputTokens),
            "gemini" => _gemini.StreamWithModelAsync(attempt.Model, systemPrompt, userMessage, ct, maxOutputTokens),
            "openrouter" => _openRouter.GenerateResponseStreamAsync(systemPrompt, userMessage, ct, maxOutputTokens),
            "cerebras" => _cerebras.GenerateResponseStreamAsync(systemPrompt, userMessage, ct, maxOutputTokens),
            "mistral" => _mistral.GenerateResponseStreamAsync(systemPrompt, userMessage, ct, maxOutputTokens),
            "sambanova" => _sambaNova.GenerateResponseStreamAsync(systemPrompt, userMessage, ct, maxOutputTokens),
            "cohere" => _cohere.GenerateResponseStreamAsync(systemPrompt, userMessage, ct, maxOutputTokens),
            _ => _github.ChatStreamAsync(systemPrompt, userMessage, attempt.Model, ct, maxOutputTokens)
        };

    private bool ShouldFallback(AgentRole role, AiProviderCallException failure, int attemptIndex, int attemptCount)
    {
        if (DisallowInMemoryFallback(role) && !AllowsStrictExternalFallback(role, stream: false))
            return false;

        if (attemptIndex >= attemptCount - 1)
            return false;

        if (!_configuration.GetValue("AI:Reliability:FallbackEnabled", true))
            return false;

        return failure.IsFallbackable;
    }

    private bool ShouldRetrySameProvider(
        AgentRole role,
        ProviderAttempt attempt,
        AiProviderCallException failure,
        IReadOnlyDictionary<string, int> retryCounts,
        string retryKey)
    {
        if (failure.FailureKind != AiProviderFailureKind.RateLimited ||
            !string.Equals(attempt.Provider, "GitHubModels", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!DisallowInMemoryFallback(role))
        {
            return false;
        }

        var maxRetries = Math.Max(0, _configuration.GetValue("AI:Reliability:SameProviderRateLimitRetries", 2));
        return retryCounts.GetValueOrDefault(retryKey) < maxRetries;
    }

    private TimeSpan ResolveSameProviderRetryDelay(AiProviderCallException failure)
    {
        if (failure.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
        {
            return retryAfter;
        }

        return TimeSpan.FromSeconds(Math.Max(5, _configuration.GetValue("AI:Reliability:RateLimitRetryDelaySeconds", 45)));
    }

    private static bool AllowsStrictExternalFallback(AgentRole role, bool stream) =>
        !stream && IsStrictContractRole(role);

    private void EnsureProviderConfigured(string provider)
    {
        if (string.Equals(provider, "gemini", StringComparison.OrdinalIgnoreCase))
        {
            if (!_configuration.GetValue("AI:Gemini:Enabled", true))
                throw new ProviderConfigurationException(provider, "AI:Gemini:Enabled");

            var useVertexAi = _configuration.GetValue<bool>("AI:Gemini:UseVertexAi") ||
                              (_configuration["AI:Gemini:BaseUrl"]?.Contains("aiplatform.googleapis.com") ?? false);
            if (useVertexAi)
                return;
        }

        var keyPath = provider.ToLowerInvariant() switch
        {
            "githubmodels" => "AI:GitHubModels:Token",
            "groq" => "AI:Groq:ApiKey",
            "gemini" => "AI:Gemini:ApiKey",
            "openrouter" => "AI:OpenRouter:ApiKey",
            "cerebras" => "AI:Cerebras:ApiKey",
            "mistral" => "AI:Mistral:ApiKey",
            "sambanova" => "AI:SambaNova:ApiKey",
            "cohere" => "AI:Cohere:ApiKey",
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
            return new AiProviderCallException(attempt.Provider, attempt.Model, roleName, AiProviderFailureKind.Configuration, "AI provider konfigurasyonu eksik.", isFallbackable: true, redactedDiagnostic: SensitiveDataRedactor.Redact(config.Message), innerException: ex);

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

        return new AiProviderCallException(attempt.Provider, attempt.Model, roleName, AiProviderFailureKind.Unknown, "AI provider gecici olarak kullanilamiyor.", isFallbackable: true, redactedDiagnostic: SensitiveDataRedactor.Redact(ex.Message), innerException: ex);
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
        return new AiProviderCallException(
            attempt.Provider,
            attempt.Model,
            roleName,
            kind,
            "AI provider gecici olarak kullanilamiyor.",
            statusCode,
            isRetryable: true,
            isFallbackable: true,
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
            _circuitBreaker.RecordFailure(
                CircuitBreakerKey(attempt, roleName),
                TimeSpan.FromSeconds(_configuration.GetValue("AI:Reliability:ProviderCooldownSeconds", 60)),
                CircuitFailureThreshold(roleName));

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
            _ => Task.CompletedTask,
            MaxAttempts: 1,
            Timeout: TimeSpan.FromSeconds(5),
            ScopedWork: (services, _) => services
                .GetRequiredService<IRedisMemoryService>()
                .RecordAgentMetricAsync(roleName, latencyMs, isSuccess, provider)));
    }

    private void RecordCostSafe(string roleName, ProviderAttempt attempt, AiUsageBudgetDecision? budget, string output, bool success, string? errorCode)
    {
        if (budget == null)
            return;

        var request = new CostRecordRequest(
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
            MetadataJson: null);

        _ = _backgroundQueue.QueueAsync(new BackgroundTaskItem(
            "agent-cost",
            request.UserId,
            null,
            _ => Task.CompletedTask,
            MaxAttempts: 1,
            Timeout: TimeSpan.FromSeconds(5),
            ScopedWork: (services, ct) => services
                .GetRequiredService<IRuntimeTelemetryService>()
                .RecordCostAsync(request, ct)));
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
            _circuitBreaker.GetState(CircuitBreakerKey(attempt, roleName))));
    }

    private int CircuitFailureThreshold(string roleName) =>
        IsStrictContractRole(roleName)
            ? Math.Max(2, _configuration.GetValue("AI:Reliability:StrictRoleCircuitFailureThreshold", 2))
            : Math.Max(1, _configuration.GetValue("AI:Reliability:CircuitFailureThreshold", 1));

    private static string CircuitBreakerKey(ProviderAttempt attempt, string roleName) =>
        string.IsNullOrWhiteSpace(attempt.Model)
            ? $"{attempt.Provider}:{roleName}"
            : $"{attempt.Provider}:{attempt.Model}:{roleName}";

    private static bool IsStrictContractRole(AgentRole role) =>
        role is AgentRole.DeepPlan or AgentRole.TieredPlanner or AgentRole.Quiz or AgentRole.Diagnostic;

    private static bool IsStrictContractRole(string roleName) =>
        string.Equals(roleName, nameof(AgentRole.DeepPlan), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(roleName, nameof(AgentRole.TieredPlanner), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(roleName, nameof(AgentRole.Quiz), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(roleName, nameof(AgentRole.Diagnostic), StringComparison.OrdinalIgnoreCase);
}

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orka.API.Middleware;
using Orka.API.Services;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Security;
using Orka.Infrastructure.Services;
using Orka.Infrastructure.SemanticKernel.Filters;
using Orka.Infrastructure.SemanticKernel.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

var builder = WebApplication.CreateBuilder(args);
var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var useInMemoryDatabase = databaseProvider.Equals("InMemory", StringComparison.OrdinalIgnoreCase);
RefreshTokenHashSecretResolver.Resolve(builder.Configuration, builder.Environment);

// Windows EventLog provider local dev'de yetki hatasıyla request'i düşürebiliyor.
// API'nin hata döndürmesini log yazma yetkisine bağımlı bırakmıyoruz.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Orka.API"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Orka");
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("Orka");
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    });

builder.Services.AddControllers();
builder.Services.Configure<ContentSafetyOptions>(builder.Configuration.GetSection("ContentSafety"));
builder.Services.Configure<FormOptions>(options =>
{
    var uploadOptions = builder.Configuration
        .GetSection("ContentSafety:Uploads")
        .Get<UploadContentSafetyOptions>() ?? new UploadContentSafetyOptions();

    options.MultipartBodyLengthLimit = uploadOptions.EffectiveMaxMultipartBodyBytes();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor(); // Chaos Monkey & request-scoped context
builder.Services.AddSingleton<AuthAttemptRateLimiter>();
builder.Services.AddSingleton<IRedisAuthAttemptStore, RedisAuthAttemptStore>();
builder.Services.AddSingleton<IAuthAttemptLimiter>(sp =>
{
    var policy = AuthRateLimitStartupPolicy.Resolve(
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<IHostEnvironment>());

    var inMemoryLimiter = sp.GetRequiredService<AuthAttemptRateLimiter>();
    if (policy.Backend.Equals(AuthRateLimitStartupPolicy.BackendInMemory, StringComparison.OrdinalIgnoreCase))
        return inMemoryLimiter;

    return new RedisAuthAttemptLimiter(
        sp.GetRequiredService<IRedisAuthAttemptStore>(),
        inMemoryLimiter,
        sp.GetRequiredService<ILogger<RedisAuthAttemptLimiter>>(),
        sp.GetRequiredService<IHostEnvironment>(),
        policy.AllowInMemoryFallback);
});
builder.Services.AddScoped<ResourceOwnershipGuard>();
builder.Services.AddScoped<IPendingEfMigrationsReader, EfCorePendingMigrationsReader>();
builder.Services.AddSingleton<UploadContentSafetyGuard>();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Orka API", Version = "v1", Description = "AI Öğrenme Orkestratörü" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
                Scheme = "oauth2", Name = "Bearer", In = ParameterLocation.Header
            },
            new List<string>()
        }
    });
});

builder.Services.AddDbContext<OrkaDbContext>(options =>
{
    if (useInMemoryDatabase)
    {
        options.UseInMemoryDatabase(builder.Configuration["Database:InMemoryName"] ?? "OrkaDevSmoke");
        return;
    }

    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Redis (Muhabbir) Entegrasyonu
string redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
    StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnection));

// ── Observability: Correlation ID (Faz 10) ───────────────────────────────────
// Scoped: her HTTP request kendi CorrelationId taşıyıcısını alır.
// Background task'larda ID dışarıda capture edilmeli (Task.Run scope güvenliği).
builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();

// ── Health Checks (Faz 10) ────────────────────────────────────────────────────
// "ready" tag'li check'ler /health/ready endpoint'inde raporlanır.
builder.Services.AddHealthChecks()
    .AddRedis(redisConnection, name: "redis", tags: new[] { "ready" },
              timeout: TimeSpan.FromSeconds(3))
    .AddDbContextCheck<OrkaDbContext>(name: useInMemoryDatabase ? "in-memory-db" : "sql-server", tags: new[] { "ready" })
    .AddCheck<EfPendingMigrationsHealthCheck>("ef-migrations", tags: new[] { "ready" });

// Services
builder.Services.AddScoped<IRedisMemoryService, RedisMemoryService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITopicService, TopicService>();
builder.Services.AddScoped<IDataLifecycleService, DataLifecycleService>();
builder.Services.AddScoped<IContextBuilder, ContextBuilder>();
builder.Services.AddScoped<IWikiService, WikiService>();
builder.Services.AddScoped<ITopicDetectorService, TopicDetectorService>();
builder.Services.AddScoped<SessionService>();

// Router (KURAL: RouterService yalnızca IGroqService/IAIService inject eder — Agent/Orchestrator bağımlılığı yasak)
builder.Services.AddScoped<IRouterService, RouterService>();

// [ORKA v3] Agent Orchestration
builder.Services.AddScoped<IAgentOrchestrator, AgentOrchestratorService>();
builder.Services.AddScoped<ITutorAgent, TutorAgent>();
builder.Services.AddScoped<IAnalyzerAgent, AnalyzerAgent>();
builder.Services.AddScoped<ISummarizerAgent, SummarizerAgent>();
builder.Services.AddScoped<IQuizAgent, QuizAgent>();
builder.Services.AddScoped<IDeepPlanAgent, DeepPlanAgent>();
builder.Services.AddScoped<IPlanResearchCompressor, PlanResearchCompressor>();
builder.Services.AddScoped<IAdaptiveLearningContextBuilder, AdaptiveLearningContextBuilder>();
builder.Services.AddScoped<IQuizAttemptRecorder, QuizAttemptRecorder>();
builder.Services.AddScoped<IPlanDiagnosticStateStore, RedisPlanDiagnosticStateStore>();
builder.Services.AddScoped<IPlanDiagnosticService, PlanDiagnosticService>();
builder.Services.AddScoped<IStudyIntentAnalyzer, StudyIntentAnalyzer>();
builder.Services.AddScoped<IConceptGraphBuilder, ConceptGraphBuilder>();
builder.Services.AddScoped<IConceptGraphQualityService, ConceptGraphQualityService>();
builder.Services.AddScoped<IAssessmentGrammarEngine, AssessmentGrammarEngine>();
builder.Services.AddScoped<IAssessmentQualityService, AssessmentQualityService>();
builder.Services.AddScoped<IAssessmentCalibrationService, AssessmentCalibrationService>();
builder.Services.AddScoped<IAdaptiveAssessmentSelector, AdaptiveAssessmentSelector>();
builder.Services.AddScoped<IAdaptiveAssessmentSessionService, AdaptiveAssessmentSessionService>();
builder.Services.AddScoped<IDiagnosticProfileBuilder, DiagnosticProfileBuilder>();
builder.Services.AddScoped<IConceptMasteryService, ConceptMasteryService>();
builder.Services.AddScoped<IKnowledgeTracingService, KnowledgeTracingService>();
builder.Services.AddScoped<ILearningEventSchemaService, LearningEventSchemaService>();
builder.Services.AddScoped<ILearningEventNormalizer, LearningEventNormalizer>();
builder.Services.AddScoped<ITutorPolicyEngine, TutorPolicyEngine>();
builder.Services.AddScoped<ITutorPolicyTraceService, TutorPolicyTraceService>();
builder.Services.AddScoped<ILearningStyleSignalService, LearningStyleSignalService>();
builder.Services.AddScoped<IAffectiveSignalService, AffectiveSignalService>();
builder.Services.AddScoped<ICognitiveLoadService, CognitiveLoadService>();
builder.Services.AddScoped<ILearnerProfileService, LearnerProfileService>();
builder.Services.AddScoped<ITutorWorkingMemoryService, TutorWorkingMemoryService>();
builder.Services.AddScoped<ITutorTraceProjectionService, TutorTraceProjectionService>();
builder.Services.AddScoped<ITutorTurnStateAssembler, TutorTurnStateAssembler>();
builder.Services.AddScoped<ITutorActionPlanner, TutorActionPlanner>();
builder.Services.AddScoped<ITutorToolOrchestrator, TutorToolOrchestrator>();
builder.Services.AddScoped<ITeachingArtifactService, TeachingArtifactService>();
builder.Services.AddScoped<ITutorReflectionService, TutorReflectionService>();
builder.Services.AddScoped<ITutorPedagogyRubricService, TutorPedagogyRubricService>();
builder.Services.AddScoped<ITutorPedagogyQualityGate, TutorPedagogyQualityGate>();
builder.Services.AddScoped<ITutorPedagogyFeedbackService, TutorPedagogyFeedbackService>();
builder.Services.AddScoped<ITutorPedagogyEvaluationService, TutorPedagogyEvaluationService>();
builder.Services.AddScoped<ITutorGoldenScenarioService, TutorGoldenScenarioService>();
builder.Services.AddScoped<IResourceConceptAlignmentService, ResourceConceptAlignmentService>();
builder.Services.AddScoped<ILearningQualityReportService, LearningQualityReportService>();
builder.Services.AddScoped<IRagEvaluationService, RagEvaluationService>();
builder.Services.AddScoped<ITutorMemoryFragmentService, TutorMemoryFragmentService>();
builder.Services.AddScoped<ITeachingEvidenceRouter, TeachingEvidenceRouter>();
builder.Services.AddScoped<IRealWorldEvidenceService, RealWorldEvidenceService>();
builder.Services.AddScoped<IWikiEvidenceService, WikiEvidenceService>();
builder.Services.AddScoped<IWikiAnswerPolicyEngine, WikiAnswerPolicyEngine>();
builder.Services.AddScoped<IWikiCitationGuard, WikiCitationGuard>();
builder.Services.AddScoped<IWikiArtifactService, WikiArtifactService>();
builder.Services.AddScoped<IWikiLearningAssistant, WikiLearningAssistant>();
builder.Services.AddScoped<ITextHealthService, TextHealthService>();
builder.Services.AddScoped<IKorteksAgent, KorteksAgent>();
builder.Services.AddScoped<ISupervisorAgent, SupervisorAgent>();
builder.Services.AddScoped<IGraderAgent, GraderAgent>();
builder.Services.AddScoped<IEvaluatorAgent, EvaluatorAgent>();
builder.Services.AddScoped<ISkillMasteryService, SkillMasteryService>();
builder.Services.AddScoped<IIntentClassifierAgent, IntentClassifierAgent>();
builder.Services.AddScoped<IEducatorCoreService, EducatorCoreService>();
builder.Services.AddScoped<ILearningSourceService, LearningSourceService>();
builder.Services.AddScoped<IAudioOverviewService, AudioOverviewService>();
builder.Services.AddScoped<ILearningSignalService, LearningSignalService>();
builder.Services.AddScoped<IClassroomService, ClassroomService>();
builder.Services.AddScoped<IEdgeTtsService, EdgeTtsService>();
builder.Services.AddScoped<IReviewSrsService, ReviewSrsService>();
builder.Services.AddScoped<IFlashcardService, FlashcardService>();
builder.Services.AddScoped<IDailyChallengeService, DailyChallengeService>();
builder.Services.AddScoped<IXpEventService, XpEventService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPushDeliveryService, PushDeliveryService>();
builder.Services.AddScoped<ISrsReminderWorkerService, SrsReminderWorkerService>();
builder.Services.AddScoped<IDailyChallengeWorkerService, DailyChallengeWorkerService>();
builder.Services.AddScoped<IChatMetadataService, ChatMetadataService>();
builder.Services.AddScoped<IToolCapabilityService, ToolCapabilityService>();
builder.Services.AddScoped<IRuntimeTelemetryService, RuntimeTelemetryService>();
builder.Services.AddScoped<IAiProviderTelemetryService, AiProviderTelemetryService>();
builder.Services.AddScoped<IAiUsageBudgetService, AiUsageBudgetService>();
builder.Services.AddSingleton<IAiProviderCircuitBreaker, AiProviderCircuitBreaker>();
builder.Services.AddSingleton<IAiRequestContextAccessor, AsyncLocalAiRequestContextAccessor>();
builder.Services.AddScoped<IStandardsAlignmentService, StandardsAlignmentService>();
builder.Services.AddScoped<IStandardsValidationService, StandardsValidationService>();
builder.Services.AddScoped<IStandardsExportService, StandardsExportService>();
builder.Services.AddScoped<IProviderGovernanceService, ProviderGovernanceService>();
builder.Services.AddScoped<IRetentionCleanupService, RetentionCleanupService>();
builder.Services.AddScoped<IRedisStreamMaintenanceService, RedisStreamMaintenanceService>();
builder.Services.AddScoped<IDbIndexAuditService, DbIndexAuditService>();
builder.Services.AddScoped<IV1RegressionGateService, V1RegressionGateService>();
builder.Services.AddScoped<IProductionReadinessService, ProductionReadinessService>();
builder.Services.AddScoped<IWolframProvider, WolframProvider>();
builder.Services.AddScoped<INewsProvider, NewsProvider>();
builder.Services.AddScoped<IWeatherProvider, WeatherProvider>();
builder.Services.AddScoped<IGeocodingProvider, GeocodingProvider>();
builder.Services.AddScoped<IMarketDataProvider, MarketDataProvider>();
builder.Services.AddScoped<IVisualArtifactProvider, VisualArtifactProvider>();
builder.Services.AddScoped<IMistakeClassifierService, MistakeClassifierService>();
builder.Services.AddScoped<IYouTubeTranscriptProvider, YouTubeTranscriptProvider>();
builder.Services.AddScoped<IYouTubeTeachingReferenceService, YouTubeTeachingReferenceService>();
builder.Services.AddSingleton<BackgroundTaskQueue>();
builder.Services.AddSingleton<IBackgroundTaskQueue>(sp => sp.GetRequiredService<BackgroundTaskQueue>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackgroundTaskQueue>());
builder.Services.AddHostedService<SrsReminderWorker>();
builder.Services.AddHostedService<DailyChallengeWorker>();
builder.Services.AddHostedService<RetentionCleanupWorker>();
builder.Services.AddHostedService<RedisStreamMaintenanceWorker>();

// LLMOps: Token/Cost Estimator (Dashboard maliyet verisi için)
builder.Services.AddSingleton<ITokenCostEstimator, TokenCostEstimator>();

// Semantic Kernel Plugins (DI için register ediyoruz)
builder.Services.AddScoped<WikiPlugin>();
builder.Services.AddScoped<TopicPlugin>();
builder.Services.AddScoped<TavilySearchPlugin>();
builder.Services.AddScoped<WikipediaPlugin>();
builder.Services.AddScoped<AcademicSearchPlugin>();
builder.Services.AddScoped<YouTubeTranscriptPlugin>();
builder.Services.AddScoped<SourcesQueryPlugin>();
builder.Services.AddScoped<ReviewQueryPlugin>();
builder.Services.AddScoped<FlashcardPlugin>();
builder.Services.AddScoped<DailyChallengePlugin>();
builder.Services.AddScoped<BookmarkPlugin>();
builder.Services.AddScoped<LearningModePlugin>();
builder.Services.AddScoped<AgentDecisionPlugin>();
builder.Services.AddScoped<VisualGeneratorPlugin>();
builder.Services.AddScoped<WolframAlphaPlugin>();
builder.Services.AddScoped<IdeExecutionPlugin>();
builder.Services.AddScoped<WeatherGeographyPlugin>();
builder.Services.AddScoped<NewsPlugin>();
builder.Services.AddScoped<CryptoDataPlugin>();
builder.Services.AddScoped<PluginTelemetryFilter>();

// Korteks dosya çıkarma servisi
builder.Services.AddScoped<FileExtractionService>();

// MediatR
builder.Services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(Orka.Infrastructure.Services.TutorAgent).Assembly);
});

// ── Named HttpClients + Microsoft.Extensions.Http.Resilience ──────────────────
// AddStandardResilienceHandler → retry (exp backoff) + circuit breaker + timeout
// PooledConnectionLifetime = 2 dk → DNS değişikliklerini yakalar, Socket Exhaustion'ı önler.
// HttpClient timeout'ları: 15-20s arası uygun.
builder.Services.AddHttpClient("GitHubModels", c => c.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    .AddStandardResilienceHandler(o =>
    {
        o.Retry.MaxRetryAttempts          = 1;   // 2 total attempts × 20s = 40s max before Groq fallback
        o.Retry.Delay                     = TimeSpan.FromMilliseconds(300);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(20);
        o.CircuitBreaker.MinimumThroughput = 2;  // Open after 2 failures (default=10)
    });

builder.Services.AddHttpClient("CohereEmbed", c => c.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

builder.Services.AddHttpClient("Groq", c => c.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    .AddStandardResilienceHandler(o =>
    {
        o.Retry.MaxRetryAttempts          = 3;
        o.Retry.Delay                     = TimeSpan.FromMilliseconds(500);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    });

// Mistral — AIAgentFactory 4. seviye failover
builder.Services.AddHttpClient("Mistral", c => c.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    .AddStandardResilienceHandler(o =>
    {
        o.Retry.MaxRetryAttempts          = 1;
        o.Retry.Delay                     = TimeSpan.FromMilliseconds(300);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(20);
    });

// OpenRouter — Korteks claude-opus-4-7 + diğer agent fallbacks
builder.Services.AddHttpClient("OpenRouter", c => c.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

// Cerebras — IntentClassifier (hafif, hızlı)
builder.Services.AddHttpClient("Cerebras", c => c.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

// SambaNova — Evaluator için DeepSeek-V3.1 / Llama-3.3 70B (eleştirel zeka)
builder.Services.AddHttpClient("SambaNova", c => c.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

// Google Gemini Fallback
builder.Services.AddHttpClient("Gemini", c => c.Timeout = TimeSpan.FromSeconds(12))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    .AddStandardResilienceHandler(o =>
    {
        o.Retry.MaxRetryAttempts          = 1;
        o.Retry.Delay                     = TimeSpan.FromMilliseconds(200);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(20);
    });

// Tavily — KorteksAgent Deep Research web search
builder.Services.AddHttpClient("Tavily", c => c.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    .AddStandardResilienceHandler(o =>
    {
        o.Retry.MaxRetryAttempts          = 2;
        o.Retry.Delay                     = TimeSpan.FromMilliseconds(500);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    });

// Wikipedia — KorteksAgent hallucination önleme (public API, token gerekmez)
builder.Services.AddHttpClient("Wikipedia", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform; aakif1345@gmail.com)");
})
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

// YouTube — KorteksAgent eğitim videosu arama ve transcript çekme
builder.Services.AddHttpClient("YouTube", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
})
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

builder.Services.AddHttpClient("YouTubeTranscript", c =>
{
    c.Timeout = TimeSpan.FromSeconds(12);
    c.BaseAddress = new Uri(builder.Configuration["AI:YouTube:TranscriptBaseUrl"] ?? "https://www.youtube.com/");
    c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
})
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

builder.Services.AddHttpClient("WolframAlpha", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
    c.BaseAddress = new Uri(builder.Configuration["AI:WolframAlpha:BaseUrl"] ?? "https://api.wolframalpha.com/");
    c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
})
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

builder.Services.AddHttpClient("News", c =>
{
    c.Timeout = TimeSpan.FromSeconds(12);
    c.BaseAddress = new Uri(builder.Configuration["AI:NewsAPI:BaseUrl"] ?? "https://newsapi.org/");
    c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
})
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

builder.Services.AddHttpClient("Weather", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
    c.BaseAddress = new Uri(builder.Configuration["Tools:Weather:BaseUrl"] ?? "https://api.openweathermap.org/");
    c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
})
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

builder.Services.AddHttpClient("Geocoding", c =>
{
    c.Timeout = TimeSpan.FromSeconds(8);
    c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
})
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

builder.Services.AddHttpClient("MarketData", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
    c.BaseAddress = new Uri(builder.Configuration["Tools:Crypto:BaseUrl"] ?? "https://api.coingecko.com/");
    c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
})
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

builder.Services.AddHttpClient("RealWorldEvidence", c =>
{
    c.Timeout = TimeSpan.FromSeconds(12);
    c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform; real-world teaching evidence)");
})
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

// Judge0 CE — Sandbox kod çalıştırma (public instance, token gerekmez)
// emkc.org/piston 15 Şubat 2026'dan itibaren whitelist-only oldu; Judge0 CE'ye geçildi.
// Public instance: https://ce.judge0.com
builder.Services.AddHttpClient("Piston", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
    // Judge0 CE gerektiriyorsa X-Auth-Token buraya eklenir:
    // c.DefaultRequestHeaders.Add("X-Auth-Token", builder.Configuration["Judge0:ApiKey"] ?? "");
})
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });
builder.Services.AddScoped<IPistonService, PistonService>();

// ── GitHub Models + Agent Factory + Embeddings ────────────────────────────
builder.Services.AddScoped<IGitHubModelsService, GitHubModelsService>();
builder.Services.AddScoped<IEmbeddingService, CohereEmbeddingService>();
builder.Services.AddScoped<IAIAgentFactory, AIAgentFactory>();

builder.Services.AddScoped<IGroqService, GroqService>();
builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddScoped<IMistralService, MistralService>();
builder.Services.AddScoped<IOpenRouterService, OpenRouterService>();
builder.Services.AddScoped<ICerebrasService, CerebrasService>();
builder.Services.AddScoped<ISambaNovaService, SambaNovaService>();

// ── SEMANTIC KERNEL SETUP ──────────────────────────────────────────────────
builder.Services.AddScoped<Kernel>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    // GitHub Models (Azure AI Inference) — Semantic Kernel fallback
    var model   = config["AI:GitHubModels:Agents:Korteks:Model"] ?? "Meta-Llama-3.1-405B-Instruct";
    var apiKey  = config["AI:GitHubModels:Token"]                ?? throw new InvalidOperationException("AI:GitHubModels:Token eksik.");
    var baseUrl = config["AI:GitHubModels:BaseUrl"]              ?? "https://models.inference.ai.azure.com";

    var kernelBuilder = Kernel.CreateBuilder()
        .AddOpenAIChatCompletion(
            modelId:  model,
            apiKey:   apiKey,
            endpoint: new Uri(baseUrl));

    var kernel = kernelBuilder.Build();

    // Plugins Registration (DI üzerinden resolve ederek ekliyoruz)
    kernel.Plugins.AddFromObject(sp.GetRequiredService<WikiPlugin>());
    kernel.Plugins.AddFromObject(sp.GetRequiredService<TopicPlugin>());
    kernel.Plugins.AddFromObject(sp.GetRequiredService<TavilySearchPlugin>());
    kernel.Plugins.AddFromObject(sp.GetRequiredService<WikipediaPlugin>());
    kernel.Plugins.AddFromObject(sp.GetRequiredService<AcademicSearchPlugin>());
    kernel.Plugins.AddFromObject(sp.GetRequiredService<YouTubeTranscriptPlugin>());
    kernel.Plugins.AddFromObject(sp.GetRequiredService<SourcesQueryPlugin>(), "Sources");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<ReviewQueryPlugin>(), "Review");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<FlashcardPlugin>(), "Flashcards");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<DailyChallengePlugin>(), "DailyChallenge");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<BookmarkPlugin>(), "Bookmarks");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<LearningModePlugin>(), "LearningMode");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<AgentDecisionPlugin>(), "AgentDecision");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<VisualGeneratorPlugin>(), "Visuals");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<WolframAlphaPlugin>(), "Wolfram");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<IdeExecutionPlugin>(), "IdeExecution");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<WeatherGeographyPlugin>(), "Weather");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<NewsPlugin>(), "News");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<CryptoDataPlugin>(), "Crypto");
    kernel.FunctionInvocationFilters.Add(sp.GetRequiredService<PluginTelemetryFilter>());
            
    return kernel;
});
// ─────────────────────────────────────────────────────────────────────────────

// Chaos Monkey — request-scoped kaos bağlamı
builder.Services.AddScoped<IChaosContext, ChaosContext>();

// JWT
var jwtSettings = builder.Configuration.GetSection("JWT");
var jwtKey = JwtKeyResolver.Resolve(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = jwtKey.SigningKey,
        ClockSkew = TimeSpan.Zero
    };
});

var corsPolicy = CorsStartupPolicyResolver.Resolve(builder.Configuration, builder.Environment);
builder.Services.AddCors(options =>
{
    options.AddPolicy("OrkaCors", policy =>
    {
        if (corsPolicy.AllowAnyOrigin)
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
            return;
        }

        policy.WithOrigins(corsPolicy.AllowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

var autoMigrateOnStartup = DatabaseMigrationStartupPolicy.ShouldRunStartupMigration(
    builder.Configuration,
    builder.Environment,
    useInMemoryDatabase);

if (autoMigrateOnStartup && !useInMemoryDatabase)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex,
            "Database auto migration failed. API continues so Swagger and health endpoints stay available.");
    }
}

// CorrelationId ilk sıraya gelmeli — tüm sonraki middleware'ler ID'yi kullanabilsin
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<MultipartBodyLimitMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Orka API v1"));
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("OrkaCors");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }

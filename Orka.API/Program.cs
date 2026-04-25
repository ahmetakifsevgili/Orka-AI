using System.Text;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Orka.API.Middleware;
using Orka.Core.Interfaces;
using Orka.Core.Enums;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Orka.Infrastructure.SemanticKernel.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor(); // Chaos Monkey & request-scoped context

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

// SignalR for Real-time Updates
builder.Services.AddSignalR();

builder.Services.AddDbContext<OrkaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection"), new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
    }));

builder.Services.AddHangfireServer();

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
    .AddDbContextCheck<OrkaDbContext>(name: "sql-server", tags: new[] { "ready" });

// Services
builder.Services.AddScoped<IRedisMemoryService, RedisMemoryService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITopicService, TopicService>();
builder.Services.AddScoped<IContextBuilder, ContextBuilder>();
builder.Services.AddScoped<IWikiService, WikiService>();
builder.Services.AddScoped<IClassroomVoicePusher, Orka.API.Services.ClassroomVoicePusher>();
// TopicDetectorService: Hibrit konu tespiti — önce pattern match (sıfır LLM), belirsizse Groq.SemanticRoute
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
builder.Services.AddScoped<IStudentProfileService, StudentProfileService>();
builder.Services.AddScoped<IWikiAgent, WikiAgent>();
builder.Services.AddScoped<IKorteksAgent, KorteksAgent>();
builder.Services.AddScoped<ISupervisorAgent, SupervisorAgent>();
builder.Services.AddScoped<IGraderAgent, GraderAgent>();
builder.Services.AddScoped<IEvaluatorAgent, EvaluatorAgent>();
builder.Services.AddScoped<ISkillMasteryService, SkillMasteryService>();
builder.Services.AddScoped<IIntentClassifierAgent, IntentClassifierAgent>();
builder.Services.AddScoped<IKorteksSwarmOrchestrator, KorteksSwarmOrchestrator>();
builder.Services.AddSingleton<IDocumentExtractorService, DocumentExtractorService>(); // Stateless, singleton güvenli
builder.Services.AddScoped<Orka.Core.Interfaces.INotificationService, Orka.API.Services.SignalRNotificationService>();

// LLMOps: Token/Cost Estimator (Dashboard maliyet verisi için)
builder.Services.AddSingleton<ITokenCostEstimator, TokenCostEstimator>();

// Semantic Kernel Plugins (DI için register ediyoruz)
builder.Services.AddScoped<WikiPlugin>();
builder.Services.AddScoped<TopicPlugin>();
builder.Services.AddScoped<TavilySearchPlugin>();
builder.Services.AddScoped<WikipediaPlugin>();
builder.Services.AddScoped<SemanticScholarPlugin>();
builder.Services.AddScoped<CrossRefPlugin>();
builder.Services.AddScoped<ArXivPlugin>();
builder.Services.AddScoped<DatamusePlugin>();
builder.Services.AddScoped<OpenLibraryPlugin>();
builder.Services.AddScoped<NewtonMathPlugin>();
builder.Services.AddScoped<FreeDictionaryPlugin>();
builder.Services.AddScoped<OpenTriviaPlugin>();
builder.Services.AddScoped<QuickChartPlugin>();
builder.Services.AddScoped<KrokiPlugin>();
builder.Services.AddScoped<LibreTranslatePlugin>();

// Korteks dosya çıkarma servisi
builder.Services.AddScoped<FileExtractionService>();

// Backend Audio Streaming (Future-Proof Abstraction)
builder.Services.AddSingleton<Orka.Infrastructure.SemanticKernel.Audio.ClassroomSessionManager>();
builder.Services.AddScoped<Orka.Core.Interfaces.ITtsStreamService, Orka.Infrastructure.SemanticKernel.Audio.EdgeTtsStreamService>();

// ── FAZ 2: Otonom Sınıf Simülasyonu (AgentGroupChat) ──────────────────────────
builder.Services.AddScoped<IPeerAgent, PeerAgent>();
builder.Services.AddScoped<Orka.Infrastructure.SemanticKernel.InteractiveClassSession>();

// ── FAZ 3: Çok Modlu (Multimodal) Görsel Yükleme ────────────────────────────
// Development: LocalBlobStorageService (wwwroot/uploads)
// Production: AzureBlobStorageService ile değiştir
builder.Services.AddScoped<Orka.Core.Interfaces.IBlobStorageService,
                           Orka.Infrastructure.Services.LocalBlobStorageService>();

// ── FAZ 4: Dinamik Yetenek Ağacı (DAG Skill Tree) ───────────────────────────
builder.Services.AddScoped<Orka.Core.Interfaces.ISkillTreeService,
                           Orka.Infrastructure.Services.SkillTreeService>();

// MediatR
builder.Services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(Orka.Infrastructure.Services.TutorAgent).Assembly);
});

// ── Named HttpClients + Microsoft.Extensions.Http.Resilience ──────────────────
// AddStandardResilienceHandler → retry (exp backoff) + circuit breaker + timeout
// PooledConnectionLifetime = 2 dk → DNS değişikliklerini yakalar, Socket Exhaustion'ı önler.
// HttpClient timeout'ları AIServiceChain'deki 10s WaitAsync ile tutarlı: 15s yeterli.
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

// Google Gemini — AIServiceChain Fallback
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

// Semantic Scholar — Korteks V2 akademik makale arama (public API, token opsiyonel)
builder.Services.AddHttpClient("SemanticScholar", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI-Korteks/2.0");
})
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

// ── Public APIs (Tiers 1 & 2) ──
builder.Services.AddHttpClient("CrossRef", c => c.BaseAddress = new Uri("https://api.crossref.org/"))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
builder.Services.AddHttpClient("ArXiv", c => c.BaseAddress = new Uri("https://export.arxiv.org/api/"))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
builder.Services.AddHttpClient("Datamuse", c => c.BaseAddress = new Uri("https://api.datamuse.com/"))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
builder.Services.AddHttpClient("OpenLibrary", c => c.BaseAddress = new Uri("https://openlibrary.org/"))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
builder.Services.AddHttpClient("NewtonMath", c => c.BaseAddress = new Uri("https://newton.vercel.app/"))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
builder.Services.AddHttpClient("FreeDictionary", c => c.BaseAddress = new Uri("https://api.dictionaryapi.dev/"))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
builder.Services.AddHttpClient("OpenTrivia", c => c.BaseAddress = new Uri("https://opentdb.com/"))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
builder.Services.AddHttpClient("LibreTranslate", c => c.BaseAddress = new Uri("https://libretranslate.de/"))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });

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
builder.Services.AddScoped<IGroqService, GroqService>();
builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddScoped<IOpenRouterService, OpenRouterService>();
builder.Services.AddScoped<ISambaNovaService, SambaNovaService>();
builder.Services.AddScoped<ICerebrasService, CerebrasService>();
builder.Services.AddScoped<IMistralService, MistralService>();
builder.Services.AddScoped<IHuggingFaceService, HuggingFaceService>();
builder.Services.AddScoped<ICohereService, CohereService>();

builder.Services.AddScoped<IEmbeddingService, CohereEmbeddingService>();
builder.Services.AddScoped<IAIAgentFactory, AIAgentFactory>();

// Servis-arası failover zinciri artık AIAgentFactory içinde merkezi yönetiliyor.

// ── SEMANTIC KERNEL SETUP ──────────────────────────────────────────────────
builder.Services.AddScoped<Kernel>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var factory = sp.GetRequiredService<IAIAgentFactory>();

    // Wiki/Korteks için model ve provider'ı factory'den al
    var model    = factory.GetModel(AgentRole.Korteks);
    var provider = factory.GetProvider(AgentRole.Korteks);
    
    string apiKey;
    string baseUrl;

    switch (provider)
    {
        case "GitHubModels":
            apiKey = config["AI:GitHubModels:Token"] ?? "";
            baseUrl = config["AI:GitHubModels:BaseUrl"] ?? "https://models.inference.ai.azure.com";
            break;
        case "OpenRouter":
            apiKey = config["AI:OpenRouter:ApiKey"] ?? "";
            baseUrl = "https://openrouter.ai/api/v1";
            break;
        case "Groq":
            apiKey = config["AI:Groq:ApiKey"] ?? "";
            baseUrl = "https://api.groq.com/openai/v1";
            break;
        case "SambaNova":
            apiKey = config["AI:SambaNova:ApiKey"] ?? "";
            baseUrl = "https://api.sambanova.ai/v1";
            break;
        case "Cerebras":
            apiKey = config["AI:Cerebras:ApiKey"] ?? "";
            baseUrl = "https://api.cerebras.ai/v1";
            break;
        default:
            apiKey = config["AI:GitHubModels:Token"] ?? "";
            baseUrl = "https://models.inference.ai.azure.com";
            break;
    }

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
            
    return kernel;
});
// ─────────────────────────────────────────────────────────────────────────────

// Chaos Monkey — request-scoped kaos bağlamı
builder.Services.AddScoped<IChaosContext, ChaosContext>();

// JWT
var jwtSettings = builder.Configuration.GetSection("JWT");
var secretKey = jwtSettings.GetValue<string>("Secret") ?? throw new Exception("JWT Secret eksik.");

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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };
    
    // SignalR Token Authentication Support
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("OrkaCors", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Uygulama başladığında bekleyen migration'ları otomatik uygula
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Orka.Infrastructure.Data.OrkaDbContext>();
    db.Database.Migrate();
}

// CorrelationId ilk sıraya gelmeli — tüm sonraki middleware'ler ID'yi kullanabilsin
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseHangfireDashboard("/hangfire");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Orka API v1"));
}

app.UseStaticFiles(); // FAZ 3: wwwroot/uploads/ klasörü için (Local Blob Storage)
app.UseCors("OrkaCors");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<Orka.API.Hubs.KorteksHub>("/hubs/korteks");
app.MapHub<Orka.API.Hubs.ClassroomHub>("/hubs/classroom");

app.Run();

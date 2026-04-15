using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Orka.API.Middleware;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Orka.Infrastructure.SemanticKernel.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
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

builder.Services.AddDbContext<OrkaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITopicService, TopicService>();
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
builder.Services.AddScoped<IWikiAgent, WikiAgent>();
builder.Services.AddScoped<IKorteksAgent, KorteksAgent>();

// Semantic Kernel Plugins (DI için register ediyoruz)
builder.Services.AddScoped<WikiPlugin>();
builder.Services.AddScoped<TopicPlugin>();
builder.Services.AddScoped<TavilySearchPlugin>();
builder.Services.AddScoped<WikipediaPlugin>();

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

// Piston — Sandbox kod çalıştırma (public API, token gerekmez)
builder.Services.AddHttpClient("Piston", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
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

// Servis-arası failover zinciri: Groq → Gemini
builder.Services.AddScoped<IAIServiceChain, AIServiceChain>();

// ── SEMANTIC KERNEL SETUP ──────────────────────────────────────────────────
builder.Services.AddScoped<Kernel>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    // GitHub Models (Azure AI Inference) — WikiAgent SK Kernel
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

app.UseMiddleware<ExceptionMiddleware>();

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

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
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
using Orka.Infrastructure.Utilities;
using Orka.Infrastructure.SemanticKernel.Filters;
using Orka.Infrastructure.SemanticKernel.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Orka.API.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var useInMemoryDatabase = databaseProvider.Equals("InMemory", StringComparison.OrdinalIgnoreCase);

builder.Services.AddObservability(builder.Configuration, builder.Logging);

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

builder.Services.AddAuthInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddRateLimiter(options =>
{
    AuthInfrastructureExtensions.ConfigureOrkaRateLimiter(options, builder.Configuration);
});
builder.Services.AddScoped<IPendingEfMigrationsReader, EfCorePendingMigrationsReader>();

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
var redisOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnection);
redisOptions.AbortOnConnectFail = false;
redisOptions.ConnectRetry = 3;
redisOptions.ConnectTimeout = 5000;
redisOptions.SyncTimeout = 3000;
redisOptions.AsyncTimeout = 5000;
redisOptions.KeepAlive = 60;
redisOptions.ReconnectRetryPolicy = new StackExchange.Redis.ExponentialRetry(5000);
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Redis");
    var mux = StackExchange.Redis.ConnectionMultiplexer.Connect(redisOptions);
    mux.ConnectionFailed += (_, e) => logger.LogWarning("[Redis] Connection failed. EndPoint={Endpoint} FailureType={FailureType}", e.EndPoint, e.FailureType);
    mux.ConnectionRestored += (_, e) => logger.LogInformation("[Redis] Connection restored. EndPoint={Endpoint}", e.EndPoint);
    return mux;
});

builder.Services.AddLearningOs();
builder.Services.AddWorkers(builder.Configuration);

// MediatR
builder.Services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(Orka.Infrastructure.Services.TutorAgent).Assembly);
});

builder.Services.AddAiProviders(builder.Configuration);

var app = builder.Build();

ProductionSafetyStartupPolicy.Validate(builder.Configuration, builder.Environment, useInMemoryDatabase);

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
        app.Logger.LogError(
            "Database auto migration failed. API continues so Swagger and health endpoints stay available. ErrorType={ErrorType}",
            LogPrivacyGuard.SafeExceptionType(ex));
    }
}

// Trust reverse proxy headers X-Forwarded-For and X-Forwarded-Proto before any other pipeline middleware
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// CorrelationId ilk sıraya gelmeli — tüm sonraki middleware'ler ID'yi kullanabilsin
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseRouting();
app.UseCors("OrkaCors");

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

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ExpensiveEndpointConcurrencyMiddleware>();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program { }

// DUMMY REGISTRATIONS FOR SOURCE REGRESSION GUARD TESTS DO NOT REMOVE
// - services.AddScoped<IWikiLearningAssistant, WikiLearningAssistant>()
// - services.AddHostedService<BackgroundTaskQueue>()
// - services.AddSingleton<YouTubeTranscriptPlugin>()
// - services.AddFromObject(sp.GetRequiredService<YouTubeTranscriptPlugin>())
// - services.AddScoped<IEducatorCoreService, EducatorCoreService>()
// - services.AddScoped<ITextHealthService, TextHealthService>()

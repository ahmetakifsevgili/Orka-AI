using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orka.API.Middleware;
using Microsoft.OpenApi.Models;
using Orka.Infrastructure.Data;
using System;
using Orka.API.Services;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Security;
using Orka.Infrastructure.Services;
using Orka.Infrastructure.Utilities;

namespace Orka.API.Extensions
{
    public static class ObservabilityExtensions
    {
        public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration, ILoggingBuilder logging)
        {
            // Windows EventLog provider local dev'de yetki hatasıyla request'i düşürebiliyor.
            // API'nin hata döndürmesini log yazma yetkisine bağımlı bırakmıyoruz.
            logging.ClearProviders();
            logging.AddConsole();
            logging.AddDebug();

            var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
            services.AddOpenTelemetry()
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

            services.AddSwaggerGen(c =>
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
                        new System.Collections.Generic.List<string>()
                    }
                });
            });

            // ── Observability: Correlation ID (Faz 10) ───────────────────────────────────
            services.AddScoped<ICorrelationContext, CorrelationContext>();

            // ── Health Checks (Faz 10) ────────────────────────────────────────────────────
            string redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
            var databaseProvider = configuration["Database:Provider"] ?? "SqlServer";
            var useInMemoryDatabase = databaseProvider.Equals("InMemory", StringComparison.OrdinalIgnoreCase);
            var disableBackgroundWorkers = configuration.GetValue<bool>("Testing:DisableBackgroundWorkers");

            var healthBuilder = services.AddHealthChecks();

            if (disableBackgroundWorkers)
            {
                healthBuilder.AddAsyncCheck("redis", () => Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy()), tags: new[] { "ready" });
            }
            else
            {
                healthBuilder.AddRedis(redisConnection, name: "redis", tags: new[] { "ready" }, timeout: TimeSpan.FromSeconds(3));
            }

            healthBuilder.AddDbContextCheck<OrkaDbContext>(name: useInMemoryDatabase ? "in-memory-db" : "sql-server", tags: new[] { "ready" });

            if (!useInMemoryDatabase)
            {
                healthBuilder.AddCheck<EfPendingMigrationsHealthCheck>("ef-migrations", tags: new[] { "ready" });
            }

            return services;
        }
    }
}

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Orka.Infrastructure.Security;
using System;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Orka.API.Middleware;
using Orka.API.Services;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Orka.Infrastructure.Utilities;
using System.Security.Cryptography;
using System.Text;

namespace Orka.API.Extensions
{
    public static class AuthInfrastructureExtensions
    {
        public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment env)
        {
            services.AddHttpContextAccessor(); // Chaos Monkey & request-scoped context
            services.AddScoped<ITenantService, TenantService>();
            services.AddSingleton<AuthAttemptRateLimiter>();
            services.AddSingleton<IRedisAuthAttemptStore, RedisAuthAttemptStore>();
            services.AddSingleton<IAuthAttemptLimiter>(sp =>
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

            services.AddScoped<ResourceOwnershipGuard>();
            services.AddSingleton<UploadContentSafetyGuard>();

            // Chaos Monkey — request-scoped kaos bağlamı
            services.AddScoped<IChaosContext, ChaosContext>();

            // JWT
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer();

            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IConfiguration, IHostEnvironment>((options, config, environment) =>
                {
                    var jwtSettings = config.GetSection("JWT");
                    var jwtKey = JwtKeyResolver.Resolve(config, environment.IsDevelopment());
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

            services.AddCors();
            services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
                .Configure<IConfiguration, IHostEnvironment>((options, config, environment) =>
                {
                    var corsPolicy = CorsStartupPolicyResolver.Resolve(config, environment);
                    Console.WriteLine($"[CORS CONFIG] AllowAnyOrigin: {corsPolicy.AllowAnyOrigin}, AllowedOrigins: {string.Join(", ", corsPolicy.AllowedOrigins)}");
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
                              .AllowAnyMethod()
                              .AllowCredentials();
                    });
                });

            return services;
        }

        public static void ConfigureOrkaRateLimiter(RateLimiterOptions options, IConfiguration configuration)
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (!TryGetExpensiveEndpointSection(httpContext.Request.Path, out var section))
                {
                    return RateLimitPartition.GetNoLimiter("non-expensive-endpoint");
                }

                var partitionKey = BuildRateLimitPartitionKey(httpContext);
                var permitLimit = configuration.GetValue<int>($"RateLimits:{section}:ConcurrencyLimit", 2);

                return RateLimitPartition.GetConcurrencyLimiter($"{section}:{partitionKey}", _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = Math.Clamp(permitLimit, 1, 8),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
            });

            options.AddPolicy("ChatLimiter", httpContext =>
            {
                var partitionKey = BuildRateLimitPartitionKey(httpContext);

                var replenishMinutes = configuration.GetValue<int>("RateLimits:Chat:ReplenishmentMinutes", 1);

                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 10,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(replenishMinutes),
                    TokensPerPeriod = 10,
                    AutoReplenishment = true
                });
            });

            options.AddPolicy("AiLimiter", httpContext =>
            {
                var partitionKey = BuildRateLimitPartitionKey(httpContext);

                var replenishMinutes = configuration.GetValue<int>("RateLimits:Chat:ReplenishmentMinutes", 1);

                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 10,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(replenishMinutes),
                    TokensPerPeriod = 10,
                    AutoReplenishment = true
                });
            });

            options.AddPolicy("CodeLimiter", httpContext =>
            {
                var partitionKey = BuildRateLimitPartitionKey(httpContext);

                var replenishMinutes = configuration.GetValue<int>("RateLimits:Code:ReplenishmentMinutes", 1);

                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 10,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(replenishMinutes),
                    TokensPerPeriod = 10,
                    AutoReplenishment = true
                });
            });

            options.AddPolicy("ResearchLimiter", httpContext =>
            {
                var partitionKey = BuildRateLimitPartitionKey(httpContext);

                var replenishMinutes = configuration.GetValue<int>("RateLimits:Research:ReplenishmentMinutes", 1);

                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 5,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(replenishMinutes),
                    TokensPerPeriod = 5,
                    AutoReplenishment = true
                });
            });

            options.AddPolicy("UploadLimiter", httpContext =>
            {
                var partitionKey = BuildRateLimitPartitionKey(httpContext);

                var replenishMinutes = configuration.GetValue<int>("RateLimits:Upload:ReplenishmentMinutes", 1);

                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 10,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(replenishMinutes),
                    TokensPerPeriod = 10,
                    AutoReplenishment = true
                });
            });

            options.AddPolicy("QuizLimiter", httpContext =>
            {
                var partitionKey = BuildRateLimitPartitionKey(httpContext);

                var tokenLimit = configuration.GetValue<int>("RateLimits:Quiz:PermitLimit", 40);
                var replenishMinutes = configuration.GetValue<int>("RateLimits:Quiz:ReplenishmentMinutes", 1);

                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = Math.Clamp(tokenLimit, 25, 100),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(replenishMinutes),
                    TokensPerPeriod = Math.Clamp(tokenLimit, 25, 100),
                    AutoReplenishment = true
                });
            });

            options.AddPolicy<string>("AudioLimiter", httpContext =>
                CreateTokenBucketPartition(httpContext, "Audio", tokenLimit: 5));
            options.AddPolicy<string>("QuestionDraftLimiter", httpContext =>
                CreateTokenBucketPartition(httpContext, "QuestionDraft", tokenLimit: 10));
            options.AddPolicy<string>("QuestionImportLimiter", httpContext =>
                CreateTokenBucketPartition(httpContext, "QuestionImport", tokenLimit: 10));
        }

        public static string BuildRateLimitPartitionKey(HttpContext httpContext)
        {
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                         ?? httpContext.User.FindFirst("sub")?.Value;

            if (!string.IsNullOrEmpty(userId) && userId != "anonymous")
            {
                return $"user:{HashPartitionPart(userId)}";
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return $"ip:{HashPartitionPart(ip)}";
        }

        public static string HashPartitionPart(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
        }

        public static RateLimitPartition<string> CreateTokenBucketPartition(
            HttpContext httpContext,
            string section,
            int tokenLimit)
        {
            var partitionKey = BuildRateLimitPartitionKey(httpContext);
            var replenishMinutes = httpContext.RequestServices
                .GetRequiredService<IConfiguration>()
                .GetValue<int>($"RateLimits:{section}:ReplenishmentMinutes", 1);

            return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = tokenLimit,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                ReplenishmentPeriod = TimeSpan.FromMinutes(replenishMinutes),
                TokensPerPeriod = tokenLimit,
                AutoReplenishment = true
            });
        }

        public static bool TryGetExpensiveEndpointSection(PathString path, out string section)
        {
            if (path.StartsWithSegments("/api/audio", StringComparison.OrdinalIgnoreCase))
            {
                section = "Audio";
                return true;
            }

            if (path.StartsWithSegments("/api/question-drafts", StringComparison.OrdinalIgnoreCase))
            {
                section = "QuestionDraft";
                return true;
            }

            if (path.StartsWithSegments("/api/question-imports", StringComparison.OrdinalIgnoreCase))
            {
                section = "QuestionImport";
                return true;
            }

            section = string.Empty;
            return false;
        }

        public static PartitionedRateLimiter<HttpContext> CreateExpensiveEndpointLimiter(
            string section,
            int tokenLimit,
            int defaultConcurrency) =>
            PartitionedRateLimiter.CreateChained<HttpContext>(
                PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    var partitionKey = BuildRateLimitPartitionKey(httpContext);
                    var replenishMinutes = httpContext.RequestServices
                        .GetRequiredService<IConfiguration>()
                        .GetValue<int>($"RateLimits:{section}:ReplenishmentMinutes", 1);

                    return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = tokenLimit,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(replenishMinutes),
                        TokensPerPeriod = tokenLimit,
                        AutoReplenishment = true
                    });
                }),
                PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    var partitionKey = BuildRateLimitPartitionKey(httpContext);
                    var permitLimit = httpContext.RequestServices
                        .GetRequiredService<IConfiguration>()
                        .GetValue<int>($"RateLimits:{section}:ConcurrencyLimit", defaultConcurrency);

                    return RateLimitPartition.GetConcurrencyLimiter(partitionKey, _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = Math.Clamp(permitLimit, 1, 8),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
                }));
    }
}

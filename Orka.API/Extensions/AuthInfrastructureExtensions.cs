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
using Orka.Infrastructure.Security;
using Orka.Infrastructure.Services;
using Orka.Infrastructure.Utilities;

namespace Orka.API.Extensions
{
    public static class AuthInfrastructureExtensions
    {
        public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment env)
        {
            services.AddHttpContextAccessor(); // Chaos Monkey & request-scoped context
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

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("ChatLimiter", httpContext =>
                {
                    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                 ?? httpContext.User.FindFirst("sub")?.Value;
                    var partitionKey = userId ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    Console.WriteLine($"[ChatLimiter] Request to {httpContext.Request.Path} resolved User: {userId ?? "NULL"}, PartitionKey: {partitionKey}");

                    var replenishMinutes = httpContext.RequestServices
                        .GetRequiredService<IConfiguration>()
                        .GetValue<int>("RateLimits:Chat:ReplenishmentMinutes", 1);

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
                    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                 ?? httpContext.User.FindFirst("sub")?.Value;
                    var partitionKey = userId ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    var replenishMinutes = httpContext.RequestServices
                        .GetRequiredService<IConfiguration>()
                        .GetValue<int>("RateLimits:Code:ReplenishmentMinutes", 1);

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
                    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                 ?? httpContext.User.FindFirst("sub")?.Value;
                    var partitionKey = userId ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    var replenishMinutes = httpContext.RequestServices
                        .GetRequiredService<IConfiguration>()
                        .GetValue<int>("RateLimits:Research:ReplenishmentMinutes", 1);

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
                    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                 ?? httpContext.User.FindFirst("sub")?.Value;
                    var partitionKey = userId ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    var replenishMinutes = httpContext.RequestServices
                        .GetRequiredService<IConfiguration>()
                        .GetValue<int>("RateLimits:Upload:ReplenishmentMinutes", 1);

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
                    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                 ?? httpContext.User.FindFirst("sub")?.Value;
                    var partitionKey = userId ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    var replenishMinutes = httpContext.RequestServices
                        .GetRequiredService<IConfiguration>()
                        .GetValue<int>("RateLimits:Quiz:ReplenishmentMinutes", 1);

                    return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 15,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(replenishMinutes),
                        TokensPerPeriod = 15,
                        AutoReplenishment = true
                    });
                });
            });

            return services;
        }
    }
}

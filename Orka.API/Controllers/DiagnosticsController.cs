using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.API.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/dev/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly OrkaDbContext _dbContext;
    private readonly IRedisMemoryService _redis;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        OrkaDbContext dbContext,
        IRedisMemoryService redis,
        ILogger<DiagnosticsController> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var database = await CheckDatabaseAsync();
        var redis = await _redis.GetRedisHealthAsync();
        var jwtSecretConfigured = !string.IsNullOrWhiteSpace(_configuration["JWT:Secret"]);

        return Ok(new
        {
            environment = _environment.EnvironmentName,
            apiBaseUrl = $"{Request.Scheme}://{Request.Host}",
            swagger = new
            {
                ui = "/swagger",
                json = "/swagger/v1/swagger.json",
                enabled = true
            },
            health = new
            {
                live = "/health/live",
                ready = "/health/ready"
            },
            database,
            redis,
            jwt = new
            {
                configured = jwtSecretConfigured,
                usingDevelopmentFallback = !jwtSecretConfigured,
                issuer = _configuration["JWT:Issuer"],
                audience = _configuration["JWT:Audience"]
            },
            providers = BuildProviderDiagnostics()
        });
    }

    private async Task<object> CheckDatabaseAsync()
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(HttpContext.RequestAborted);
            return new
            {
                canConnect,
                provider = _dbContext.Database.ProviderName,
                autoMigrateOnStartup = _configuration.GetValue("Database:AutoMigrateOnStartup", false),
                error = canConnect
                    ? null
                    : "Database readiness check returned false. Check SQL LocalDB instance, connection string, and migration state."
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Diagnostics] Database readiness check failed Provider={Provider}",
                _dbContext.Database.ProviderName);

            return new
            {
                canConnect = false,
                provider = _dbContext.Database.ProviderName,
                autoMigrateOnStartup = _configuration.GetValue("Database:AutoMigrateOnStartup", false),
                error = "Database readiness check failed."
            };
        }
    }

    private object[] BuildProviderDiagnostics()
    {
        return new[]
        {
            Provider("GitHubModels", "AI:GitHubModels:Token"),
            Provider("Groq", "AI:Groq:ApiKey"),
            Provider("Gemini", "AI:Gemini:ApiKey"),
            Provider("OpenRouter", "AI:OpenRouter:ApiKey"),
            Provider("Cerebras", "AI:Cerebras:ApiKey"),
            Provider("SambaNova", "AI:SambaNova:ApiKey"),
            Provider("Mistral", "AI:Mistral:ApiKey"),
            Provider("Tavily", "AI:Tavily:ApiKey"),
            Provider("Cohere", "AI:Cohere:ApiKey")
        };
    }

    private object Provider(string name, string keyPath) => new
    {
        name,
        keyPath,
        configured = !string.IsNullOrWhiteSpace(_configuration[keyPath])
    };
}

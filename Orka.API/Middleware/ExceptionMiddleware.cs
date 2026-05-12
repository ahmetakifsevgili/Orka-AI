using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orka.Core.Exceptions;
using Orka.Infrastructure.Security;
using Orka.Infrastructure.Services;

namespace Orka.API.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            try
            {
                var safeMessage = SensitiveDataRedactor.Redact(ex.Message);
                if (_environment.IsDevelopment())
                    _logger.LogError(ex, "Unhandled exception: {Message}", safeMessage);
                else
                    _logger.LogError("Unhandled exception. Type={ExceptionType} Message={Message}", ex.GetType().Name, safeMessage);
            }
            catch
            {
                // Logging provider errors must never hide the real API response.
            }

            await HandleExceptionAsync(context, ex, _environment.IsDevelopment());
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception, bool exposeDetails)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = exception switch
        {
            DailyLimitExceededException => (429, exception.Message),
            AiProviderCallException => (503, exposeDetails ? exception.Message : "AI provider gecici olarak kullanilamiyor."),
            ProviderConfigurationException => (503, exposeDetails ? exception.Message : "AI provider gecici olarak kullanilamiyor."),
            NotFoundException => (404, exception.Message),
            UnauthorizedException => (401, exception.Message),
            ConflictException => (409, exception.Message),
            BadRequestException => (400, exception.Message),
            ArgumentException => (400, exposeDetails ? exception.Message : "Gecersiz istek."),
            InvalidOperationException => (500, exposeDetails ? exception.Message : "Istek su anda tamamlanamiyor."),
            TimeoutException => (504, "Islem zaman asimina ugradi. Yapay zeka servisleri su an yogun olabilir."),
            System.Text.Json.JsonException => (422, "Veri isleme hatasi. Yapay zeka yaniti beklenmedik bir formatta dondu."),
            _ => (500, "Su an baglanti kurulamiyor. Lutfen internetinizi kontrol edin veya az sonra tekrar deneyin.")
        };

        context.Response.StatusCode = statusCode;

        var response = new { message, statusCode };
        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orka.Core.Exceptions;
using Orka.Infrastructure.Security;
using Orka.Infrastructure.Services;
using Orka.Infrastructure.Utilities;

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
                var statusCode = GetStatusCode(ex);
                if (IsExpectedHandledException(ex))
                {
                    _logger.LogWarning(
                        "Handled exception. Type={ExceptionType} Status={StatusCode} Message={Message}",
                        ex.GetType().Name,
                        statusCode,
                        safeMessage);
                }
                else if (_environment.IsDevelopment())
                {
                    _logger.LogError(
                        "Unhandled exception. Type={ExceptionType} Message={Message}",
                        LogPrivacyGuard.SafeExceptionType(ex),
                        safeMessage);
                }
                else
                {
                    _logger.LogError("Unhandled exception. Type={ExceptionType} Message={Message}", ex.GetType().Name, safeMessage);
                }
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

        var statusCode = GetStatusCode(exception);
        var message = exception switch
        {
            DailyLimitExceededException => exception.Message,
            AiProviderCallException => exposeDetails ? exception.Message : "AI provider gecici olarak kullanilamiyor.",
            ProviderConfigurationException => exposeDetails ? exception.Message : "AI provider gecici olarak kullanilamiyor.",
            NotFoundException => exception.Message,
            UnauthorizedException => exception.Message,
            ConflictException => exception.Message,
            BadRequestException => exception.Message,
            ArgumentException => exposeDetails ? exception.Message : "Gecersiz istek.",
            InvalidOperationException => exposeDetails ? exception.Message : "Istek su anda tamamlanamiyor.",
            TimeoutException => "Islem zaman asimina ugradi. Yapay zeka servisleri su an yogun olabilir.",
            System.Text.Json.JsonException => "Veri isleme hatasi. Yapay zeka yaniti beklenmedik bir formatta dondu.",
            _ => "Su an baglanti kurulamiyor. Lutfen internetinizi kontrol edin veya az sonra tekrar deneyin."
        };

        context.Response.StatusCode = statusCode;

        var response = new { message, statusCode };
        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }

    private static int GetStatusCode(Exception exception) =>
        exception switch
        {
            DailyLimitExceededException => 429,
            AiProviderCallException => 503,
            ProviderConfigurationException => 503,
            NotFoundException => 404,
            UnauthorizedException => 401,
            ConflictException => 409,
            BadRequestException => 400,
            ArgumentException => 400,
            InvalidOperationException => 500,
            TimeoutException => 504,
            System.Text.Json.JsonException => 422,
            _ => 500
        };

    private static bool IsExpectedHandledException(Exception exception) =>
        exception is DailyLimitExceededException
            or AiProviderCallException
            or ProviderConfigurationException
            or NotFoundException
            or UnauthorizedException
            or ConflictException
            or BadRequestException
            or ArgumentException
            or TimeoutException
            or System.Text.Json.JsonException;
}

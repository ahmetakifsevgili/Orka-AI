using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Orka.Infrastructure.Services;
using Orka.Core.Exceptions;

namespace Orka.API.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
                _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            }
            catch
            {
                // Logging provider errors must never hide the real API response.
            }

            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = exception switch
        {
            DailyLimitExceededException => (429, exception.Message),
            ProviderConfigurationException => (503, exception.Message),
            NotFoundException => (404, exception.Message),
            UnauthorizedException => (401, exception.Message),
            BadRequestException => (400, exception.Message),
            ArgumentException => (400, exception.Message),
            TimeoutException => (504, "İşlem zaman aşımına uğradı. Yapay zeka servisleri şu an yoğun olabilir."),
            System.Text.Json.JsonException => (422, "Veri işleme hatası. Yapay zeka yanıtı beklenmedik bir formatta döndü."),
            _ => (500, "Şu an bağlantı kurulamıyor. Lütfen internetinizi kontrol edin veya az sonra tekrar deneyin.")
        };

        context.Response.StatusCode = statusCode;

        var response = new { message, statusCode };
        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}

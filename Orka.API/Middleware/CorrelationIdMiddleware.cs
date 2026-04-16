using Orka.Core.Interfaces;

namespace Orka.API.Middleware;

/// <summary>
/// Her gelen request'e bir Correlation ID atar.
/// X-Correlation-Id header'ı varsa kullanır, yoksa yeni bir UUID üretir.
/// Tüm log satırlarına propagate edilmek üzere ICorrelationContext'e yazar.
/// Response header'ına da ekler — client'ın log trace yapabilmesi için.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
                            ?? Guid.NewGuid().ToString("N")[..12]; // Kısa format: 12 hex karakter

        correlationContext.CorrelationId = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}

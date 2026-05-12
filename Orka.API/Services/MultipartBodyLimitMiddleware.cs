using Microsoft.Extensions.Options;
using Orka.Infrastructure.Services;

namespace Orka.API.Services;

public sealed class MultipartBodyLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptions<ContentSafetyOptions> _options;

    public MultipartBodyLimitMiddleware(
        RequestDelegate next,
        IOptions<ContentSafetyOptions> options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.HasFormContentType &&
            context.Request.ContentLength is long contentLength &&
            contentLength > _options.Value.Uploads.EffectiveMaxMultipartBodyBytes())
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Multipart istek boyutu izin verilen limiti asiyor."
            });
            return;
        }

        await _next(context);
    }
}

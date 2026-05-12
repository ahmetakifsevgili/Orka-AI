using System.Text;

namespace Orka.API.Services;

public sealed class SecurityHeadersMiddleware
{
    private static readonly string[] DefaultConnectSrc = ["'self'", "https:", "wss:"];
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _next = next;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            ApplyCspHeader(context);
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private void ApplyCspHeader(HttpContext context)
    {
        var cspSection = _configuration.GetSection("SecurityHeaders:Csp");
        var enabled = cspSection.GetValue<bool?>("Enabled") ?? !_environment.IsDevelopment();
        if (!enabled)
            return;

        var reportOnly = cspSection.GetValue<bool>("ReportOnly");
        var headerName = reportOnly
            ? "Content-Security-Policy-Report-Only"
            : "Content-Security-Policy";

        if (context.Response.Headers.ContainsKey("Content-Security-Policy") ||
            context.Response.Headers.ContainsKey("Content-Security-Policy-Report-Only"))
        {
            return;
        }

        context.Response.Headers[headerName] = BuildPolicy(cspSection);
    }

    private static string BuildPolicy(IConfigurationSection cspSection)
    {
        var additionalConnectSrc = cspSection
            .GetSection("AdditionalConnectSrc")
            .Get<string[]>() ?? [];

        var connectSrc = DefaultConnectSrc
            .Concat(additionalConnectSrc.Select(s => s.Trim()))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder();
        builder.Append("default-src 'self'; ");
        builder.Append("object-src 'none'; ");
        builder.Append("base-uri 'self'; ");
        builder.Append("frame-ancestors 'none'; ");
        builder.Append("script-src 'self'; ");
        builder.Append("style-src 'self' 'unsafe-inline'; ");
        builder.Append("img-src 'self' data: blob: https://image.pollinations.ai; ");
        builder.Append("font-src 'self' data:; ");
        builder.Append("connect-src ");
        builder.Append(string.Join(' ', connectSrc));
        builder.Append("; ");
        builder.Append("media-src 'self' blob: data:; ");
        builder.Append("frame-src 'none'");
        return builder.ToString();
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orka.Infrastructure.Security;

namespace Orka.Infrastructure.Utilities;

/// <summary>
/// Safe AI provider diagnostics. This logger never writes raw prompts, request
/// bodies, response bodies, source chunks, tool payloads, or stack traces.
/// File logging is opt-in and development-only.
/// </summary>
public static class AiDebugLogger
{
    private const int MaxSafeValueLength = 120;
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Orka", "logs");

    private static readonly string LogFile = Path.Combine(LogDir, "ai_debug.log");
    private static readonly Regex StatusRegex = new(@"(?im)^\s*Status\s*:\s*(?<status>\d{3})\b", RegexOptions.Compiled);
    private static readonly Regex ModelRegex = new(@"(?im)^\s*Model\s*:\s*(?<model>[^\r\n]+)", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"(?im)^\s*URL\s*:\s*(?<url>[^\r\n]+)", RegexOptions.Compiled);

    public static void LogRequest(string provider, string content, ILogger? logger = null)
    {
        var safeProvider = SanitizeIdentifier(provider, "provider");
        var summary = BuildSafeSummary(content);
        logger?.LogDebug("[AI-DEBUG] {Provider} REQUEST: {Summary}", safeProvider, summary);
        AppendIfEnabled(BuildSafeLogPreview(provider, "REQUEST", content));
    }

    public static void LogResponse(string provider, string content, ILogger? logger = null)
    {
        var safeProvider = SanitizeIdentifier(provider, "provider");
        var summary = BuildSafeSummary(content);
        logger?.LogDebug("[AI-DEBUG] {Provider} RESPONSE: {Summary}", safeProvider, summary);
        AppendIfEnabled(BuildSafeLogPreview(provider, "RESPONSE", content));
    }

    public static void LogError(string provider, string error, ILogger? logger = null)
    {
        var safeProvider = SanitizeIdentifier(provider, "provider");
        var summary = BuildSafeSummary(error);
        logger?.LogWarning("[AI-DEBUG] {Provider} ERROR: {Summary}", safeProvider, summary);
        AppendIfEnabled(BuildSafeLogPreview(provider, "ERROR", error));
    }

    public static string BuildSafeLogPreview(string provider, string operation, string? content)
    {
        var safeProvider = SanitizeIdentifier(provider, "provider");
        var safeOperation = SanitizeIdentifier(operation, "operation").ToUpperInvariant();
        return $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} provider={safeProvider} operation={safeOperation} {BuildSafeSummary(content)}";
    }

    public static bool IsFileLoggingEnabledForCurrentEnvironment() => IsFileLoggingEnabled();

    private static string BuildSafeSummary(string? content)
    {
        var value = content ?? string.Empty;
        var parts = new List<string>
        {
            $"contentLength={value.Length}",
            $"contentHash={Hash(value)}"
        };

        var status = Capture(StatusRegex, value, "status");
        if (!string.IsNullOrWhiteSpace(status))
            parts.Add($"httpStatus={status}");

        var model = SanitizeIdentifier(Capture(ModelRegex, value, "model"), string.Empty);
        if (!string.IsNullOrWhiteSpace(model))
            parts.Add($"model={model}");

        var url = SanitizeUrl(Capture(UrlRegex, value, "url"));
        if (!string.IsNullOrWhiteSpace(url))
            parts.Add($"endpoint={url}");

        var unsafeCount = CountUnsafeSignals(value);
        if (unsafeCount > 0)
            parts.Add($"redactedFieldCount={unsafeCount}");

        return string.Join(' ', parts);
    }

    private static void AppendIfEnabled(string content)
    {
        if (!IsFileLoggingEnabled())
            return;

        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(LogFile, content + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never affect provider request handling.
        }
    }

    private static bool IsFileLoggingEnabled()
    {
        if (!IsTrue(Environment.GetEnvironmentVariable("ORKA_AI_DEBUG_LOGGING")))
            return false;

        return IsDevelopment(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")) ||
               IsDevelopment(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
    }

    private static bool IsDevelopment(string? value) =>
        string.Equals(value, "Development", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static string Capture(Regex regex, string value, string groupName)
    {
        var match = regex.Match(value);
        return match.Success ? match.Groups[groupName].Value.Trim() : string.Empty;
    }

    private static string SanitizeIdentifier(string? value, string fallback)
    {
        var redacted = SensitiveDataRedactor.Redact(value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(redacted))
            return fallback;

        if (CountUnsafeSignals(redacted) > 0)
            return fallback;

        var safeChars = redacted
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '/' or ':')
            .ToArray();
        var safe = new string(safeChars).Trim('/', ':');

        if (string.IsNullOrWhiteSpace(safe))
            return fallback;

        return safe.Length <= MaxSafeValueLength ? safe : safe[..MaxSafeValueLength];
    }

    private static string SanitizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return string.Empty;

        var safePath = uri.AbsolutePath.Length <= MaxSafeValueLength
            ? uri.AbsolutePath
            : uri.AbsolutePath[..MaxSafeValueLength];

        if (CountUnsafeSignals($"{uri.Host}{safePath}") > 0)
            return string.Empty;

        return $"{uri.Scheme}://{uri.Host}{safePath}";
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static int CountUnsafeSignals(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var markers = new[]
        {
            "rawPrompt",
            "hiddenPrompt",
            "systemPrompt",
            "developerPrompt",
            "rawProviderPayload",
            "rawSourceChunk",
            "rawToolPayload",
            "debugTrace",
            "localPath",
            "apiKey",
            "secret",
            "token",
            "answerKey",
            "correctAnswer",
            "stackTrace",
            "ownerId",
            "userId",
            "C:\\",
            "D:\\",
            "/Users/",
            "/home/",
            "/var/"
        };

        return markers.Count(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

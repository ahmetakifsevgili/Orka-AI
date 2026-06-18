using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Orka.Infrastructure.Security;

namespace Orka.Infrastructure.Utilities;

/// <summary>
/// Production-safe helpers for application logs. Keep logs useful without
/// writing raw learner identifiers, local paths, prompts, provider bodies, or
/// stack traces.
/// </summary>
public static class LogPrivacyGuard
{
    private const int DefaultMaxLength = 160;
    public static readonly Regex GuidRegex = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled);

    private static readonly Regex WindowsPathRegex = new(
        @"\b[A-Za-z]:\\[^\s""']+",
        RegexOptions.Compiled);

    private static readonly Regex UnixPathRegex = new(
        @"(?<![A-Za-z0-9])/(Users|home|var|tmp|etc|mnt|workspace|app)(/[^\s""']*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] UnsafeMarkers =
    [
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
        "provider payload",
        "provider response"
    ];

    public static string SafeId(Guid id, string prefix) => SafeId((Guid?)id, prefix);

    public static string SafeId(Guid? id, string prefix)
    {
        var safePrefix = SafePrefix(prefix);
        if (!id.HasValue || id.Value == Guid.Empty)
            return $"{safePrefix}_none";

        return $"{safePrefix}_{Hash(id.Value.ToString("N"))}";
    }

    public static string SafeTextRef(string? value, string prefix)
    {
        var safePrefix = SafePrefix(prefix);
        if (string.IsNullOrWhiteSpace(value))
            return $"{safePrefix}_none";

        return $"{safePrefix}_{Hash(value.Trim())}";
    }

    public static string SafeMessage(string? value, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized = SensitiveDataRedactor.Redact(value)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        sanitized = GuidRegex.Replace(sanitized, match =>
        {
            return $"id_ref_{Hash(match.Value.ToLowerInvariant())}";
        });
        sanitized = WindowsPathRegex.Replace(sanitized, "path_ref");
        sanitized = UnixPathRegex.Replace(sanitized, "path_ref");

        foreach (var marker in UnsafeMarkers)
        {
            sanitized = Regex.Replace(
                sanitized,
                Regex.Escape(marker),
                "redacted",
                RegexOptions.IgnoreCase);
        }

        if (sanitized.Length > Math.Max(16, maxLength))
            sanitized = sanitized[..Math.Max(16, maxLength)];

        return sanitized;
    }

    public static string SafeExceptionType(Exception? exception) =>
        SafeMessage(exception?.GetType().Name, 80) is { Length: > 0 } value ? value : "exception";

    public static bool ContainsUnsafeMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (GuidRegex.IsMatch(value) || WindowsPathRegex.IsMatch(value) || UnixPathRegex.IsMatch(value))
            return true;

        return UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string SafePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return "id";

        var chars = prefix
            .Where(c => char.IsLetterOrDigit(c) || c is '_' or '-')
            .Take(16)
            .ToArray();

        return chars.Length == 0 ? "id" : new string(chars).ToLowerInvariant();
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Orka.Infrastructure.Security;

public static class SensitiveDataRedactor
{
    private static readonly Regex EmailRegex = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JwtRegex = new(
        @"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b",
        RegexOptions.Compiled);

    private static readonly Regex SecretAssignmentRegex = new(
        @"(?i)\b(api[_-]?key|secret|token|password|refresh[_-]?token|access[_-]?token)\b['""]?\s*[:=]\s*['""]?[^'"",\s;}]+",
        RegexOptions.Compiled);

    private static readonly Regex SecretConfigPathRegex = new(
        @"(?i)\b(?:JWT|AI|ConnectionStrings|Redis|OpenTelemetry)(?::[A-Za-z0-9_.-]+)*(?::(?:Secret|Token|ApiKey|Password|ConnectionString|Key))(?:[A-Za-z0-9_.:-]*)?",
        RegexOptions.Compiled);

    private static readonly Regex LongTokenRegex = new(
        @"\b[A-Za-z0-9_+/=-]{48,}\b",
        RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var redacted = EmailRegex.Replace(value, MaskEmail);
        redacted = JwtRegex.Replace(redacted, "[REDACTED_TOKEN]");
        redacted = SecretAssignmentRegex.Replace(redacted, match =>
        {
            var separator = match.Value.Contains('=') ? "=" : ":";
            var key = match.Value.Split(separator[0], 2)[0].Trim();
            return $"{key}{separator}[REDACTED]";
        });
        redacted = SecretConfigPathRegex.Replace(redacted, "[REDACTED_CONFIG_KEY]");
        redacted = LongTokenRegex.Replace(redacted, "[REDACTED_TOKEN]");
        return redacted;
    }

    public static string MaskFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "file:unknown";

        var extension = Path.GetExtension(fileName);
        if (extension.Length > 12)
            extension = string.Empty;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fileName.Trim()));
        return $"file:{Convert.ToHexString(hash)[..12].ToLowerInvariant()}{extension.ToLowerInvariant()}";
    }

    private static string MaskEmail(Match match)
    {
        var value = match.Value;
        var at = value.IndexOf('@');
        if (at <= 0)
            return "[REDACTED_EMAIL]";

        var first = value[0];
        var domain = value[(at + 1)..];
        return $"{first}***@{domain}";
    }
}

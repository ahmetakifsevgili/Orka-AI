using System.Text.Json;

namespace Orka.Infrastructure.Services;

internal sealed record TelemetryPrivacyResult(
    bool IsSafe,
    IReadOnlyList<string> BlockedTerms,
    IReadOnlyDictionary<string, string> SafeMetadata);

internal static class TelemetryPrivacyGuard
{
    private const int MaxValueLength = 240;
    private static readonly string[] UnsafeKeyParts =
    [
        "rawprompt",
        "systemprompt",
        "hiddenprompt",
        "rawproviderpayload",
        "rawtoolpayload",
        "rawsourcechunk",
        "rawanswerrows",
        "apikey",
        "api_key",
        "secret",
        "stacktrace",
        "localpath",
        "storagekey",
        "debugtrace",
        "providerdebug"
    ];

    private static readonly string[] UnsafeValueParts =
    [
        "-----BEGIN",
        "api_key",
        "apikey",
        "secret=",
        "stacktrace",
        "rawPrompt",
        "systemPrompt",
        "hiddenPrompt",
        "rawProviderPayload",
        "rawToolPayload",
        "rawSourceChunk",
        "rawAnswerRows",
        "D:\\",
        "C:\\",
        "/Users/",
        "/home/",
        "/var/",
        "AppData\\"
    ];

    public static string? SanitizeJson(string? metadataJson, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        var result = Validate(metadataJson, null);
        if (result.SafeMetadata.Count == 0)
            return null;

        var json = JsonSerializer.Serialize(result.SafeMetadata);
        return json.Length <= maxLength ? json : json[..maxLength];
    }

    public static TelemetryPrivacyResult Validate(string? metadataJson, IReadOnlyDictionary<string, string>? metadata)
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var safe = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (metadata is not null)
        {
            foreach (var pair in metadata)
                AddIfSafe(pair.Key, pair.Value, safe, blocked);
        }

        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            try
            {
                using var document = JsonDocument.Parse(metadataJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                        AddIfSafe(property.Name, JsonElementToSafeString(property.Value), safe, blocked);
                }
                else
                {
                    AddIfSafe("metadata", metadataJson, safe, blocked);
                }
            }
            catch
            {
                AddIfSafe("metadata_parse_status", "invalid_json_ignored", safe, blocked);
            }
        }

        return new TelemetryPrivacyResult(blocked.Count == 0, blocked.Order(StringComparer.OrdinalIgnoreCase).ToArray(), safe);
    }

    public static IReadOnlyDictionary<string, string> FromJson(string? metadataJson)
    {
        return Validate(metadataJson, null).SafeMetadata;
    }

    private static void AddIfSafe(string key, string? value, Dictionary<string, string> safe, HashSet<string> blocked)
    {
        var normalizedKey = Normalize(key);
        if (string.IsNullOrWhiteSpace(normalizedKey))
            return;

        if (UnsafeKeyParts.Any(part => normalizedKey.Contains(part, StringComparison.OrdinalIgnoreCase)))
        {
            blocked.Add(key);
            return;
        }

        var cleanValue = value?.Trim();
        if (string.IsNullOrWhiteSpace(cleanValue))
            return;

        foreach (var part in UnsafeValueParts)
        {
            if (cleanValue.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                blocked.Add(key);
                return;
            }
        }

        safe[key.Trim()] = cleanValue.Length <= MaxValueLength ? cleanValue : cleanValue[..MaxValueLength];
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string JsonElementToSafeString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => value.GetRawText()
        };
    }
}

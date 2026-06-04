using System.Text.Json.Nodes;

namespace Orka.Infrastructure.Utilities;

public static class PublicTextNormalizer
{
    private static readonly IReadOnlyList<(string Dirty, string Clean)> MojibakePairs =
    [
        ("\u00c3\u00a7", "\u00e7"),
        ("\u00c3\u0087", "\u00c7"),
        ("\u00c3\u00bc", "\u00fc"),
        ("\u00c3\u009c", "\u00dc"),
        ("\u00c3\u00b6", "\u00f6"),
        ("\u00c3\u0096", "\u00d6"),
        ("\u00c4\u00b1", "\u0131"),
        ("\u00c4\u00b0", "\u0130"),
        ("\u00c4\u009f", "\u011f"),
        ("\u00c4\u009e", "\u011e"),
        ("\u00c5\u009f", "\u015f"),
        ("\u00c5\u009e", "\u015e"),
        ("\u00c2\u00a0", " "),
        ("\u00e2\u20ac\u2122", "'"),
        ("\u00e2\u20ac\u0153", "\""),
        ("\u00e2\u20ac\u009d", "\""),
        ("\u00e2\u20ac\u201c", "-"),
        ("\u00e2\u20ac\u0093", "-"),
        ("\u00e2\u20ac\u0094", "-"),
        ("\u00e2\u0080\u0098", "'"),
        ("\u00e2\u0080\u0099", "'"),
        ("\u00e2\u0080\u009c", "\""),
        ("\u00e2\u0080\u009d", "\""),
        ("\u00e2\u0080\u0093", "-"),
        ("\u00e2\u0080\u0094", "-"),
        ("\u00e2\u0086\u0092", "->"),
        ("\u00e2\u0086\u0090", "<-"),
        ("\u00e2\u0086\u0091", "^"),
        ("\u00e2\u0086\u0093", "v"),
        ("\u00e2\u0080\u00a2", "-"),
        ("\u00e2\u009a\u00a0\u00ef\u00b8\u008f", "[warning]"),
        ("\u00ef\u00b8\u008f", ""),
        ("\u00c2\u00ab", "\""),
        ("\u00c2\u00bb", "\"")
    ];

    public static string RepairMojibake(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

        var repaired = value;
        foreach (var (dirty, clean) in MojibakePairs)
        {
            repaired = repaired.Replace(dirty, clean, StringComparison.Ordinal);
        }

        return repaired;
    }

    public static void RepairJsonStrings(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kvp => kvp.Key).ToArray())
                {
                    if (obj[key] is JsonValue value &&
                        value.TryGetValue<string>(out var text))
                    {
                        obj[key] = RepairMojibake(text);
                    }
                    else
                    {
                        RepairJsonStrings(obj[key]);
                    }
                }
                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    RepairJsonStrings(child);
                }
                break;
        }
    }
}

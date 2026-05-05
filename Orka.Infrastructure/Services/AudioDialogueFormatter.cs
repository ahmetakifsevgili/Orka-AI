using System.Text.RegularExpressions;

namespace Orka.Infrastructure.Services;

public static class AudioDialogueFormatter
{
    private static readonly Regex SpeakerBlockRegex =
        new(@"\[(HOCA|ASISTAN|AS\u0130STAN|KONUK|OGRETMEN|TEACHER|ASSISTANT|GUEST)\]\s*:\s*(.+?)(?=\n\s*\[(?:HOCA|ASISTAN|AS\u0130STAN|KONUK|OGRETMEN|TEACHER|ASSISTANT|GUEST)\]\s*:|\z)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SpeakerTagRegex =
        new(@"\[(HOCA|ASISTAN|AS\u0130STAN|KONUK|OGRETMEN|TEACHER|ASSISTANT|GUEST)\]\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeScript(string? raw)
    {
        var clean = (raw ?? string.Empty).Replace("```", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = "Sesli anlatim metni su anda bos geldi; kisa bir ozetle devam ediyoruz.";
        }

        if (!SpeakerTagRegex.IsMatch(clean))
        {
            clean = $"[HOCA]: {clean}";
        }

        return SpeakerTagRegex.Replace(clean, match => $"[{NormalizeSpeaker(match.Groups[1].Value)}]:").Trim();
    }

    public static IReadOnlyList<string> ParseSpeakers(string? script)
    {
        var normalized = NormalizeScript(script);
        var speakers = SpeakerTagRegex.Matches(normalized)
            .Select(m => NormalizeSpeaker(m.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return speakers.Count == 0 ? ["HOCA"] : speakers;
    }

    public static IReadOnlyList<(string Speaker, string Text)> ParseSegments(string? script)
    {
        var normalized = NormalizeScript(script);
        var segments = SpeakerBlockRegex.Matches(normalized)
            .Select(m => (Speaker: NormalizeSpeaker(m.Groups[1].Value), Text: m.Groups[2].Value.Trim()))
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .ToList();

        if (segments.Count == 0)
        {
            segments.Add(("HOCA", normalized.Replace("[HOCA]:", string.Empty).Trim()));
        }

        return segments;
    }

    private static string NormalizeSpeaker(string? raw)
    {
        var label = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return label switch
        {
            "AS\u0130STAN" or "ASSISTANT" => "ASISTAN",
            "OGRETMEN" or "TEACHER" => "HOCA",
            "GUEST" => "KONUK",
            "HOCA" or "ASISTAN" or "KONUK" => label,
            _ => "HOCA"
        };
    }
}

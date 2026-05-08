namespace Orka.Infrastructure.Services;

internal static class WikiCitationText
{
    public static string ExtractClaimNearCitation(string answer, int citationIndex)
    {
        if (string.IsNullOrWhiteSpace(answer)) return string.Empty;
        var start = Math.Max(0, citationIndex - 240);
        var length = Math.Min(answer.Length - start, 320);
        return TrimForStorage(answer.Substring(start, length), 320);
    }

    public static string TrimForStorage(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var clean = value.Trim();
        return clean.Length <= maxChars ? clean : clean[..maxChars];
    }
}

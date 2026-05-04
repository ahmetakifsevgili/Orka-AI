using System.Text.RegularExpressions;
using Orka.Core.DTOs.Korteks;

namespace Orka.Infrastructure.Services;

public static partial class KorteksSourceEvidenceExtractor
{
    private const int SnippetLimit = 500;

    public static List<SourceEvidenceDto> Extract(string provider, string toolName, string? resultText, DateTimeOffset retrievedAt)
    {
        if (string.IsNullOrWhiteSpace(resultText))
        {
            return [];
        }

        var lines = resultText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries);
        var useStructuredWebResultUrls =
            provider.Equals("WebSearch", StringComparison.OrdinalIgnoreCase) &&
            resultText.Contains("[Kaynak", StringComparison.OrdinalIgnoreCase) &&
            resultText.Contains("URL:", StringComparison.OrdinalIgnoreCase);

        var sources = new List<SourceEvidenceDto>();
        foreach (var candidate in FindSourceUrlCandidates(resultText, lines, useStructuredWebResultUrls))
        {
            var url = CleanUrl(candidate.Value);
            if (!IsValidSourceUrl(url))
            {
                continue;
            }

            var title = FindTitle(lines, candidate.LineIndex, provider, url);
            var snippet = FindSnippet(lines, candidate.LineIndex);
            var publishedAt = TryFindPublishedAt(lines, candidate.LineIndex);
            var externalId = TryFindExternalId(provider, url, lines, candidate.LineIndex);

            sources.Add(new SourceEvidenceDto(
                Provider: provider,
                ToolName: toolName,
                Url: url,
                Title: title,
                Snippet: Truncate(snippet),
                PublishedAt: publishedAt,
                RetrievedAt: retrievedAt,
                RelevanceScore: null,
                SourceType: GuessSourceType(provider, url),
                ExternalId: externalId,
                Warning: null));
        }

        return sources
            .GroupBy(s => NormalizeUrl(s.Url), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static IEnumerable<(string Value, int LineIndex)> FindSourceUrlCandidates(
        string resultText,
        string[] lines,
        bool useStructuredWebResultUrls)
    {
        if (useStructuredWebResultUrls)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!IsStructuredWebResultUrl(lines, i))
                {
                    continue;
                }

                foreach (Match match in UrlRegex().Matches(lines[i]))
                {
                    yield return (match.Value, i);
                }
            }

            yield break;
        }

        foreach (Match match in UrlRegex().Matches(resultText))
        {
            yield return (match.Value, FindLineIndex(lines, match.Value));
        }
    }

    public static string? FindDegradedMarker(string? resultText)
    {
        if (string.IsNullOrWhiteSpace(resultText))
        {
            return null;
        }

        var match = DegradedRegex().Match(resultText);
        return match.Success ? match.Value : null;
    }

    private static int FindLineIndex(string[] lines, string value)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsStructuredWebResultUrl(string[] lines, int lineIndex) =>
        lineIndex >= 0 &&
        lines[lineIndex].StartsWith("URL:", StringComparison.OrdinalIgnoreCase);

    private static string FindTitle(string[] lines, int lineIndex, string provider, string url)
    {
        var candidates = new List<string>();
        if (lineIndex > 0) candidates.Add(lines[lineIndex - 1]);
        if (lineIndex > 1) candidates.Add(lines[lineIndex - 2]);
        if (lineIndex >= 0) candidates.Add(lines[lineIndex]);

        foreach (var candidate in candidates)
        {
            var cleaned = CleanTitle(candidate);
            if (!string.IsNullOrWhiteSpace(cleaned) && !cleaned.StartsWith("URL", StringComparison.OrdinalIgnoreCase) && !cleaned.StartsWith("Kaynak", StringComparison.OrdinalIgnoreCase))
            {
                return cleaned;
            }
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? $"{provider}: {uri.Host}"
            : provider;
    }

    private static string? FindSnippet(string[] lines, int lineIndex)
    {
        if (lineIndex < 0)
        {
            return null;
        }

        for (var i = lineIndex + 1; i < Math.Min(lines.Length, lineIndex + 5); i++)
        {
            if (lines[i].StartsWith("Icerik:", StringComparison.OrdinalIgnoreCase) ||
                lines[i].StartsWith("İçerik:", StringComparison.OrdinalIgnoreCase) ||
                lines[i].StartsWith("TLDR:", StringComparison.OrdinalIgnoreCase) ||
                lines[i].StartsWith("Ozet:", StringComparison.OrdinalIgnoreCase) ||
                lines[i].StartsWith("Özet:", StringComparison.OrdinalIgnoreCase) ||
                lines[i].StartsWith("Aciklama:", StringComparison.OrdinalIgnoreCase) ||
                lines[i].StartsWith("Açıklama:", StringComparison.OrdinalIgnoreCase))
            {
                return lines[i].Split(':', 2).LastOrDefault()?.Trim();
            }
        }

        return null;
    }

    private static DateTimeOffset? TryFindPublishedAt(string[] lines, int lineIndex)
    {
        var start = Math.Max(0, lineIndex - 3);
        var end = Math.Min(lines.Length, lineIndex + 4);
        for (var i = start; i < end; i++)
        {
            var match = DateRegex().Match(lines[i]);
            if (match.Success && DateTimeOffset.TryParse(match.Value, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? TryFindExternalId(string provider, string url, string[] lines, int lineIndex)
    {
        if (provider.Equals("YouTube", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in query)
            {
                var pair = part.Split('=', 2);
                if (pair.Length == 2 && pair[0].Equals("v", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pair[1]);
                }
            }
        }

        var start = Math.Max(0, lineIndex - 3);
        var end = Math.Min(lines.Length, lineIndex + 4);
        for (var i = start; i < end; i++)
        {
            if (lines[i].Contains("VideoId:", StringComparison.OrdinalIgnoreCase))
            {
                return lines[i].Split(':', 2).LastOrDefault()?.Trim(' ', '`');
            }
        }

        return null;
    }

    private static string GuessSourceType(string provider, string url)
    {
        if (provider.Equals("Academic", StringComparison.OrdinalIgnoreCase)) return "academic";
        if (provider.Equals("Wikipedia", StringComparison.OrdinalIgnoreCase)) return "encyclopedia";
        if (provider.Equals("YouTube", StringComparison.OrdinalIgnoreCase)) return "video";
        if (url.Contains("doi.org", StringComparison.OrdinalIgnoreCase)) return "academic";
        return "web";
    }

    private static string CleanTitle(string value)
    {
        var cleaned = value
            .Replace("**", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal)
            .Trim('-', ' ', '\t');

        cleaned = Regex.Replace(cleaned, @"^\d+\.\s*", "");
        cleaned = Regex.Replace(cleaned, @"^\[Kaynak\s+\d+\]\s*", "", RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

    private static string CleanUrl(string value) =>
        value.Trim().TrimEnd('.', ',', ')', ']', '}', '"', '\'');

    public static bool IsValidSourceUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        !string.IsNullOrWhiteSpace(uri.Host) &&
        !IsNoisySourceUri(uri);

    private static bool IsNoisySourceUri(Uri uri)
    {
        var path = uri.AbsolutePath.ToLowerInvariant();
        var query = Uri.UnescapeDataString(uri.Query).ToLowerInvariant();
        var combined = $"{path}?{query}";

        if (path.Contains("sitemap", StringComparison.Ordinal) ||
            path.Contains("/login", StringComparison.Ordinal) ||
            path.Contains("/signin", StringComparison.Ordinal) ||
            path.Contains("/register", StringComparison.Ordinal) ||
            path.Contains("/signup", StringComparison.Ordinal) ||
            path.Contains("/accounts/login", StringComparison.Ordinal) ||
            path.Contains("/wp-login", StringComparison.Ordinal))
        {
            return true;
        }

        if (uri.Host.Equals("www.w3.org", StringComparison.OrdinalIgnoreCase) &&
            path.Equals("/2000/svg", StringComparison.Ordinal))
        {
            return true;
        }

        if (path.Contains("/_next/image", StringComparison.Ordinal) ||
            path.Contains("/static/media/", StringComparison.Ordinal) ||
            path.Contains("/assets/", StringComparison.Ordinal) ||
            path.Contains("/asset/", StringComparison.Ordinal))
        {
            return true;
        }

        return NoisyAssetExtensionRegex().IsMatch(combined) ||
               NoisyAssetNameRegex().IsMatch(combined);
    }

    private static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string? Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length > SnippetLimit ? value[..SnippetLimit] + "..." : value;

    [GeneratedRegex(@"https?://[^\s\])}""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\[(web|wikipedia|semantic-scholar|arxiv|youtube|wolfram):(?:degraded|disabled|error)\][^\n\r]*", RegexOptions.IgnoreCase)]
    private static partial Regex DegradedRegex();

    [GeneratedRegex(@"\b(19|20)\d{2}(-\d{2}-\d{2})?\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\.(svg|png|jpe?g|gif|webp|ico|css|js|woff2?)(\?|#|$)", RegexOptions.IgnoreCase)]
    private static partial Regex NoisyAssetExtensionRegex();

    [GeneratedRegex(@"(^|[/_-])(favicon|sprite|logo)([/_.-]|\?|#|$)", RegexOptions.IgnoreCase)]
    private static partial Regex NoisyAssetNameRegex();
}

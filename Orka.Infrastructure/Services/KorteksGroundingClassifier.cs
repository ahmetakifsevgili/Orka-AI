using Orka.Core.DTOs.Korteks;
using Orka.Core.Enums;

namespace Orka.Infrastructure.Services;

public static class KorteksGroundingClassifier
{
    private static readonly string[] DegradedMarkers =
    [
        "[web:degraded]",
        "[wikipedia:degraded]",
        "[semantic-scholar:degraded]",
        "[arxiv:degraded]",
        "[youtube:degraded]",
        "[youtube:disabled]",
        "[wolfram:disabled]",
        "[wolfram:error]",
        "[error]",
        "degraded",
        "devre disi",
        "gecici olarak kullanilamiyor"
    ];

    public static GroundingMode Classify(string? report, IReadOnlyCollection<SourceEvidenceDto> sources, IReadOnlyCollection<ToolCallEvidenceDto> calls)
    {
        var trimmed = report?.Trim() ?? string.Empty;
        var uniqueUrlCount = sources
            .Select(s => NormalizeUrl(s.Url))
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (string.IsNullOrWhiteSpace(trimmed) || (uniqueUrlCount == 0 && IsOnlyDegradedMarkers(trimmed)))
        {
            return GroundingMode.BlockedProvider;
        }

        var hasSuccessfulSourceTool = calls.Any(c =>
            c.Success &&
            IsSourceProvider(c.Provider) &&
            string.IsNullOrWhiteSpace(c.DegradedMarker));

        if (uniqueUrlCount >= 2 && hasSuccessfulSourceTool)
        {
            return GroundingMode.SourceGrounded;
        }

        if (uniqueUrlCount >= 1)
        {
            return GroundingMode.PartialSourceGrounded;
        }

        return GroundingMode.FallbackInternalKnowledge;
    }

    public static bool IsFallback(GroundingMode mode) =>
        mode is GroundingMode.FallbackInternalKnowledge or GroundingMode.BlockedProvider;

    internal static bool IsSourceProvider(string provider) =>
        provider.Equals("WebSearch", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("Wikipedia", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("Academic", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("YouTube", StringComparison.OrdinalIgnoreCase);

    private static bool IsOnlyDegradedMarkers(string report)
    {
        var withoutMarkers = report;
        foreach (var marker in DegradedMarkers)
        {
            withoutMarkers = withoutMarkers.Replace(marker, "", StringComparison.OrdinalIgnoreCase);
        }

        withoutMarkers = withoutMarkers
            .Replace("[", "", StringComparison.Ordinal)
            .Replace("]", "", StringComparison.Ordinal)
            .Replace(":", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Trim();

        return withoutMarkers.Length < 40 && DegradedMarkers.Any(m => report.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}

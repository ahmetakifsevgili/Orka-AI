using System.Text;
using System.Text.RegularExpressions;
using Orka.Core.DTOs.Korteks;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed partial class PlanResearchCompressor : IPlanResearchCompressor
{
    private static readonly string[] FreshnessKeywords =
    [
        "202", "current", "latest", "recent", "guncel", "güncel", "bugun", "bugün",
        "trend", "release", "version", "surum", "sürüm", "now"
    ];

    private static readonly string[] CurriculumKeywords =
    [
        "curriculum", "roadmap", "learning path", "mufredat", "müfredat", "sira",
        "sıra", "sequence", "module", "lesson", "practice", "project", "lab"
    ];

    private static readonly string[] PrerequisiteKeywords =
    [
        "prerequisite", "before", "required", "temel", "on kosul", "ön koşul",
        "bilmesi", "foundation", "hazirlik", "hazırlık"
    ];

    private static readonly string[] MisconceptionKeywords =
    [
        "misconception", "mistake", "common error", "confuse", "yanilgi", "yanılgı",
        "hata", "karistir", "karıştır", "zorlan"
    ];

    private static readonly string[] YouTubeKeywords =
    [
        "youtube", "video", "playlist", "channel", "transcript", "teaching flow",
        "examples:", "practice ideas:", "common mistakes:"
    ];

    public CompressedPlanResearchContextDto Compress(
        KorteksResearchResultDto researchResult,
        PlanResearchCompressionOptions? options = null)
    {
        var opt = options ?? new PlanResearchCompressionOptions();
        var lines = CleanReportLines(researchResult.Report, opt).ToList();
        var warnings = researchResult.ProviderFailures
            .Concat(researchResult.Warnings)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => TrimItem(w, opt.MaxItemLength))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allowSources = researchResult.GroundingMode is GroundingMode.SourceGrounded or GroundingMode.PartialSourceGrounded;
        var topSources = allowSources ? SelectTopSources(researchResult.Sources, opt) : [];

        return new CompressedPlanResearchContextDto
        {
            Topic = researchResult.Topic,
            GroundingMode = researchResult.GroundingMode,
            SourceCount = allowSources ? topSources.Count : 0,
            TopSources = topSources,
            ProviderWarnings = warnings,
            FallbackWarning = BuildFallbackWarning(researchResult.GroundingMode, warnings),
            KeyFacts = ExtractKeyFacts(lines, opt),
            WebFreshnessFacts = ExtractByKeywords(lines, FreshnessKeywords, opt.MaxFreshnessFacts, opt),
            YouTubeLearningReferences = ExtractYouTubeReferences(lines, researchResult.Sources, opt),
            CurriculumMapHints = ExtractByKeywords(lines, CurriculumKeywords, opt.MaxCurriculumHints, opt),
            PrerequisiteHints = ExtractByKeywords(lines, PrerequisiteKeywords, opt.MaxPrerequisiteHints, opt),
            LikelyMisconceptions = ExtractByKeywords(lines, MisconceptionKeywords, opt.MaxMisconceptions, opt),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    public string BuildPromptBlock(
        CompressedPlanResearchContextDto context,
        PlanResearchCompressionOptions? options = null)
    {
        var opt = options ?? new PlanResearchCompressionOptions();
        var sections = new List<PromptSection>
        {
            new("TopSources", FormatSources(context.TopSources, opt)),
            new("KeyFacts", context.KeyFacts),
            new("WebFreshnessFacts", context.WebFreshnessFacts),
            new("YouTubeLearningReferences", context.YouTubeLearningReferences),
            new("CurriculumMapHints", context.CurriculumMapHints),
            new("PrerequisiteHints", context.PrerequisiteHints),
            new("LikelyMisconceptions", context.LikelyMisconceptions)
        };

        var providerWarnings = context.ProviderWarnings
            .Take(5)
            .Select(w => TrimItem(w, opt.MaxItemLength))
            .ToList();

        while (BuildPromptBlock(context, providerWarnings, sections).Length > opt.MaxTotalChars && sections.Any(s => s.Items.Count > 0))
        {
            RemoveLowestPriorityItem(sections);
        }

        var block = BuildPromptBlock(context, providerWarnings, sections);
        if (block.Length <= opt.MaxTotalChars)
        {
            return block;
        }

        return block[..Math.Max(0, opt.MaxTotalChars)];
    }

    private static List<SourceEvidenceDto> SelectTopSources(
        IEnumerable<SourceEvidenceDto> sources,
        PlanResearchCompressionOptions options)
    {
        return sources
            .Where(s => IsValidUrl(s.Url))
            .GroupBy(s => NormalizeUrl(s.Url), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(s => s.RelevanceScore ?? 0)
            .ThenBy(s => ProviderRank(s.Provider))
            .Take(options.MaxSources)
            .Select(s => s with
            {
                Title = TrimItem(s.Title, 120),
                Snippet = TrimItem(s.Snippet, Math.Min(options.MaxItemLength, 220))
            })
            .ToList();
    }

    private static IEnumerable<string> CleanReportLines(string? report, PlanResearchCompressionOptions options)
    {
        if (string.IsNullOrWhiteSpace(report))
        {
            yield break;
        }

        foreach (var raw in report.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = NormalizeLine(raw);
            if (string.IsNullOrWhiteSpace(line) || ShouldDropLine(line))
            {
                continue;
            }

            foreach (var sentence in SentenceRegex().Split(line))
            {
                var cleaned = NormalizeLine(sentence);
                if (string.IsNullOrWhiteSpace(cleaned) || ShouldDropLine(cleaned))
                {
                    continue;
                }

                yield return TrimItem(cleaned, options.MaxItemLength);
            }
        }
    }

    private static List<string> ExtractKeyFacts(IEnumerable<string> lines, PlanResearchCompressionOptions options)
    {
        return TrimAndDeduplicate(
            lines.Where(line =>
                LooksLikeFact(line) &&
                !ContainsAny(line, YouTubeKeywords) &&
                !ContainsAny(line, MisconceptionKeywords)),
            options.MaxKeyFacts,
            options);
    }

    private static List<string> ExtractByKeywords(
        IEnumerable<string> lines,
        IReadOnlyCollection<string> keywords,
        int maxItems,
        PlanResearchCompressionOptions options)
    {
        return TrimAndDeduplicate(
            lines.Where(line => ContainsAny(line, keywords)),
            maxItems,
            options);
    }

    private static List<string> ExtractYouTubeReferences(
        IEnumerable<string> lines,
        IEnumerable<SourceEvidenceDto> sources,
        PlanResearchCompressionOptions options)
    {
        var sourceRefs = sources
            .Where(s => s.Provider.Equals("YouTube", StringComparison.OrdinalIgnoreCase) || s.SourceType?.Equals("video", StringComparison.OrdinalIgnoreCase) == true)
            .Where(s => IsValidUrl(s.Url))
            .Select(s => $"{TrimItem(s.Title, 120)} ({s.Url})");

        var textRefs = lines.Where(line => ContainsAny(line, YouTubeKeywords));
        return TrimAndDeduplicate(sourceRefs.Concat(textRefs), options.MaxYouTubeReferences, options);
    }

    private static List<string> TrimAndDeduplicate(
        IEnumerable<string> values,
        int maxItems,
        PlanResearchCompressionOptions options)
    {
        return values
            .Select(v => TrimItem(v, options.MaxItemLength))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();
    }

    private static string? BuildFallbackWarning(GroundingMode mode, IReadOnlyCollection<string> providerWarnings)
    {
        return mode switch
        {
            GroundingMode.SourceGrounded => null,
            GroundingMode.PartialSourceGrounded => "Research is partially source-grounded; use source-backed facts carefully.",
            GroundingMode.FallbackInternalKnowledge => "Korteks returned fallback/internal-knowledge research; do not present it as current source-grounded evidence.",
            GroundingMode.BlockedProvider => "Korteks research was blocked or empty; continue planning from adaptive context and general curriculum knowledge.",
            _ => providerWarnings.Count > 0 ? "Research grounding is uncertain." : null
        };
    }

    private static string BuildPromptBlock(
        CompressedPlanResearchContextDto context,
        IReadOnlyCollection<string> providerWarnings,
        IReadOnlyCollection<PromptSection> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]");
        sb.AppendLine($"Topic: {context.Topic}");
        sb.AppendLine($"GroundingMode: {context.GroundingMode}");
        sb.AppendLine($"SourceCount: {context.SourceCount}");
        if (!string.IsNullOrWhiteSpace(context.FallbackWarning))
        {
            sb.AppendLine($"FallbackWarning: {context.FallbackWarning}");
        }

        if (providerWarnings.Count > 0)
        {
            sb.AppendLine("ProviderWarnings:");
            foreach (var warning in providerWarnings)
            {
                sb.AppendLine($"- {warning}");
            }
        }

        foreach (var section in sections)
        {
            if (section.Items.Count == 0)
            {
                continue;
            }

            sb.AppendLine($"{section.Name}:");
            foreach (var item in section.Items)
            {
                sb.AppendLine($"- {item}");
            }
        }

        sb.AppendLine("Instruction: Use this compressed Korteks research only for factual/current/topic-breadth support. Do not let it override [ADAPTIF OGRENME BAGLAMI].");
        return sb.ToString();
    }

    private static List<string> FormatSources(IEnumerable<SourceEvidenceDto> sources, PlanResearchCompressionOptions options)
    {
        return sources
            .Select(s =>
            {
                var snippet = string.IsNullOrWhiteSpace(s.Snippet) ? string.Empty : $" - {TrimItem(s.Snippet, Math.Min(options.MaxItemLength, 180))}";
                return $"{s.Provider}: {TrimItem(s.Title, 120)} ({s.Url}){snippet}";
            })
            .ToList();
    }

    private static void RemoveLowestPriorityItem(IReadOnlyList<PromptSection> sections)
    {
        var priority = new[] { "LikelyMisconceptions", "PrerequisiteHints", "CurriculumMapHints", "YouTubeLearningReferences", "WebFreshnessFacts", "KeyFacts", "TopSources" };
        foreach (var name in priority)
        {
            var section = sections.FirstOrDefault(s => s.Name == name);
            if (section is { Items.Count: > 0 })
            {
                section.Items.RemoveAt(section.Items.Count - 1);
                return;
            }
        }
    }

    private static bool LooksLikeFact(string line)
    {
        return line.Length >= 30 &&
               !line.EndsWith(":", StringComparison.Ordinal) &&
               (line.Contains(" is ", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(" are ", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(":", StringComparison.Ordinal) ||
                line.Contains("dir", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("dır", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("tir", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("tır", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldDropLine(string line)
    {
        if (line.Length > 1200)
        {
            return true;
        }

        if (UrlRegex().Matches(line).Count > 2)
        {
            return true;
        }

        return line.Contains("[raw", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("raw provider", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("transcript segment", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("timestamp", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("caption", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("{", StringComparison.Ordinal) ||
               line.StartsWith("}", StringComparison.Ordinal) ||
               line.StartsWith("\"", StringComparison.Ordinal);
    }

    private static bool ContainsAny(string line, IEnumerable<string> keywords) =>
        keywords.Any(keyword => line.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeLine(string value)
    {
        return value
            .Replace("**", "", StringComparison.Ordinal)
            .Replace("```", "", StringComparison.Ordinal)
            .Trim(' ', '\t', '-', '*', '#');
    }

    private static string TrimItem(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = WhitespaceRegex().Replace(value.Trim(), " ");
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static bool IsValidUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return url;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static int ProviderRank(string provider) => provider.ToLowerInvariant() switch
    {
        "websearch" => 5,
        "wikipedia" => 4,
        "youtube" => 3,
        "academic" => 2,
        _ => 1
    };

    private sealed record PromptSection(string Name, List<string> Items);

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"https?://[^\s\])}""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

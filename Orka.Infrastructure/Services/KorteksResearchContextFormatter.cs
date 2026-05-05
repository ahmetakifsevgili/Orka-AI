using System.Text;
using Orka.Core.DTOs.Korteks;
using Orka.Core.Enums;

namespace Orka.Infrastructure.Services;

public static class KorteksResearchContextFormatter
{
    private const int MaxSources = 8;
    private const int MaxSnippetLength = 220;
    private const int MaxReportLength = 6000;
    private const int MaxContextLength = 9000;

    public static string FormatForTutor(KorteksResearchResultDto? result)
    {
        if (result is null)
        {
            return "[KORTEKS SOURCE-GROUNDING]\nStatus: unavailable\nFallback: true\nNo source-backed research result was produced.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("[KORTEKS SOURCE-GROUNDING]");
        builder.AppendLine($"Topic: {TrimLine(result.Topic, 180)}");
        builder.AppendLine($"GroundingMode: {result.GroundingMode}");
        builder.AppendLine($"Fallback: {result.IsFallback.ToString().ToLowerInvariant()}");
        builder.AppendLine($"AcceptedSourceCount: {result.Sources.Count}");
        builder.AppendLine($"ToolCallCount: {result.ProviderCalls.Count(c => c.Invoked)}");

        var successfulToolCalls = result.ProviderCalls
            .Where(c => c.Invoked && c.Success)
            .Select(c => $"{c.Provider}.{c.ToolName}({c.ResultCount})")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (successfulToolCalls.Count > 0)
        {
            builder.AppendLine($"SuccessfulTools: {string.Join(", ", successfulToolCalls)}");
        }

        var failures = result.ProviderFailures
            .Concat(result.ProviderCalls.Where(c => !c.Success && !string.IsNullOrWhiteSpace(c.FailureReason)).Select(c => c.FailureReason!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        if (failures.Count > 0)
        {
            builder.AppendLine("ProviderFailures:");
            foreach (var failure in failures)
            {
                builder.AppendLine($"- {TrimLine(failure, 220)}");
            }
        }

        var warnings = result.Warnings
            .Concat(result.ProviderCalls.Where(c => !string.IsNullOrWhiteSpace(c.DegradedMarker)).Select(c => c.DegradedMarker!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        if (warnings.Count > 0)
        {
            builder.AppendLine("Warnings:");
            foreach (var warning in warnings)
            {
                builder.AppendLine($"- {TrimLine(warning, 220)}");
            }
        }

        if (result.IsFallback ||
            result.GroundingMode is GroundingMode.FallbackInternalKnowledge or GroundingMode.BlockedProvider ||
            result.Sources.Count == 0)
        {
            builder.AppendLine("GroundingWarning: No URL-backed source evidence is available. Do not present the research as source-grounded.");
        }

        var sources = result.Sources
            .Where(s => !string.IsNullOrWhiteSpace(s.Url))
            .GroupBy(s => NormalizeUrl(s.Url), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(MaxSources)
            .ToList();

        if (sources.Count > 0)
        {
            builder.AppendLine("AcceptedSources:");
            for (var i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                var title = string.IsNullOrWhiteSpace(source.Title) ? source.Url : source.Title;
                var type = string.IsNullOrWhiteSpace(source.SourceType) ? "web" : source.SourceType;
                builder.AppendLine($"[{i + 1}] ({type}) {TrimLine(title, 160)}");
                builder.AppendLine($"    URL: {source.Url}");
                if (!string.IsNullOrWhiteSpace(source.Snippet))
                {
                    builder.AppendLine($"    Snippet: {TrimLine(source.Snippet, MaxSnippetLength)}");
                }
            }
        }

        builder.AppendLine("ReportExcerpt:");
        builder.AppendLine(TrimBlock(result.Report, MaxReportLength));

        return TrimBlock(builder.ToString(), MaxContextLength);
    }

    private static string NormalizeUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri)
            {
                Fragment = string.Empty
            };

            return builder.Uri.ToString().TrimEnd('/');
        }

        return url.Trim();
    }

    private static string TrimLine(string? value, int maxLength)
    {
        var normalized = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static string TrimBlock(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 40)] + "\n[truncated for downstream context]";
    }
}

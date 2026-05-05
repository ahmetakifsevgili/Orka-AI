using System.Text.RegularExpressions;
using Orka.Core.DTOs.Chat;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class ChatMetadataService : IChatMetadataService
{
    private static readonly Regex DocCitationRegex = new(@"\[doc:(?<id>[0-9a-fA-F-]{36}):p(?<page>\d+)\]", RegexOptions.Compiled);
    private static readonly Regex MermaidFenceRegex = new(@"```\s*mermaid\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PollinationsImageRegex = new(@"https://image\.pollinations\.ai/prompt/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YouTubeMarkerRegex = new(@"\[youtube:(?<marker>disabled|degraded|unknown|[A-Za-z0-9_-]{6,})\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ChatResponseMetadata Build(string content, string? fallbackReason = null, IEnumerable<UsedToolDto>? usedTools = null)
    {
        var text = content ?? string.Empty;
        var citations = DocCitationRegex.Matches(text)
            .Cast<Match>()
            .Select(m =>
            {
                var sourceId = Guid.TryParse(m.Groups["id"].Value, out var parsed) ? parsed : (Guid?)null;
                var page = int.TryParse(m.Groups["page"].Value, out var p) ? p : (int?)null;
                return new CitationDto(
                    m.Value,
                    "document",
                    sourceId,
                    page,
                    $"Document page {page}",
                    Url: null,
                    Confidence: 1.0);
            })
            .GroupBy(c => c.CitationId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(20)
            .ToList();

        var tools = usedTools?.ToList() ?? [];
        var providerWarnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(fallbackReason))
            providerWarnings.Add(fallbackReason);

        AddDetectedPedagogyTools(text, tools, providerWarnings);

        return new ChatResponseMetadata
        {
            Citations = citations,
            UsedTools = tools.Take(12).ToList(),
            GroundingMode = citations.Count > 0 ? "source_grounded" : providerWarnings.Count == 0 ? "model_fallback" : "degraded_fallback",
            FallbackReason = fallbackReason,
            SourceConfidence = citations.Count > 0 ? 1.0 : null,
            ProviderWarnings = providerWarnings
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList()
        };
    }

    private static void AddDetectedPedagogyTools(string text, List<UsedToolDto> tools, List<string> providerWarnings)
    {
        if (MermaidFenceRegex.IsMatch(text))
        {
            AddToolIfMissing(tools, new UsedToolDto(
                "mermaid",
                "generated_text",
                "mermaid_fenced_block",
                null));
        }

        if (PollinationsImageRegex.IsMatch(text))
        {
            AddToolIfMissing(tools, new UsedToolDto(
                "pollinations",
                "generated_image_url",
                "embedded_image_markdown",
                null));
        }

        foreach (Match match in YouTubeMarkerRegex.Matches(text))
        {
            var marker = match.Groups["marker"].Value;
            var status = marker.Equals("disabled", StringComparison.OrdinalIgnoreCase)
                ? "disabled"
                : marker.Equals("degraded", StringComparison.OrdinalIgnoreCase)
                    ? "degraded"
                    : "ready";

            AddToolIfMissing(tools, new UsedToolDto(
                "youtube",
                status,
                status == "ready" ? $"youtube:{marker}" : $"youtube:{status}",
                status == "ready" ? null : $"youtube_{status}"));

            if (status is "disabled" or "degraded")
                providerWarnings.Add($"youtube_{status}");
        }
    }

    private static void AddToolIfMissing(List<UsedToolDto> tools, UsedToolDto tool)
    {
        if (tools.Any(t =>
            t.Name.Equals(tool.Name, StringComparison.OrdinalIgnoreCase) &&
            t.Status.Equals(tool.Status, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        tools.Add(tool);
    }
}

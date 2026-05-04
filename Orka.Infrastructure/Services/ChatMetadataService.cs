using System.Text.RegularExpressions;
using Orka.Core.DTOs.Chat;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class ChatMetadataService : IChatMetadataService
{
    private static readonly Regex DocCitationRegex = new(@"\[doc:(?<id>[0-9a-fA-F-]{36}):p(?<page>\d+)\]", RegexOptions.Compiled);

    public ChatResponseMetadata Build(string content, string? fallbackReason = null, IEnumerable<UsedToolDto>? usedTools = null)
    {
        var citations = DocCitationRegex.Matches(content ?? string.Empty)
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

        var tools = usedTools?.Take(12).ToList() ?? [];
        return new ChatResponseMetadata
        {
            Citations = citations,
            UsedTools = tools,
            GroundingMode = citations.Count > 0 ? "source_grounded" : fallbackReason == null ? "model_fallback" : "degraded_fallback",
            FallbackReason = fallbackReason,
            SourceConfidence = citations.Count > 0 ? 1.0 : null,
            ProviderWarnings = fallbackReason == null ? Array.Empty<string>() : new[] { fallbackReason }
        };
    }
}

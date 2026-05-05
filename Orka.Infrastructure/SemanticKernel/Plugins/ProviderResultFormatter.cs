using System.Text;
using System.Text.Json;
using Orka.Core.DTOs;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

internal static class ProviderResultFormatter
{
    public static string Format(ProviderToolResultDto result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{result.ToolId}:{result.Status}] {result.SafeMessage}");
        sb.AppendLine($"Provider: {result.Provider}");
        sb.AppendLine($"Success: {result.Success}");
        sb.AppendLine($"FallbackUsed: {result.FallbackUsed}");
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
            sb.AppendLine($"ErrorCode: {result.ErrorCode}");
        sb.AppendLine($"Timestamp: {result.Timestamp:O}");
        sb.AppendLine($"LatencyMs: {result.LatencyMs}");

        if (result.Citations.Count > 0)
        {
            sb.AppendLine("Sources:");
            foreach (var citation in result.Citations.Take(8))
                sb.AppendLine($"- {citation.Label} | {citation.SourceName} | {citation.PublishedAt:O} | {citation.Url}");
        }

        if (result.Data != null)
        {
            sb.AppendLine("Data:");
            sb.AppendLine(JsonSerializer.Serialize(result.Data, new JsonSerializerOptions { WriteIndented = false }));
        }

        return sb.ToString();
    }
}

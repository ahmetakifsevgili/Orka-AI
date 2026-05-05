using System.ComponentModel;
using Microsoft.SemanticKernel;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class SourcesQueryPlugin
{
    private readonly ILearningSourceService _sources;

    public SourcesQueryPlugin(ILearningSourceService sources)
    {
        _sources = sources;
    }

    [KernelFunction, Description("List active learning sources for a user/topic. Requires authenticated user id.")]
    public async Task<string> ListSourcesForTopic(Guid userId, Guid topicId)
    {
        var sources = await _sources.GetTopicSourcesAsync(userId, topicId);
        if (sources.Count == 0) return "Bu konu için aktif kaynak yok.";

        return string.Join("\n", sources.Take(12).Select(s =>
            $"- {s.Id}: {s.Title} ({s.ChunkCount} chunk, status={s.Status})"));
    }

    [KernelFunction, Description("Ask a question against one uploaded source. Requires authenticated user id.")]
    public async Task<string> AskSource(Guid userId, Guid sourceId, string question)
    {
        var result = await _sources.AskAsync(userId, sourceId, question);
        return $"[source_query citations={result.Citations.Count}]\n{result.Answer}";
    }
}

using Orka.Core.Entities;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public class NotebookSourceContextFormatterTests
{
    [Fact]
    public void BuildSourceContext_EmitsStableDocCitationTags()
    {
        var sourceId = Guid.NewGuid();
        var context = NotebookSourceContextFormatter.BuildSourceContext(
        [
            new SourceChunk
            {
                LearningSourceId = sourceId,
                PageNumber = 4,
                ChunkIndex = 2,
                Text = "Sync-over-async can deadlock captured contexts."
            }
        ]);

        Assert.Contains($"[doc:{sourceId}:p4]", context);
        Assert.Contains("chunk:2", context);
        Assert.Contains("Sync-over-async", context);
    }

    [Fact]
    public void BuildSourceContext_RespectsContextLimitBeforeAddingNextChunk()
    {
        var sourceId = Guid.NewGuid();
        var context = NotebookSourceContextFormatter.BuildSourceContext(
        [
            new SourceChunk { LearningSourceId = sourceId, PageNumber = 1, ChunkIndex = 0, Text = new string('a', 80) },
            new SourceChunk { LearningSourceId = sourceId, PageNumber = 2, ChunkIndex = 1, Text = new string('b', 80) }
        ], maxContextChars: 130);

        Assert.Contains($"[doc:{sourceId}:p1]", context);
        Assert.DoesNotContain($"[doc:{sourceId}:p2]", context);
    }

    [Fact]
    public void ScoreLexical_RanksMatchingChunkAboveUnrelatedChunk()
    {
        var matching = new SourceChunk { Text = "Captured context deadlock happens with Task.Result." };
        var unrelated = new SourceChunk { Text = "Photosynthesis uses chlorophyll in plants." };

        var matchingScore = NotebookSourceContextFormatter.ScoreLexical(matching, "Task.Result captured deadlock");
        var unrelatedScore = NotebookSourceContextFormatter.ScoreLexical(unrelated, "Task.Result captured deadlock");

        Assert.True(matchingScore > unrelatedScore);
    }
}

using Orka.Core.Entities;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class DurableLearningContractTests
{
    [Fact]
    public void ReviewKey_PrefersConceptBeforeSkill()
    {
        var service = new ReviewSrsService(null!, null!, null!);
        var topicId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var key = service.BuildReviewKey(topicId, "Async Deadlock", "CSharp Async", "Await basics", "Path");

        Assert.Equal("concept:11111111111111111111111111111111:async-deadlock", key);
    }

    [Fact]
    public void ReviewKey_FallsBackToSkillObjectiveThenTopic()
    {
        var service = new ReviewSrsService(null!, null!, null!);
        var topicId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Assert.Equal("skill:11111111111111111111111111111111:csharp-async", service.BuildReviewKey(topicId, null, "CSharp Async", "Await basics", "Path"));
        Assert.Equal("objective:11111111111111111111111111111111:await-basics", service.BuildReviewKey(topicId, null, null, "Await basics", "Path"));
        Assert.Equal("topic:11111111111111111111111111111111:path", service.BuildReviewKey(topicId, null, null, null, "Path"));
    }

    [Fact]
    public void ApplyReview_HighQualityAdvancesSchedule()
    {
        var item = new ReviewItem { DueAt = DateTime.UtcNow, EaseFactor = 2.5, Status = "active" };

        ReviewSrsService.ApplyReview(item, 5);

        Assert.Equal(1, item.RepetitionCount);
        Assert.Equal(1, item.SuccessStreak);
        Assert.Equal(1, item.IntervalDays);
        Assert.NotNull(item.LastReviewedAt);
        Assert.True(item.DueAt > DateTime.UtcNow.AddHours(-1));
    }

    [Fact]
    public void ApplyReview_LowQualityRecordsLapse()
    {
        var item = new ReviewItem { DueAt = DateTime.UtcNow, EaseFactor = 2.5, RepetitionCount = 3, SuccessStreak = 3, IntervalDays = 10 };

        ReviewSrsService.ApplyReview(item, 1);

        Assert.Equal(0, item.RepetitionCount);
        Assert.Equal(0, item.SuccessStreak);
        Assert.Equal(1, item.LapseCount);
        Assert.Equal(1, item.IntervalDays);
        Assert.True(item.EaseFactor < 2.5);
    }

    [Fact]
    public void ChatMetadata_ExtractsInlineDocCitations()
    {
        var metadata = new ChatMetadataService().Build("Unique fact [doc:22222222-2222-2222-2222-222222222222:p3].");

        Assert.Equal("source_grounded", metadata.GroundingMode);
        var citation = Assert.Single(metadata.Citations);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), citation.SourceId);
        Assert.Equal(3, citation.PageNumber);
    }

    [Fact]
    public void ChatMetadata_DoesNotInventCitations()
    {
        var metadata = new ChatMetadataService().Build("No source tags here.");

        Assert.Empty(metadata.Citations);
        Assert.Equal("model_fallback", metadata.GroundingMode);
    }

    [Fact]
    public void ChatMetadata_DetectsMermaidDiagramTool()
    {
        var metadata = new ChatMetadataService().Build("""
            ```mermaid
            flowchart LR
              A[Request] --> B[Await]
            ```
            """);

        var tool = Assert.Single(metadata.UsedTools);
        Assert.Equal("mermaid", tool.Name);
        Assert.Equal("generated_text", tool.Status);
        Assert.Equal("mermaid_fenced_block", tool.Evidence);
    }

    [Fact]
    public void ChatMetadata_DetectsYouTubeDegradedToolWarning()
    {
        var metadata = new ChatMetadataService().Build("[youtube:degraded] YouTube aramasi gecici olarak kullanilamiyor.");

        var tool = Assert.Single(metadata.UsedTools);
        Assert.Equal("youtube", tool.Name);
        Assert.Equal("degraded", tool.Status);
        Assert.Contains("youtube_degraded", metadata.ProviderWarnings);
        Assert.Equal("degraded_fallback", metadata.GroundingMode);
    }

    [Fact]
    public void ChatMetadata_DetectsPollinationsVisualUrl()
    {
        var metadata = new ChatMetadataService().Build("![diagram](https://image.pollinations.ai/prompt/async%20diagram?width=512&height=512&nologo=true)");

        var tool = Assert.Single(metadata.UsedTools);
        Assert.Equal("pollinations", tool.Name);
        Assert.Equal("generated_image_url", tool.Status);
        Assert.Equal("embedded_image_markdown", tool.Evidence);
    }
}

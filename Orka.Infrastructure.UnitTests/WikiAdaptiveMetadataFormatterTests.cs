using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public class WikiAdaptiveMetadataFormatterTests
{
    [Fact]
    public void BuildAdaptiveBlock_IncludesMistakeCategorySignalsAndRemediation()
    {
        var block = WikiAdaptiveMetadataFormatter.BuildAdaptiveBlock(
            (3.5, "Task.Result deadlock confusion"),
            [
                new QuizAttempt
                {
                    Question = "Task.Result neden deadlock riski dogurur?",
                    IsCorrect = false,
                    SkillTag = "async-deadlock",
                    CognitiveType = "debugging",
                    SourceRefsJson = """{"mistakeCategory":"ConceptualMisunderstanding"}"""
                }
            ],
            [
                new LearningSignal
                {
                    SignalType = "WeaknessDetected",
                    SkillTag = "async-deadlock",
                    Score = 35,
                    IsPositive = false
                }
            ],
            [
                new StudyRecommendationDto(
                    Guid.NewGuid(),
                    "remedial-practice",
                    "Deadlock tekrar",
                    "Kavramsal hata",
                    "async-deadlock",
                    "Mikro pratik",
                    IsDone: false,
                    CreatedAt: DateTime.UtcNow)
            ],
            [
                new RemediationPlan
                {
                    SkillTag = "Task.Result",
                    Status = "pending"
                }
            ]);

        Assert.Contains("ADAPTIVE_WIKI_METADATA", block);
        Assert.Contains("ConceptualMisunderstanding", block);
        Assert.Contains("WeaknessDetected", block);
        Assert.Contains("Deadlock tekrar", block);
        Assert.Contains("Task.Result", block);
    }

    [Fact]
    public void BuildSourceBlock_PreservesDocCitationTags()
    {
        var sourceId = Guid.NewGuid();
        var block = WikiAdaptiveMetadataFormatter.BuildSourceBlock(
        [
            new SourceChunk
            {
                LearningSourceId = sourceId,
                PageNumber = 2,
                ChunkIndex = 0,
                Text = "Captured-context deadlock happens with sync-over-async."
            }
        ]);

        Assert.Contains("SOURCE_BACKED_WIKI_CONTEXT", block);
        Assert.Contains($"[doc:{sourceId}:p2]", block);
        Assert.Contains("sync-over-async", block);
    }

    [Fact]
    public void EnsureSourceTagsPresent_AppendsMissingDocTags()
    {
        var sourceId = Guid.NewGuid();
        var sourceBlock = $"[SOURCE_BACKED_WIKI_CONTEXT]\n- [doc:{sourceId}:p3] Citation-sensitive fact.";

        var markdown = WikiAdaptiveMetadataFormatter.EnsureSourceTagsPresent(
            "# Async\nCaptured context deadlock summary.",
            sourceBlock);

        Assert.Contains("Kaynaklı Notlar", markdown);
        Assert.Contains($"[doc:{sourceId}:p3]", markdown);
    }
}

using Orka.Core.DTOs;
using Orka.Core.DTOs.Chat;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class WikiLearningServicesTests
{
    [Fact]
    public void Policy_SourceSelectedButEmpty_DoesNotInventAnswer()
    {
        var engine = new WikiAnswerPolicyEngine();
        var request = new WikiLearningRequestDto
        {
            SourceId = Guid.NewGuid(),
            Question = "Bu belgede olmayan şeyi anlat"
        };

        var policy = engine.BuildPolicy(request, new WikiEvidenceBundleDto());

        Assert.False(policy.CanAnswer);
        Assert.True(policy.RequiresCitation);
        Assert.Equal("source_retrieval_empty", policy.GroundingStatus);
        Assert.Contains("net dayanak", policy.UserSafeMessage);
    }

    [Fact]
    public void CitationGuard_AppendsExistingCitationWhenModelForgotIt()
    {
        var guard = new WikiCitationGuard();
        var sourceId = Guid.NewGuid();
        var evidence = new WikiEvidenceBundleDto
        {
            Citations =
            [
                new CitationDto($"[doc:{sourceId}:p2]", "document", sourceId, 2, "Kesirler / s.2", null, 0.92)
            ],
            SourceChunks =
            [
                new TopicSourceEvidenceDto
                {
                    SourceId = sourceId,
                    SourceTitle = "Kesirler",
                    FileName = "kesirler.txt",
                    PageNumber = 2,
                    Text = "Payda eşitleme kesir toplamada kullanılır.",
                    Score = 0.92
                }
            ],
            LearnerState = "needs_scaffold"
        };
        var policy = new WikiAnswerPolicyDto
        {
            CanAnswer = true,
            RequiresCitation = true,
            GroundingStatus = "source_grounded"
        };

        var result = guard.Apply("Payda eşitleme ortak payda bulma işidir.", evidence, policy);

        Assert.True(result.IsHealthy);
        Assert.True(result.Repaired);
        Assert.Contains($"[doc:{sourceId}:p2]", result.Answer);
        Assert.Equal("source_grounded", result.Metadata.GroundingStatus);
        Assert.Single(result.Metadata.Citations);
    }

    [Fact]
    public void CitationGuard_SourceMissing_ReturnsSafeMessage()
    {
        var guard = new WikiCitationGuard();
        var policy = new WikiAnswerPolicyDto
        {
            CanAnswer = false,
            RequiresCitation = false,
            GroundingStatus = "no_source",
            FallbackReason = "source_retrieval_empty",
            UserSafeMessage = "Bu bilgi mevcut kaynaklarda net görünmüyor."
        };

        var result = guard.Apply("Genel bilgiyle uydurulmuş cevap", new WikiEvidenceBundleDto(), policy);

        Assert.True(result.IsHealthy);
        Assert.Contains("mevcut kaynaklarda net görünmüyor", result.Answer);
        Assert.Equal("no_source", result.Metadata.GroundingStatus);
        Assert.Equal("source_retrieval_empty", result.Metadata.FallbackReason);
    }
}

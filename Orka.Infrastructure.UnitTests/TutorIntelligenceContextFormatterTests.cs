using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public class TutorIntelligenceContextFormatterTests
{
    [Fact]
    public void BuildPlanIntentHint_MapsPlanRemediationToTeachingMode()
    {
        var hint = TutorIntelligenceContextFormatter.BuildPlanIntentHint("Plan:Remediation");

        Assert.Contains("PLAN_INTENT_TEACHING_MODE", hint);
        Assert.Contains("Plan:Remediation", hint);
        Assert.Contains("misconception repair", hint);
    }

    [Fact]
    public void BuildFailedAttemptSummary_PreservesSkillAndMistakeCategory()
    {
        var summary = TutorIntelligenceContextFormatter.BuildFailedAttemptSummary(
        [
            new QuizAttempt
            {
                Question = "await neden UI thread'i bloklamaz?",
                UserAnswer = "Thread'i uyutur.",
                IsCorrect = false,
                SkillTag = "async-await",
                TopicPath = "C# Async > await",
                CognitiveType = "debugging",
                SourceRefsJson = """{"mistakeCategory":"Conceptual"}""",
                Explanation = "await continuation mantigini onar."
            }
        ]);

        Assert.Contains("QUIZ-DERIVED PERSONALIZATION", summary);
        Assert.Contains("async-await", summary);
        Assert.Contains("Conceptual", summary);
        Assert.Contains("debugging", summary);
    }

    [Fact]
    public void BuildReviewPressureSummary_IncludesOpenRecommendationsAndPendingRemediation()
    {
        var summary = TutorIntelligenceContextFormatter.BuildReviewPressureSummary(
        [
            new StudyRecommendationDto(
                Guid.NewGuid(),
                "remedial-practice",
                "Deadlock riskini tekrar et",
                "Task.Result kullaniminda zorlanma",
                "deadlock",
                "Once kisa tekrar, sonra mikro pratik",
                IsDone: false,
                CreatedAt: DateTime.UtcNow)
        ],
        [
            new RemediationPlan
            {
                SkillTag = "Task.Wait deadlock",
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            }
        ]);

        Assert.Contains("REVIEW_AND_REMEDIATION_PRESSURE", summary);
        Assert.Contains("Deadlock riskini tekrar et", summary);
        Assert.Contains("Task.Wait deadlock", summary);
        Assert.Contains("Use this only when the user asks what to review", summary);
    }
}

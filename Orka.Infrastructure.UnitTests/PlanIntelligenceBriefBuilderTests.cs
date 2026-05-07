using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class PlanIntelligenceBriefBuilderTests
{
    [Fact]
    public void BuildForPlan_FiltersKorteksNoiseAndKeepsPlanningSignals()
    {
        var compressed = """
            [SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]
            Topic: C# çalışmak
            GroundingMode: SourceGrounded
            SourceCount: 4
            TopSources:
            - Random blog source (https://example.com/noisy) - Install Visual Studio first and copy this exact tutorial outline.
            KeyFacts:
            - C# curriculum should include types, control flow, OOP, collections, async and debugging practice.
            - Search result says this page ranks well for beginners.
            CurriculumMapHints:
            - Roadmap sequence: syntax foundations, control flow, methods, OOP, collections, async, mini project.
            PrerequisiteHints:
            - Before async, learner needs method calls, return values, and basic control flow.
            LikelyMisconceptions:
            - Common error: blocking on Task.Result instead of awaiting.
            YouTubeLearningReferences:
            - Tutorial title with URL https://youtube.example/watch?v=abc
            Instruction: Use this compressed Korteks research only for factual/current/topic-breadth support.
            """;

        var brief = PlanIntelligenceBriefBuilder.BuildForPlan(
            "C# çalışmak",
            compressed,
            "WeakConcepts: async-await: 2 | methods: 1\nMistakePatterns: Conceptual: 2");

        Assert.Contains("PLAN INTELLIGENCE BRIEF", brief);
        Assert.Contains("Learning research is advisory", brief);
        Assert.Contains("concept-first", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Orka IDE/sandbox only as practice support", brief);
        Assert.Contains("syntax foundations", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("async", brief, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Random blog source", brief);
        Assert.DoesNotContain("copy this exact tutorial outline", brief, StringComparison.OrdinalIgnoreCase);
        Assert.True(brief.Length <= 5200);
    }

    [Fact]
    public void BuildForPlan_BlocksFallbackResearchFromBecomingVerifiedCurriculum()
    {
        var compressed = """
            [SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]
            Topic: KPSS genel yetenek
            GroundingMode: FallbackInternalKnowledge
            SourceCount: 0
            FallbackWarning: Korteks returned fallback/internal-knowledge research; do not present it as current source-grounded evidence.
            KeyFacts:
            - KPSS plan should combine konu anlatimi, soru tipi, deneme and yanlis analizi.
            """;

        var brief = PlanIntelligenceBriefBuilder.BuildForPlan(
            "KPSS genel yetenek",
            compressed,
            "Mode: StartFromZero\nWeakConcepts: none");

        Assert.Contains("GroundingMode: FallbackInternalKnowledge", brief);
        Assert.Contains("do not treat", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not invent weak skills", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Exam", brief, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildForDiagnosticQuiz_UsesFilteredBriefInsteadOfRawContext()
    {
        var compressed = """
            [SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]
            Topic: Python
            GroundingMode: SourceGrounded
            SourceCount: 3
            TopSources:
            - Noisy provider source that should not become a question.
            CurriculumMapHints:
            - Roadmap: variables, functions, lists, files, exceptions, mini project.
            """;

        var brief = PlanIntelligenceBriefBuilder.BuildForDiagnosticQuiz("Python", compressed);

        Assert.Contains("Purpose: diagnostic_quiz", brief);
        Assert.Contains("variables", brief, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Noisy provider source", brief);
        Assert.Contains("QualityContract", brief);
    }
}

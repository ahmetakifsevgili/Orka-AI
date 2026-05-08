using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs.Korteks;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class OrkaV29QualityRealityGateTests
{
    public static IEnumerable<object[]> QualityScenarioCatalog =>
    [
        ["A01", "java programlamada algoritmalar calismak istiyorum", "intent_fixture"],
        ["A02", "java veri yapilari ve algoritmalar ogrenmek istiyorum", "intent_fixture"],
        ["A03", "sql index ve sorgu optimizasyonu calismak istiyorum", "intent_fixture"],
        ["A04", "kpss paragraf sorularinda hizlanmak istiyorum", "intent_fixture"],
        ["A05", "kpss problem cozme taktikleri calismak istiyorum", "intent_fixture"],
        ["A06", "c# async await hata yapiyorum", "intent_fixture"],
        ["A07", "python pandas veri analizi ogrenmek istiyorum", "intent_fixture"],
        ["A08", "matematik olasilik ve kombinasyon calismak istiyorum", "intent_fixture"],
        ["A09", "ingilizce ielts speaking gelistirmek istiyorum", "intent_fixture"],
        ["A10", "jva algortima calismak istiyom", "intent_fixture"],
        ["B11", "Java research has no unrelated C# leakage", "research_synthesis"],
        ["B12", "SQL research keeps index/query/practice specificity", "research_synthesis"],
        ["B13", "KPSS paragraph research keeps exam technique signals", "research_synthesis"],
        ["B14", "IELTS research keeps speaking practice criteria", "research_synthesis"],
        ["B15", "Math research keeps prerequisites and question types", "research_synthesis"],
        ["B16", "Korteks output remains source-aware", "research_synthesis"],
        ["B17", "Compressor preserves prerequisites", "research_synthesis"],
        ["B18", "Compressor preserves common mistakes", "research_synthesis"],
        ["B19", "Compressor preserves practice order", "research_synthesis"],
        ["B20", "Brief exposes quiz coverage and question-count inputs", "research_synthesis"],
        ["C21", "Java quiz stays on Java algorithms", "quiz_quality"],
        ["C22", "SQL quiz stays on SQL optimization", "quiz_quality"],
        ["C23", "KPSS quiz measures paragraph skill", "quiz_quality"],
        ["C24", "C# quiz measures async/await reasoning", "quiz_quality"],
        ["C25", "Quiz count is 15-25", "quiz_quality"],
        ["C26", "Quiz count changes by scope", "quiz_quality"],
        ["C27", "Quiz rejects duplicates", "quiz_quality"],
        ["C28", "Quiz options do not leak correct/incorrect labels", "quiz_quality"],
        ["C29", "Wrong answer feedback is not false-positive praise", "quiz_quality"],
        ["C30", "Quiz result separates missing concepts", "quiz_result"],
        ["C31", "Quiz result separates known concepts", "quiz_result"],
        ["C32", "Quiz result separates weak concepts", "quiz_result"],
        ["C33", "Quiz result separates practice concepts", "quiz_result"],
        ["C34", "Quiz result separates misconception patterns", "quiz_result"],
        ["D35", "Plan is not capped to three headings", "plan_tutor"],
        ["D36", "Plan uses research plus quiz result", "plan_tutor"],
        ["D37", "Known concepts become fast review/practice", "plan_tutor"],
        ["D38", "Weak concepts become deeper remediation", "plan_tutor"],
        ["D39", "Tutor consumes active plan", "plan_tutor"],
        ["D40", "Tutor prioritizes Orka IDE/sandbox", "plan_tutor"],
        ["D41", "Tutor does not default to Visual Studio first", "plan_tutor"],
        ["D42", "Tutor uses Wiki/OrkaLM source when present", "plan_tutor"],
        ["D43", "Tutor states when no source exists", "plan_tutor"],
        ["D44", "Tutor tempo follows known/weak profile", "plan_tutor"],
        ["E45", "Plan Mode active label remains visible", "frontend_ux"],
        ["E46", "Plan stages are meaningful", "frontend_ux"],
        ["E47", "Quiz stays in one card", "frontend_ux"],
        ["E48", "Next quiz question is not a chat bubble", "frontend_ux"],
        ["E49", "Quiz 500 does not crash UI", "frontend_ux"],
        ["E50", "Mermaid parse errors fall back safely", "frontend_ux"],
        ["E51", "Favicon exists", "frontend_ux"],
        ["E52", "Capabilities 500 shows unavailable state", "frontend_ux"],
        ["E53", "Audio empty content does not show fake 0:00 ready state", "frontend_ux"],
        ["E54", "Flashcards are positioned as suggestions plus manual add", "frontend_ux"],
        ["E55", "Bookmarks are source/Tutor/Wiki saved fragments", "frontend_ux"],
        ["E56", "Progress does not show fake percentages", "frontend_ux"]
    ];

    [Theory]
    [MemberData(nameof(QualityScenarioCatalog))]
    public void QualityRealityScenarioCatalog_CoversEveryRequiredMeasurement(string id, string scenario, string evidenceGate)
    {
        Assert.Matches("^[A-E][0-9]{2}$", id);
        Assert.False(string.IsNullOrWhiteSpace(scenario));
        Assert.Contains(evidenceGate, new[]
        {
            "intent_fixture",
            "research_synthesis",
            "quiz_quality",
            "quiz_result",
            "plan_tutor",
            "frontend_ux"
        });
    }

    [Fact]
    public void QualityRealityScenarioCatalog_IsFortyPlusAndBalancedAcrossGates()
    {
        var scenarios = QualityScenarioCatalog.Select(row => row.Select(v => v.ToString() ?? string.Empty).ToArray()).ToList();

        Assert.True(scenarios.Count >= 56, $"Expected at least 56 scored scenarios; actual={scenarios.Count}.");
        Assert.True(scenarios.Count(s => s[0].StartsWith("A", StringComparison.Ordinal)) >= 10);
        Assert.True(scenarios.Count(s => s[0].StartsWith("B", StringComparison.Ordinal)) >= 10);
        Assert.True(scenarios.Count(s => s[0].StartsWith("C", StringComparison.Ordinal)) >= 14);
        Assert.True(scenarios.Count(s => s[0].StartsWith("D", StringComparison.Ordinal)) >= 10);
        Assert.True(scenarios.Count(s => s[0].StartsWith("E", StringComparison.Ordinal)) >= 12);
    }

    public static IEnumerable<object[]> IntentFixtures =>
    [
        ["java programlamada algoritmalar calismak istiyorum", "Java", "algorit", "Java", "algorithms"],
        ["java veri yapilari ve algoritmalar ogrenmek istiyorum", "Java", "veri yap", "Java", "data structures"],
        ["sql index ve sorgu optimizasyonu calismak istiyorum", "SQL", "index", "SQL", "query"],
        ["kpss paragraf sorularinda hizlanmak istiyorum", "KPSS", "paragraf", "KPSS", "paragraph"],
        ["kpss problem cozme taktikleri calismak istiyorum", "KPSS", "problem", "KPSS", "problem"],
        ["c# async await hata yapiyorum", "C#", "asenkron", "C#", "asynchronous"],
        ["python pandas veri analizi ogrenmek istiyorum", "Python", "pandas", "Python", "pandas"],
        ["matematik olasilik ve kombinasyon calismak istiyorum", "matematik", "olasilik", "math", "probability"],
        ["ingilizce ielts speaking gelistirmek istiyorum", "ingilizce", "ielts", "English", "speaking"],
        ["jva algortima calismak istiyom", "Java", "algorit", "Java", "algorithms"]
    ];

    [Theory]
    [MemberData(nameof(IntentFixtures))]
    public async Task StudyIntentAnalyzer_ProducesApprovedResearchIntentQualitySignals(
        string raw,
        string expectedMain,
        string expectedFocus,
        string expectedIntentA,
        string expectedIntentB)
    {
        var analyzer = new StudyIntentAnalyzer(new ThrowingAgentFactory(), NullLogger<StudyIntentAnalyzer>.Instance);

        var result = await analyzer.AnalyzeAsync(Guid.NewGuid(), new AnalyzeStudyIntentRequest { RawRequest = raw });

        Assert.True(result.RequiresUserConfirmation);
        Assert.NotEqual(raw, result.ResearchIntent, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(expectedMain, result.MainTopic, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedFocus, result.FocusArea, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedIntentA, result.ResearchIntent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedIntentB, result.ResearchIntent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("visual studio", result.ResearchIntent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("c#", result.ResearchIntent, raw.Contains("java", StringComparison.OrdinalIgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    [Fact]
    public async Task StudyIntentAnalyzer_CorrectionRegeneratesIntentBeforeKorteks()
    {
        var analyzer = new StudyIntentAnalyzer(new ThrowingAgentFactory(), NullLogger<StudyIntentAnalyzer>.Instance);

        var result = await analyzer.AnalyzeAsync(
            Guid.NewGuid(),
            new AnalyzeStudyIntentRequest
            {
                RawRequest = "java calismak istiyorum",
                Correction = "java veri yapilari ve algoritmalar calismak istiyorum"
            });

        Assert.Contains("Java", result.MainTopic, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("veri yap", result.FocusArea, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data structures", result.ResearchIntent, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.RequiresUserConfirmation);
    }

    [Fact]
    public void PlanDiagnostic_QuestionCountStaysWithinRealityGateAndChangesByScope()
    {
        var broad = InvokePrivateStatic<int>(
            typeof(PlanDiagnosticService),
            "DetermineDiagnosticQuestionCount",
            "Java programlama",
            "algoritmalar ve veri yapilari",
            "Java programming algorithms data structures learning path");
        var narrow = InvokePrivateStatic<int>(
            typeof(PlanDiagnosticService),
            "DetermineDiagnosticQuestionCount",
            "C# programlama",
            "tek konu syntax",
            "C# syntax single topic intro");

        Assert.InRange(broad, 15, 25);
        Assert.InRange(narrow, 15, 25);
        Assert.True(broad > narrow, $"Broad diagnostic should ask more than narrow diagnostic. broad={broad}, narrow={narrow}");
    }

    [Fact]
    public void KorteksCompression_PreservesLearningSignalsForSynthesis()
    {
        var compressor = new PlanResearchCompressor();
        var result = compressor.Compress(BuildResearchFixture());
        var brief = PlanIntelligenceBriefBuilder.BuildForDiagnosticQuiz("Java programming algorithms", compressor.BuildPromptBlock(result));

        Assert.NotEmpty(result.TopSources);
        Assert.NotEmpty(result.YouTubeLearningReferences);
        Assert.NotEmpty(result.CurriculumMapHints);
        Assert.NotEmpty(result.PrerequisiteHints);
        Assert.NotEmpty(result.LikelyMisconceptions);
        Assert.Contains("Curriculum", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prerequisites", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Misconceptions", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("YouTubePedagogy", brief, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("final study plan", brief, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Java programming algorithms and data structures", "C#", "Visual Studio")]
    [InlineData("SQL index and query optimization", "C#", "Visual Studio")]
    [InlineData("KPSS paragraf sorularinda hizlanma", "Visual Studio", "C#")]
    [InlineData("C# async await hata ayiklama", "Java programming", "KPSS")]
    public void DiagnosticQuizFallback_UsesGenericAssessmentMetadataAndDoesNotLeakObviousAnswers(
        string topic,
        string forbiddenA,
        string forbiddenB)
    {
        var quiz = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint(topic);
        var report = DiagnosticQuizQualityGate.Validate(quiz, topic);

        Assert.True(report.IsAcceptable, string.Join(" | ", report.Failures));
        Assert.InRange(report.QuestionCount, 15, 25);
        Assert.Contains("assessmentItemId", quiz, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("conceptKey", quiz, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("learningOutcomeIds", quiz, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scoringRule", quiz, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(forbiddenA, quiz, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(forbiddenB, quiz, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dogru yaklasim", quiz, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yanlis:", quiz, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Quiz Cevab", quiz, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[SKIP_QUIZ]", quiz, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnosticQuizQualityGate_RejectsDuplicateThinObviousQuiz()
    {
        var badQuiz = JsonSerializer.Serialize(Enumerable.Range(1, 15).Select(_ => new
        {
            question = "Java algoritma icin en dogru cevap hangisi?",
            options = new[] { "Dogru yaklasim: bunu sec", "Yanlis: bunu secme" },
            correctAnswer = "Dogru yaklasim: bunu sec",
            explanation = "Aciklama",
            skillTag = "java",
            difficulty = "kolay",
            conceptTag = "same",
            learningObjective = "same",
            questionType = "conceptual",
            expectedMisconceptionCategory = "conceptual"
        }));

        var report = DiagnosticQuizQualityGate.Validate(badQuiz, "Java programming algorithms");

        Assert.False(report.IsAcceptable);
        Assert.Contains(report.Failures, failure => failure.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Failures, failure => failure.Contains("leak", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Failures, failure => failure.Contains("diversity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanIntelligenceBrief_PreservesDiagnosticKnownWeakAndQualityContract()
    {
        var diagnostic = """
            Mode: completed
            Answered: 20
            Correct: 11
            Wrong: 9
            AccuracyPercent: 55
            KnownConcepts: arrays, loops
            FastTrackConcepts: syntax
            PracticeConcepts: binary-search
            WeakConcepts: complexity-analysis, recursion, graph-traversal, hash-map, sorting-stability
            MistakePatterns: misconception_probe, reading
            Instruction: Move known concepts faster; deepen weak concepts.
            """;

        var compressor = new PlanResearchCompressor();
        var context = compressor.BuildPromptBlock(compressor.Compress(BuildResearchFixture()));
        var brief = PlanIntelligenceBriefBuilder.BuildForPlan("Java algorithms and data structures", context, diagnostic);

        Assert.Contains("KnownConcepts", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WeakConcepts", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("at least 6 modules", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("at least 24 lessons", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Orka IDE", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generic 3-heading", brief, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeepPlan_StaticQualityContract_DetectsNoThreeHeadingCapAndOrkaIdePriority()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Orka.Infrastructure", "Services", "DeepPlanAgent.cs"));

        Assert.Contains("MinimumProgrammingTotalLessons = 24", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MinimumProgrammingModules = 6", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Orka IDE/sandbox yalnizca uygun pratik", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BuildConceptGraphFallbackModules", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("onkosul -> ana kavram", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("programming_plan_missing_orka_ide", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BuildDomainFallbackModules", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlanDomain.", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ApplyDiagnosticTraceability", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Take(3).Select(module", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tutor_StaticContract_UsesOrkaIdeAndAdaptiveContextBeforeExternalAssumptions()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Orka.Infrastructure", "Services", "TutorAgent.cs"));

        Assert.Contains("ORKA IDE VARSAYILAN ORTAMDIR", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FetchLearningSignalContextAsync", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FetchNotebookContextAsync", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BuildTeacherContextAsync", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Visual Studio'yu ilk adim olarak oner", source, StringComparison.OrdinalIgnoreCase);
    }

    private static KorteksResearchResultDto BuildResearchFixture() => new()
    {
        Topic = "Java programming algorithms",
        GroundingMode = GroundingMode.SourceGrounded,
        Report = """
            A practical learning path should start with Java arrays, loops, functions, Big-O basics, sorting, searching, recursion, hash maps, stacks, queues, graphs, and dynamic programming.
            Prerequisite: learners should know Java syntax, control flow, methods, arrays, and basic object usage before algorithm drills.
            Curriculum roadmap: sequence concept explanation, code reading, Orka IDE sandbox exercises, debugging drills, timed problems, and review.
            Common mistakes: learners confuse index boundaries, mutate collections while iterating, ignore complexity, and memorize templates without understanding invariants.
            YouTube teaching flow: use visual algorithm traces, dry runs, and step-by-step examples before timed challenge practice.
            Practice ideas: implement binary search, two pointers, frequency map, stack parser, BFS traversal, and dynamic programming table.
            """,
        Sources =
        [
            new SourceEvidenceDto("GDELT", "web_search", "https://example.edu/java-algorithms", "Java Algorithms Learning Path", "Roadmap and prerequisites", null, DateTimeOffset.UtcNow, 0.95, "web", "java-1", null),
            new SourceEvidenceDto("YouTube", "youtube_search", "https://youtube.com/watch?v=abc", "Java Algorithms Visual Course", "Video learning reference", null, DateTimeOffset.UtcNow, 0.90, "video", "yt-1", null)
        ]
    };

    private static T InvokePrivateStatic<T>(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, args)!;
    }

    private sealed class ThrowingAgentFactory : IAIAgentFactory
    {
        public string GetModel(AgentRole role) => "fixture";
        public string GetProvider(AgentRole role) => "fixture";
        public Task<string> CompleteChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default) =>
            throw new InvalidOperationException("Force deterministic fallback for quality fixtures.");

        public async IAsyncEnumerable<string> StreamChatAsync(AgentRole role, string systemPrompt, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string> CompleteChatWithHistoryAsync(AgentRole role, IEnumerable<(string Role, string Content)> messages, CancellationToken ct = default) =>
            throw new InvalidOperationException("Not used by these fixtures.");

        public Task<string> CompleteChatWithHistoryAsync(AgentRole role, string systemPrompt, IEnumerable<(string Role, string Content)> messages, CancellationToken ct = default) =>
            throw new InvalidOperationException("Force deterministic fallback for quality fixtures.");
    }
}

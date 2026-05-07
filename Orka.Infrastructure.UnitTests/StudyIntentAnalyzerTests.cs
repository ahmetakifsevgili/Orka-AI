using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class StudyIntentAnalyzerTests
{
    [Fact]
    public async Task StudyIntentAnalyzer_SplitsJavaAlgorithmsIntoResearchIntent()
    {
        var analyzer = new StudyIntentAnalyzer(new ThrowingAgentFactory(), NullLogger<StudyIntentAnalyzer>.Instance);

        var result = await analyzer.AnalyzeAsync(
            Guid.NewGuid(),
            new AnalyzeStudyIntentRequest { RawRequest = "java programlamada algoritmalar calismak istiyorum" });

        Assert.Equal("Java programlama", result.MainTopic);
        Assert.Equal("algoritmalar", result.FocusArea);
        Assert.Contains("Java", result.ResearchIntent);
        Assert.Contains("algorithms", result.ResearchIntent, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.RequiresUserConfirmation);
    }

    [Fact]
    public async Task StudyIntentAnalyzer_HandlesTurkishCharactersAndDataStructures()
    {
        var analyzer = new StudyIntentAnalyzer(new ThrowingAgentFactory(), NullLogger<StudyIntentAnalyzer>.Instance);

        var result = await analyzer.AnalyzeAsync(
            Guid.NewGuid(),
            new AnalyzeStudyIntentRequest { RawRequest = "Java programlamada algoritmalar ve veri yapıları çalışmak istiyorum" });

        Assert.Equal("Java programlama", result.MainTopic);
        Assert.Equal("algoritmalar ve veri yapilari", result.FocusArea);
        Assert.Contains("Java", result.ResearchIntent);
        Assert.Contains("data structures", result.ResearchIntent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("çalışmak", result.ResearchIntent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StudyIntentAnalyzer_UsesCorrectionAsFreshIntentPreview()
    {
        var analyzer = new StudyIntentAnalyzer(new ThrowingAgentFactory(), NullLogger<StudyIntentAnalyzer>.Instance);

        var result = await analyzer.AnalyzeAsync(
            Guid.NewGuid(),
            new AnalyzeStudyIntentRequest
            {
                RawRequest = "java calismak istiyorum",
                Correction = "Hayir, Java algoritmalar ve veri yapilari istiyorum"
            });

        Assert.Equal("Hayir, Java algoritmalar ve veri yapilari istiyorum", result.RawRequest);
        Assert.Equal("Java programlama", result.MainTopic);
        Assert.Equal("algoritmalar ve veri yapilari", result.FocusArea);
        Assert.True(result.RequiresUserConfirmation);
    }

    [Fact]
    public async Task StudyIntentAnalyzer_PreservesExamAcronymWithoutInventingWeakness()
    {
        var analyzer = new StudyIntentAnalyzer(new ThrowingAgentFactory(), NullLogger<StudyIntentAnalyzer>.Instance);

        var result = await analyzer.AnalyzeAsync(
            Guid.NewGuid(),
            new AnalyzeStudyIntentRequest { RawRequest = "KPSS paragraf sorularında hızlanmak istiyorum" });

        Assert.Equal("KPSS", result.MainTopic);
        Assert.Contains("paragraf", result.FocusArea, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KPSS", result.ResearchIntent, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.RequiresUserConfirmation);
    }

    [Fact]
    public async Task StudyIntentAnalyzer_UsesModelJsonWhenValid()
    {
        var analyzer = new StudyIntentAnalyzer(
            new JsonAgentFactory("""
                {
                  "mainTopic": "Cognitive science",
                  "focusArea": "learning strategies",
                  "studyGoal": "learning and practice",
                  "researchIntent": "cognitive science learning strategies learning path",
                  "confirmationText": "Ogrenme stratejileri calismak istedigini anladim.",
                  "language": "tr",
                  "clarifyingNotes": ["Korteks onaydan sonra calisir."]
                }
                """),
            NullLogger<StudyIntentAnalyzer>.Instance);

        var result = await analyzer.AnalyzeAsync(
            Guid.NewGuid(),
            new AnalyzeStudyIntentRequest { RawRequest = "ogrenme stratejileri hakkinda kafam karisik" });

        Assert.Equal("Cognitive science", result.MainTopic);
        Assert.Equal("learning strategies", result.FocusArea);
        Assert.Equal("cognitive science learning strategies learning path", result.ResearchIntent);
    }

    [Fact]
    public async Task StudyIntentAnalyzer_RefinesGenericModelIntentWithRawDomain()
    {
        var analyzer = new StudyIntentAnalyzer(
            new JsonAgentFactory("""
                {
                  "mainTopic": "programming",
                  "focusArea": "Java algorithms",
                  "studyGoal": "learning and practice",
                  "researchIntent": "study algorithms in Java programming",
                  "confirmationText": "Java algoritmalarini calismak istedigini anladim.",
                  "language": "tr",
                  "clarifyingNotes": []
                }
                """),
            NullLogger<StudyIntentAnalyzer>.Instance);

        var result = await analyzer.AnalyzeAsync(
            Guid.NewGuid(),
            new AnalyzeStudyIntentRequest { RawRequest = "java programlamada algoritmalar calismak istiyorum" });

        Assert.Equal("Java programlama", result.MainTopic);
        Assert.Contains("Java", result.ResearchIntent);
        Assert.Contains("learning path", result.ResearchIntent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StudyIntentAnalyzer_RejectsCrossDomainModelLeakage()
    {
        var analyzer = new StudyIntentAnalyzer(
            new JsonAgentFactory("""
                {
                  "mainTopic": "programming",
                  "focusArea": "JavaScript async",
                  "studyGoal": "learning and practice",
                  "researchIntent": "understand the difference between async await and parallel programming in JavaScript",
                  "confirmationText": "Asenkron programlama calismak istedigini anladim.",
                  "language": "tr",
                  "clarifyingNotes": []
                }
                """),
            NullLogger<StudyIntentAnalyzer>.Instance);

        var result = await analyzer.AnalyzeAsync(
            Guid.NewGuid(),
            new AnalyzeStudyIntentRequest { RawRequest = "c# async await ile paralel programlama karisiyor" });

        Assert.Equal("C# programlama", result.MainTopic);
        Assert.Contains("C#", result.ResearchIntent);
        Assert.DoesNotContain("JavaScript", result.ResearchIntent, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingAgentFactory : IAIAgentFactory
    {
        public string GetModel(AgentRole role) => "fake";
        public string GetProvider(AgentRole role) => "fake";
        public Task<string> CompleteChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default) =>
            throw new InvalidOperationException("model unavailable");
        public async IAsyncEnumerable<string> StreamChatAsync(AgentRole role, string systemPrompt, string userMessage, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "fake";
            await Task.CompletedTask;
        }
        public Task<string> CompleteChatWithHistoryAsync(AgentRole role, string systemPrompt, IEnumerable<(string Role, string Content)> messages, CancellationToken ct = default) =>
            Task.FromResult("fake");
    }

    private sealed class JsonAgentFactory : IAIAgentFactory
    {
        private readonly string _json;
        public JsonAgentFactory(string json) => _json = json;
        public string GetModel(AgentRole role) => "fake";
        public string GetProvider(AgentRole role) => "fake";
        public Task<string> CompleteChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default) =>
            Task.FromResult(_json);
        public async IAsyncEnumerable<string> StreamChatAsync(AgentRole role, string systemPrompt, string userMessage, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "fake";
            await Task.CompletedTask;
        }
        public Task<string> CompleteChatWithHistoryAsync(AgentRole role, string systemPrompt, IEnumerable<(string Role, string Content)> messages, CancellationToken ct = default) =>
            Task.FromResult("fake");
    }
}

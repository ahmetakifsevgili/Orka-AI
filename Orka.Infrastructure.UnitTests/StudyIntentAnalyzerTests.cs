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
    public async Task StudyIntentAnalyzer_UsesModelJsonWhenValid()
    {
        var analyzer = new StudyIntentAnalyzer(
            new JsonAgentFactory("""
                {
                  "mainTopic": "Java programming",
                  "focusArea": "algorithms and data structures",
                  "studyGoal": "learning and practice",
                  "researchIntent": "Java algorithms and data structures learning path",
                  "confirmationText": "Java algoritmalarini calismak istedigini anladim.",
                  "language": "tr",
                  "clarifyingNotes": ["Korteks onaydan sonra calisir."]
                }
                """),
            NullLogger<StudyIntentAnalyzer>.Instance);

        var result = await analyzer.AnalyzeAsync(
            Guid.NewGuid(),
            new AnalyzeStudyIntentRequest { RawRequest = "java algoritma" });

        Assert.Equal("Java programming", result.MainTopic);
        Assert.Equal("algorithms and data structures", result.FocusArea);
        Assert.Equal("Java algorithms and data structures learning path", result.ResearchIntent);
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

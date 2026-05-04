using System.Text.Json;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class DiagnosticQuizQualityGateTests
{
    [Fact]
    public void DiagnosticQuizQuality_DetectsDuplicateQuestions()
    {
        var json = BuildQuiz(questionOverride: "Ayni soru metni?");

        var report = DiagnosticQuizQualityGate.Validate(json, "C# async await");

        Assert.False(report.IsAcceptable);
        Assert.True(report.DuplicateQuestionCount > 0);
        Assert.Contains(report.Failures, f => f.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiagnosticQuizQuality_FailsTechnicalQuizWithoutCodeSnippet()
    {
        var json = BuildQuiz(includeCodeQuestion: false);

        var report = DiagnosticQuizQualityGate.Validate(json, "C# async/await ve Task");

        Assert.False(report.IsAcceptable);
        Assert.Contains(report.Failures, f => f.Contains("code", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiagnosticQuizQuality_FailsWhenConceptDiversityIsLow()
    {
        var json = BuildQuiz(conceptFactory: _ => "same-concept");

        var report = DiagnosticQuizQualityGate.Validate(json, "C# async await");

        Assert.False(report.IsAcceptable);
        Assert.Contains(report.Failures, f => f.Contains("Concept diversity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiagnosticQuizQuality_FailsWhenQuestionTypeDiversityIsLow()
    {
        var json = BuildQuiz(questionTypeFactory: _ => "conceptual");

        var report = DiagnosticQuizQualityGate.Validate(json, "C# async await");

        Assert.False(report.IsAcceptable);
        Assert.Contains(report.Failures, f => f.Contains("Question type diversity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiagnosticQuizQuality_FailsWhenMisconceptionProbesAreLow()
    {
        var json = BuildQuiz(
            questionTypeFactory: i => i % 4 == 0 ? "analysis" : "application",
            misconceptionFactory: _ => "Careless");

        var report = DiagnosticQuizQualityGate.Validate(json, "C# async await");

        Assert.False(report.IsAcceptable);
        Assert.Contains(report.Failures, f => f.Contains("Misconception probes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiagnosticQuizQuality_ReturnsFallbackBlueprintWhenQualityIsLow()
    {
        var lowQuality = """
        [
          {"question":"Async nedir?","options":["A","B"],"correctAnswer":"A"}
        ]
        """;

        var result = DiagnosticQuizQualityGate.EnsureQualityOrFallback(lowQuality, "C# async/await", out var report);

        Assert.False(report.IsAcceptable);
        Assert.Equal(20, DiagnosticQuizQualityGate.CountQuestions(result));
        Assert.Contains("```csharp", result);
        Assert.Contains("misconception_probe", result);
    }

    [Fact]
    public void DiagnosticQuizQuality_AcceptsVariedDiagnosticQuiz()
    {
        var json = BuildQuiz();

        var result = DiagnosticQuizQualityGate.EnsureQualityOrFallback(json, "C# async await", out var report);

        Assert.True(report.IsAcceptable);
        Assert.Equal(20, report.QuestionCount);
        Assert.Equal(DiagnosticQuizQualityGate.ExtractJsonArray(json), result);
    }

    private static string BuildQuiz(
        string? questionOverride = null,
        bool includeCodeQuestion = true,
        Func<int, string>? conceptFactory = null,
        Func<int, string>? questionTypeFactory = null,
        Func<int, string>? misconceptionFactory = null)
    {
        var types = new[] { "conceptual", "procedural", "application", "analysis", "misconception_probe" };
        var misconceptions = new[] { "Conceptual", "Procedural", "Application", "Reading", "Careless" };
        var difficulties = new[] { "kolay", "orta", "zor" };

        var questions = Enumerable.Range(1, 20).Select(i =>
        {
            var type = questionTypeFactory?.Invoke(i) ?? types[(i - 1) % types.Length];
            var misconception = misconceptionFactory?.Invoke(i) ?? (type == "misconception_probe" || i <= 5 ? misconceptions[(i - 1) % misconceptions.Length] : "Careless");
            var question = questionOverride ?? $"C# async await tani sorusu {i}: Task tabanli akista karar ver.";
            if (includeCodeQuestion && i == 4)
            {
                question += "\n```csharp\nvar result = LoadAsync().Result;\n```";
            }

            return new
            {
                type = "multiple_choice",
                question,
                options = new[]
                {
                    new { text = "Dogru secenek", isCorrect = true },
                    new { text = "Yanlis secenek 1", isCorrect = false },
                    new { text = "Yanlis secenek 2", isCorrect = false },
                    new { text = "Yanlis secenek 3", isCorrect = false }
                },
                correctAnswer = "Dogru secenek",
                explanation = $"Aciklama {i}",
                skillTag = $"skill-{i}",
                difficulty = difficulties[(i - 1) % difficulties.Length],
                conceptTag = conceptFactory?.Invoke(i) ?? $"concept-{i}",
                learningObjective = $"Hedef {i}",
                questionType = type,
                expectedMisconceptionCategory = misconception,
                topic = "C# async await"
            };
        });

        return JsonSerializer.Serialize(questions, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}

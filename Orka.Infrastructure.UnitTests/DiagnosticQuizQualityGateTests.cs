using System.Text.Json;
using Orka.Core.DTOs;
using Orka.Core.DTOs.PlanDiagnostic;
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
    public void DiagnosticQuizQuality_UsesGrammarConceptKeyWhenConceptTagIsMissing()
    {
        var json = BuildQuiz(conceptFactory: i => i <= 4 ? $"concept-{i}" : null!);

        using var doc = JsonDocument.Parse(json);
        var normalized = doc.RootElement.EnumerateArray()
            .Select((question, index) => JsonSerializer.Deserialize<Dictionary<string, object?>>(question.GetRawText())!)
            .Select((question, index) =>
            {
                question.Remove("conceptTag");
                question["conceptKey"] = $"grammar-concept-{index + 1}";
                question["skillTag"] = $"grammar-concept-{index + 1}";
                return question;
            })
            .ToArray();

        var report = DiagnosticQuizQualityGate.Validate(JsonSerializer.Serialize(normalized, new JsonSerializerOptions(JsonSerializerDefaults.Web)), "C# async await");

        Assert.True(report.IsAcceptable, string.Join(" | ", report.Failures));
        Assert.True(report.ConceptDiversity >= 5);
    }

    [Fact]
    public void DiagnosticQuizQuality_BuildsMetadataRichFallbackBlueprint()
    {
        var quiz = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint("SQL index ve sorgu optimizasyonu", 15, "en");
        var report = DiagnosticQuizQualityGate.Validate(quiz, "SQL index ve sorgu optimizasyonu", 15);

        Assert.True(report.IsAcceptable, string.Join(" | ", report.Failures));
        DiagnosticQuizQualityGate.EnsureAssessmentMetadataOrThrow(quiz, 15);
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
    public void DiagnosticQuizQuality_FailsWhenOptionsLeakCorrectnessLabels()
    {
        var json = BuildQuiz(optionFactory: i => new[]
        {
            new { text = $"Dogru secenek: kavrami uygula {i}", isCorrect = true },
            new { text = $"Yanlis secenek: ezberle {i}", isCorrect = false },
            new { text = $"Yanlis secenek: tahmin et {i}", isCorrect = false },
            new { text = $"Yanlis secenek: konuyu yok say {i}", isCorrect = false }
        });

        var report = DiagnosticQuizQualityGate.Validate(json, "Java programlama: algoritmalar");

        Assert.False(report.IsAcceptable);
        Assert.Contains(report.Failures, f => f.Contains("correctness labels", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiagnosticQuizQuality_ThrowsInsteadOfGeneratingFallbackWhenQualityIsLow()
    {
        var lowQuality = """
        [
          {"question":"Async nedir?","options":["A","B"],"correctAnswer":"A"}
        ]
        """;

        Assert.Throws<InvalidOperationException>(() =>
            DiagnosticQuizQualityGate.EnsureQualityOrFallback(lowQuality, "C# async/await", out _));
    }

    [Fact]
    public void DiagnosticQuizQuality_FallbackBlueprintGenerationKeepsQualityContract()
    {
        var quiz = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint("C# async/await");
        var report = DiagnosticQuizQualityGate.Validate(quiz, "C# async/await", 20);

        Assert.True(report.IsAcceptable, string.Join(" | ", report.Failures));
        DiagnosticQuizQualityGate.EnsureAssessmentMetadataOrThrow(quiz, 20);
    }

    [Fact]
    public void DiagnosticQuizQuality_FallbackWithBlueprintUsesRecommendedQuestionCount()
    {
        var blueprint = new LearningBlueprintDto
        {
            Domain = "legacy-adapter",
            RecommendedQuestionCount = 20
        };

        var quiz = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint("Selcuklu tarihi: tarih", blueprint);
        var report = DiagnosticQuizQualityGate.Validate(quiz, "Selcuklu tarihi: tarih", 20);

        Assert.True(report.IsAcceptable, string.Join(" | ", report.Failures));
        DiagnosticQuizQualityGate.EnsureAssessmentMetadataOrThrow(quiz, 20);
    }

    [Fact]
    public void DiagnosticQuizQuality_FallbackWithAssessmentGrammarUsesGrammarCount()
    {
        var quiz = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint(
            "SQL index ve sorgu optimizasyonu",
            new AssessmentGrammarDto { RequestedQuestionCount = 20 });
        var report = DiagnosticQuizQualityGate.Validate(quiz, "SQL index ve sorgu optimizasyonu", 20);

        Assert.True(report.IsAcceptable, string.Join(" | ", report.Failures));
        DiagnosticQuizQualityGate.EnsureAssessmentMetadataOrThrow(quiz, 20);
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
        Func<int, string>? misconceptionFactory = null,
        Func<int, object[]>? optionFactory = null)
    {
        var types = new[] { "conceptual", "procedural", "application", "analysis" };
        var misconceptions = new[] { "Conceptual", "Procedural", "Application", "Reading", "Careless" };
        var difficulties = new[] { "kolay", "orta", "zor" };

        var questions = Enumerable.Range(1, 20).Select(i =>
        {
            var type = questionTypeFactory?.Invoke(i) ?? (i % 4 == 0 ? "misconception_probe" : types[(i - 1) % types.Length]);
            var misconception = misconceptionFactory?.Invoke(i) ?? (type == "misconception_probe" || i <= 5 ? $"async-specific-gap-{i}" : "Careless");
            var question = questionOverride ?? $"C# async await tani sorusu {i}: Task tabanli akista karar ver.";
            if (includeCodeQuestion && i == 4)
            {
                question += "\n```csharp\nvar result = LoadAsync().Result;\n```";
            }

            var correctOption = new { text = $"Kavrami senaryodaki kisitlara gore uygula {i}", isCorrect = true, rationale = "Matches the target evidence.", misconceptionKey = "" };
            var distractors = new object[]
            {
                new { text = $"Tanimi ezberden yazip senaryoyu atla {i}", isCorrect = false, rationale = "Checks memorized-definition distractor.", misconceptionKey = $"memorized-definition-{i}" },
                new { text = $"Benzer gorunen terimi asil kavram yerine sec {i}", isCorrect = false, rationale = "Checks nearby-concept confusion.", misconceptionKey = $"nearby-concept-{i}" },
                new { text = $"Sonucu hata mesajinin ilk kelimesinden tahmin et {i}", isCorrect = false, rationale = "Checks surface-clue guessing.", misconceptionKey = $"surface-clue-{i}" }
            };
            var generatedOptions = new List<object>(distractors);
            generatedOptions.Insert((i - 1) % 4, correctOption);
            var options = optionFactory?.Invoke(i) ?? generatedOptions.ToArray();

            return new
            {
                type = "multiple_choice",
                question,
                options,
                correctAnswer = $"Kavrami senaryodaki kisitlara gore uygula {i}",
                explanation = $"Aciklama {i}",
                skillTag = $"skill-{i}",
                difficulty = difficulties[(i - 1) % difficulties.Length],
                conceptTag = conceptFactory?.Invoke(i) ?? $"concept-{i}",
                learningObjective = $"Hedef {i}",
                questionType = type,
                misconceptionTarget = type == "misconception_probe" ? $"async-specific-gap-{i}" : "evidence-insufficient",
                expectedMisconceptionCategory = misconception,
                topic = "C# async await"
            };
        });

        return JsonSerializer.Serialize(questions, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}

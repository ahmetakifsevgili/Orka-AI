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
    public void DiagnosticQuizQuality_FallbackOptionsDoNotLeakCorrectnessLabels()
    {
        var result = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint("C# async/await");

        Assert.DoesNotContain("Dogru yaklasim", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yanlis:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Doğru yaklaşım", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yanlış:", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnosticQuizQuality_FallbackIsDomainAwareForNonTechnicalTopics()
    {
        var result = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint("KPSS tarih ve genel kultur");

        Assert.Equal(20, DiagnosticQuizQualityGate.CountQuestions(result));
        Assert.DoesNotContain("```csharp", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".Result", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("async/await", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dogru cevaba ulasmak", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnosticQuizQuality_FallbackUsesExamReadingOptionsForKpssParagraph()
    {
        var result = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint("KPSS paragraf sorularinda hizlanmak");

        Assert.Equal(20, DiagnosticQuizQualityGate.CountQuestions(result));
        Assert.Contains("paragraf", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ana fikir", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("celdirici", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("```csharp", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Orka IDE", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(DiagnosticQuizQualityGate.Validate(result, "KPSS paragraf sorularinda hizlanmak").IsAcceptable);
    }

    [Fact]
    public void DiagnosticQuizQuality_FallbackUsesSqlOptimizationOptions()
    {
        var result = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint("SQL index ve sorgu optimizasyonu");

        Assert.Equal(20, DiagnosticQuizQualityGate.CountQuestions(result));
        Assert.Contains("```sql", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("index", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("execution plan", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("```csharp", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Orka IDE", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(DiagnosticQuizQualityGate.Validate(result, "SQL index ve sorgu optimizasyonu").IsAcceptable);
    }

    [Fact]
    public void DiagnosticQuizQuality_FallbackUsesRequestedProgrammingProfile()
    {
        var result = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint("Python listeler ve hata ayiklama");

        Assert.Equal(20, DiagnosticQuizQualityGate.CountQuestions(result));
        Assert.Contains("```python", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("```csharp", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Orka IDE", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bu kodu dogru okumak icin hangi yaklasim", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnosticQuizQuality_FallbackDoesNotShowInternalObjectiveAsQuestion()
    {
        var result = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint("Python listeler ve hata ayiklama");

        Assert.Equal(20, DiagnosticQuizQualityGate.CountQuestions(result));
        Assert.DoesNotContain("Kod parcasinda veri akisini ve karar noktasini tespit eder", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bu parcada seviye belirlemek icin en onemli risk", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Islem siralamasini kurar", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bu kodu dogru okumak icin hangi yaklasim", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnosticQuizQuality_FallbackDoesNotLeakAsyncIntoJavaAlgorithms()
    {
        var result = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint("Java programlama: algoritmalar");

        Assert.Equal(20, DiagnosticQuizQualityGate.CountQuestions(result));
        Assert.Contains("```java", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Arrays.sort", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Binary search", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HashMap", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dynamic programming", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("async", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Task.Result", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Visual Studio", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Orka IDE", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("En kucuk calisan akisi", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tani sorusu", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("seviye belirlemek icin en onemli risk", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(DiagnosticQuizQualityGate.Validate(result, "Java programlama: algoritmalar").IsAcceptable);
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

    [Fact]
    public void DiagnosticQuizQuality_ThrowsWhenPlanDiagnosticLeaksGenericJavaScaffold()
    {
        var generic = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint("C# async await");

        Assert.Throws<InvalidOperationException>(() =>
            DiagnosticQuizQualityGate.EnsureQualityOrThrow(generic, "Java programlama: algoritmalar", 20, out _));
    }

    private static string BuildQuiz(
        string? questionOverride = null,
        bool includeCodeQuestion = true,
        Func<int, string>? conceptFactory = null,
        Func<int, string>? questionTypeFactory = null,
        Func<int, string>? misconceptionFactory = null,
        Func<int, object[]>? optionFactory = null)
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

            var options = optionFactory?.Invoke(i) ?? new object[]
            {
                new { text = $"Kavrami senaryodaki kisitlara gore uygula {i}", isCorrect = true },
                new { text = $"Tanimi ezberden yazip senaryoyu atla {i}", isCorrect = false },
                new { text = $"Benzer gorunen terimi asil kavram yerine sec {i}", isCorrect = false },
                new { text = $"Sonucu hata mesajinin ilk kelimesinden tahmin et {i}", isCorrect = false }
            };

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
                expectedMisconceptionCategory = misconception,
                topic = "C# async await"
            };
        });

        return JsonSerializer.Serialize(questions, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}

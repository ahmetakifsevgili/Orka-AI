using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orka.Infrastructure.Services;

public static class DiagnosticQuizQualityGate
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private static readonly string[] RequiredQuestionTypes =
    [
        "conceptual",
        "procedural",
        "application",
        "analysis",
        "misconception_probe"
    ];

    public static string EnsureQualityOrFallback(string rawJson, string topicTitle, out DiagnosticQuizQualityReport report)
    {
        var cleaned = ExtractJsonArray(rawJson);
        report = Validate(cleaned, topicTitle);

        if (report.IsAcceptable)
        {
            return cleaned;
        }

        return BuildFallbackDiagnosticBlueprint(topicTitle);
    }

    public static DiagnosticQuizQualityReport Validate(string rawJson, string topicTitle)
    {
        var failures = new List<string>();
        var questions = ParseQuestions(rawJson, failures);

        if (questions.Count == 0)
        {
            failures.Add("No parseable quiz questions.");
            return new DiagnosticQuizQualityReport(false, 0, 0, 0, 0, false, failures);
        }

        var duplicateCount = CountDuplicates(questions.Select(q => q.Question));
        if (duplicateCount > 0)
        {
            failures.Add($"Duplicate or near-duplicate questions detected: {duplicateCount}.");
        }

        var missingMetadata = questions.Count(q =>
            string.IsNullOrWhiteSpace(q.Question) ||
            q.Options.Count < 2 ||
            string.IsNullOrWhiteSpace(q.CorrectAnswer) ||
            string.IsNullOrWhiteSpace(q.Explanation) ||
            string.IsNullOrWhiteSpace(q.SkillTag) ||
            string.IsNullOrWhiteSpace(q.Difficulty) ||
            string.IsNullOrWhiteSpace(q.ConceptTag) ||
            string.IsNullOrWhiteSpace(q.LearningObjective) ||
            string.IsNullOrWhiteSpace(q.QuestionType) ||
            string.IsNullOrWhiteSpace(q.ExpectedMisconceptionCategory));
        if (missingMetadata > 0)
        {
            failures.Add($"Questions with missing required metadata/options: {missingMetadata}.");
        }

        var conceptDiversity = questions
            .Select(q => NormalizeTag(q.ConceptTag))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var minConceptDiversity = questions.Count >= 15 ? 8 : Math.Min(4, Math.Max(2, questions.Count / 2));
        if (conceptDiversity < minConceptDiversity)
        {
            failures.Add($"Concept diversity too low: {conceptDiversity}/{minConceptDiversity}.");
        }

        var questionTypeDiversity = questions
            .Select(q => NormalizeTag(q.QuestionType))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var minQuestionTypeDiversity = questions.Count >= 15 ? 4 : Math.Min(3, Math.Max(2, questions.Count / 2));
        if (questionTypeDiversity < minQuestionTypeDiversity)
        {
            failures.Add($"Question type diversity too low: {questionTypeDiversity}/{minQuestionTypeDiversity}.");
        }

        var misconceptionProbeCount = questions.Count(IsMisconceptionProbe);
        var minMisconceptionProbes = questions.Count >= 15 ? 5 : Math.Min(2, Math.Max(1, questions.Count / 4));
        if (misconceptionProbeCount < minMisconceptionProbes)
        {
            failures.Add($"Misconception probes too low: {misconceptionProbeCount}/{minMisconceptionProbes}.");
        }

        var hasCodeLikeQuestion = questions.Any(q => LooksCodeLike(q.Question) || q.Options.Any(LooksCodeLike));
        if (IsTechnicalTopic(topicTitle) && !hasCodeLikeQuestion)
        {
            failures.Add("Technical diagnostic quiz is missing a code-reading/debugging style question.");
        }

        return new DiagnosticQuizQualityReport(
            failures.Count == 0,
            questions.Count,
            duplicateCount,
            conceptDiversity,
            questionTypeDiversity,
            hasCodeLikeQuestion,
            failures);
    }

    public static string BuildFallbackDiagnosticBlueprint(string topicTitle)
    {
        var templates = new[]
        {
            ("code_reading", "kolay", "async-blocking-debug", "Reading", "Kod parcasinda engelleyici async kullanimini tespit eder."),
            ("procedural", "kolay", "basic-flow", "Procedural", "Islem siralamasini kurar."),
            ("application", "orta", "real-world-use", "Application", "Kavrami senaryoda uygular."),
            ("analysis", "orta", "code-reading", "Reading", "Kod akisini analiz eder."),
            ("misconception_probe", "orta", "common-misconception", "Conceptual", "Yaygin yanilgiyi tanir."),
            ("conceptual", "kolay", "prerequisite", "Conceptual", "On kosulu kavrar."),
            ("procedural", "orta", "workflow", "Procedural", "Dogru uygulama adimlarini secer."),
            ("application", "orta", "debugging", "Application", "Hata ayiklama yaklasimi secer."),
            ("analysis", "zor", "edge-case", "Reading", "Uc durumlari yorumlar."),
            ("misconception_probe", "orta", "blocking-vs-async", "Application", "Engelleme ve asenkronluk farkini ayirt eder."),
            ("conceptual", "orta", "task-model", "Conceptual", "Calisma modelini aciklar."),
            ("procedural", "orta", "error-handling", "Procedural", "Hata yonetimi adimini secer."),
            ("application", "zor", "cancellation", "Application", "Iptal ve zaman asimi senaryosunu cozer."),
            ("analysis", "zor", "performance", "Reading", "Performans etkisini analiz eder."),
            ("misconception_probe", "zor", "thread-confusion", "Conceptual", "Thread ve is mantigi karisikligini yakalar."),
            ("conceptual", "orta", "api-shape", "MisreadQuestion", "API seklini dogru okur."),
            ("procedural", "zor", "composition", "Procedural", "Bilesik akisi tasarlar."),
            ("application", "zor", "production-scenario", "Application", "Uretim senaryosunda karar verir."),
            ("analysis", "zor", "debug-trace", "Reading", "Iz ve hata ciktisini yorumlar."),
            ("misconception_probe", "zor", "advanced-misconception", "Careless", "Ince yanilgilari ayirt eder.")
        };

        var questions = templates.Select((t, i) =>
        {
            var codeSnippet = i is 0 or 3 or 7 or 18
                ? "\n\nKod:\n```csharp\nvar task = LoadAsync();\nConsole.WriteLine(task.Result);\n```\nBu kodda hangi async/await veya Task kullanim problemi tani icin en onemlidir?"
                : string.Empty;

            return new DiagnosticQuestionBlueprint
            {
                Type = "multiple_choice",
                Question = $"{topicTitle}: {i + 1}. tani sorusu - {t.Item5}.{codeSnippet}",
                Options =
                [
                    new DiagnosticOption("Dogru yaklasim: kavrami baglama gore uygula.", true),
                    new DiagnosticOption("Yanlis: ezber tanimi her durumda aynen uygula.", false),
                    new DiagnosticOption("Yanlis: senaryodaki kisitlari yok say.", false),
                    new DiagnosticOption("Yanlis: hata belirtisini kok neden san.", false)
                ],
                CorrectAnswer = "Dogru yaklasim: kavrami baglama gore uygula.",
                Explanation = i == 0
                    ? $"{t.Item5} Bu soru, .Result ile asenkron isin senkron bloklanmasi ve olasi deadlock/yanlis zihinsel model riskini olcer."
                    : $"{t.Item5} Bu soru, {topicTitle} icin tani amacli kavram ve uygulama ayrimini olcer.",
                SkillTag = t.Item3,
                Difficulty = t.Item2,
                ConceptTag = $"{t.Item3}-{i + 1}",
                LearningObjective = t.Item5,
                QuestionType = t.Item1,
                ExpectedMisconceptionCategory = t.Item4,
                Topic = topicTitle
            };
        }).ToList();

        return JsonSerializer.Serialize(questions, JsonOptions)
            .Replace("\\u0060", "`", StringComparison.OrdinalIgnoreCase);
    }

    public static string ExtractJsonArray(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "[]";
        }

        var cleaned = raw.Trim();
        if (cleaned.Contains("```", StringComparison.Ordinal))
        {
            var lines = cleaned.Split('\n')
                .Where(line => !line.TrimStart().StartsWith("```", StringComparison.Ordinal));
            cleaned = string.Join("\n", lines);
        }

        var start = cleaned.IndexOf('[');
        var end = cleaned.LastIndexOf(']');
        return start >= 0 && end > start ? cleaned[start..(end + 1)] : cleaned;
    }

    public static int CountQuestions(string rawJson)
    {
        var failures = new List<string>();
        return ParseQuestions(ExtractJsonArray(rawJson), failures).Count;
    }

    private static List<DiagnosticQuestion> ParseQuestions(string rawJson, List<string> failures)
    {
        try
        {
            using var doc = JsonDocument.Parse(ExtractJsonArray(rawJson));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                failures.Add("Quiz output is not a JSON array.");
                return [];
            }

            return doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(ParseQuestion)
                .ToList();
        }
        catch (Exception ex)
        {
            failures.Add($"Quiz JSON parse failed: {ex.Message}");
            return [];
        }
    }

    private static DiagnosticQuestion ParseQuestion(JsonElement element)
    {
        var options = new List<string>();
        if (element.TryGetProperty("options", out var optionsElement) &&
            optionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in optionsElement.EnumerateArray())
            {
                if (option.ValueKind == JsonValueKind.String)
                {
                    options.Add(option.GetString() ?? string.Empty);
                }
                else if (option.ValueKind == JsonValueKind.Object)
                {
                    options.Add(
                        GetString(option, "text") ??
                        GetString(option, "value") ??
                        GetString(option, "label") ??
                        GetString(option, "id") ??
                        string.Empty);
                }
            }
        }

        return new DiagnosticQuestion(
            GetString(element, "question") ?? string.Empty,
            options.Where(o => !string.IsNullOrWhiteSpace(o)).ToList(),
            GetString(element, "correctAnswer") ?? string.Empty,
            GetString(element, "explanation") ?? string.Empty,
            GetString(element, "skillTag") ?? string.Empty,
            GetString(element, "difficulty") ?? string.Empty,
            GetString(element, "conceptTag") ?? string.Empty,
            GetString(element, "learningObjective") ?? string.Empty,
            GetString(element, "questionType") ?? string.Empty,
            GetString(element, "expectedMisconceptionCategory") ?? string.Empty);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int CountDuplicates(IEnumerable<string> questions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0;
        foreach (var question in questions)
        {
            var normalized = NormalizeQuestion(question);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (!seen.Add(normalized))
            {
                duplicates++;
            }
        }

        return duplicates;
    }

    private static string NormalizeQuestion(string value)
    {
        var normalized = Regex.Replace(value.ToLowerInvariant(), @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"[^\p{L}\p{Nd}\s]", "");
        return normalized.Length <= 120 ? normalized : normalized[..120];
    }

    private static string NormalizeTag(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", "_");

    private static bool IsMisconceptionProbe(DiagnosticQuestion question) =>
        NormalizeTag(question.QuestionType).Contains("misconception", StringComparison.OrdinalIgnoreCase) ||
        NormalizeTag(question.ExpectedMisconceptionCategory) is "conceptual" or "procedural" or "application" or "calculation" or "reading";

    private static bool IsTechnicalTopic(string topicTitle) =>
        Regex.IsMatch(topicTitle, @"\b(c#|csharp|java|python|javascript|typescript|sql|async|await|task|api|programlama|kod|debug|thread)\b", RegexOptions.IgnoreCase);

    private static bool LooksCodeLike(string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Contains("```", StringComparison.Ordinal) ||
         Regex.IsMatch(text, @"\b(await|async|Task|Thread|try|catch|return|if|for|while|class|public|var)\b") &&
         Regex.IsMatch(text, @"[;{}()=]"));

    private sealed record DiagnosticQuestion(
        string Question,
        List<string> Options,
        string CorrectAnswer,
        string Explanation,
        string SkillTag,
        string Difficulty,
        string ConceptTag,
        string LearningObjective,
        string QuestionType,
        string ExpectedMisconceptionCategory);

    private sealed class DiagnosticQuestionBlueprint
    {
        public string Type { get; set; } = "multiple_choice";
        public string Question { get; set; } = string.Empty;
        public List<DiagnosticOption> Options { get; set; } = [];
        public string CorrectAnswer { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public string SkillTag { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;
        public string ConceptTag { get; set; } = string.Empty;
        public string LearningObjective { get; set; } = string.Empty;
        public string QuestionType { get; set; } = string.Empty;
        public string ExpectedMisconceptionCategory { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
    }

    private sealed record DiagnosticOption(string Text, bool IsCorrect);
}

public sealed record DiagnosticQuizQualityReport(
    bool IsAcceptable,
    int QuestionCount,
    int DuplicateQuestionCount,
    int ConceptDiversity,
    int QuestionTypeDiversity,
    bool HasCodeLikeQuestion,
    IReadOnlyList<string> Failures);

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

    public static string EnsureQualityOrThrow(
        string rawJson,
        string topicTitle,
        int expectedQuestionCount,
        out DiagnosticQuizQualityReport report)
    {
        var cleaned = ExtractJsonArray(rawJson);
        report = Validate(cleaned, topicTitle);

        var failures = report.Failures.ToList();
        if (report.QuestionCount != expectedQuestionCount)
        {
            failures.Add($"Question count mismatch: {report.QuestionCount}/{expectedQuestionCount}.");
        }

        if (report.QuestionCount is < 15 or > 25)
        {
            failures.Add($"Diagnostic quiz must contain 15-25 questions; actual={report.QuestionCount}.");
        }

        if (ContainsForbiddenPlanDiagnosticScaffold(cleaned))
        {
            failures.Add("Quiz leaked internal diagnostic scaffold or generic pipeline wording.");
        }

        if (IsJavaAlgorithmTopic(topicTitle) && !LooksLikeJavaAlgorithmQuiz(cleaned))
        {
            failures.Add("Java algorithms diagnostic must stay on Java + algorithms/data-structures concepts.");
        }

        if (IsJavaAlgorithmTopic(topicTitle) &&
            Regex.IsMatch(cleaned, @"\b(c#|csharp|\.net|visual studio)\b", RegexOptions.IgnoreCase))
        {
            failures.Add("Java diagnostic leaked unrelated C#/.NET/Visual Studio wording.");
        }

        report = new DiagnosticQuizQualityReport(
            failures.Count == 0,
            report.QuestionCount,
            report.DuplicateQuestionCount,
            report.ConceptDiversity,
            report.QuestionTypeDiversity,
            report.HasCodeLikeQuestion,
            failures);

        if (!report.IsAcceptable)
        {
            throw new InvalidOperationException($"Diagnostic quiz quality failed: {string.Join(" | ", failures.Take(5))}");
        }

        return cleaned;
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

        var correctnessLabelLeakCount = questions.Count(q =>
            q.Options.Any(LeaksCorrectnessLabel) ||
            LeaksCorrectnessLabel(q.CorrectAnswer));
        if (correctnessLabelLeakCount > 0)
        {
            failures.Add($"Answer options leak correctness labels instead of testing knowledge: {correctnessLabelLeakCount}.");
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
        var profile = DetectFallbackProfile(topicTitle);
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
            var codeSnippet = profile.IsTechnical && i is 0 or 3 or 7 or 18
                ? $"\n\nKod:\n```{profile.CodeFenceLanguage}\n{profile.CodeSnippet}\n```\nBu parcada seviye belirlemek icin en onemli risk veya karar noktasi nedir?"
                : string.Empty;

            var options = BuildNeutralDiagnosticOptions(i, profile);
            var correctOption = options.First(option => option.IsCorrect).Text;

            return new DiagnosticQuestionBlueprint
            {
                Type = "multiple_choice",
                Question = $"{topicTitle}: {i + 1}. seviye sorusu - {BuildQuestionStem(profile, t.Item5)}{codeSnippet}",
                Options = options,
                CorrectAnswer = correctOption,
                Explanation = i == 0
                    ? BuildFirstExplanation(profile, t.Item5)
                    : $"{t.Item5} Bu soru, {topicTitle} icin seviye belirleme amacli kavram ve uygulama ayrimini olcer.",
                SkillTag = $"{profile.SkillPrefix}-{t.Item3}",
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

    private static DiagnosticFallbackProfile DetectFallbackProfile(string topicTitle)
    {
        var normalized = topicTitle.ToLowerInvariant();

        if (normalized.Contains("c#", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("csharp", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(".net", StringComparison.OrdinalIgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                true,
                "csharp",
                "csharp",
                "C#",
                "Orka IDE sandbox'ta C# akisini okuyup derleme/runtime riskini ayirt etmek.",
                "var user = users.First(u => u.Id == selectedId);\nConsole.WriteLine(user.Name.ToUpper());");
        }

        if (Regex.IsMatch(normalized, @"\bpython|py\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                true,
                "python",
                "python",
                "Python",
                "Orka IDE sandbox'ta Python veri akisini ve hata kaynagini okumak.",
                "items = [1, 2, 3]\nprint(items[3])");
        }

        if (Regex.IsMatch(normalized, @"\bjava\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                true,
                "java",
                "java",
                "Java",
                "Orka IDE sandbox'ta Java kod akisini, algoritma adimlarini ve veri yapisi kararini ayirt etmek.",
                "int[] numbers = {4, 1, 3};\nArrays.sort(numbers);\nSystem.out.println(numbers[0]);");
        }

        if (Regex.IsMatch(normalized, @"\b(javascript|typescript|react|node|js|ts)\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                true,
                "javascript",
                "javascript",
                "JavaScript",
                "Orka IDE sandbox'ta async/veri akisini ve state etkisini ayirt etmek.",
                "const data = fetch('/api/items');\nconsole.log(data.length);");
        }

        if (Regex.IsMatch(normalized, @"\bsql|database|postgres|veritabani|veri tabani\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                true,
                "sql",
                "sql",
                "SQL",
                "Sorgu niyetini, filtreyi ve veri sonucunu ayirt etmek.",
                "SELECT name FROM users WHERE created_at > NOW();");
        }

        if (IsTechnicalTopic(topicTitle))
        {
            return new DiagnosticFallbackProfile(
                true,
                "coding",
                "text",
                "programlama",
                "Orka IDE akisi icinde kavrami kucuk bir kod veya pratik adimla test etmek.",
                "read input\napply the selected concept\ncompare the observed output with the expected result");
        }

        if (Regex.IsMatch(normalized, @"\bkpss|yks|tyt|ayt|sinav|exam\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                false,
                "exam",
                "text",
                "sinav",
                "Soru kokunu, kavrami ve distractor tuzaklarini ayirt etmek.",
                string.Empty);
        }

        if (Regex.IsMatch(normalized, @"\bmatematik|math|geometri|olasilik|kombinasyon\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                false,
                "math",
                "text",
                "matematik",
                "Verilen kosulu, formulu ve sonuc kontrolunu ayirt etmek.",
                string.Empty);
        }

        return new DiagnosticFallbackProfile(
            false,
            "general",
            "text",
            "genel",
            "Kavrami ezberden degil, senaryo kosullarindan okuyarak uygulamak.",
            string.Empty);
    }

    private static string BuildQuestionStem(DiagnosticFallbackProfile profile, string objective)
    {
        if (profile.IsTechnical)
        {
            return $"{objective}. {profile.Scenario}";
        }

        return $"{objective}. {profile.Scenario} Bu tanida Orka once kosullari, sonra kavrami ve son olarak uygulanacak kucuk adimi olcer.";
    }

    private static string BuildFirstExplanation(DiagnosticFallbackProfile profile, string objective)
    {
        if (profile.IsTechnical)
        {
            return $"{objective} Bu soru, {profile.DisplayName} icin Orka IDE odakli hata okuma ve kavrami uygulama ayrimini olcer.";
        }

        return $"{objective} Bu soru, ezber cevapla senaryodan karar verme arasindaki farki olcer.";
    }

    private static List<DiagnosticOption> BuildNeutralDiagnosticOptions(int index, DiagnosticFallbackProfile profile)
    {
        var technicalSets = new[]
        {
            new[]
            {
                new DiagnosticOption("Orka IDE'de en kucuk akisi calistirip sonucu hatanin kok nedeniyle karsilastirmak.", true),
                new DiagnosticOption("Hata mesajina bakmadan ilk gorunen satiri tamamen silmek.", false),
                new DiagnosticOption("Kod calismiyorsa kavrami degil sadece dosya adini degistirmek.", false),
                new DiagnosticOption("Ciktiyi okumadan en kisa secenegi secmek.", false)
            },
            new[]
            {
                new DiagnosticOption("Senaryodaki kisitlari okuyup kavrami ona gore uygulamak.", true),
                new DiagnosticOption("Tanimi ezberden yazip senaryoyu dikkate almamak.", false),
                new DiagnosticOption("Ilgili kavram yerine benzer gorunen terimi secmek.", false),
                new DiagnosticOption("Sonucu sadece hata mesajinin ilk kelimesinden tahmin etmek.", false)
            },
            new[]
            {
                new DiagnosticOption("Once veri akisini, sonra karar adimini belirlemek.", true),
                new DiagnosticOption("Kontrol akisini okumadan ciktiyi tahmin etmek.", false),
                new DiagnosticOption("Tum degiskenleri ayni anda sabit kabul etmek.", false),
                new DiagnosticOption("Ornek senaryodaki on kosullari yok saymak.", false)
            },
            new[]
            {
                new DiagnosticOption("Hatanin belirtisini kok nedeninden ayirmak.", true),
                new DiagnosticOption("Derleme hatasini her zaman mantik hatasi saymak.", false),
                new DiagnosticOption("Cozumu sadece satir sayisini azaltmak olarak gormek.", false),
                new DiagnosticOption("Kod ciktisini okumadan en kisa secenegi secmek.", false)
            }
        };

        var generalSets = new[]
        {
            new[]
            {
                new DiagnosticOption("Once soru kosullarini ayirip kavrami bu kosullara gore uygulamak.", true),
                new DiagnosticOption("Tanimi ezberden tekrar edip senaryoyu dikkate almamak.", false),
                new DiagnosticOption("Benzer gorunen terimi asil kavram yerine secmek.", false),
                new DiagnosticOption("Sonucu yalnizca ilk kelimeye bakarak tahmin etmek.", false)
            },
            new[]
            {
                new DiagnosticOption("Ornek durumdaki neden-sonuc iliskisini kurup kucuk adimla ilerlemek.", true),
                new DiagnosticOption("Tum durumlarda ayni sabit cevabi kullanmak.", false),
                new DiagnosticOption("On kosullari yok sayip sadece basliga gore karar vermek.", false),
                new DiagnosticOption("Aciklamayi okumadan ezber kural secmek.", false)
            },
            new[]
            {
                new DiagnosticOption("Kavrami bir senaryoda uygulayip cevabin nedenini aciklamak.", true),
                new DiagnosticOption("Soru kokundeki kisitlari gereksiz ayrinti saymak.", false),
                new DiagnosticOption("Yan kavrami ana kavram gibi kullanmak.", false),
                new DiagnosticOption("Cozumu sadece daha uzun cevap yazmak sanmak.", false)
            },
            new[]
            {
                new DiagnosticOption("Belirti ile kok nedeni ayirip buna gore karar vermek.", true),
                new DiagnosticOption("Her hatayi ayni kategoriye koymak.", false),
                new DiagnosticOption("Ornekteki istisnayi yok saymak.", false),
                new DiagnosticOption("Kanitsiz tahmini kesin bilgi gibi kullanmak.", false)
            }
        };

        return (profile.IsTechnical ? technicalSets : generalSets)[index % 4].ToList();
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

    private static bool IsTechnicalTopic(string topicTitle)
    {
        var normalized = topicTitle.ToLowerInvariant();
        if (normalized.Contains("c#", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(".net", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Regex.IsMatch(topicTitle, @"\b(csharp|java|python|javascript|typescript|sql|async|await|task|api|programlama|kod|debug|thread)\b", RegexOptions.IgnoreCase);
    }

    private static bool ContainsForbiddenPlanDiagnosticScaffold(string text) =>
        ContainsAny(text, "tani sorusu", "tanı sorusu", "input -> transform", "validation fails", "generic pipeline", "basic-flow");

    private static bool IsJavaAlgorithmTopic(string topicTitle)
    {
        var normalized = topicTitle.ToLowerInvariant();
        return normalized.Contains("java", StringComparison.OrdinalIgnoreCase) &&
               ContainsAny(normalized, "algoritma", "algorithm", "data structure", "veri yap");
    }

    private static bool LooksLikeJavaAlgorithmQuiz(string text)
    {
        var normalized = text.ToLowerInvariant();
        var hasJava = normalized.Contains("java", StringComparison.OrdinalIgnoreCase) ||
                      ContainsAny(normalized, "public static void main", "arraylist", "hashmap", "int[]", "list<");
        var hasAlgorithm = ContainsAny(normalized, "algoritma", "algorithm", "veri yap", "data structure", "big-o", "complexity", "karmaşıklık", "karmasiklik", "siralama", "sorting", "arama", "search");
        return hasJava && hasAlgorithm;
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static bool LooksCodeLike(string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Contains("```", StringComparison.Ordinal) ||
         Regex.IsMatch(text, @"\b(await|async|Task|Thread|try|catch|return|if|for|while|class|public|var)\b") &&
         Regex.IsMatch(text, @"[;{}()=]"));

    private static bool LeaksCorrectnessLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = NormalizeOptionText(text);
        return Regex.IsMatch(normalized, @"^(a\)|b\)|c\)|d\))?\s*(dogru|yanlis|correct|wrong)(\s+(yaklasim|secenek|option|answer))?\s*[:\-.]", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(normalized, @"^(dogru|yanlis|correct|wrong)\s+(secenek|option|answer)$", RegexOptions.IgnoreCase);
    }

    private static string NormalizeOptionText(string value) =>
        value.Trim()
            .ToLowerInvariant()
            .Replace('ç', 'c')
            .Replace('ğ', 'g')
            .Replace('ı', 'i')
            .Replace('ö', 'o')
            .Replace('ş', 's')
            .Replace('ü', 'u');

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

    private sealed record DiagnosticFallbackProfile(
        bool IsTechnical,
        string SkillPrefix,
        string CodeFenceLanguage,
        string DisplayName,
        string Scenario,
        string CodeSnippet);

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

using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orka.Core.DTOs;
using Orka.Core.DTOs.PlanDiagnostic;

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
        return report.IsAcceptable ? cleaned : BuildFallbackDiagnosticBlueprint(topicTitle);
    }

    public static string EnsureQualityOrThrow(
        string rawJson,
        string topicTitle,
        int expectedQuestionCount,
        out DiagnosticQuizQualityReport report,
        LearningBlueprintDto? learningBlueprint = null)
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

        if (ContainsForbiddenPlanDiagnosticScaffold(cleaned) ||
            ContainsAny(cleaned, "orka ide", "sandbox"))
        {
            failures.Add("Quiz leaked internal diagnostic scaffold or generic pipeline wording.");
        }

        if ((learningBlueprint?.Domain.Equals("history", StringComparison.OrdinalIgnoreCase) == true ||
             IsHistoryTopic(topicTitle)) &&
            LooksLikeProgrammingDiagnostic(cleaned))
        {
            failures.Add("History diagnostic leaked programming/debugging/API/performance scaffolding.");
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

    public static string BuildFallbackDiagnosticBlueprint(string topicTitle, LearningBlueprintDto? learningBlueprint = null)
    {
        var profile = DetectFallbackProfile(topicTitle);
        var items = BuildLegacyAdapterSpecs(topicTitle, learningBlueprint, profile, 20);
        return BuildFallbackDiagnosticBlueprint(topicTitle, new AssessmentGrammarDto
        {
            RequestedQuestionCount = items.Count,
            Items = items
        });
    }

    public static string BuildFallbackDiagnosticBlueprint(string topicTitle, AssessmentGrammarDto assessmentGrammar)
    {
        var profile = DetectFallbackProfile(topicTitle);
        var items = assessmentGrammar.Items
            .OrderBy(i => i.Order)
            .Where(i => !string.IsNullOrWhiteSpace(i.ConceptKey))
            .ToList();

        if (items.Count == 0)
        {
            items = BuildLegacyAdapterSpecs(topicTitle, null, profile, 20);
        }

        var requestedCount = Math.Clamp(
            assessmentGrammar.RequestedQuestionCount > 0 ? assessmentGrammar.RequestedQuestionCount : items.Count,
            15,
            25);

        var questions = Enumerable.Range(0, requestedCount).Select(i =>
        {
            var spec = items[i % items.Count];
            var codeSnippet = profile.IsTechnical && i is 0 or 5 or 10 or 15
                ? $"\n\nKod:\n```{profile.CodeFenceLanguage}\n{BuildGenericCodeSnippet(profile, spec)}\n```"
                : string.Empty;
            var options = BuildNeutralDiagnosticOptions(i, profile, spec);
            var correctOption = options.First(option => option.IsCorrect).Text;
            var misconception = string.IsNullOrWhiteSpace(spec.MisconceptionTarget)
                ? ((i % 5) == 4 ? "Conceptual" : "Reading")
                : spec.MisconceptionTarget;

            return new DiagnosticQuestionBlueprint
            {
                Type = "multiple_choice",
                AssessmentItemId = spec.AssessmentItemId == Guid.Empty ? Guid.NewGuid() : spec.AssessmentItemId,
                AssessmentItemKey = string.IsNullOrWhiteSpace(spec.AssessmentItemKey)
                    ? $"fallback:{spec.ConceptKey}:{i + 1:00}"
                    : spec.AssessmentItemKey,
                ConceptKey = spec.ConceptKey,
                CognitiveSkill = string.IsNullOrWhiteSpace(spec.CognitiveSkill)
                    ? RequiredQuestionTypes[i % RequiredQuestionTypes.Length]
                    : spec.CognitiveSkill,
                MisconceptionTarget = spec.MisconceptionTarget,
                EvidenceExpected = string.IsNullOrWhiteSpace(spec.EvidenceExpected)
                    ? BuildSafeLearningObjective(profile, i)
                    : spec.EvidenceExpected,
                ScoringRule = string.IsNullOrWhiteSpace(spec.ScoringRule)
                    ? "selected_option_exact_match"
                    : spec.ScoringRule,
                LearningOutcomeIds = spec.LearningOutcomeKeys.Count > 0
                    ? spec.LearningOutcomeKeys
                    : [$"{spec.ConceptKey}-outcome"],
                Question = BuildFallbackQuestionText(topicTitle, i, profile, spec, codeSnippet),
                Options = options,
                CorrectAnswer = correctOption,
                Explanation = BuildFallbackExplanation(profile, topicTitle, i, spec),
                SkillTag = spec.ConceptKey,
                Difficulty = string.IsNullOrWhiteSpace(spec.Difficulty) ? BuildDifficulty(i, requestedCount) : spec.Difficulty,
                ConceptTag = spec.ConceptKey,
                LearningObjective = string.IsNullOrWhiteSpace(spec.EvidenceExpected)
                    ? BuildSafeLearningObjective(profile, i)
                    : spec.EvidenceExpected,
                QuestionType = string.IsNullOrWhiteSpace(spec.CognitiveSkill)
                    ? RequiredQuestionTypes[i % RequiredQuestionTypes.Length]
                    : spec.CognitiveSkill,
                ExpectedMisconceptionCategory = misconception,
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

    public static void EnsureAssessmentMetadataOrThrow(string rawJson, int expectedQuestionCount)
    {
        using var doc = JsonDocument.Parse(ExtractJsonArray(rawJson));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Diagnostic quiz metadata validation failed: output is not an array.");
        }

        var questions = doc.RootElement.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Object)
            .ToList();
        if (questions.Count != expectedQuestionCount)
        {
            throw new InvalidOperationException($"Diagnostic quiz metadata validation failed: question count {questions.Count}/{expectedQuestionCount}.");
        }

        var missing = questions.Count(q =>
            string.IsNullOrWhiteSpace(GetString(q, "assessmentItemId")) ||
            string.IsNullOrWhiteSpace(GetString(q, "conceptKey")) ||
            string.IsNullOrWhiteSpace(GetString(q, "cognitiveSkill")) ||
            string.IsNullOrWhiteSpace(GetString(q, "evidenceExpected")) ||
            string.IsNullOrWhiteSpace(GetString(q, "scoringRule")) ||
            !q.TryGetProperty("learningOutcomeIds", out var outcomes) ||
            outcomes.ValueKind != JsonValueKind.Array);
        if (missing > 0)
        {
            throw new InvalidOperationException($"Diagnostic quiz metadata validation failed: {missing} questions are missing assessment grammar metadata.");
        }

        var conceptCoverage = questions
            .Select(q => GetString(q, "conceptKey"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var minimumConceptCoverage = Math.Min(8, Math.Max(3, (int)Math.Ceiling(expectedQuestionCount * 0.20)));
        if (conceptCoverage < minimumConceptCoverage)
        {
            throw new InvalidOperationException($"Diagnostic quiz metadata validation failed: concept coverage {conceptCoverage}/{minimumConceptCoverage}.");
        }

        var difficultySpread = questions
            .Select(q => GetString(q, "difficulty"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (difficultySpread < 2)
        {
            throw new InvalidOperationException($"Diagnostic quiz metadata validation failed: difficulty spread {difficultySpread}/2.");
        }

        var cognitiveSpread = questions
            .Select(q => GetString(q, "cognitiveSkill"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (cognitiveSpread < 3)
        {
            throw new InvalidOperationException($"Diagnostic quiz metadata validation failed: cognitive skill spread {cognitiveSpread}/3.");
        }
    }

    private static List<AssessmentItemSpecDto> BuildLegacyAdapterSpecs(
        string topicTitle,
        LearningBlueprintDto? blueprint,
        DiagnosticFallbackProfile profile,
        int count)
    {
        var seeds = (blueprint?.Concepts.Count > 0 ? blueprint.Concepts : [])
            .Concat(blueprint?.AssessmentAxes ?? [])
            .Concat(blueprint?.SubConcepts ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        if (seeds.Count == 0)
        {
            seeds = Enumerable.Range(1, Math.Max(8, Math.Min(12, count)))
                .Select(i => $"{profile.SkillPrefix}-concept-{i:00}")
                .ToList();
        }

        return Enumerable.Range(0, count).Select(i =>
        {
            var label = CleanLabel(seeds[i % seeds.Count], topicTitle, i);
            var key = StableKey(label, i);
            var cognitive = RequiredQuestionTypes[i % RequiredQuestionTypes.Length];
            return new AssessmentItemSpecDto
            {
                AssessmentItemId = Guid.NewGuid(),
                AssessmentItemKey = $"legacy-adapter:{StableKey(topicTitle, 0)}:{key}:{i + 1:00}",
                ConceptKey = key,
                ConceptLabel = label,
                CognitiveSkill = cognitive,
                Difficulty = BuildDifficulty(i, count),
                MisconceptionTarget = cognitive == "misconception_probe"
                    ? "conceptual confusion or nearby distractor"
                    : string.Empty,
                EvidenceExpected = BuildSafeLearningObjective(profile, i),
                LearningOutcomeKeys = [$"{key}-outcome"],
                OptionQualityRules =
                [
                    "one clearly correct option",
                    "three plausible distractors",
                    "no correctness labels",
                    "no Orka UI wording"
                ],
                ScoringRule = "selected_option_exact_match",
                Order = i
            };
        }).ToList();
    }

    private static string BuildFallbackQuestionText(
        string topicTitle,
        int index,
        DiagnosticFallbackProfile profile,
        AssessmentItemSpecDto spec,
        string codeSnippet)
    {
        var concept = string.IsNullOrWhiteSpace(spec.ConceptLabel) ? spec.ConceptKey : spec.ConceptLabel;
        var skill = string.IsNullOrWhiteSpace(spec.CognitiveSkill)
            ? RequiredQuestionTypes[index % RequiredQuestionTypes.Length]
            : spec.CognitiveSkill;
        var stem = skill switch
        {
            "procedural" => $"{concept} icin dogru adim sirasi hangisidir?",
            "application" => $"{concept} bilgisini verilen durumda uygularken hangi karar daha saglamdir?",
            "analysis" => $"{concept} ile ilgili kaniti yorumlarken once neye bakilmalidir?",
            "misconception_probe" => $"{concept} konusunda en olasi kavram yanilgisini ayirmak icin hangi ipucu kullanilir?",
            _ => $"{concept} kavramini anlamak icin en guvenilir kontrol hangisidir?"
        };

        if (!string.IsNullOrWhiteSpace(codeSnippet))
        {
            stem = skill switch
            {
                "analysis" => $"{concept} için aşağıdaki örnekte sonuç veya risk nasıl okunmalıdır?",
                "misconception_probe" => $"{concept} için aşağıdaki örnekte hangi yanılgıya dikkat edilmelidir?",
                _ => $"{concept} için aşağıdaki örnek hangi akıl yürütmeyi gerektirir?"
            };
        }

        return $"{topicTitle}: Soru {index + 1} - {stem}{codeSnippet}";
    }

    private static string BuildFallbackExplanation(
        DiagnosticFallbackProfile profile,
        string topicTitle,
        int index,
        AssessmentItemSpecDto spec)
    {
        var concept = string.IsNullOrWhiteSpace(spec.ConceptLabel) ? spec.ConceptKey : spec.ConceptLabel;
        var evidence = string.IsNullOrWhiteSpace(spec.EvidenceExpected)
            ? BuildSafeLearningObjective(profile, index)
            : spec.EvidenceExpected;
        return $"{topicTitle} icin {concept} cevabi, ezberden degil verilen kosul ve beklenen kanit uzerinden degerlendirilir: {evidence}.";
    }

    private static string BuildSafeLearningObjective(DiagnosticFallbackProfile profile, int index)
    {
        if (profile.IsTechnical)
        {
            return (index % 5) switch
            {
                0 => "Kod okuma ve veri akisini yorumlama",
                1 => "Kavrami senaryo kisitlarina gore uygulama",
                2 => "Hata belirtisi ile kok nedeni ayirma",
                3 => "Dogru uygulama adimini secme",
                _ => "Kavram yanilgisini fark etme"
            };
        }

        return (index % 5) switch
        {
            0 => "Soru kosulunu dogru okuma",
            1 => "Kavrami senaryoya uygulama",
            2 => "Distractor veya yanilgi ayirma",
            3 => "Cozum adimini siralama",
            _ => "Sonucu gerekceyle kontrol etme"
        };
    }

    private static List<DiagnosticOption> BuildNeutralDiagnosticOptions(
        int index,
        DiagnosticFallbackProfile profile,
        AssessmentItemSpecDto spec)
    {
        var concept = string.IsNullOrWhiteSpace(spec.ConceptLabel) ? "hedef kavram" : spec.ConceptLabel;
        var evidence = string.IsNullOrWhiteSpace(spec.EvidenceExpected)
            ? "verilen kosulu kavramla eslestirmek"
            : spec.EvidenceExpected;

        List<DiagnosticOption> options;
        if (index % 4 == 0)
        {
            options = new List<DiagnosticOption>
            {
                new DiagnosticOption($"{concept} icin verilen kosulu okuyup {evidence} kanitini aramak.", true),
                new DiagnosticOption($"{concept} basligini gorunce ayrinti okumadan ezber cevap vermek.", false),
                new DiagnosticOption("Benzer gorunen ama hedef kavrama ait olmayan ipucunu secmek.", false),
                new DiagnosticOption("Kaynak veya soru kosulu olmadan tahmini kesin bilgi saymak.", false)
            };
        }
        else if (index % 4 == 1)
        {
            options = new List<DiagnosticOption>
            {
                new DiagnosticOption("Once on kosulu, sonra uygulama adimini kontrol etmek.", true),
                new DiagnosticOption("On kosullari atlayip sonuca dogrudan atlamak.", false),
                new DiagnosticOption("Yan kavrami asil kavramin yerine kullanmak.", false),
                new DiagnosticOption("Sadece en uzun secenegi guvenilir kabul etmek.", false)
            };
        }
        else if (index % 4 == 2)
        {
            options = new List<DiagnosticOption>
            {
                new DiagnosticOption("Kucuk ornekte kavram, kanit ve sonucu birlikte eslestirmek.", true),
                new DiagnosticOption("Ornekteki kisitlari gereksiz ayrinti saymak.", false),
                new DiagnosticOption("Belirti ile kok nedeni ayni sey gibi yorumlamak.", false),
                new DiagnosticOption("Ilk tanidik kelimeyi dogru cevap saymak.", false)
            };
        }
        else
        {
            options = new List<DiagnosticOption>
            {
                new DiagnosticOption("Kaniti, kavrami ve sonucu ayni anda kontrol etmek.", true),
                new DiagnosticOption("Sadece basliga bakarak cevap secmek.", false),
                new DiagnosticOption("Benzer terimleri kanitsiz ayni kabul etmek.", false),
                new DiagnosticOption("Aciklamayi okumadan tahmin yapmak.", false)
            };
        }

        // Shuffling using a deterministic seed based on index & concept hash to avoid static random lock/contention
        var seed = Math.Abs(index + concept.GetHashCode());
        var rng = new Random(seed);
        int n = options.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            var value = options[k];
            options[k] = options[n];
            options[n] = value;
        }

        return options;
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
                "var user = users.First(u => u.Id == selectedId);\nConsole.WriteLine(user.Name.ToUpper());");
        }

        if (Regex.IsMatch(normalized, @"\bpython|py\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                true,
                "python",
                "python",
                "items = [1, 2, 3]\nprint(items[3])");
        }

        if (Regex.IsMatch(normalized, @"\bjava\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                true,
                "java",
                "java",
                "int[] numbers = {4, 1, 3};\nArrays.sort(numbers);\nSystem.out.println(numbers[0]);");
        }

        if (Regex.IsMatch(normalized, @"\b(javascript|typescript|react|node|js|ts)\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                true,
                "javascript",
                "javascript",
                "const data = fetch('/api/items');\nconsole.log(data.length);");
        }

        if (Regex.IsMatch(normalized, @"\bsql|database|postgres|veritabani|veri tabani\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                true,
                "sql",
                "sql",
                "SELECT name FROM users WHERE created_at > NOW();");
        }

        if (IsTechnicalTopic(topicTitle))
        {
            return new DiagnosticFallbackProfile(
                true,
                "technical",
                "text",
                "read input\napply selected concept\ncompare observed output with expected result");
        }

        if (Regex.IsMatch(normalized, @"\bkpss|yks|tyt|ayt|sinav|exam\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(false, "exam", "text", string.Empty);
        }

        if (Regex.IsMatch(normalized, @"\bmatematik|math|geometri|olasilik|kombinasyon\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(false, "math", "text", string.Empty);
        }

        return new DiagnosticFallbackProfile(false, "general", "text", string.Empty);
    }

    private static string BuildGenericCodeSnippet(DiagnosticFallbackProfile profile, AssessmentItemSpecDto spec)
    {
        if (!string.IsNullOrWhiteSpace(profile.CodeSnippet))
        {
            return profile.CodeSnippet;
        }

        var concept = string.IsNullOrWhiteSpace(spec.ConceptKey)
            ? "concept"
            : spec.ConceptKey.Replace("-", "_", StringComparison.Ordinal);
        return $"input = [1, 2, 3]\nresult = apply_{concept}(input)\nprint(result)";
    }

    private static string BuildDifficulty(int index, int count) =>
        index < count * 0.3 ? "kolay" : index < count * 0.75 ? "orta" : "zor";

    private static string CleanLabel(string value, string topicTitle, int index)
    {
        var cleaned = Regex.Replace(value ?? string.Empty, @"[_\-]+", " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = $"{topicTitle} kavram {index + 1}";
        }

        return cleaned.Length <= 80 ? cleaned : cleaned[..80];
    }

    private static string StableKey(string value, int index)
    {
        var normalized = NormalizeOptionText(value ?? string.Empty);
        normalized = Regex.Replace(normalized, @"[^a-z0-9]+", "-", RegexOptions.IgnoreCase).Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = $"concept-{index + 1:00}";
        }

        return normalized.Length <= 48 ? normalized : normalized[..48].Trim('-');
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

    private static bool IsHistoryTopic(string topicTitle)
    {
        var normalized = NormalizeOptionText(topicTitle);
        return ContainsAny(normalized, "tarih", "history", "selcuk", "selcuklu", "ottoman", "osmanli", "roma", "medieval");
    }

    private static bool LooksLikeProgrammingDiagnostic(string text)
    {
        var normalized = NormalizeOptionText(text);
        return ContainsAny(normalized,
            "code-flow",
            "api-shape",
            "debugging",
            "production-scenario",
            "performance",
            "orka ide",
            "sandbox",
            "visual studio",
            "java code",
            "sql query");
    }

    private static bool ContainsForbiddenPlanDiagnosticScaffold(string text) =>
        ContainsAny(text, "tani sorusu", "input -> transform", "validation fails", "generic pipeline", "basic-flow");

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static bool LooksCodeLike(string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Contains("```", StringComparison.Ordinal) ||
         Regex.IsMatch(text, @"\b(await|async|Task|Thread|try|catch|return|if|for|while|class|public|var|SELECT)\b", RegexOptions.IgnoreCase) &&
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
        string CodeSnippet);

    private sealed class DiagnosticQuestionBlueprint
    {
        public string Type { get; set; } = "multiple_choice";
        public Guid AssessmentItemId { get; set; }
        public string AssessmentItemKey { get; set; } = string.Empty;
        public string ConceptKey { get; set; } = string.Empty;
        public string CognitiveSkill { get; set; } = string.Empty;
        public string MisconceptionTarget { get; set; } = string.Empty;
        public string EvidenceExpected { get; set; } = string.Empty;
        public string ScoringRule { get; set; } = "selected_option_exact_match";
        public List<string> LearningOutcomeIds { get; set; } = [];
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

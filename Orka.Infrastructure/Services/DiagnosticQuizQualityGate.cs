using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnyAscii;
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
        return EnsureQualityOrFallback(rawJson, topicTitle, 20, "Turkish", out report);
    }

    public static string EnsureQualityOrFallback(string rawJson, string topicTitle, int expectedQuestionCount, string language, out DiagnosticQuizQualityReport report)
    {
        var cleaned = ExtractJsonArray(rawJson);
        report = Validate(cleaned, topicTitle, expectedQuestionCount);
        if (!report.IsAcceptable)
        {
            throw new InvalidOperationException($"Diagnostic quiz quality failed: {string.Join(" | ", report.Failures.Take(5))}");
        }

        return cleaned;
    }

    public static string EnsureQualityOrThrow(
        string rawJson,
        string topicTitle,
        int expectedQuestionCount,
        out DiagnosticQuizQualityReport report,
        LearningBlueprintDto? learningBlueprint = null)
    {
        var cleaned = ExtractJsonArray(rawJson);
        report = Validate(cleaned, topicTitle, expectedQuestionCount);

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
        return Validate(rawJson, topicTitle, 20);
    }

    public static DiagnosticQuizQualityReport Validate(string rawJson, string topicTitle, int expectedQuestionCount)
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
            q.Options.Count < 4 ||
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

        var unresolvedCorrectOptions = questions.Count(q => q.CorrectOptionIndex < 0);
        if (unresolvedCorrectOptions > 0)
        {
            failures.Add($"Questions where correct option cannot be resolved from options: {unresolvedCorrectOptions}.");
        }

        var firstOptionCorrectCount = questions.Count(q => q.CorrectOptionIndex == 0);
        if (questions.Count >= 8 && firstOptionCorrectCount >= Math.Ceiling(questions.Count * 0.60m))
        {
            failures.Add($"Correct option position pattern is unsafe: first option is correct {firstOptionCorrectCount}/{questions.Count}.");
        }

        var conceptDiversity = questions
            .Select(q => NormalizeTag(q.ConceptTag))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var minConceptDiversity = expectedQuestionCount >= 15 ? 5 : Math.Min(4, Math.Max(2, expectedQuestionCount / 2));
        if (conceptDiversity < minConceptDiversity)
        {
            failures.Add($"Concept diversity too low: {conceptDiversity}/{minConceptDiversity}.");
        }

        var questionTypeDiversity = questions
            .Select(q => NormalizeTag(q.QuestionType))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var minQuestionTypeDiversity = expectedQuestionCount >= 15 ? 4 : Math.Min(3, Math.Max(2, expectedQuestionCount / 2));
        if (questionTypeDiversity < minQuestionTypeDiversity)
        {
            failures.Add($"Question type diversity too low: {questionTypeDiversity}/{minQuestionTypeDiversity}.");
        }

        var misconceptionProbeCount = questions.Count(IsMisconceptionProbe);
        var minMisconceptionProbes = expectedQuestionCount >= 15 ? 5 : Math.Min(2, Math.Max(1, expectedQuestionCount / 4));
        if (misconceptionProbeCount < minMisconceptionProbes)
        {
            failures.Add($"Misconception probes too low: {misconceptionProbeCount}/{minMisconceptionProbes}.");
        }

        var misconceptionProbeWithoutRationales = questions.Count(q => IsMisconceptionProbe(q) && q.DistractorRationaleCount < 3);
        if (misconceptionProbeWithoutRationales > 0)
        {
            failures.Add($"Misconception probes missing option-level distractor rationales: {misconceptionProbeWithoutRationales}.");
        }

        var genericMisconceptionTargets = questions.Count(q =>
            IsMisconceptionProbe(q) &&
            (string.IsNullOrWhiteSpace(q.MisconceptionTarget) ||
             NormalizeTag(q.MisconceptionTarget) is "commonmistakes" or "common_mistakes" or "misconception" or "conceptual" or "procedural"));
        if (genericMisconceptionTargets > Math.Max(1, misconceptionProbeCount / 2))
        {
            failures.Add($"Misconception probes are too generic: {genericMisconceptionTargets}/{misconceptionProbeCount}.");
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
        var questionCount = NormalizeQuestionCount(learningBlueprint?.RecommendedQuestionCount ?? 20);
        return BuildFallbackDiagnosticBlueprint(topicTitle, questionCount, "Turkish", learningBlueprint);
    }

    public static string BuildFallbackDiagnosticBlueprint(string topicTitle, int questionCount, string language, LearningBlueprintDto? learningBlueprint = null)
    {
        return BuildFallbackDiagnosticBlueprintCore(topicTitle, NormalizeQuestionCount(questionCount), language, learningBlueprint, null);
    }

    public static string BuildFallbackDiagnosticBlueprint(string topicTitle, AssessmentGrammarDto assessmentGrammar)
    {
        return BuildFallbackDiagnosticBlueprint(topicTitle, assessmentGrammar, "Turkish");
    }

    public static string BuildFallbackDiagnosticBlueprint(string topicTitle, AssessmentGrammarDto assessmentGrammar, string language)
    {
        var questionCount = assessmentGrammar.RequestedQuestionCount > 0
            ? assessmentGrammar.RequestedQuestionCount
            : assessmentGrammar.Items.Count > 0 ? assessmentGrammar.Items.Count : 20;
        return BuildFallbackDiagnosticBlueprintCore(topicTitle, NormalizeQuestionCount(questionCount), language, null, assessmentGrammar);
    }

    private static string BuildFallbackDiagnosticBlueprintCore(
        string topicTitle,
        int questionCount,
        string language,
        LearningBlueprintDto? learningBlueprint,
        AssessmentGrammarDto? assessmentGrammar)
    {
        var topicLabel = CleanTopicLabel(topicTitle);
        var topicKey = BuildTopicKey(topicLabel);
        var misconceptionProbeTarget = Math.Max(5, (int)Math.Ceiling(questionCount * 0.35m));
        var typeCycle = new[] { "conceptual", "procedural", "application", "analysis" };
        var difficultyCycle = new[] { "kolay", "orta", "zor" };
        var grammarItems = assessmentGrammar?.Items.OrderBy(i => i.Order).ToArray() ?? [];

        var questions = Enumerable.Range(1, questionCount).Select(i =>
        {
            var grammarItem = i <= grammarItems.Length ? grammarItems[i - 1] : null;
            var questionType = i <= misconceptionProbeTarget
                ? "misconception_probe"
                : typeCycle[(i - misconceptionProbeTarget - 1) % typeCycle.Length];
            var conceptKey = CleanOrDefault(grammarItem?.ConceptKey, $"{topicKey}-concept-{((i - 1) % 8) + 1}");
            var conceptLabel = CleanOrDefault(grammarItem?.ConceptLabel, $"{topicLabel} kavram {((i - 1) % 8) + 1}");
            var misconceptionTarget = CleanOrDefault(
                grammarItem?.MisconceptionTarget,
                $"{conceptKey}-evidence-misread-{i}");
            var correctText = $"{conceptLabel} icin kaniti, kavrami ve kisiti birlikte kontrol eder {i}";
            var options = BuildFallbackOptions(i, correctText, misconceptionTarget);
            var question = BuildFallbackQuestionText(topicLabel, conceptLabel, questionType, i);
            var assessmentItemId = grammarItem?.AssessmentItemId is { } id && id != Guid.Empty
                ? id
                : Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}");

            return new
            {
                type = "multiple_choice",
                assessmentItemId,
                assessmentItemKey = CleanOrDefault(grammarItem?.AssessmentItemKey, $"{topicKey}-diagnostic-{i}"),
                question,
                options,
                correctAnswer = correctText,
                explanation = $"{conceptLabel} icin cevap, kanit ve kisitlari birlikte degerlendiren secenektir.",
                skillTag = conceptKey,
                difficulty = CleanOrDefault(grammarItem?.Difficulty, difficultyCycle[(i - 1) % difficultyCycle.Length]),
                conceptTag = conceptKey,
                conceptKey,
                cognitiveSkill = CleanOrDefault(grammarItem?.CognitiveSkill, questionType),
                learningObjective = CleanOrDefault(
                    grammarItem?.EvidenceExpected,
                    $"{conceptLabel} icin tani kaniti uretmek."),
                learningOutcomeIds = grammarItem?.LearningOutcomeKeys.Count > 0
                    ? grammarItem.LearningOutcomeKeys.ToArray()
                    : new[] { $"outcome:{conceptKey}" },
                questionType,
                misconceptionTarget,
                expectedMisconceptionCategory = $"{conceptKey}-misconception-pattern",
                evidenceExpected = CleanOrDefault(
                    grammarItem?.EvidenceExpected,
                    $"{conceptLabel} hakkinda gerekceli karar."),
                scoringRule = CleanOrDefault(grammarItem?.ScoringRule, "selected_option_exact_match"),
                language = string.IsNullOrWhiteSpace(language) ? "Turkish" : language,
                source = learningBlueprint?.Domain ?? "deterministic_assessment_contract"
            };
        });

        return JsonSerializer.Serialize(questions, JsonOptions);
    }

    private static object[] BuildFallbackOptions(int index, string correctText, string misconceptionTarget)
    {
        var distractors = new object[]
        {
            new { text = $"Sadece tanimi ezberler ve senaryodaki kaniti atlar {index}", isCorrect = false, rationale = "Evidence is ignored.", misconceptionKey = $"{misconceptionTarget}-definition-only" },
            new { text = $"Benzer kavrami asil hedefle karistirir {index}", isCorrect = false, rationale = "Nearby concept confusion.", misconceptionKey = $"{misconceptionTarget}-nearby-concept" },
            new { text = $"Ilk ipucuna bakip gerekce yazmadan karar verir {index}", isCorrect = false, rationale = "Surface clue guessing.", misconceptionKey = $"{misconceptionTarget}-surface-clue" }
        };
        var options = new List<object>(distractors);
        options.Insert((index - 1) % 4, new { text = correctText, isCorrect = true, rationale = "Matches the target evidence.", misconceptionKey = string.Empty });
        return options.ToArray();
    }

    private static string BuildFallbackQuestionText(string topicLabel, string conceptLabel, string questionType, int index)
    {
        var prompt = $"Tani {index}: {topicLabel} icin {conceptLabel} konusunda hangi secenek kanita dayali en iyi karari verir?";
        if (index == 2 && IsTechnicalTopic(topicLabel))
        {
            prompt += topicLabel.Contains("sql", StringComparison.OrdinalIgnoreCase)
                ? "\n```sql\nSELECT key, value FROM sample_table WHERE key = @target;\n```"
                : "\n```text\nfor (int i = 0; i < n; i++) { total += values[i]; }\n```";
        }

        return questionType == "misconception_probe"
            ? $"{prompt} Ayrica yaygin yanilgiyi ayirt et."
            : prompt;
    }

    private static int NormalizeQuestionCount(int questionCount) =>
        Math.Clamp(questionCount <= 0 ? 20 : questionCount, 15, 25);

    private static string CleanTopicLabel(string topicTitle) =>
        string.IsNullOrWhiteSpace(topicTitle) ? "Konu" : topicTitle.Trim();

    private static string BuildTopicKey(string topicTitle)
    {
        var key = Regex.Replace(topicTitle.Transliterate().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(key))
            key = "konu";

        return key.Length <= 48 ? key : key[..48].Trim('-');
    }

    private static string CleanOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

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
            string.IsNullOrWhiteSpace(GetString(q, "misconceptionTarget")) ||
            string.IsNullOrWhiteSpace(GetString(q, "evidenceExpected")) ||
            string.IsNullOrWhiteSpace(GetString(q, "scoringRule")) ||
            !HasAtLeastOptions(q, 4) ||
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

        var misconceptionProbeCount = questions.Count(q =>
            NormalizeTag(GetString(q, "questionType") ?? GetString(q, "cognitiveSkill") ?? string.Empty)
                .Contains("misconception", StringComparison.OrdinalIgnoreCase));
        var minimumMisconceptionProbes = Math.Max(3, (int)Math.Ceiling(expectedQuestionCount * 0.30m));
        if (misconceptionProbeCount < minimumMisconceptionProbes)
        {
            throw new InvalidOperationException($"Diagnostic quiz metadata validation failed: misconception probes {misconceptionProbeCount}/{minimumMisconceptionProbes}.");
        }
    }

    private static bool HasAtLeastOptions(JsonElement question, int minimum) =>
        question.TryGetProperty("options", out var options) &&
        options.ValueKind == JsonValueKind.Array &&
        options.GetArrayLength() >= minimum;

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
        var distractorRationaleCount = 0;
        var correctOptionIndex = -1;
        if (element.TryGetProperty("options", out var optionsElement) &&
            optionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in optionsElement.EnumerateArray())
            {
                if (option.ValueKind == JsonValueKind.String)
                {
                    if (MatchesCorrectAnswer(option.GetString(), GetString(element, "correctAnswer")))
                    {
                        correctOptionIndex = options.Count;
                    }

                    options.Add(option.GetString() ?? string.Empty);
                }
                else if (option.ValueKind == JsonValueKind.Object)
                {
                    var isCorrect = option.TryGetProperty("isCorrect", out var correctProp) &&
                                    correctProp.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                                    correctProp.GetBoolean();
                    var rationale = GetString(option, "rationale") ??
                                    GetString(option, "distractorRationale") ??
                                    GetString(option, "diagnosticSignalIfChosen");
                    var misconceptionKey = GetString(option, "misconceptionKey") ??
                                           GetString(option, "distractorMisconceptionKey");
                    if (!isCorrect && (!string.IsNullOrWhiteSpace(rationale) || !string.IsNullOrWhiteSpace(misconceptionKey)))
                    {
                        distractorRationaleCount++;
                    }

                    var text =
                        GetString(option, "text") ??
                        GetString(option, "value") ??
                        GetString(option, "label") ??
                        GetString(option, "id") ??
                        string.Empty;
                    if (isCorrect || MatchesCorrectAnswer(text, GetString(element, "correctAnswer")))
                    {
                        correctOptionIndex = options.Count;
                    }

                    options.Add(text);
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
            GetString(element, "conceptTag") ?? GetString(element, "conceptKey") ?? string.Empty,
            GetString(element, "learningObjective") ?? string.Empty,
            GetString(element, "questionType") ?? string.Empty,
            GetString(element, "expectedMisconceptionCategory") ?? string.Empty,
            GetString(element, "misconceptionTarget") ?? string.Empty,
            distractorRationaleCount,
            correctOptionIndex);
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
        value.Trim().ToLowerInvariant().Transliterate();

    private static bool MatchesCorrectAnswer(string? option, string? answer)
    {
        if (string.IsNullOrWhiteSpace(option) || string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        return string.Equals(NormalizeOptionText(option), NormalizeOptionText(answer), StringComparison.OrdinalIgnoreCase);
    }

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
        string ExpectedMisconceptionCategory,
        string MisconceptionTarget,
        int DistractorRationaleCount,
        int CorrectOptionIndex);

}

public sealed record DiagnosticQuizQualityReport(
    bool IsAcceptable,
    int QuestionCount,
    int DuplicateQuestionCount,
    int ConceptDiversity,
    int QuestionTypeDiversity,
    bool HasCodeLikeQuestion,
    IReadOnlyList<string> Failures);

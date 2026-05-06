using System.Text;

namespace Orka.Infrastructure.Services;

public static class PlanIntelligenceBriefBuilder
{
    private const int MaxBriefLength = 5200;

    public static string BuildForPlan(
        string topicTitle,
        string? compressedResearchPromptBlock,
        string? diagnosticQuizSummary = null)
    {
        return Build("deep_plan", topicTitle, compressedResearchPromptBlock, diagnosticQuizSummary);
    }

    public static string BuildForDiagnosticQuiz(
        string topicTitle,
        string? compressedResearchPromptBlock)
    {
        return Build("diagnostic_quiz", topicTitle, compressedResearchPromptBlock, diagnosticQuizSummary: null);
    }

    private static string Build(
        string purpose,
        string topicTitle,
        string? compressedResearchPromptBlock,
        string? diagnosticQuizSummary)
    {
        var topic = string.IsNullOrWhiteSpace(topicTitle) ? "Unknown topic" : topicTitle.Trim();
        var block = compressedResearchPromptBlock ?? string.Empty;
        var parsed = Parse(block);
        var domain = DetectDomain(topic);
        var sb = new StringBuilder();

        sb.AppendLine("[PLAN INTELLIGENCE BRIEF - KORTEKS FILTERED]");
        sb.AppendLine($"Purpose: {purpose}");
        sb.AppendLine($"Topic: {topic}");
        sb.AppendLine($"DomainPolicy: {BuildDomainPolicy(domain)}");
        var groundingMode = parsed.TryGetValue("GroundingMode", out var grounding)
            ? grounding.FirstOrDefault() ?? "Unknown"
            : "Unknown";
        var sourceCountValue = parsed.TryGetValue("SourceCount", out var sourceCount)
            ? sourceCount.FirstOrDefault() ?? "0"
            : "0";
        sb.AppendLine($"GroundingMode: {groundingMode}");
        sb.AppendLine($"SourceAwareness: {sourceCountValue} bounded source signals; source titles are not curriculum titles.");

        if (parsed.TryGetValue("FallbackWarning", out var fallback) && fallback.Count > 0)
        {
            sb.AppendLine($"ResearchCaution: {Trim(fallback[0], 220)}");
        }

        AppendDiagnosticPriority(sb, diagnosticQuizSummary);

        sb.AppendLine("DecisionRule: Korteks is advisory evidence. Diagnostic results, domain scaffold, and adaptive learning context have priority.");
        sb.AppendLine("MustUse:");
        foreach (var item in BuildMustUse(domain))
        {
            sb.AppendLine($"- {item}");
        }

        AppendSection(sb, "MayUseFromKorteks.Curriculum", parsed, "CurriculumMapHints", 5);
        AppendSection(sb, "MayUseFromKorteks.Prerequisites", parsed, "PrerequisiteHints", 4);
        AppendSection(sb, "MayUseFromKorteks.Misconceptions", parsed, "LikelyMisconceptions", 4);
        AppendSection(sb, "MayUseFromKorteks.Freshness", parsed, "WebFreshnessFacts", 3);
        AppendSection(sb, "MayUseFromKorteks.YouTubePedagogy", parsed, "YouTubeLearningReferences", 3);
        AppendKeyFactsOnlyIfUseful(sb, parsed, topic);

        sb.AppendLine("MustIgnore:");
        sb.AppendLine("- Raw provider prose, SEO snippets, website outlines, source names, and exact article/video titles as module titles.");
        sb.AppendLine("- Unrelated current-info facts that do not change the learning sequence.");
        sb.AppendLine("- Do not treat fallback/internal research as verified live evidence.");
        sb.AppendLine("- Generic 3-heading plans, generic 'overview' modules, and shallow lesson names.");

        sb.AppendLine("QualityContract:");
        sb.AppendLine("- Produce at least 6 modules, at least 4 lessons per module, and at least 24 lessons.");
        sb.AppendLine("- Module and lesson names must be specific to the topic and domain, not generic placeholders.");
        sb.AppendLine("- Programming plans must start from Orka IDE/sandbox practice and include code reading, debugging, refactor, mini project, and review loops.");
        sb.AppendLine("- Exam/math/language plans must follow their own domain sequence instead of programming patterns.");
        sb.AppendLine("- If the diagnostic was skipped, start beginner-safe but do not invent weak skills.");

        var result = sb.ToString();
        return result.Length <= MaxBriefLength ? result : result[..MaxBriefLength];
    }

    private static Dictionary<string, List<string>> Parse(string block)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentSection = null;

        foreach (var rawLine in block.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("[", StringComparison.Ordinal))
            {
                continue;
            }

            if (!line.StartsWith("-", StringComparison.Ordinal) && line.EndsWith(":", StringComparison.Ordinal))
            {
                currentSection = line.TrimEnd(':').Trim();
                Ensure(result, currentSection);
                continue;
            }

            var colon = line.IndexOf(':');
            if (!line.StartsWith("-", StringComparison.Ordinal) && colon > 0)
            {
                var key = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                Ensure(result, key).Add(Trim(value, 260));
                currentSection = null;
                continue;
            }

            if (currentSection != null && line.StartsWith("-", StringComparison.Ordinal))
            {
                var item = Trim(line[1..].Trim(), 260);
                if (ShouldKeepItem(item))
                {
                    Ensure(result, currentSection).Add(item);
                }
            }
        }

        return result;
    }

    private static void AppendDiagnosticPriority(StringBuilder sb, string? diagnosticQuizSummary)
    {
        if (string.IsNullOrWhiteSpace(diagnosticQuizSummary))
        {
            sb.AppendLine("DiagnosticPriority: no diagnostic summary supplied; do not invent weak skills.");
            return;
        }

        var lines = diagnosticQuizSummary
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line =>
                line.StartsWith("Mode:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Answered:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Correct:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Wrong:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("WeakConcepts:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("MistakePatterns:", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .Select(line => Trim(line, 220))
            .ToList();

        sb.AppendLine(lines.Count == 0
            ? "DiagnosticPriority: diagnostic supplied but no structured weakness line found; avoid guessing."
            : $"DiagnosticPriority: {string.Join(" | ", lines)}");
    }

    private static void AppendSection(
        StringBuilder sb,
        string outputName,
        IReadOnlyDictionary<string, List<string>> parsed,
        string sectionName,
        int maxItems)
    {
        if (!parsed.TryGetValue(sectionName, out var items) || items.Count == 0)
        {
            return;
        }

        sb.AppendLine($"{outputName}:");
        foreach (var item in items.Where(ShouldKeepItem).Distinct(StringComparer.OrdinalIgnoreCase).Take(maxItems))
        {
            sb.AppendLine($"- {Trim(RemoveUrlTail(item), 190)}");
        }
    }

    private static void AppendKeyFactsOnlyIfUseful(
        StringBuilder sb,
        IReadOnlyDictionary<string, List<string>> parsed,
        string topic)
    {
        if (!parsed.TryGetValue("KeyFacts", out var facts) || facts.Count == 0)
        {
            return;
        }

        var topicTokens = topic
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3)
            .Take(4)
            .ToArray();

        var usefulFacts = facts
            .Where(fact => topicTokens.Any(token => fact.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
                           ContainsAny(fact, "curriculum", "roadmap", "mufredat", "module", "lesson", "practice", "prerequisite", "temel", "hata"))
            .Where(ShouldKeepItem)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        if (usefulFacts.Count == 0)
        {
            return;
        }

        sb.AppendLine("MayUseFromKorteks.KeyFactsHighSignalOnly:");
        foreach (var fact in usefulFacts)
        {
            sb.AppendLine($"- {Trim(RemoveUrlTail(fact), 190)}");
        }
    }

    private static IEnumerable<string> BuildMustUse(PlanBriefDomain domain)
    {
        yield return "Build the curriculum from the learner's requested topic, not from the wording of a source snippet.";
        yield return "Use diagnostic weakness lines only when they exist; skipped diagnostics are not evidence of mistakes.";

        switch (domain)
        {
            case PlanBriefDomain.Programming:
                yield return "For programming, Orka IDE/sandbox is the primary practice environment.";
                yield return "Include code reading, debugging, refactor, mini project, and review checkpoints.";
                break;
            case PlanBriefDomain.Algorithm:
                yield return "For algorithms, sequence by patterns, data structures, complexity, drills, and timed problem solving.";
                break;
            case PlanBriefDomain.Exam:
                yield return "For exams, sequence by kazanım, soru tipi, timed practice, wrong-answer analysis, and review.";
                break;
            case PlanBriefDomain.Math:
                yield return "For math, sequence by concept, formula intuition, worked examples, mixed problems, and remediation.";
                break;
            case PlanBriefDomain.Language:
                yield return "For language learning, sequence vocabulary, grammar in use, listening/reading, speaking/writing prompts, and spaced review.";
                break;
            default:
                yield return "Use a concept-to-practice-to-review learning arc.";
                break;
        }
    }

    private static string BuildDomainPolicy(PlanBriefDomain domain) => domain switch
    {
        PlanBriefDomain.Programming => "Programming/Yazilim: Orka IDE first, external IDEs optional only, code practice required.",
        PlanBriefDomain.Algorithm => "Algorithm: pattern taxonomy, data structures, complexity, drills, timed practice.",
        PlanBriefDomain.Exam => "Exam: kazanım map, soru tipi, deneme, wrong-answer analysis, review pressure.",
        PlanBriefDomain.Math => "Math: theory intuition, worked examples, mixed practice, misconception repair.",
        PlanBriefDomain.Language => "Language: input, output, grammar in use, speaking/writing practice, spaced repetition.",
        _ => "General: concept map, examples, practice, review, project/application."
    };

    private static PlanBriefDomain DetectDomain(string topic)
    {
        var text = topic.ToLowerInvariant();
        if (ContainsAny(text, "hackerrank", "leetcode", "algoritma", "algorithm", "data structure", "veri yap", "dynamic programming", "two pointer"))
        {
            return PlanBriefDomain.Algorithm;
        }

        if (ContainsAny(text, "c#", "csharp", ".net", "python", "javascript", "typescript", "react", "java", "sql", "programlama", "yazilim", "yazılım", "kod", "api", "backend", "frontend"))
        {
            return PlanBriefDomain.Programming;
        }

        if (ContainsAny(text, "kpss", "yks", "tyt", "ayt", "ales", "dgs", "lgs", "sinav", "sınav", "deneme", "genel yetenek", "genel kultur", "genel kültür"))
        {
            return PlanBriefDomain.Exam;
        }

        if (ContainsAny(text, "matematik", "olasilik", "olasılık", "kombinasyon", "permutasyon", "integral", "turev", "türev", "geometri", "cebir"))
        {
            return PlanBriefDomain.Math;
        }

        if (ContainsAny(text, "ielts", "toefl", "yds", "yokdil", "yökdil", "ingilizce", "almanca", "fransizca", "fransızca", "language", "speaking", "konusma", "konuşma"))
        {
            return PlanBriefDomain.Language;
        }

        return PlanBriefDomain.General;
    }

    private static List<string> Ensure(Dictionary<string, List<string>> result, string key)
    {
        if (!result.TryGetValue(key, out var list))
        {
            list = [];
            result[key] = list;
        }

        return list;
    }

    private static bool ShouldKeepItem(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            return false;
        }

        var lower = item.ToLowerInvariant();
        return !lower.Contains("raw provider", StringComparison.Ordinal) &&
               !lower.Contains("transcript segment", StringComparison.Ordinal) &&
               !lower.Contains("timestamp", StringComparison.Ordinal) &&
               !lower.StartsWith("{", StringComparison.Ordinal) &&
               !lower.StartsWith("\"", StringComparison.Ordinal);
    }

    private static string RemoveUrlTail(string item)
    {
        var urlIndex = item.IndexOf("http", StringComparison.OrdinalIgnoreCase);
        return urlIndex <= 0 ? item : item[..urlIndex].Trim(' ', '-', '(');
    }

    private static string Trim(string value, int maxLength)
    {
        var normalized = string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private enum PlanBriefDomain
    {
        General,
        Programming,
        Algorithm,
        Exam,
        Math,
        Language
    }
}

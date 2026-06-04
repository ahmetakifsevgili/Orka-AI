using System.Text.RegularExpressions;
using AnyAscii;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Korteks;

namespace Orka.Infrastructure.Utilities;

public static class ResearchConceptExtractor
{
    public static List<string> ExtractConceptLabels(
        CompressedPlanResearchContextDto context,
        string domain,
        string mainTopic,
        string focusArea,
        string intent,
        string topicTitle,
        int maxConcepts = 12)
    {
        var candidates = ExtractMeasuredConceptCandidates(context, domain);
        var fallback = DefaultConceptLabels(domain, mainTopic, focusArea, intent, topicTitle);

        return candidates.Concat(fallback)
            .Select(label => Trim(label, 90))
            .Where(IsUsefulLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxConcepts)
            .ToList();
    }

    public static List<string> DefaultConceptLabels(string domain, string mainTopic, string focusArea, string intent, string topicTitle)
    {
        var baseTopic = FirstNonBlank(mainTopic, focusArea, intent, topicTitle, "core topic");
        return domain switch
        {
            "programming" => [$"{baseTopic} execution model", "syntax and types", "control flow", "functions and data flow", "error reading", "small implementation practice", "debugging reflection", "mixed project checkpoint"],
            "algorithms" => [$"{baseTopic} problem reading", "arrays and lists", "search and sorting", "complexity reasoning", "data structure choice", "recursion", "graph traversal", "pattern selection"],
            "sql" => [$"{baseTopic} schema reading", "filter selectivity", "index fundamentals", "join plans", "aggregation and sorting cost", "execution plan evidence", "safe optimization", "before-after validation"],
            "history" => [$"{baseTopic} chronology", "geography and setting", "main actors", "key events", "institutions", "cause and effect", "culture and legacy", "source comparison"],
            "math" => [$"{baseTopic} definitions", "formula intuition", "condition reading", "procedure selection", "mixed problems", "error checking", "review transfer", "applied model"],
            "language" => [$"{baseTopic} vocabulary", "grammar in use", "listening and reading input", "speaking prompts", "writing practice", "error correction", "spaced review", "real context transfer"],
            "exam" => [$"{baseTopic} objective map", "question stem reading", "distractor recognition", "timed decision", "wrong answer analysis", "mixed drill", "review pressure", "mock checkpoint"],
            _ => [$"{baseTopic} prerequisites", "core vocabulary", "concept model", "guided practice", "misconception repair", "mixed application", "review checkpoint", "transfer task"]
        };
    }

    private static List<string> ExtractMeasuredConceptCandidates(CompressedPlanResearchContextDto context, string domain)
    {
        var raw = context.CurriculumMapHints
            .SelectMany(line => SplitConceptLikePhrases(line, "curriculum"))
            .Concat(context.PrerequisiteHints.SelectMany(line => SplitConceptLikePhrases(line, "prerequisite")))
            .Concat(context.KeyFacts.SelectMany(line => SplitConceptLikePhrases(line, "fact")))
            .Concat(context.LikelyMisconceptions.SelectMany(line => SplitConceptLikePhrases(line, "misconception")))
            .Concat(context.TopSources.SelectMany(source => SplitConceptLikePhrases($"{source.Title}. {source.Snippet}", "source")));

        return raw
            .Select(label => NormalizeConceptCandidate(label, domain))
            .Where(label => IsUsefulLabel(label) && LooksLikeMeasuredConcept(label, domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private static IEnumerable<string> SplitConceptLikePhrases(string text, string sourceKind)
    {
        var clean = ConceptLabelFromText(text);
        if (string.IsNullOrWhiteSpace(clean) ||
            LooksLikeResearchInstruction(clean) ||
            LooksLikeSourceReference(clean) ||
            LooksLikeCurriculumContainer(clean))
        {
            yield break;
        }

        var delimiters = sourceKind == "curriculum"
            ? new[] { "->", "=>", ";", "|", "/", "," }
            : new[] { ";", "|", "," };

        var parts = delimiters.Aggregate(new[] { clean }, (current, delimiter) =>
            current.SelectMany(part => part.Split(delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToArray());

        foreach (var part in parts)
        {
            var candidate = Trim(part, 90);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static string NormalizeConceptCandidate(string label, string domain)
    {
        var clean = Regex.Replace(label, @"\([^)]*(youtube|video|channel|tutorial|playlist|e\.g\.|example)[^)]*\)", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\b(e\.g\.|for example|examples?)\b.*$", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"^\s*(module|lesson|step|unit|topic)\s+\d+[:\.\-\s]+", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s+", " ").Trim(' ', '.', ':', ';', '-');
        if (domain == "exam")
        {
            clean = clean.Replace("kazanim", "learning objective", StringComparison.OrdinalIgnoreCase);
        }

        return Trim(clean, 90);
    }

    private static bool LooksLikeMeasuredConcept(string label, string domain)
    {
        var normalized = NormalizeSearch(label);
        var wordCount = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount is < 2 or > 7)
        {
            return false;
        }

        if (LooksLikeResearchInstruction(label) || LooksLikeSourceReference(label) || LooksLikeCurriculumContainer(label))
        {
            return false;
        }

        if (ContainsAny(normalized,
            "prerequisite", "prerequisites", "practice", "worked examples", "examples", "quiz", "review",
            "learning objective", "learning outcomes", "study plan", "curriculum", "roadmap", "lesson",
            "introduction", "overview", "summary", "definition", "definitions", "basics", "fundamentals only",
            "video", "youtube", "playlist", "course", "tutorial", "official documentation", "documentation",
            "research", "source", "available", "provider", "compressed", "metadata", "transcript"))
        {
            return false;
        }

        return domain switch
        {
            "programming" => ContainsAny(normalized,
                "syntax", "type", "loop", "function", "class", "object", "async", "state", "error", "debug", "data", "api", "component", "hook", "query", "transaction", "memory", "exception"),
            "algorithms" => ContainsAny(normalized,
                "array", "list", "tree", "graph", "queue", "stack", "recursion", "sorting", "search", "complexity", "dynamic programming", "greedy", "hash", "heap"),
            "sql" => ContainsAny(normalized,
                "select", "join", "index", "where", "group", "aggregate", "transaction", "schema", "normalization",
                "query plan", "execution plan", "optimizer", "optimization", "selectivity", "cardinality", "statistics",
                "scan", "seek", "cost", "sorting", "sort", "constraint", "cte", "window", "validation"),
            "math" => ContainsAny(normalized,
                "integral", "derivative", "limit", "function", "equation", "theorem", "probability", "statistics", "bayes", "bayesian", "prior", "posterior", "likelihood", "sensitivity", "specificity", "base rate", "combination", "permutation", "matrix", "vector", "area", "rate", "substitution", "geometry", "algebra"),
            "language" => ContainsAny(normalized,
                "vocabulary", "grammar", "tense", "speaking", "listening", "reading", "writing", "pronunciation", "sentence", "paragraph"),
            "history" => ContainsAny(normalized,
                "chronology", "period", "empire", "reform", "war", "treaty", "institution", "cause", "effect", "geography", "actor", "source"),
            "exam" => ContainsAny(normalized,
                "question stem", "distractor", "timed", "wrong answer", "paragraph", "evidence", "elimination", "topic objective"),
            _ => !ContainsAny(normalized, "start", "use", "watch", "identify", "break down", "move from", "available", "source", "research")
        };
    }

    private static string ConceptLabelFromText(string text)
    {
        var clean = Regex.Replace(text, @"https?://\S+", " ");
        clean = Regex.Replace(clean, @"^[\-\*\d\.\)\s]+", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim(' ', '.', ':', ';');
        if (clean.Contains(':'))
        {
            clean = clean.Split(':', 2)[0].Trim();
        }
        return Trim(clean, 90);
    }

    private static bool IsUsefulLabel(string label) =>
        !string.IsNullOrWhiteSpace(label) &&
        label.Length >= 4 &&
        !ContainsAny(NormalizeSearch(label),
            "start from prerequisites",
            "move from small examples",
            "small examp",
            "map the focus area",
            "sub-concepts",
            "sub concepts",
            "prior skills",
            "basic examples",
            "learner starts",
            "watch for",
            "memorized definitions",
            "confused terminology",
            "direct learning",
            "research brief",
            "need source grounded",
            "practiceorder",
            "learning path",
            "conservative curriculum",
            "provider-backed sources",
            "available videos",
            "generated from",
            "no specific video",
            "no reliable transcript",
            "video metadata",
            "source metadata",
            "research intent") &&
        !label.Contains("degraded", StringComparison.OrdinalIgnoreCase) &&
        !label.Contains("unavailable", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeResearchInstruction(string value) =>
        ContainsAny(NormalizeSearch(value),
            "source grounded",
            "use source",
            "do not present",
            "fallback internal",
            "korteks",
            "provider",
            "warnings",
            "groundingmode",
            "no specific video",
            "no reliable transcript",
            "video metadata",
            "source metadata");

    private static bool LooksLikeSourceReference(string value) =>
        ContainsAny(NormalizeSearch(value), "http", "www.", ".com", ".org", "youtube", "wikipedia");

    private static bool LooksLikeCurriculumContainer(string value) =>
        ContainsAny(NormalizeSearch(value),
            "unit ", "module ", "lesson ", "week ", "day ", "part ", "phase ",
            "checkpoint", "roadmap", "syllabus", "curriculum", "course outline",
            "introductory", "advanced topics", "practice set", "homework", "exercise set",
            "exam prep", "mock test", "review session");

    private static string NormalizeSearch(string value) =>
        value.ToLowerInvariant().Transliterate();

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var clean = Regex.Replace(value.Trim(), @"\s+", " ");
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }
}

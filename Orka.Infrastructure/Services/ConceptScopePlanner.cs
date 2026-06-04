using System.Text.RegularExpressions;
using AnyAscii;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Korteks;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class ConceptScopePlanner : IConceptScopePlanner
{
    private static readonly string[] CognitiveProgression =
    [
        "foundations and vocabulary",
        "core model",
        "worked example reasoning",
        "procedure selection",
        "application constraints",
        "evidence interpretation",
        "misconception repair",
        "mixed transfer"
    ];

    public ConceptScopePlanDto BuildScope(
        string approvedResearchIntent,
        string topicTitle,
        string approvedMainTopic,
        string approvedFocusArea,
        CompressedPlanResearchContextDto compressedContext)
    {
        var domain = DetectDomain(approvedResearchIntent, topicTitle, approvedMainTopic, approvedFocusArea);
        var anchor = FirstNonBlank(approvedMainTopic, approvedFocusArea, topicTitle, approvedResearchIntent, "core topic");
        var warnings = new List<string>();

        var sourceLabels = compressedContext.TopSources
            .Select(s => $"{Trim(s.Provider, 40)}: {Trim(s.Title, 120)}")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        var candidates = DistinctByStableKey(ExtractCandidateLabels(
                approvedResearchIntent,
                topicTitle,
                approvedMainTopic,
                approvedFocusArea,
                compressedContext,
                domain)
            .Select(label => NormalizeCandidate(label, domain, anchor))
            .Where(label => IsUsefulMeasuredLabel(label, domain, anchor))
            .Distinct(StringComparer.OrdinalIgnoreCase))
            .Take(12)
            .ToList();

        if (candidates.Count < 8 && HasUsableResearchSignal(compressedContext))
        {
            var researchBackedCandidates = ResearchConceptExtractor.ExtractConceptLabels(
                    compressedContext,
                    domain,
                    approvedMainTopic,
                    approvedFocusArea,
                    approvedResearchIntent,
                    topicTitle,
                    maxConcepts: 12)
                .Select(label => NormalizeCandidate(label, domain, anchor))
                .Where(label => IsUsefulMeasuredLabel(label, domain, anchor));

            candidates = DistinctByStableKey(candidates.Concat(researchBackedCandidates))
                .Take(12)
                .ToList();
        }

        if (candidates.Count < 8)
        {
            warnings.Add("concept_scope_low_research_signal");
        }

        if (candidates.Count == 0)
        {
            return new ConceptScopePlanDto
            {
                Domain = domain,
                ScopeStatus = "insufficient_research_signal",
                TopicAnchor = anchor,
                Seeds = [],
                Warnings = warnings
            };
        }

        var misconceptions = compressedContext.LikelyMisconceptions
            .Select(m => Trim(m, 180))
            .Where(m => !string.IsNullOrWhiteSpace(m) && !LooksLikeResearchInstruction(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        if (misconceptions.Count == 0)
        {
            warnings.Add("concept_scope_missing_misconceptions");
        }

        var seeds = candidates.Select((label, index) =>
        {
            var key = StableKey(label);
            return new ConceptScopeSeedDto
            {
                Label = label,
                StableKey = key,
                Role = index == 0 ? "foundation" : "target",
                DifficultyBand = index < Math.Max(2, candidates.Count / 4)
                    ? "foundation"
                    : index >= Math.Ceiling(candidates.Count * 0.75m) ? "advanced" : "core",
                Order = index,
                PrerequisiteKeys = index == 0 ? [] : [StableKey(candidates[index - 1])],
                Misconceptions = misconceptions.Count == 0 ? [] : [misconceptions[index % misconceptions.Count]],
                SourceEvidenceLabels = sourceLabels.Take(3).ToList(),
                EvidenceBasis = compressedContext.SourceCount > 0 ? "source_grounded_research_brief" : "llm_research_concept_signal"
            };
        }).ToList();

        return new ConceptScopePlanDto
        {
            Domain = domain,
            ScopeStatus = warnings.Count == 0 ? "research_scoped" : "research_scoped_degraded",
            TopicAnchor = anchor,
            Seeds = seeds,
            Warnings = warnings
        };
    }

    private static IEnumerable<string> ExtractCandidateLabels(
        string intent,
        string topicTitle,
        string mainTopic,
        string focusArea,
        CompressedPlanResearchContextDto context,
        string domain)
    {
        foreach (var label in SplitFocus(focusArea))
        {
            yield return label;
        }

        foreach (var label in SplitFocus(mainTopic))
        {
            yield return label;
        }

        var lines = context.CurriculumMapHints
            .Concat(context.PrerequisiteHints)
            .Concat(context.KeyFacts.Where(line => LooksDomainSpecific(line, domain)));

        foreach (var line in lines)
        {
            foreach (var label in SplitConceptLine(line))
            {
                yield return label;
            }
        }

        foreach (var label in SplitFocus(topicTitle))
        {
            yield return label;
        }

        foreach (var label in SplitFocus(intent))
        {
            if (LooksDomainSpecific(label, domain))
            {
                yield return label;
            }
        }
    }

    private static IEnumerable<string> SplitConceptLine(string text)
    {
        var clean = StripSourceNoise(text);
        if (string.IsNullOrWhiteSpace(clean) || LooksLikeResearchInstruction(clean))
        {
            yield break;
        }

        if (clean.Contains(':'))
        {
            clean = clean.Split(':', 2)[1];
        }

        foreach (var part in Regex.Split(clean, @"(?:->|=>|;|\||,|/|\band\b|\bthen\b)", RegexOptions.IgnoreCase))
        {
            var trimmed = Trim(part, 90);
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                yield return trimmed;
            }
        }
    }

    private static IEnumerable<string> SplitFocus(string text)
    {
        foreach (var part in Regex.Split(text ?? string.Empty, @"(?:,|;|\||/|\band\b|\bwith\b|\bfrom\b|\bto\b)", RegexOptions.IgnoreCase))
        {
            var trimmed = Trim(part, 90);
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                yield return trimmed;
            }
        }
    }

    private static string NormalizeCandidate(string label, string domain, string anchor)
    {
        var clean = StripSourceNoise(label);
        clean = Regex.Replace(clean, @"\([^)]*(youtube|video|channel|tutorial|playlist|example|e\.g\.)[^)]*\)", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\b(e\.g\.|for example|examples?)\b.*$", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"^\s*(module|lesson|step|unit|topic|chapter)\s+\d+[:\.\-\s]+", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\b(learning path|study plan|roadmap|curriculum|practice order|quiz scope)\b", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\b(use|utilize|rely on|watch|read|find|search|source|sources like|available videos?)\b.*$", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"^\s*(start|begin)\s+(with|by|from)\s+", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"^\s*(explore|verify|check|review|practice|introduce|cover)\s+", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\b(from|by)\s+[A-Z][A-Za-z0-9&\.\- ]{2,}$", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"^\s*(ensuring|ensure)\s+(a\s+)?(solid\s+)?(grasp|understanding|command)\s+of\s+", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"^\s*(understanding|learning|studying)\s+(what|how)\s+", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"^\s*(understanding|learning|studying|finding|using|applying)\s+", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\b(are|is)\s*$", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s+", " ").Trim(' ', '.', ':', ';', '-', '(', ')', '"');

        if (IsBareAnchorEcho(clean, anchor, domain))
        {
            return string.Empty;
        }

        if (clean.Length > 0 &&
            clean.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 1 &&
            LooksDomainSpecific(clean, domain))
        {
            clean = $"{anchor} {clean}";
        }

        return Trim(ToTitleLike(clean), 90);
    }

    private static bool IsUsefulMeasuredLabel(string label, string domain, string anchor)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length < 4)
        {
            return false;
        }

        var normalized = NormalizeSearch(label);
        var wordCount = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount is < 2 or > 8)
        {
            return false;
        }

        if (LooksLikeResearchInstruction(label) || LooksLikeSourceReference(label))
        {
            return false;
        }

        if (ContainsAny(normalized,
            "start from prerequisites", "start with", "begin with", "move from small examples", "small examples", "watch for",
            "practiceorder", "guided practice", "mixed practice", "learning path", "study plan",
            "direct learning", "research brief", "provider warning", "source grounded",
            "no specific video", "no reliable transcript", "video metadata", "metadata is available",
            "metadata available", "transcript is available", "transcript available", "source metadata",
            "recommended question count", "reliable sources", "youtube learning references",
            "map the focus", "sub-concepts", "sub concepts", "prior skills", "skipped prerequisites",
            "confused terminology", "questions should", "approved intent", "internal product",
            "system wording", "diagnose my", "weak concepts", "concept check", "small worked",
            "error reflection", "applying a rule outside", "begeni",
            "rely on", "conservative knowledge", "practice creating", "practice ", "work on",
            "utilize", "sources like", "source like", "available video", "with guidance",
            "small dataset", "university", "course", "tutorial", "playlist", "i master",
            "diagnostic", "professional plan", "prerequisite", "prerequisites", "verify understanding",
            "check understanding", "review ", "explore ", "introduce "))
        {
            return false;
        }

        if (Regex.IsMatch(normalized, @"^(break|diagnose|create|build|generate|make|master|learn|study|teach|explain|cover|include|use|watch|read|find|search|ensure|ensuring|understand|understanding|start|begin|explore|verify|check|review|practice|introduce)\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        if (domain is "sql" or "programming" or "algorithms" or "math" or "history" or "language" or "exam")
        {
            return LooksDomainSpecific(label, domain);
        }

        return OverlapsAnchor(label, anchor) ||
               !ContainsAny(normalized, "use ", "watch ", "identify ", "break down ", "available ", "source ");
    }

    private static bool LooksDomainSpecific(string label, string domain)
    {
        var text = NormalizeSearch(label);
        return domain switch
        {
            "programming" => ContainsAny(text, "syntax", "type", "loop", "function", "class", "object", "async", "state", "error", "debug", "data", "api", "component", "hook", "query", "test"),
            "algorithms" => ContainsAny(text, "array", "list", "tree", "graph", "queue", "stack", "recursion", "sorting", "search", "complexity", "dynamic programming", "greedy", "hash"),
            "sql" => ContainsAny(text,
                "select", "join", "index", "where", "group", "aggregate", "transaction", "schema", "normalization",
                "query plan", "execution plan", "optimizer", "optimization", "selectivity", "cardinality", "statistics",
                "scan", "seek", "cost", "sorting", "sort", "constraint", "validation"),
            "math" => ContainsAny(text, "integral", "derivative", "limit", "function", "equation", "theorem", "probability", "statistics", "bayes", "bayesian", "prior", "posterior", "likelihood", "sensitivity", "specificity", "base rate", "matrix", "vector", "area", "rate", "rates", "substitution", "calculus", "chain rule", "implicit differentiation", "optimization", "continuity", "antiderivative"),
            "language" => ContainsAny(text, "vocabulary", "grammar", "tense", "speaking", "listening", "reading", "writing", "pronunciation", "sentence"),
            "history" => ContainsAny(text,
                "chronology", "period", "empire", "reform", "war", "treaty", "institution", "cause", "effect", "geography",
                "industrial", "revolution", "transformation", "technology", "adoption", "diffusion", "production", "labor",
                "labour", "market", "capital", "urban", "social", "global", "imperial", "trade", "source", "evidence",
                "interpretation", "legacy", "conditions", "drivers", "consequences"),
            "exam" => ContainsAny(text, "question stem", "distractor", "timed", "wrong answer", "paragraph", "evidence", "elimination", "objective"),
            _ => true
        };
    }

    private static string DetectDomain(params string[] values)
    {
        var text = NormalizeSearch(string.Join(' ', values));
        if (ContainsAny(text, "sql", "postgres", "mssql", "database", "veritabani", "query", "index")) return "sql";
        if (ContainsAny(text, "algorithm", "algoritma", "data structure", "veri yapi", "leetcode", "dynamic programming")) return "algorithms";
        if (ContainsAny(text, "python", "java", "csharp", "c#", ".net", "javascript", "typescript", "programlama", "programming", "coding", "react", "async", "await")) return "programming";
        if (ContainsAny(text, "kpss", "yks", "tyt", "ayt", "ielts", "toefl", "sinav", "exam", "paragraf")) return "exam";
        if (ContainsAny(text, "matematik", "math", "olasilik", "kombinasyon", "geometry", "geometri", "integral", "derivative", "turev", "calculus", "statistics", "statistik", "bayes", "bayesian", "prior", "posterior", "likelihood", "probability", "sensitivity", "specificity", "base rate")) return "math";
        if (ContainsAny(text, "english", "ingilizce", "language", "speaking", "grammar")) return "language";
        if (ContainsAny(text, "history", "tarih", "ottoman", "osmanli", "seljuk", "selcuk", "roma", "medieval", "industrial revolution", "revolution", "imperial")) return "history";
        return "general";
    }


    private static bool OverlapsAnchor(string label, string anchor)
    {
        var labelTerms = NormalizeSearch(label).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 4).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return NormalizeSearch(anchor).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(t => t.Length >= 4 && labelTerms.Contains(t));
    }

    private static bool IsBareAnchorEcho(string label, string anchor, string domain)
    {
        var normalized = NormalizeSearch(label);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        if (normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 1)
        {
            return false;
        }

        var anchorTerms = NormalizeSearch(anchor)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return anchorTerms.Contains(normalized) ||
               domain switch
               {
                   "math" => ContainsAny(normalized, "math", "calculus", "integral", "derivative", "function"),
                   "programming" => ContainsAny(normalized, "programming", "python", "react", "javascript", "typescript", "java"),
                   "sql" => ContainsAny(normalized, "sql", "query", "database"),
                   _ => false
               };
    }

    private static bool LooksLikeResearchInstruction(string value) =>
        ContainsAny(NormalizeSearch(value),
            "extract", "convert", "produce sections", "planning notes", "learningroute", "reliable sources",
            "youtubelearningreferences", "recommendedquestioncount", "use this compressed", "do not",
            "map the focus", "sub-concepts", "sub concepts", "move from small", "watch for",
            "questions should", "avoid internal", "system wording",
            "rely on conservative", "use sources", "utilize sources", "available videos",
            "no specific video", "no reliable transcript", "video metadata", "source metadata",
            "break ", "break down", "diagnose ", "create ", "generate ", "professional plan");

    private static bool LooksLikeSourceReference(string value) =>
        ContainsAny(NormalizeSearch(value), "http://", "https://", "wikipedia", "youtube", "provider", "searchwebdeep",
            "university", "khan academy", "coursera", "edx", "official documentation", "docs");

    private static bool HasUsableResearchSignal(CompressedPlanResearchContextDto context)
    {
        if (context.SourceCount > 0 || context.TopSources.Count > 0)
        {
            return true;
        }

        return context.CurriculumMapHints
            .Concat(context.PrerequisiteHints)
            .Concat(context.KeyFacts)
            .Concat(context.LikelyMisconceptions)
            .Any(value =>
                !string.IsNullOrWhiteSpace(value) &&
                !LooksLikeResearchInstruction(value) &&
                !LooksLikeSourceReference(value));
    }

    private static string StripSourceNoise(string value)
    {
        var clean = Regex.Replace(value ?? string.Empty, @"https?://\S+", " ");
        clean = Regex.Replace(clean, @"^[\-\*\d\.\)\s]+", " ");
        return Regex.Replace(clean, @"\s+", " ").Trim();
    }

    private static string StableKey(string value) =>
        Regex.Replace(NormalizeSearch(value), @"[^a-z0-9]+", "-").Trim('-');

    private static IEnumerable<string> DistinctByStableKey(IEnumerable<string> labels)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels)
        {
            var key = StableKey(label);
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
            {
                continue;
            }

            yield return label;
        }
    }

    private static string NormalizeSearch(string value) =>
        Regex.Replace((value ?? string.Empty).ToLowerInvariant().Transliterate(), @"\s+", " ").Trim();

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonBlank(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string Trim(string value, int max) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max].Trim();

    private static string ToTitleLike(string value)
    {
        var clean = value.Trim();
        if (clean.Length == 0 || clean.Any(char.IsUpper))
        {
            return clean;
        }

        return char.ToUpperInvariant(clean[0]) + clean[1..];
    }
}


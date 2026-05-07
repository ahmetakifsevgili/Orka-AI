using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orka.Core.DTOs.Korteks;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Enums;

namespace Orka.Infrastructure.Services;

public static class LearningBlueprintBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static LearningBlueprintDto Build(
        string approvedResearchIntent,
        string topicTitle,
        string approvedMainTopic,
        string approvedFocusArea,
        CompressedPlanResearchContextDto context)
    {
        var domain = DetectDomain(approvedResearchIntent, topicTitle, approvedMainTopic, approvedFocusArea);
        var blueprint = domain switch
        {
            "history" => BuildHistoryBlueprint(approvedResearchIntent, topicTitle, context),
            "sql" => BuildSqlBlueprint(approvedResearchIntent, topicTitle, context),
            "algorithms" => BuildAlgorithmBlueprint(approvedResearchIntent, topicTitle, context),
            "programming" => BuildProgrammingBlueprint(approvedResearchIntent, topicTitle, context),
            "exam" => BuildExamBlueprint(approvedResearchIntent, topicTitle, context),
            _ => BuildGeneralBlueprint(approvedResearchIntent, topicTitle, context)
        };

        blueprint.Domain = domain;
        blueprint.ApprovedResearchIntent = Clean(approvedResearchIntent, 180);
        blueprint.SourceConfidence = SourceConfidence(context);
        blueprint.SourceSignals = SourceSignals(context);
        blueprint.RecommendedQuestionCount = Math.Clamp(blueprint.RecommendedQuestionCount, 15, 25);
        return blueprint;
    }

    public static string BuildPromptBlock(LearningBlueprintDto blueprint)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[LEARNING BLUEPRINT]");
        sb.AppendLine($"BlueprintDomain: {blueprint.Domain}");
        sb.AppendLine($"BlueprintSourceConfidence: {blueprint.SourceConfidence}");
        Append(sb, "BlueprintLearningRoute", blueprint.LearningRoute);
        Append(sb, "BlueprintPrerequisites", blueprint.Prerequisites);
        Append(sb, "BlueprintSubConcepts", blueprint.SubConcepts);
        Append(sb, "BlueprintCommonMistakes", blueprint.CommonMistakes);
        Append(sb, "BlueprintPracticeOrder", blueprint.PracticeOrder);
        Append(sb, "BlueprintAssessmentAxes", blueprint.AssessmentAxes);
        Append(sb, "BlueprintPlanModules", blueprint.PlanModules.Select(m => $"{m.Title}: {string.Join(" | ", m.Lessons.Take(5))}"));
        Append(sb, "BlueprintTimeline", blueprint.Timeline);
        Append(sb, "BlueprintActors", blueprint.Actors);
        Append(sb, "BlueprintEvents", blueprint.Events);
        Append(sb, "BlueprintInstitutions", blueprint.Institutions);
        Append(sb, "BlueprintCauseEffectPairs", blueprint.CauseEffectPairs);
        Append(sb, "BlueprintSourceSignals", blueprint.SourceSignals);
        sb.AppendLine($"BlueprintRecommendedQuestionCount: {blueprint.RecommendedQuestionCount}");
        sb.AppendLine("Instruction: Quiz and plan must use this blueprint as the curriculum spine. Do not copy paid course content. If source confidence is low, stay conservative and domain-specific.");
        return sb.ToString();
    }

    public static string ComputeHash(LearningBlueprintDto blueprint)
    {
        var json = JsonSerializer.Serialize(blueprint, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    public static string CacheKey(string approvedResearchIntent)
    {
        var normalized = NormalizeForKey(approvedResearchIntent);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"learning-blueprint:{Convert.ToHexString(bytes)[..24].ToLowerInvariant()}";
    }

    private static LearningBlueprintDto BuildHistoryBlueprint(
        string approvedResearchIntent,
        string topicTitle,
        CompressedPlanResearchContextDto context)
    {
        var isSeljuk = ContainsAny(approvedResearchIntent, topicTitle, "seljuk", "selcuk", "selcuklu", "selçuklu");
        if (isSeljuk)
        {
            return new LearningBlueprintDto
            {
                LearningRoute =
                [
                    "Oghuz and Kinik origins of the Seljuks",
                    "Tughril and Chaghri Beg: Khorasan power base",
                    "Dandanakan and the formation of the Great Seljuk state",
                    "Abbasid legitimacy and Baghdad entry",
                    "Alp Arslan, Manzikert, and Anatolia's opening",
                    "Malik-Shah and Nizam al-Mulk: state organization",
                    "Iqta, madrasa, army, and administration",
                    "Culture, art, architecture, and Persianate bureaucracy",
                    "Succession struggles, Sanjar, Qatwan, and fragmentation",
                    "Anatolian Seljuk connection and long-term legacy"
                ],
                Prerequisites =
                [
                    "Basic medieval Islamic world geography",
                    "Turkic tribes and steppe political culture",
                    "Abbasid Caliphate and Ghaznavid context",
                    "Chronology reading and cause-effect reasoning"
                ],
                SubConcepts =
                [
                    "Oghuz-Kinik origins", "Khorasan", "Dandanakan", "Tughril Beg", "Alp Arslan",
                    "Manzikert", "Malik-Shah", "Nizam al-Mulk", "iqta", "Nizamiya madrasas",
                    "Sanjar", "Qatwan", "Anatolian Seljuks"
                ],
                CommonMistakes =
                [
                    "Confusing Great Seljuks with Anatolian Seljuks as the same political phase",
                    "Reducing Manzikert to a single battle without cause and consequence",
                    "Skipping Abbasid legitimacy and the Baghdad connection",
                    "Mixing Tughril, Alp Arslan, Malik-Shah, and Sanjar chronologically",
                    "Treating culture, administration, and military organization as separate from political history"
                ],
                PracticeOrder =
                [
                    "timeline ordering", "map Khorasan-Iran-Iraq-Anatolia", "actor-event matching",
                    "cause-effect paragraphs", "institution analysis", "mixed chronology quiz"
                ],
                AssessmentAxes =
                [
                    "chronology", "actor-event matching", "cause-effect", "geography",
                    "administration-institutions", "culture-civilization", "common-confusions",
                    "legacy-and-transition"
                ],
                Timeline =
                [
                    "Oghuz/Kinik background", "Khorasan rise", "1040 Dandanakan", "Baghdad and Abbasid legitimacy",
                    "1071 Manzikert", "Malik-Shah high period", "Sanjar and Qatwan", "fragmentation and Anatolian legacy"
                ],
                Actors = ["Tughril Beg", "Chaghri Beg", "Alp Arslan", "Malik-Shah", "Nizam al-Mulk", "Ahmad Sanjar", "Ghaznavids", "Abbasid Caliph"],
                Events = ["Dandanakan", "Baghdad entry", "Manzikert", "Nizamiya madrasas", "Qatwan"],
                Institutions = ["iqta", "madrasa", "vizierate", "army organization", "Abbasid legitimacy"],
                CauseEffectPairs =
                [
                    "Dandanakan -> Great Seljuk state formation",
                    "Baghdad entry -> Sunni political legitimacy",
                    "Manzikert -> Anatolia migration and frontier change",
                    "Nizam al-Mulk reforms -> administrative consolidation",
                    "succession struggles -> fragmentation"
                ],
                PlanModules =
                [
                    Module("Koken ve Ilk Yukselis", "Oghuz/Kinik arka plani", "Khorasan ve Gazneliler", "Tughril ve Chaghri Beg", "Ilk harita okuma"),
                    Module("Devletlesme ve Mesruiyet", "Dandanakan", "Baghdad ve Abbasi mesruiyeti", "sultanlik fikri", "neden-sonuc pratigi"),
                    Module("Alp Arslan ve Malazgirt", "Bizans-Seljuk iliskisi", "Manzikert sebepleri", "sonuclari", "Anadolu baglantisi"),
                    Module("Malik-Shah ve Nizam al-Mulk", "yuksek donem", "vezirlik", "Nizamiya", "iqta ve idare"),
                    Module("Kultur, Kurumlar ve Toplum", "ordu", "medrese", "sanat-mimari", "Persianate bureaucracy"),
                    Module("Dagilma ve Miras", "Sanjar", "Qatwan", "parcalanma", "Anadolu Seljuk mirasi")
                ],
                RecommendedQuestionCount = 20
            };
        }

        return new LearningBlueprintDto
        {
            LearningRoute = MergeHighSignal(context.CurriculumMapHints,
            [
                "chronological background and geography",
                "main actors and institutions",
                "key events and turning points",
                "cause-effect analysis",
                "culture and legacy",
                "mixed source-based review"
            ]),
            Prerequisites = MergeHighSignal(context.PrerequisiteHints, ["chronology reading", "map awareness", "basic vocabulary"]),
            SubConcepts = MergeHighSignal(context.KeyFacts, ["periodization", "actors", "events", "institutions", "causes", "consequences"]),
            CommonMistakes = MergeHighSignal(context.LikelyMisconceptions, ["mixing periods", "memorizing names without cause-effect", "ignoring geography"]),
            PracticeOrder = ["timeline", "actor-event match", "cause-effect", "map check", "short explanation"],
            AssessmentAxes = ["chronology", "actor-event matching", "cause-effect", "geography", "institutions", "common-confusions"],
            PlanModules =
            [
                Module($"{topicTitle} Donem ve Kronoloji", "baslangic baglami", "zaman sirasi", "harita baglami", "ana kavramlar"),
                Module("Aktörler ve Olaylar", "liderler", "ana olaylar", "donum noktalari", "actor-event pratigi"),
                Module("Neden-Sonuc ve Kurumlar", "sebep-sonuc", "idari kurumlar", "toplum ve ekonomi", "kurum analizi"),
                Module("Kultur ve Miras", "kultur", "sanat", "uzun vadeli etkiler", "kaynakli tekrar"),
                Module("Yanilgi Onarimi", "karisan donemler", "karisan aktorler", "kisa cevap pratigi", "mini quiz"),
                Module("Karma Tarih Pratigi", "kronoloji testi", "neden-sonuc yazimi", "harita-kavram baglantisi", "final kontrol")
            ],
            RecommendedQuestionCount = 20
        };
    }

    private static LearningBlueprintDto BuildAlgorithmBlueprint(string approvedResearchIntent, string topicTitle, CompressedPlanResearchContextDto context) => new()
    {
        LearningRoute = MergeHighSignal(context.CurriculumMapHints,
        [
            "arrays and lists", "search and sorting", "Big-O and complexity", "data structures",
            "recursion", "graphs", "algorithm patterns", "mixed timed practice"
        ]),
        Prerequisites = MergeHighSignal(context.PrerequisiteHints, ["language basics", "loops", "arrays", "functions", "reading small traces"]),
        SubConcepts = ["arrays", "lists", "linear search", "binary search", "sorting", "Big-O", "stack", "queue", "set", "map", "recursion", "BFS", "DFS", "dynamic programming"],
        CommonMistakes = MergeHighSignal(context.LikelyMisconceptions, ["binary search on unsorted data", "off-by-one", "wrong data structure", "memorized Big-O", "missing base case"]),
        PracticeOrder = ["trace arrays", "implement search", "sort and compare", "choose data structure", "recursion", "graph traversal", "pattern drills"],
        AssessmentAxes = ["array-reading", "search-preconditions", "sorting", "complexity", "data-structure-choice", "recursion", "graph-traversal", "pattern-selection"],
        Concepts = ["array", "list", "search", "sort", "complexity", "map", "set", "stack", "queue", "recursion", "graph", "dp"],
        CodeReadingTargets = ["small Java/Python/C# traces only when topic language requires it", "loop bounds", "collection behavior"],
        DebuggingTargets = ["off-by-one", "wrong precondition", "wrong structure", "missing base case"],
        PracticeLabs = ["search lab", "sort lab", "map/set frequency lab", "BFS/DFS toy graph", "DP table lab"],
        PlanModules =
        [
            Module("Problem Okuma ve Veri Akisi", "input-output", "edge cases", "small trace", "mistake check"),
            Module("Diziler, Listeler ve Arama", "array/list model", "linear search", "binary search", "sort-read practice"),
            Module("Karmasiklik ve Veri Yapisi Secimi", "Big-O", "map/set", "stack/queue", "priority queue"),
            Module("Algoritma Patternleri", "two pointers", "sliding window", "prefix sum", "greedy proof"),
            Module("Recursion ve Graph", "base case", "call stack", "BFS", "DFS"),
            Module("Karma Pratik ve Mastery", "timed drills", "wrong solution analysis", "mixed pattern selection", "final check")
        ],
        RecommendedQuestionCount = ComputeAlgorithmQuestionCount(approvedResearchIntent, topicTitle, context)
    };

    private static LearningBlueprintDto BuildSqlBlueprint(string approvedResearchIntent, string topicTitle, CompressedPlanResearchContextDto context) => new()
    {
        LearningRoute = MergeHighSignal(context.CurriculumMapHints,
        [
            "schema and data distribution", "filter selectivity", "index fundamentals",
            "joins and execution plans", "aggregation and sorting costs", "write-cost tradeoffs", "optimization lab"
        ]),
        Prerequisites = MergeHighSignal(context.PrerequisiteHints, ["SELECT/WHERE/JOIN basics", "table and index vocabulary", "reading query result shape"]),
        SubConcepts = ["index", "selectivity", "execution plan", "join", "covering index", "composite index", "aggregation", "sort", "transaction cost"],
        CommonMistakes = MergeHighSignal(context.LikelyMisconceptions, ["index every column", "ignore query plan", "remove joins to go faster", "confuse cache speed with optimization"]),
        PracticeOrder = ["read query", "measure plan", "identify bottleneck", "choose index/query rewrite", "test result equivalence", "compare before-after"],
        AssessmentAxes = ["query-shape", "index-selectivity", "join-plan", "execution-plan-reading", "aggregation-cost", "write-read-tradeoff", "safe-optimization"],
        Concepts = ["index", "query plan", "selectivity", "join", "aggregation", "covering index", "composite index"],
        CodeReadingTargets = ["SQL WHERE/JOIN/GROUP BY snippets", "EXPLAIN-like evidence"],
        DebuggingTargets = ["wrong index", "missing predicate", "join explosion", "duplicate/null effect"],
        PracticeLabs = ["slow query triage", "index candidate lab", "join rewrite lab", "plan comparison lab"],
        PlanModules =
        [
            Module("Sorgu Sekli ve Veri Modeli", "table relationships", "filters", "result shape", "safe baseline"),
            Module("Index Mantigi", "B-tree intuition", "selectivity", "composite order", "write cost"),
            Module("Execution Plan Okuma", "scan vs seek", "rows estimate", "join type", "bottleneck"),
            Module("Join ve Aggregation Optimizasyonu", "join filters", "grouping cost", "sort cost", "rewrite lab"),
            Module("Guvenli Optimizasyon Lab", "before-after test", "result equivalence", "transaction impact", "rollback thinking"),
            Module("Karma SQL Mastery", "mixed scenarios", "wrong index diagnosis", "timed query review", "final check")
        ],
        RecommendedQuestionCount = 24
    };

    private static LearningBlueprintDto BuildProgrammingBlueprint(string approvedResearchIntent, string topicTitle, CompressedPlanResearchContextDto context) => new()
    {
        LearningRoute = MergeHighSignal(context.CurriculumMapHints,
        [
            "language basics and execution model", "data and control flow", "functions and types",
            "error reading", "small implementation practice", "debugging reflection"
        ]),
        Prerequisites = MergeHighSignal(context.PrerequisiteHints, ["basic syntax", "variables", "conditions", "loops", "functions"]),
        SubConcepts = MergeHighSignal(context.KeyFacts, ["syntax", "types", "control flow", "functions", "error messages", "small programs"]),
        CommonMistakes = MergeHighSignal(context.LikelyMisconceptions, ["copying syntax without trace", "ignoring error message", "mixing compile-time and runtime behavior"]),
        PracticeOrder = ["read a tiny example", "predict output", "change one condition", "handle one error", "write a short exercise"],
        AssessmentAxes = ["syntax-reading", "control-flow", "types", "error-reading", "small-implementation", "misconception"],
        Concepts = ["syntax", "types", "control flow", "functions", "errors", "practice"],
        CodeReadingTargets = ["small code traces", "input-output", "compile/runtime distinction"],
        DebuggingTargets = ["syntax error", "runtime error", "wrong branch", "wrong type assumption"],
        PracticeLabs = ["trace lab", "small implementation lab", "error diagnosis lab", "mini refactor lab"],
        PlanModules =
        [
            Module("Dil Modeli ve Kucuk Program Okuma", "syntax", "types", "control flow", "tiny trace"),
            Module("Fonksiyonlar ve Veri Akisi", "function inputs", "return values", "side effects", "simple composition"),
            Module("Hata Okuma ve Duzeltme", "compile-time error", "runtime error", "root cause", "fix verification"),
            Module("Kucuk Uygulama Pratigi", "requirements", "step-by-step build", "test examples", "review"),
            Module("Yanilgi Onarimi", "common confusion", "contrast examples", "targeted drill", "reflection"),
            Module("Karma Pratik ve Sonraki Rota", "mixed exercises", "timed practice", "weak point review", "next topic")
        ],
        RecommendedQuestionCount = 20
    };

    private static LearningBlueprintDto BuildExamBlueprint(string approvedResearchIntent, string topicTitle, CompressedPlanResearchContextDto context) => new()
    {
        LearningRoute = MergeHighSignal(context.CurriculumMapHints, ["skill mapping", "question type recognition", "timed drills", "wrong-answer analysis", "review loop"]),
        Prerequisites = MergeHighSignal(context.PrerequisiteHints, ["question stem reading", "basic topic vocabulary", "short timed focus"]),
        SubConcepts = ["question root", "main idea", "inference", "distractor", "timing", "wrong-answer log"],
        CommonMistakes = MergeHighSignal(context.LikelyMisconceptions, ["bringing outside interpretation", "missing negative wording", "choosing familiar option", "ignoring time"]),
        PracticeOrder = ["stem first", "evidence mark", "option elimination", "timed set", "wrong-answer reflection", "spaced review"],
        AssessmentAxes = ["stem-reading", "main-idea", "inference", "distractor-elimination", "timing", "wrong-answer-analysis"],
        QuestionTypes = ["concept check", "short paragraph", "trap option", "timed decision", "wrong-answer diagnosis"],
        TimingStrategies = ["stem first", "mark evidence", "skip and return", "limit rereading"],
        CommonTrapTypes = ["overgeneralization", "outside knowledge", "negative stem", "similar wording"],
        PlanModules =
        [
            Module("Soru Koku ve Kazanim Haritasi", "soru kokunu ayirma", "istenen beceri", "kanit cizgisi", "mini kontrol"),
            Module("Ana Fikir ve Cikarim", "ana fikir", "yardimci fikir", "cikarim", "celdirici"),
            Module("Sure ve Strateji", "hizli okuma", "zaman siniri", "skip-return", "deneme mini seti"),
            Module("Yanlis Analizi", "yanlis tipi", "kanit eksigi", "dikkat hatasi", "telafi"),
            Module("Karma Pratik", "farkli soru tipleri", "sureli set", "sonuc kontrol", "review"),
            Module("Mastery ve Tekrar", "zayif beceri", "SRS", "gunluk mini hedef", "final kontrol")
        ],
        RecommendedQuestionCount = 20
    };

    private static LearningBlueprintDto BuildGeneralBlueprint(string approvedResearchIntent, string topicTitle, CompressedPlanResearchContextDto context) => new()
    {
        LearningRoute = MergeHighSignal(context.CurriculumMapHints, ["prerequisites", "core concepts", "worked examples", "practice", "misconception repair", "review"]),
        Prerequisites = MergeHighSignal(context.PrerequisiteHints, ["basic vocabulary", "prior concepts", "small examples"]),
        SubConcepts = MergeHighSignal(context.KeyFacts, ["core concept", "related concept", "application", "review"]),
        CommonMistakes = MergeHighSignal(context.LikelyMisconceptions, ["memorization without context", "skipping prerequisite", "mixing similar terms"]),
        PracticeOrder = ["concept check", "worked example", "guided practice", "mixed practice", "reflection"],
        AssessmentAxes = ["prerequisite", "conceptual", "procedural", "application", "misconception", "review"],
        PlanModules =
        [
            Module($"{topicTitle} Kavram Rotasi", "on bilgi", "ana kavram", "kavram iliskisi", "ilk kontrol"),
            Module("Temel Ornekler", "ornek okuma", "adim adim cozum", "yanilgi ayirma", "mini pratik"),
            Module("Uygulama", "senaryo", "uygulama", "hata kontrol", "geri bildirim"),
            Module("Yanilgi Onarimi", "sik hata", "telafi", "karsilastirma", "mini quiz"),
            Module("Karma Pratik", "mixed practice", "review", "flashcard", "daily step"),
            Module("Kapanis ve Sonraki Rota", "mastery check", "eksik notu", "sonraki hedef", "final kontrol")
        ],
        RecommendedQuestionCount = 18
    };

    private static string DetectDomain(params string[] values)
    {
        var text = NormalizeForSearch(string.Join(' ', values));
        if (ContainsAny(text, "seljuk", "selcuk", "selcuklu", "history", "tarih", "ottoman", "osmanli", "roma", "roman empire", "medieval"))
        {
            return "history";
        }

        if (ContainsAny(text, "sql", "postgres", "mssql", "database", "veritabani", "query", "index"))
        {
            return "sql";
        }

        if (ContainsAny(text, "algorithm", "algoritma", "data structure", "veri yapi", "hackerrank", "leetcode", "dynamic programming"))
        {
            return "algorithms";
        }

        if (ContainsAny(text, "kpss", "yks", "tyt", "ayt", "ielts", "toefl", "sinav", "exam", "paragraf"))
        {
            return "exam";
        }

        if (ContainsAny(text, "python", "java", "csharp", "c#", ".net", "javascript", "typescript", "programlama", "coding"))
        {
            return "programming";
        }

        if (ContainsAny(text, "matematik", "math", "olasilik", "kombinasyon", "geometry"))
        {
            return "math";
        }

        if (ContainsAny(text, "english", "ingilizce", "language", "speaking", "grammar"))
        {
            return "language";
        }

        return "general";
    }

    private static string SourceConfidence(CompressedPlanResearchContextDto context)
    {
        if (context.GroundingMode == GroundingMode.SourceGrounded && context.SourceCount >= 3)
        {
            return "high";
        }

        if (context.GroundingMode is GroundingMode.SourceGrounded or GroundingMode.PartialSourceGrounded && context.SourceCount > 0)
        {
            return "medium";
        }

        return "low";
    }

    private static List<string> SourceSignals(CompressedPlanResearchContextDto context)
    {
        return context.TopSources
            .Select(s => $"{Clean(s.Provider, 40)}: {Clean(s.Title, 120)}")
            .Concat(context.CurriculumMapHints.Take(3))
            .Concat(context.YouTubeLearningReferences.Take(2).Select(x => $"YouTube pedagogy: {Clean(x, 140)}"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static LearningBlueprintModuleDto Module(string title, params string[] lessons) => new()
    {
        Title = title,
        Lessons = lessons.Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
    };

    private static int ComputeAlgorithmQuestionCount(
        string approvedResearchIntent,
        string topicTitle,
        CompressedPlanResearchContextDto context)
    {
        var text = NormalizeForSearch(string.Join(' ',
            approvedResearchIntent,
            topicTitle,
            string.Join(' ', context.KeyFacts),
            string.Join(' ', context.CurriculumMapHints)));

        if (ContainsAny(text, "data structures", "data structure", "veri yapi", "graph", "dynamic programming", "dp", "recursion"))
        {
            return 25;
        }

        if (ContainsAny(text, "sorting", "search", "complexity", "big-o", "leetcode", "hackerrank"))
        {
            return 22;
        }

        return 20;
    }

    private static List<string> MergeHighSignal(IEnumerable<string> source, IReadOnlyList<string> fallback)
    {
        var items = source
            .Select(x => Clean(x, 160))
            .Where(IsUseful)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
        return items.Count >= 3
            ? items.Concat(fallback).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList()
            : fallback.ToList();
    }

    private static bool IsUseful(string value) =>
        value.Length >= 8 &&
        !value.Contains("degraded", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("unavailable", StringComparison.OrdinalIgnoreCase);

    private static void Append(StringBuilder sb, string name, IEnumerable<string> values)
    {
        var clean = values.Select(x => Clean(x, 220)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        if (clean.Count == 0)
        {
            return;
        }

        sb.AppendLine($"{name}:");
        foreach (var item in clean)
        {
            sb.AppendLine($"- {item}");
        }
    }

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(value.Trim(), @"\s+", " ");
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private static string NormalizeForKey(string value) =>
        Regex.Replace(NormalizeForSearch(value), @"[^a-z0-9]+", "-").Trim('-');

    private static string NormalizeForSearch(string value) =>
        value.ToLowerInvariant()
            .Replace('ç', 'c')
            .Replace('ğ', 'g')
            .Replace('ı', 'i')
            .Replace('ö', 'o')
            .Replace('ş', 's')
            .Replace('ü', 'u');

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
}

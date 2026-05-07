using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        if (IsJavaAlgorithmTopic(topicTitle) && !LooksLikeJavaAlgorithmQuiz(cleaned))
        {
            failures.Add("Java algorithms diagnostic must stay on Java + algorithms/data-structures concepts.");
        }

        if ((learningBlueprint?.Domain.Equals("history", StringComparison.OrdinalIgnoreCase) == true ||
             IsHistoryTopic(topicTitle)) &&
            LooksLikeProgrammingDiagnostic(cleaned))
        {
            failures.Add("History diagnostic leaked programming/debugging/API/performance scaffolding.");
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

    public static string BuildFallbackDiagnosticBlueprint(string topicTitle, LearningBlueprintDto? learningBlueprint = null)
    {
        if (learningBlueprint?.Domain.Equals("history", StringComparison.OrdinalIgnoreCase) == true ||
            IsHistoryTopic(topicTitle))
        {
            return BuildHistoryFallbackDiagnostic(topicTitle, learningBlueprint);
        }

        if (IsJavaAlgorithmTopic(topicTitle))
        {
            return BuildJavaAlgorithmsFallbackDiagnostic(topicTitle);
        }

        var profile = DetectFallbackProfile(topicTitle);
        var templates = new[]
        {
            ("code_reading", "kolay", "code-flow-debug", "Reading", "Kod parcasinda veri akisini ve karar noktasini tespit eder."),
            ("procedural", "kolay", "basic-flow", "Procedural", "Islem siralamasini kurar."),
            ("application", "orta", "real-world-use", "Application", "Kavrami senaryoda uygular."),
            ("analysis", "orta", "code-reading", "Reading", "Kod akisini analiz eder."),
            ("misconception_probe", "orta", "common-misconception", "Conceptual", "Yaygin yanilgiyi tanir."),
            ("conceptual", "kolay", "prerequisite", "Conceptual", "On kosulu kavrar."),
            ("procedural", "orta", "workflow", "Procedural", "Dogru uygulama adimlarini secer."),
            ("application", "orta", "debugging", "Application", "Hata ayiklama yaklasimi secer."),
            ("analysis", "zor", "edge-case", "Reading", "Uc durumlari yorumlar."),
            ("misconception_probe", "orta", "concept-vs-implementation", "Application", "Kavram ile uygulama adimini ayirt eder."),
            ("conceptual", "orta", "mental-model", "Conceptual", "Calisma modelini aciklar."),
            ("procedural", "orta", "error-handling", "Procedural", "Hata yonetimi adimini secer."),
            ("application", "zor", "constraint-handling", "Application", "Kisit ve sinir durum senaryosunu cozer."),
            ("analysis", "zor", "performance", "Reading", "Performans etkisini analiz eder."),
            ("misconception_probe", "zor", "execution-confusion", "Conceptual", "Isleyis ve kavram karisikligini yakalar."),
            ("conceptual", "orta", "api-shape", "MisreadQuestion", "API seklini dogru okur."),
            ("procedural", "zor", "composition", "Procedural", "Bilesik akisi tasarlar."),
            ("application", "zor", "production-scenario", "Application", "Uretim senaryosunda karar verir."),
            ("analysis", "zor", "debug-trace", "Reading", "Iz ve hata ciktisini yorumlar."),
            ("misconception_probe", "zor", "advanced-misconception", "Careless", "Ince yanilgilari ayirt eder.")
        };

        var questions = templates.Select((t, i) =>
        {
            var codeSnippet = profile.IsTechnical && i is 0 or 3 or 7 or 18
                ? $"\n\nKod:\n```{profile.CodeFenceLanguage}\n{profile.CodeSnippet}\n```"
                : string.Empty;

            var options = BuildNeutralDiagnosticOptions(i, profile);
            var correctOption = options.First(option => option.IsCorrect).Text;

            return new DiagnosticQuestionBlueprint
            {
                Type = "multiple_choice",
                Question = BuildFallbackQuestionText(topicTitle, i, profile, codeSnippet),
                Options = options,
                CorrectAnswer = correctOption,
                Explanation = BuildFallbackExplanation(profile, topicTitle, i),
                SkillTag = $"{profile.SkillPrefix}-{t.Item3}",
                Difficulty = t.Item2,
                ConceptTag = $"{t.Item3}-{i + 1}",
                LearningObjective = BuildSafeLearningObjective(profile, i),
                QuestionType = t.Item1,
                ExpectedMisconceptionCategory = t.Item4,
                Topic = topicTitle
            };
        }).ToList();

        return JsonSerializer.Serialize(questions, JsonOptions)
            .Replace("\\u0060", "`", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildJavaAlgorithmsFallbackDiagnostic(string topicTitle)
    {
        var questions = new List<DiagnosticQuestionBlueprint>
        {
            Q(topicTitle, 1, "kolay", "array-sorting", "analysis", "Reading",
                "Asagidaki Java kodu calistiginda ekrana hangi deger yazilir?\n\nKod:\n```java\nint[] numbers = {4, 1, 3};\nArrays.sort(numbers);\nSystem.out.println(numbers[0]);\n```",
                ["1", "3", "4", "Kod derlenmez; int[] siralanamaz."],
                "1",
                "Arrays.sort int dizisini artan siraya dizer; ilk eleman 1 olur.",
                "Array siralama sonrasi indeks okuma"),
            Q(topicTitle, 2, "kolay", "binary-search-precondition", "misconception_probe", "Conceptual",
                "Binary search kullanmadan once Java'da dizi/list icin hangi kosul en kritiktir?",
                ["Aranacak veri sirali olmalidir.", "Dizide mutlaka tek sayida eleman olmalidir.", "Elemanlar String olmak zorundadir.", "Dizi once ters cevrilmelidir."],
                "Aranacak veri sirali olmalidir.",
                "Binary search sirali veri uzerinde anlamli sonuc verir; siralanmamis veride sonuc guvenilir degildir.",
                "Binary search on kosulu"),
            Q(topicTitle, 3, "kolay", "linear-search", "procedural", "Procedural",
                "Bir int dizisinde belirli bir degeri bulmak icin bastan sona tek tek bakiyorsan hangi yaklasimi kullaniyorsun?",
                ["Linear search", "Binary search", "Hashing", "Merge sort"],
                "Linear search",
                "Linear search elemanlari sirayla kontrol eder ve en kotu durumda n elemana bakar.",
                "Linear arama"),
            Q(topicTitle, 4, "orta", "big-o-loop", "analysis", "Reading",
                "Asagidaki dongunun n elemanli bir dizide zaman karmasikligi nedir?\n\nKod:\n```java\nfor (int i = 0; i < arr.length; i++) {\n    System.out.println(arr[i]);\n}\n```",
                ["O(n)", "O(1)", "O(n^2)", "O(log n)"],
                "O(n)",
                "Dongu her elemani bir kez ziyaret eder; islem sayisi n ile dogrusal artar.",
                "Tek dongu karmasikligi"),
            Q(topicTitle, 5, "orta", "nested-loop", "analysis", "Reading",
                "Ic ice iki dongu ayni n uzunlugundaki dizi uzerinde tum ikilileri geziyorsa tipik zaman karmasikligi nedir?",
                ["O(n^2)", "O(n)", "O(log n)", "O(1)"],
                "O(n^2)",
                "Her dis dongu adimi icin ic dongu n kez calisir; yaklasik n*n islem olur.",
                "Ic ice dongu karmasikligi"),
            Q(topicTitle, 6, "kolay", "stack-lifo", "conceptual", "Conceptual",
                "Stack veri yapisinda en son eklenen elemanin once cikmasi hangi prensiptir?",
                ["LIFO", "FIFO", "Binary search", "Stable sort"],
                "LIFO",
                "Stack Last-In-First-Out prensibiyle calisir.",
                "Stack davranisi"),
            Q(topicTitle, 7, "kolay", "queue-fifo", "conceptual", "Conceptual",
                "Queue veri yapisinda ilk eklenen elemanin once cikmasi hangi prensiptir?",
                ["FIFO", "LIFO", "Hashing", "Recursion"],
                "FIFO",
                "Queue First-In-First-Out prensibiyle calisir.",
                "Queue davranisi"),
            Q(topicTitle, 8, "orta", "hashmap-lookup", "application", "Application",
                "Kullanici id'sinden kullanici nesnesine sik erisim gerekiyorsa Java'da hangi veri yapisi ortalama durumda uygundur?",
                ["HashMap<Integer, User>", "ArrayList<User> ile her seferinde bastan arama", "Stack<User>", "PriorityQueue<User>"],
                "HashMap<Integer, User>",
                "HashMap anahtar uzerinden ortalama O(1) erisim hedefler; sirali gezinme gerekmiyorsa uygundur.",
                "Map ile anahtarli erisim"),
            Q(topicTitle, 9, "orta", "hashset-duplicates", "misconception_probe", "Conceptual",
                "Bir listedeki tekrar eden sayilari tekillestirmek icin hangi Java koleksiyonu dogal secimdir?",
                ["HashSet", "Stack", "Queue", "StringBuilder"],
                "HashSet",
                "Set ayni degeri birden fazla kez tutmaz; tekrar temizleme icin dogru soyutlamadir.",
                "Set ve tekrarlar"),
            Q(topicTitle, 10, "orta", "arraylist-insert", "analysis", "Application",
                "ArrayList'in ortasina eleman eklemek neden pahali olabilir?",
                ["Sonraki elemanlarin kaydirilmasi gerekebilir.", "ArrayList hic eleman tutamaz.", "Her ekleme binary search calistirir.", "Elemanlar otomatik siralanir."],
                "Sonraki elemanlarin kaydirilmasi gerekebilir.",
                "ArrayList indeks tabanli dizi mantigina yakindir; ortadan ekleme kaydirma maliyeti dogurabilir.",
                "ArrayList ekleme maliyeti"),
            Q(topicTitle, 11, "orta", "recursion-base-case", "misconception_probe", "Conceptual",
                "Recursive bir metotta base case eksikse en olasi risk nedir?",
                ["Cagri zinciri durmayip stack overflow'a gidebilir.", "Kod her zaman O(1) olur.", "Java recursion'a izin vermez.", "Metot otomatik olarak binary search'e donusur."],
                "Cagri zinciri durmayip stack overflow'a gidebilir.",
                "Base case recursion'in durma kosuludur; eksikse cagri kendini bitiremeyebilir.",
                "Recursion durma kosulu"),
            Q(topicTitle, 12, "orta", "bfs", "procedural", "Procedural",
                "Bir grafi katman katman gezmek icin tipik BFS uygulamasinda hangi yapi kullanilir?",
                ["Queue", "Stack", "HashMap tek basina", "Comparator"],
                "Queue",
                "BFS once yakin komsulari isler; FIFO queue bu katmanli akisi tasir.",
                "BFS veri yapisi"),
            Q(topicTitle, 13, "orta", "dfs", "procedural", "Procedural",
                "Derinlige oncelikli arama (DFS) Java'da hangi iki yaklasimla sik uygulanir?",
                ["Recursion veya Stack", "Queue veya HashSet siralamasi", "Binary search veya Comparator", "PriorityQueue veya StringBuilder"],
                "Recursion veya Stack",
                "DFS derine inmeyi hedefler; recursion call stack'i veya acik Stack kullanilabilir.",
                "DFS uygulama bicimi"),
            Q(topicTitle, 14, "zor", "priorityqueue", "application", "Application",
                "Her adimda en kucuk oncelikli elemani almak istiyorsan Java'da hangi yapi uygundur?",
                ["PriorityQueue", "HashSet", "LinkedList'i rastgele indeksle okumak", "String[]"],
                "PriorityQueue",
                "PriorityQueue dogal siralama veya Comparator ile oncelikli elemani verir.",
                "Oncelik kuyrugu"),
            Q(topicTitle, 15, "zor", "comparator", "analysis", "Reading",
                "Collections.sort(list, comparator) kullaniminda Comparator neyi belirler?",
                ["Elemanlarin hangi kurala gore siralanacagini", "Listenin kac eleman tutabilecegini", "Kodun kac thread acacagini", "Java surumunu"],
                "Elemanlarin hangi kurala gore siralanacagini",
                "Comparator karsilastirma kuralini tanimlar; siralama bu kurala gore yapilir.",
                "Comparator siralama kurali"),
            Q(topicTitle, 16, "zor", "two-pointer", "misconception_probe", "Application",
                "Two-pointer teknigi hangi durumda daha anlamli hale gelir?",
                ["Veride siralama/yon/kisit gibi iki uctan ilerlemeyi anlamli kilan bir ozellik varsa.", "Her problemde rastgele iki indeks secilirse.", "Sadece HashMap kullanilinca.", "Kodda hic dongu yoksa."],
                "Veride siralama/yon/kisit gibi iki uctan ilerlemeyi anlamli kilan bir ozellik varsa.",
                "Two-pointer bir ezber degil; veri duzeni ve problem kisiti teknigi mumkun kilar.",
                "Two-pointer on kosulu"),
            Q(topicTitle, 17, "zor", "prefix-sum", "application", "Application",
                "Ayni dizi uzerinde cok sayida aralik toplami soruluyorsa hangi fikir pratik avantaj saglar?",
                ["Prefix sum", "Her sorguda araligi bastan sona tekrar toplamak", "Diziyi String'e cevirmek", "Stack ile tum elemanlari silmek"],
                "Prefix sum",
                "Prefix sum once on hesaplama yaparak aralik toplamini hizli cevaplamayi saglar.",
                "Prefix sum"),
            Q(topicTitle, 18, "zor", "greedy-limits", "misconception_probe", "Conceptual",
                "Greedy algoritmalarla ilgili en dogru ifade hangisidir?",
                ["Yerel en iyi secim her problemde dogru sonucu garanti etmez; problem ozelligi kontrol edilmelidir.", "Greedy her zaman dynamic programming'den dogrudur.", "Greedy sadece Java'da vardir.", "Greedy algoritmada karar verilmez."],
                "Yerel en iyi secim her problemde dogru sonucu garanti etmez; problem ozelligi kontrol edilmelidir.",
                "Greedy yaklasim icin yerel secimin global optimuma goturdugu gerekcelenmelidir.",
                "Greedy yanilgisi"),
            Q(topicTitle, 19, "zor", "dynamic-programming", "analysis", "Conceptual",
                "Dynamic programming hangi problem ozelliklerinde guclu bir aday olur?",
                ["Tekrarlanan alt problemler ve optimal alt yapi varsa.", "Problemde hic tekrar yoksa ve tek adimsa.", "Sadece liste alfabetik siradaysa.", "Sadece cikti ekrana yazdiriliyorsa."],
                "Tekrarlanan alt problemler ve optimal alt yapi varsa.",
                "DP ayni alt problemleri tekrar cozmemek icin sonuc saklama/tablolasitirma fikrine dayanir.",
                "DP problem tanima"),
            Q(topicTitle, 20, "zor", "off-by-one", "misconception_probe", "Reading",
                "Asagidaki Java dongusundeki temel hata riski nedir?\n\nKod:\n```java\nfor (int i = 0; i <= arr.length; i++) {\n    System.out.println(arr[i]);\n}\n```",
                ["i == arr.length oldugunda dizi siniri asilir.", "Dongu hic calismaz.", "arr.length her zaman -1 doner.", "Java for dongusunu desteklemez."],
                "i == arr.length oldugunda dizi siniri asilir.",
                "Java dizilerinde son gecerli indeks length - 1'dir; <= siniri son adimda tasma uretir.",
                "Off-by-one hata")
        };

        return JsonSerializer.Serialize(questions, JsonOptions)
            .Replace("\\u0060", "`", StringComparison.OrdinalIgnoreCase);

        static DiagnosticQuestionBlueprint Q(
            string topic,
            int index,
            string difficulty,
            string concept,
            string questionType,
            string misconception,
            string question,
            string[] options,
            string correct,
            string explanation,
            string objective) =>
            new()
            {
                Type = "multiple_choice",
                Question = $"{topic}: Soru {index} - {question}",
                Options = options.Select(option => new DiagnosticOption(option, option == correct)).ToList(),
                CorrectAnswer = correct,
                Explanation = explanation,
                SkillTag = $"java-algorithms-{concept}",
                Difficulty = difficulty,
                ConceptTag = concept,
                LearningObjective = objective,
                QuestionType = questionType,
                ExpectedMisconceptionCategory = misconception,
                Topic = topic
            };
    }

    private static string BuildHistoryFallbackDiagnostic(string topicTitle, LearningBlueprintDto? blueprint)
    {
        var isSeljuk = IsSeljukTopic(topicTitle, blueprint);
        var topic = string.IsNullOrWhiteSpace(topicTitle) ? "Tarih" : topicTitle.Trim();
        var questions = isSeljuk
            ? BuildSeljukHistoryQuestions(topic)
            : BuildGenericHistoryQuestions(topic, blueprint);

        return JsonSerializer.Serialize(questions, JsonOptions)
            .Replace("\\u0060", "`", StringComparison.OrdinalIgnoreCase);
    }

    private static List<DiagnosticQuestionBlueprint> BuildSeljukHistoryQuestions(string topic)
    {
        return
        [
            HQ(topic, 1, "kolay", "seljuk-origins", "conceptual", "Conceptual",
                "Buyuk Selcuklularin Oghuz/Turkmen kokeni hangi boyla en cok iliskilendirilir?",
                ["Kinik", "Kayi", "Avsar", "Karluk"],
                "Kinik",
                "Selcuklu hanedani Oghuzlarin Kinik boyu ile iliskilendirilir.",
                "Selcuklu koken bilgisini ayirt etme"),
            HQ(topic, 2, "kolay", "khorasan-rise", "chronology", "Reading",
                "Selcuklularin devletlesme surecinde Horasan bolgesinin onemi nedir?",
                ["Gaznelilerle mucadele ve siyasi guc kazanma sahasidir.", "Sadece Anadolu'daki ilk baskenttir.", "Bizans'in merkezidir.", "Haçli seferlerinin baslangic noktasidir."],
                "Gaznelilerle mucadele ve siyasi guc kazanma sahasidir.",
                "Horasan, Selcuklularin Gazneliler karsisinda guc kazandigi ana sahalardan biridir.",
                "Bolge-siyasi yukselis iliskisi"),
            HQ(topic, 3, "kolay", "dandanakan", "chronology", "Procedural",
                "1040 Dandanakan Savasi'nin Selcuklu tarihi acisindan temel sonucu hangisidir?",
                ["Gaznelilere karsi ustunluk ve Buyuk Selcuklu devletlesmesinin hizlanmasi", "Anadolu Selcuklu Devleti'nin yikilmasi", "Osmanli Devleti'nin kurulmasi", "Abbasi Devleti'nin sona ermesi"],
                "Gaznelilere karsi ustunluk ve Buyuk Selcuklu devletlesmesinin hizlanmasi",
                "Dandanakan, Selcuklularin Gaznelilere karsi siyasi ustunlugunu pekistiren donum noktalarindandir.",
                "Olay-sonuc baglantisi"),
            HQ(topic, 4, "orta", "tughril-baghdad", "application", "Application",
                "Tughril Bey'in Bagdat'a girmesi Selcuklu siyasetinde en cok hangi anlamla baglantilidir?",
                ["Abbasi halifesiyle mesruiyet iliskisi kurmak", "Bizans'in baskentini almak", "Deniz ticaretini baslatmak", "Mogollari durdurmak"],
                "Abbasi halifesiyle mesruiyet iliskisi kurmak",
                "Bagdat ve Abbasi halifesiyle iliski, Selcuklu siyasi mesruiyetini guclendirdi.",
                "Mesruiyet kavrami"),
            HQ(topic, 5, "orta", "alp-arslan-manzikert", "analysis", "Reading",
                "1071 Malazgirt Savasi'nin tarihsel onemi en dogru nasil aciklanir?",
                ["Anadolu'ya Turk yerlesimi ve siyasi gecis surecini hizlandiran donum noktasi", "Selcuklularin ilk kez Horasan'a girmesi", "Nizamiye medreselerinin kurulmasi", "Gaznelilerin kurulmasi"],
                "Anadolu'ya Turk yerlesimi ve siyasi gecis surecini hizlandiran donum noktasi",
                "Malazgirt, Anadolu'nun Turklesme surecinde askeri ve siyasi acilim yaratan kritik olaydir.",
                "Malazgirt neden-sonuc okuma"),
            HQ(topic, 6, "orta", "malikshah-high-period", "conceptual", "Conceptual",
                "Meliksah donemi genellikle hangi ozelliklerle one cikar?",
                ["Siyasi genisleme, idari duzen ve Nizam al-Mulk etkisi", "Selcuklularin tamamen dagilmasi", "Osmanliyla ittifak", "Anadolu Selcuklu Devleti'nin yikilmasi"],
                "Siyasi genisleme, idari duzen ve Nizam al-Mulk etkisi",
                "Meliksah donemi Buyuk Selcuklularin guclu donemlerinden biridir; Nizam al-Mulk idari duzende etkilidir.",
                "Yuksek donem ozelligi"),
            HQ(topic, 7, "orta", "nizam-al-mulk", "application", "Application",
                "Nizam al-Mulk hangi baslikla en dogru iliskilendirilir?",
                ["Vezirlik, idari duzen ve Nizamiye medreseleri", "Haçli donanmasi", "Mogol istilasi liderligi", "Bizans imparatorlugu"],
                "Vezirlik, idari duzen ve Nizamiye medreseleri",
                "Nizam al-Mulk, Selcuklu idaresi ve Nizamiye medreseleriyle anilir.",
                "Kisi-kurum eslestirme"),
            HQ(topic, 8, "orta", "iqta", "conceptual", "Conceptual",
                "Iqta sistemi Selcuklu idaresinde hangi alanla ilgilidir?",
                ["Toprak gelirleri, asker besleme ve idari duzen", "Denizcilik teknolojisi", "Matbaa uretimi", "Roma hukuku"],
                "Toprak gelirleri, asker besleme ve idari duzen",
                "Iqta, gelir ve asker/idare organizasyonuyla baglantili bir kurumdur.",
                "Kurum-islev baglantisi"),
            HQ(topic, 9, "orta", "nizamiya-madrasa", "analysis", "Application",
                "Nizamiye medreseleri hangi amaca hizmet eden kurumlar olarak okunmalidir?",
                ["Egitim, Sunni dusunce ve idari kadro yetistirme", "Sadece askeri kale sistemi", "Bizans saray okulu", "Deniz ticareti birligi"],
                "Egitim, Sunni dusunce ve idari kadro yetistirme",
                "Nizamiye medreseleri egitim ve siyasi-dini kurumlasma acisindan onemlidir.",
                "Kurum-amac analizi"),
            HQ(topic, 10, "zor", "chronology-order", "chronology", "Procedural",
                "Asagidaki siralamalardan hangisi Selcuklu tarihi icin daha dogrudur?",
                ["Dandanakan -> Malazgirt -> Meliksah yuksek donemi -> Katvan", "Malazgirt -> Dandanakan -> Katvan -> Tughril Bey", "Katvan -> Dandanakan -> Malazgirt -> Meliksah", "Osmanli kurulusu -> Dandanakan -> Malazgirt -> Katvan"],
                "Dandanakan -> Malazgirt -> Meliksah yuksek donemi -> Katvan",
                "Dandanakan 1040, Malazgirt 1071, Meliksah yuksek donemi 11. yuzyil sonu, Katvan 1141 olarak siralanir.",
                "Kronoloji kurma"),
            HQ(topic, 11, "zor", "sanjar-qatwan", "analysis", "Reading",
                "Katvan Savasi ve Sultan Sencer donemi hangi surecle iliskilidir?",
                ["Buyuk Selcuklu gucunun zayiflamasi ve parcalanma sureci", "Devletin ilk kurulus ani", "Malazgirt'in hemen oncesi", "Osmanli'nin Avrupa'ya gecisi"],
                "Buyuk Selcuklu gucunun zayiflamasi ve parcalanma sureci",
                "Sencer donemi ve Katvan, Buyuk Selcuklu guc kaybi ve parcalanmayla iliskili okunur.",
                "Gerileme sureci"),
            HQ(topic, 12, "orta", "anatolian-link", "application", "Application",
                "Buyuk Selcuklu tarihi ile Anadolu Selcuklu tarihi arasindaki bag nasil kurulmalidir?",
                ["Malazgirt ve Anadolu'ya yonelen siyasi-goc hareketleri uzerinden", "Ikisi tamamen ayni devlet oldugu icin ayirim yapmadan", "Sadece Osmanli padisahlari uzerinden", "Roma Cumhuriyeti kurumlari uzerinden"],
                "Malazgirt ve Anadolu'ya yonelen siyasi-goc hareketleri uzerinden",
                "Buyuk Selcuklu ile Anadolu Selcuklu birbirine bagli ama ayni siyasi evre degildir.",
                "Donem ayrimi"),
            HQ(topic, 13, "zor", "common-confusion-great-anatolian", "misconception_probe", "Conceptual",
                "En yaygin kavram karisikligi hangisidir?",
                ["Buyuk Selcuklu ile Anadolu Selcuklu evrelerini ayni siyasi yapi gibi anlatmak", "Dandanakan'in Gaznelilerle ilgili oldugunu soylemek", "Nizam al-Mulk'u vezirlikle iliskilendirmek", "Malazgirt'i Bizans-Selcuklu savasi olarak gormek"],
                "Buyuk Selcuklu ile Anadolu Selcuklu evrelerini ayni siyasi yapi gibi anlatmak",
                "Bu iki evre iliskili olsa da ayni siyasi yapi gibi anlatmak ogrenme hatasi uretir.",
                "Buyuk/Anadolu Selcuklu ayrimi"),
            HQ(topic, 14, "orta", "cause-effect-manzikert", "misconception_probe", "Application",
                "Malazgirt'i yalnizca 'bir savas kazanildi' diye ezberlemek neden eksiktir?",
                ["Cunku savasin Anadolu'daki siyasi, demografik ve askeri sonuclarini gormezden gelir.", "Cunku Malazgirt hic olmamistir.", "Cunku konu sadece medrese tarihidir.", "Cunku Selcuklular Bizans'la hic karsilasmamistir."],
                "Cunku savasin Anadolu'daki siyasi, demografik ve askeri sonuclarini gormezden gelir.",
                "Tarih sorulari olay kadar sonuclari ve uzun vadeli etkileri de olcer.",
                "Tek olay ezberi yanilgisi"),
            HQ(topic, 15, "zor", "actor-event-confusion", "misconception_probe", "Reading",
                "Alp Arslan, Nizam al-Mulk ve Meliksah'i karistiran bir ogrenci icin en iyi calisma adimi hangisidir?",
                ["Kisi-rol-olay tablosu kurup her birini donemle eslestirmek", "Uc ismi tek kisilik bir lider gibi ezberlemek", "Malazgirt'i tamamen atlamak", "Kronoloji yerine rastgele kaynak okumak"],
                "Kisi-rol-olay tablosu kurup her birini donemle eslestirmek",
                "Kisi, rol ve olay eslestirmesi kronolojik hatalari azaltir.",
                "Kisi-rol ayirma"),
            HQ(topic, 16, "zor", "institution-politics", "analysis", "Conceptual",
                "Iqta ve Nizamiye gibi kurumlari plan icinde neden siyasi olaylarla birlikte calismak gerekir?",
                ["Cunku devletin gucu sadece savasla degil kurumlasmayla da aciklanir.", "Cunku kurumlar tarih disi konulardir.", "Cunku siyasi olaylar kurumlardan tamamen bagimsizdir.", "Cunku medreseler sadece modern donemde vardir."],
                "Cunku devletin gucu sadece savasla degil kurumlasmayla da aciklanir.",
                "Selcuklu gucu askeri, idari ve kulturel kurumlarin birlikte okunmasiyla anlasilir.",
                "Kurum-siyaset baglantisi"),
            HQ(topic, 17, "orta", "geography", "application", "Application",
                "Selcuklu haritasini okurken Khorasan-Iran-Iraq-Anatolia hattini takip etmek hangi beceriyi guclendirir?",
                ["Siyasi genisleme ve olaylarin mekan baglamini kurma", "Sadece isim ezberleme", "Kronolojiyi tamamen gereksiz sayma", "Kultur konularini silme"],
                "Siyasi genisleme ve olaylarin mekan baglamini kurma",
                "Harita baglami olaylarin neden ve sonuclarini daha anlamli kilar.",
                "Cografya-tarih baglantisi"),
            HQ(topic, 18, "zor", "culture-legacy", "misconception_probe", "Conceptual",
                "Selcuklu kultur ve sanat konularini planin disina atmak neden zayif bir yaklasimdir?",
                ["Cunku medrese, mimari ve burokrasi Selcuklu mirasini anlamanin parcasidir.", "Cunku tarih sadece savas listesidir.", "Cunku kultur kaynaklari hic yoktur.", "Cunku idari kurumlar onemsizdir."],
                "Cunku medrese, mimari ve burokrasi Selcuklu mirasini anlamanin parcasidir.",
                "Kultur ve kurumlar siyasi tarihin tamamlayici boyutudur.",
                "Kultur mirasi yanilgisi"),
            HQ(topic, 19, "zor", "source-reading", "analysis", "Reading",
                "Farkli kaynaklarda Selcuklu basliklari degisik sirada gelirse en saglam kontrol hangisidir?",
                ["Tarihleri, aktorleri ve olay-sonuc iliskilerini karsilastirmak", "En uzun metni otomatik dogru kabul etmek", "Ilk kaynagi ezberleyip digerlerini atmak", "YouTube basligini tek kanit saymak"],
                "Tarihleri, aktorleri ve olay-sonuc iliskilerini karsilastirmak",
                "Kaynaklar rota sinyali verir; dogrulama icin kronoloji ve neden-sonuc kontrolu gerekir.",
                "Kaynak karsilastirma"),
            HQ(topic, 20, "zor", "mixed-synthesis", "misconception_probe", "Application",
                "Selcuklu konusunu gercekten ogrenmek icin final tekrar hangi uc ayagi birlikte tasimalidir?",
                ["Kronoloji, aktor-olay eslestirme ve neden-sonuc yazimi", "Sadece savas isimleri", "Sadece tek video basligi", "Sadece rastgele tarih ezberi"],
                "Kronoloji, aktor-olay eslestirme ve neden-sonuc yazimi",
                "Seviye tespiti tarih bilgisini ezber degil, siralama ve iliski kurma olarak olcmelidir.",
                "Karma tarih sentezi")
        ];
    }

    private static List<DiagnosticQuestionBlueprint> BuildGenericHistoryQuestions(string topic, LearningBlueprintDto? blueprint)
    {
        var axes = (blueprint?.AssessmentAxes.Count > 0 ? blueprint.AssessmentAxes : ["chronology", "actor-event matching", "cause-effect", "geography", "institutions", "common-confusions"])
            .Take(8)
            .ToList();
        var events = (blueprint?.Events.Count > 0 ? blueprint.Events : ["ana olay", "donum noktasi", "kurumlasma"])
            .Take(6)
            .ToList();
        var actors = (blueprint?.Actors.Count > 0 ? blueprint.Actors : ["ana aktor", "siyasi guc", "kurum"])
            .Take(6)
            .ToList();

        var questions = new List<DiagnosticQuestionBlueprint>();
        for (var i = 0; i < 20; i++)
        {
            var axis = axes[i % axes.Count];
            var ev = events[i % events.Count];
            var actor = actors[i % actors.Count];
            var questionType = (i % 5) switch
            {
                0 => "conceptual",
                1 => "procedural",
                2 => "application",
                3 => "analysis",
                _ => "misconception_probe"
            };
            var misconception = questionType == "misconception_probe" ? "Conceptual" : i % 2 == 0 ? "Reading" : "Application";

            questions.Add(HQ(topic, i + 1, i < 6 ? "kolay" : i < 14 ? "orta" : "zor", $"history-{axis}-{i + 1}", questionType, misconception,
                $"{topic} icin {axis} ekseninde {ev} ve {actor} bilgisini calisirken en saglam seviye tespit adimi hangisidir?",
                [
                    "Olayi zaman, aktor ve neden-sonuc baglamiyla eslestirmek",
                    "Sadece en tanidik ismi ezberlemek",
                    "Kaynaktaki ilk cumleyi tek dogru saymak",
                    "Benzer donemleri ayni olay gibi kabul etmek"
                ],
                "Olayi zaman, aktor ve neden-sonuc baglamiyla eslestirmek",
                "Tarih seviye tespiti olay, aktor, zaman ve neden-sonuc iliskisini birlikte olcer.",
                $"{axis} uzerinden tarihsel iliski kurma"));
        }

        return questions;
    }

    private static DiagnosticQuestionBlueprint HQ(
        string topic,
        int index,
        string difficulty,
        string concept,
        string questionType,
        string misconception,
        string question,
        string[] options,
        string correct,
        string explanation,
        string objective) =>
        new()
        {
            Type = "multiple_choice",
            Question = $"{topic}: Soru {index} - {question}",
            Options = options.Select(option => new DiagnosticOption(option, option == correct)).ToList(),
            CorrectAnswer = correct,
            Explanation = explanation,
            SkillTag = $"history-{concept}",
            Difficulty = difficulty,
            ConceptTag = concept,
            LearningObjective = objective,
            QuestionType = questionType,
            ExpectedMisconceptionCategory = misconception,
            Topic = topic
        };

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
                "C# kod akisini okuyup derleme/runtime riskini ayirt etmek.",
                "var user = users.First(u => u.Id == selectedId);\nConsole.WriteLine(user.Name.ToUpper());");
        }

        if (Regex.IsMatch(normalized, @"\bpython|py\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                true,
                "python",
                "python",
                "Python",
                "Python veri akisini ve hata kaynagini okumak.",
                "items = [1, 2, 3]\nprint(items[3])");
        }

        if (Regex.IsMatch(normalized, @"\bjava\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                true,
                "java",
                "java",
                "Java",
                "Java kod akisini, algoritma adimlarini ve veri yapisi kararini ayirt etmek.",
                "int[] numbers = {4, 1, 3};\nArrays.sort(numbers);\nSystem.out.println(numbers[0]);");
        }

        if (Regex.IsMatch(normalized, @"\b(javascript|typescript|react|node|js|ts)\b", RegexOptions.IgnoreCase))
        {
            return new DiagnosticFallbackProfile(
                true,
                "javascript",
                "javascript",
                "JavaScript",
                "JavaScript async/veri akisini ve state etkisini ayirt etmek.",
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
                "Kavrami kucuk bir kod veya pratik adimla test etmek.",
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

    private static string BuildFallbackQuestionText(
        string topicTitle,
        int index,
        DiagnosticFallbackProfile profile,
        string codeSnippet)
    {
        if (profile.IsTechnical && !string.IsNullOrWhiteSpace(codeSnippet))
        {
            if (profile.SkillPrefix == "sql")
            {
                var sqlPrompt = (index % 4) switch
                {
                    0 => "Bu sorguda performans riskini anlamak icin once hangi nokta incelenmelidir?",
                    1 => "Bu SQL orneginde index kararini etkileyen temel ipucu hangisidir?",
                    2 => "Bu sorgunun sonucunu veya maliyetini anlamak icin hangi akil yurutme gerekir?",
                    _ => "Bu SQL akisini optimize etmeden once hangi kanit gerekir?"
                };
                return $"{topicTitle}: Soru {index + 1} - {sqlPrompt}{codeSnippet}";
            }

            var prompt = (index % 4) switch
            {
                0 => "Bu kodu dogru okumak icin hangi yaklasim en saglamdir?",
                1 => "Bu ornekte hata veya sonucu anlamak icin once ne kontrol edilmelidir?",
                2 => "Bu kod parcasi hangi kavrami uygulamayi gerektirir?",
                _ => "Bu ornekte beklenen sonucu bulmak icin hangi akil yurutme kullanilmalidir?"
            };
            return $"{topicTitle}: Soru {index + 1} - {prompt}{codeSnippet}";
        }

        if (profile.IsTechnical)
        {
            if (profile.SkillPrefix == "sql")
            {
                var sqlPrompt = (index % 4) switch
                {
                    0 => "Yavas bir sorguyu analiz ederken hangi bilgi once ayrilmalidir?",
                    1 => "Index eklemeden once hangi secim daha guvenilir olur?",
                    2 => "Benzer gorunen iki sorgu planinda karar verirken hangi adim gerekir?",
                    _ => "Sorgu sonucunu bozmadan optimizasyon yapmak icin hangi okuma sirasi daha dogrudur?"
                };
                return $"{topicTitle}: Soru {index + 1} - {sqlPrompt}";
            }

            var prompt = (index % 4) switch
            {
                0 => "Bu konuda verilen senaryoyu cozmek icin hangi bilgi once ayrilmalidir?",
                1 => "Kavrami uygularken hangi secim daha guvenilir olur?",
                2 => "Benzer gorunen iki kavram arasinda karar verirken hangi adim gerekir?",
                _ => "Hata nedenini anlamak icin hangi okuma sirasi daha dogrudur?"
            };
            return $"{topicTitle}: Soru {index + 1} - {prompt}";
        }

        var generalPrompt = profile.SkillPrefix switch
        {
            "exam" when topicTitle.Contains("paragraf", StringComparison.OrdinalIgnoreCase) => (index % 4) switch
            {
                0 => "Bu paragraf sorusunda hiz kaybetmeden once hangi bilgi ayrilmalidir?",
                1 => "Ana fikir ile yardimci dusunceyi karistirmamak icin hangi adim gerekir?",
                2 => "Celdirici secenegi elemek icin hangi okuma stratejisi daha saglamdir?",
                _ => "Sure baskisinda cevabi isaretlemeden once hangi kontrol yapilmalidir?"
            },
            "exam" => (index % 3) switch
            {
                0 => "Bu soru tipinde dogru cevaba ulasmak icin hangi adim en guvenilirdir?",
                1 => "Distractor tuzagina dusmemek icin once ne ayrilmalidir?",
                _ => "Verilen kosulu yoruma cevirirken hangi yaklasim daha saglamdir?"
            },
            "math" => (index % 3) switch
            {
                0 => "Bu tur bir problemde cozumden once hangi bilgi netlestirilmelidir?",
                1 => "Formulu uygulamadan once hangi kosul kontrol edilmelidir?",
                _ => "Sonucun mantikli olup olmadigini denetlemek icin hangi adim gerekir?"
            },
            _ => (index % 3) switch
            {
                0 => "Bu kavrami senaryoda uygulamak icin hangi adim en dogrudur?",
                1 => "Ezber cevap yerine anlamli karar vermek icin ne yapilmalidir?",
                _ => "Bu durumda kavram yanilgisini ayirmak icin hangi ipucu onemlidir?"
            }
        };

        return $"{topicTitle}: Soru {index + 1} - {generalPrompt}";
    }

    private static string BuildFallbackExplanation(DiagnosticFallbackProfile profile, string topicTitle, int index)
    {
        if (profile.IsTechnical)
        {
            return (index % 4) switch
            {
                0 => $"{profile.DisplayName} icin kodu satir satir okuyup veri, sonuc ve kavram iliskisini kurmak gerekir.",
                1 => $"{profile.DisplayName} sorularinda benzer terimleri ayirmak icin once verilen kisit okunmalidir.",
                2 => $"{profile.DisplayName} pratiginde hata belirtisi ile kok neden farkli olabilir.",
                _ => $"{profile.DisplayName} konusunda dogru cevap, ezberden degil senaryodaki kosullardan cikmalidir."
            };
        }

        return $"{topicTitle} icin dogru cevap, basliktan tahmin etmek yerine soru kosulunu ve kavram iliskisini okumayi gerektirir.";
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

    private static List<DiagnosticOption> BuildNeutralDiagnosticOptions(int index, DiagnosticFallbackProfile profile)
    {
        if (profile.SkillPrefix == "sql")
        {
            var sqlSets = new[]
            {
                new[]
                {
                    new DiagnosticOption("Filtre, join ve index adaylarini execution plan kanitiyla birlikte incelemek.", true),
                    new DiagnosticOption("Her yavas sorguya ayni index'i eklemek.", false),
                    new DiagnosticOption("WHERE kosulunu kaldirip sonucu hizli sanmak.", false),
                    new DiagnosticOption("Sadece SELECT listesini kisaltarak her zaman cozum beklemek.", false)
                },
                new[]
                {
                    new DiagnosticOption("Yuksek secicilikli filtre ve join kolonlarini veri dagilimiyle birlikte degerlendirmek.", true),
                    new DiagnosticOption("Tablodaki tum kolonlara index eklemek.", false),
                    new DiagnosticOption("Index'in yazma maliyetini hic hesaba katmamak.", false),
                    new DiagnosticOption("Query plan okumadan tahmine gore karar vermek.", false)
                },
                new[]
                {
                    new DiagnosticOption("Sorgunun sonucunu koruyup maliyeti azaltan dar degisikligi test etmek.", true),
                    new DiagnosticOption("JOIN kosulunu silerek sorguyu hizlandirmaya calismak.", false),
                    new DiagnosticOption("NULL ve duplicate etkisini yok saymak.", false),
                    new DiagnosticOption("Transaction ve constraint etkisini her durumda gereksiz saymak.", false)
                },
                new[]
                {
                    new DiagnosticOption("Once darbogazi olcen kaniti bulup sonra index/sorgu seklini denemek.", true),
                    new DiagnosticOption("Sorgu yavas diye veriyi rastgele denormalize etmek.", false),
                    new DiagnosticOption("EXPLAIN/actual rows farkini okumamak.", false),
                    new DiagnosticOption("Cache hizini kalici optimizasyon sanmak.", false)
                }
            };

            return sqlSets[index % 4].ToList();
        }

        if (profile.SkillPrefix == "exam")
        {
            var examSets = new[]
            {
                new[]
                {
                    new DiagnosticOption("Once soru kokunu ve istenen dusunce turunu ayirmak.", true),
                    new DiagnosticOption("Paragrafi okumadan en tanidik secenegi isaretlemek.", false),
                    new DiagnosticOption("Kendi yorumunu metnin yerine koymak.", false),
                    new DiagnosticOption("Celdiricideki tek dogru kelimeyi yeterli saymak.", false)
                },
                new[]
                {
                    new DiagnosticOption("Ana fikir, yardimci fikir ve yazar tutumunu ayri isaretlemek.", true),
                    new DiagnosticOption("Ilk cumleyi her zaman cevap kabul etmek.", false),
                    new DiagnosticOption("Paragraftaki ornegi ana fikirle karistirmak.", false),
                    new DiagnosticOption("Olumsuz soru kokunu olumlu gibi okumak.", false)
                },
                new[]
                {
                    new DiagnosticOption("Secenekleri metindeki kanitla tek tek eslestirmek.", true),
                    new DiagnosticOption("Uzun secenegi otomatik dogru saymak.", false),
                    new DiagnosticOption("Metinde olmayan genellemeyi kabul etmek.", false),
                    new DiagnosticOption("Benzer anlamli iki secenegi kanitsiz ayirmamak.", false)
                },
                new[]
                {
                    new DiagnosticOption("Sureyi korumak icin once soru kokunu, sonra kanit cumlesini aramak.", true),
                    new DiagnosticOption("Her paragrafi ayni hizda ayrintili okumak.", false),
                    new DiagnosticOption("Bilmedigin kelime cikinca soruyu tamamen birakmak.", false),
                    new DiagnosticOption("Cevabi sadece son cumleden tahmin etmek.", false)
                }
            };

            return examSets[index % 4].ToList();
        }

        var technicalSets = new[]
        {
            new[]
            {
                new DiagnosticOption("Veriyi, beklenen sonucu ve kavram kuralini birlikte kontrol etmek.", true),
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

    private static bool IsHistoryTopic(string topicTitle)
    {
        var normalized = NormalizeOptionText(topicTitle);
        return ContainsAny(normalized, "tarih", "history", "selcuk", "selcuklu", "ottoman", "osmanli", "roma", "medieval");
    }

    private static bool IsSeljukTopic(string topicTitle, LearningBlueprintDto? blueprint)
    {
        var text = NormalizeOptionText(string.Join(' ', new[]
        {
            topicTitle,
            blueprint?.ApprovedResearchIntent ?? string.Empty
        }.Concat(blueprint?.SubConcepts ?? [])));
        return ContainsAny(text, "seljuk", "selcuk", "selcuklu");
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

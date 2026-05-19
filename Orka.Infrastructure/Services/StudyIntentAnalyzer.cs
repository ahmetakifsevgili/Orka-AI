using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class StudyIntentAnalyzer : IStudyIntentAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IAIAgentFactory _factory;
    private readonly ILogger<StudyIntentAnalyzer> _logger;

    public StudyIntentAnalyzer(IAIAgentFactory factory, ILogger<StudyIntentAnalyzer> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<StudyIntentPreviewResponse> AnalyzeAsync(
        Guid userId,
        AnalyzeStudyIntentRequest request,
        CancellationToken ct = default)
    {
        var raw = Clean(request.Correction) ?? Clean(request.RawRequest);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("Study request is required before plan research.");
        }

        var deterministic = BuildFallback(raw);
        if (ShouldPreferDeterministicPreview(raw, deterministic))
        {
            return deterministic;
        }

        try
        {
            var systemPrompt = """
                You are Orka StudyIntentAnalyzer.
                Your only job is to understand a learner's raw study request before any research starts.

                Return ONLY a JSON object with:
                {
                  "mainTopic": "domain or course area",
                  "focusArea": "specific focus inside that domain",
                  "studyGoal": "learner goal in plain Turkish or English",
                  "researchIntent": "short English search/research intent suitable for a learning preparation research engine",
                  "confirmationText": "one short Turkish confirmation sentence",
                  "language": "tr or en",
                  "clarifyingNotes": ["short notes, max 3"]
                }

                Rules:
                - Do not create a quiz.
                - Do not create a study plan.
                - Do not do web research.
                - Do not invent learner weaknesses.
                - Split broad requests into a researchable intent.
                - For programming requests, keep the language and the exact focus together.
                - For exam requests, preserve the exam acronym and the concrete subject/focus.
                - The researchIntent must be in English and must not be the raw user sentence.
                """;

            var userMessage = JsonSerializer.Serialize(new
            {
                rawRequest = raw,
                request.TopicId,
                request.ExistingTopicTitle,
                instruction = "Analyze intent only. User will approve before Korteks research."
            }, JsonOptions);

            var response = await _factory.CompleteChatAsync(AgentRole.Analyzer, systemPrompt, userMessage, ct);
            var parsed = TryParseModelResponse(response, raw);
            if (IsUsable(parsed))
            {
                return Normalize(parsed!, raw);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[StudyIntentAnalyzer] AI intent analysis failed; using deterministic fallback. UserRef={UserRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }

        return BuildFallback(raw);
    }

    private static bool ShouldPreferDeterministicPreview(string rawRequest, StudyIntentPreviewResponse deterministic)
    {
        var raw = NormalizeForMatch(rawRequest);
        if (DetectProgrammingLanguage(raw) is not null)
        {
            return true;
        }

        if (ContainsAny(raw, "kpss", "yks", "tyt", "ayt", "ielts", "toefl"))
        {
            return true;
        }

        if (ContainsAny(raw, "matematik", "olasilik", "kombinasyon", "permutasyon", "problem cozme", "problemler"))
        {
            return true;
        }

        return false;
    }

    private static StudyIntentPreviewResponse? TryParseModelResponse(string rawResponse, string rawRequest)
    {
        var json = ExtractJsonObject(rawResponse);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var notes = new List<string>();
            if (root.TryGetProperty("clarifyingNotes", out var notesElement) &&
                notesElement.ValueKind == JsonValueKind.Array)
            {
                notes.AddRange(notesElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Take(3));
            }

            return new StudyIntentPreviewResponse
            {
                RawRequest = rawRequest,
                MainTopic = GetString(root, "mainTopic"),
                FocusArea = GetString(root, "focusArea"),
                StudyGoal = GetString(root, "studyGoal"),
                ResearchIntent = GetString(root, "researchIntent"),
                ConfirmationText = GetString(root, "confirmationText"),
                Language = GetString(root, "language"),
                ClarifyingNotes = notes,
                RequiresUserConfirmation = true
            };
        }
        catch
        {
            return null;
        }
    }

    private static StudyIntentPreviewResponse Normalize(StudyIntentPreviewResponse value, string rawRequest)
    {
        var fallback = BuildFallback(rawRequest);
        var mainTopic = Clean(value.MainTopic) ?? fallback.MainTopic;
        var focusArea = Clean(value.FocusArea) ?? fallback.FocusArea;
        var studyGoal = Clean(value.StudyGoal) ?? fallback.StudyGoal;
        var researchIntent = Clean(value.ResearchIntent) ?? fallback.ResearchIntent;
        mainTopic = PreferSpecificMainTopic(rawRequest, mainTopic, fallback.MainTopic);
        focusArea = PreferSpecificFocusArea(rawRequest, focusArea, fallback.FocusArea);
        researchIntent = PreferSpecificResearchIntent(rawRequest, researchIntent, fallback.ResearchIntent, mainTopic, focusArea);

        return new StudyIntentPreviewResponse
        {
            IntentRequestId = Guid.NewGuid(),
            RawRequest = rawRequest,
            MainTopic = Limit(mainTopic, 90),
            FocusArea = Limit(focusArea, 90),
            StudyGoal = Limit(studyGoal, 120),
            ResearchIntent = Limit(EnsureEnglishResearchIntent(researchIntent, mainTopic, focusArea), 160),
            ConfirmationText = string.IsNullOrWhiteSpace(value.ConfirmationText)
                ? BuildConfirmation(mainTopic, focusArea)
                : Limit(value.ConfirmationText.Trim(), 180),
            Language = value.Language.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "tr",
            ClarifyingNotes = value.ClarifyingNotes.Where(s => !string.IsNullOrWhiteSpace(s)).Take(3).ToList(),
            RequiresUserConfirmation = true
        };
    }

    private static StudyIntentPreviewResponse BuildFallback(string rawRequest)
    {
        var raw = Clean(rawRequest) ?? string.Empty;
        var normalized = NormalizeForMatch(raw);
        var language = LooksEnglish(normalized) ? "en" : "tr";

        var detectedLanguage = DetectProgrammingLanguage(normalized);
        var focus = DetectFocus(raw, normalized, detectedLanguage);
        var mainTopic = detectedLanguage is not null
            ? $"{detectedLanguage} programlama"
            : BuildMainTopic(raw, normalized, focus);

        var studyGoal = ContainsAny(normalized, "pratik", "practice", "problem", "soru", "uygulama", "exercise")
            ? "öğrenme ve pratik"
            : "öğrenme ve temel uygulama";

        var researchIntent = EnsureEnglishResearchIntent(string.Empty, mainTopic, focus);
        if (ContainsAny(normalized, "karistiriyorum", "karisiyor", "dikkat", "hata", "yanlis"))
        {
            researchIntent = $"{researchIntent} common mistakes practice";
        }

        return new StudyIntentPreviewResponse
        {
            IntentRequestId = Guid.NewGuid(),
            RawRequest = raw,
            MainTopic = Limit(mainTopic, 90),
            FocusArea = Limit(focus, 90),
            StudyGoal = studyGoal,
            ResearchIntent = Limit(researchIntent, 160),
            ConfirmationText = BuildConfirmation(mainTopic, focus),
            Language = language,
            ClarifyingNotes = ["Korteks arastirmasi baslamadan once bu niyeti onaylamalisin."],
            RequiresUserConfirmation = true
        };
    }

    private static string? DetectProgrammingLanguage(string normalized)
    {
        if (Regex.IsMatch(normalized, @"((^|\s)c#($|\s)|csharp|c sharp|c sahrp|c shar?p|\.net|dotnet\b)", RegexOptions.IgnoreCase)) return "C#";
        if (Regex.IsMatch(normalized, @"\bjava|jva|jav\b", RegexOptions.IgnoreCase)) return "Java";
        if (Regex.IsMatch(normalized, @"\bpython\b", RegexOptions.IgnoreCase)) return "Python";
        if (Regex.IsMatch(normalized, @"\bjavascript|node\.?js| js\b", RegexOptions.IgnoreCase)) return "JavaScript";
        if (Regex.IsMatch(normalized, @"\btypescript\b", RegexOptions.IgnoreCase)) return "TypeScript";
        if (Regex.IsMatch(normalized, @"\bsql|postgres|mssql|veritabani|veri tabani\b", RegexOptions.IgnoreCase)) return "SQL";
        if (Regex.IsMatch(normalized, @"\bc\+\+|cpp\b", RegexOptions.IgnoreCase)) return "C++";
        return null;
    }

    private static string DetectFocus(string raw, string normalizedRaw, string? detectedLanguage)
    {
        var cleaned = raw;
        if (!string.IsNullOrWhiteSpace(detectedLanguage))
        {
            cleaned = Regex.Replace(cleaned, Regex.Escape(detectedLanguage), "", RegexOptions.IgnoreCase);
        }

        cleaned = Regex.Replace(cleaned, @"\b(programlama(?:da|de)?|programming|calismak|çalışmak|ogrenmek|öğrenmek|istiyorum|isterim|konusunda|hakkinda|hakkında|bana|anlat|ders|study|learn|learning|want|to)\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b(kpss|yks|tyt|ayt)\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '.', ',', ':', ';', '-');
        var normalizedCleaned = NormalizeForMatch(cleaned);

        if (ContainsAny(normalizedRaw, "algoritma", "algortima", "algoritm", "algorithm") &&
            ContainsAny(normalizedRaw, "veri yap", "data structure", "collection"))
        {
            return "algoritmalar ve veri yapilari";
        }

        if (ContainsAny(normalizedRaw, "siralama", "sirala", "sorting", "sort"))
        {
            return "siralama algoritmalari";
        }

        if (ContainsAny(normalizedRaw, "algoritma", "algortima", "algoritm", "algorithm"))
        {
            return "algoritmalar";
        }

        if (ContainsAny(normalizedRaw, "collection framework", "hashmap", "arraylist", "linkedlist", " set ", " map "))
        {
            return "Java collection framework ve veri yapilari";
        }

        if (ContainsAny(normalizedRaw, "dsa", "mulakat", "interview"))
        {
            return "veri yapilari algoritmalar ve mulakat pratigi";
        }

        if (ContainsAny(normalizedRaw, "stack", "queue", "tree", "graph"))
        {
            return "stack queue tree graph veri yapilari";
        }

        if (ContainsAny(normalizedRaw, "veri yap", "data structure", "collection"))
        {
            return "veri yapilari";
        }

        if (ContainsAny(normalizedRaw, "oop", "nesne", "object oriented"))
        {
            return "nesne yonelimli programlama";
        }

        if (ContainsAny(normalizedRaw, "async", "await", "task", "asenkron"))
        {
            if (ContainsAny(normalizedRaw, "deadlock", "result"))
            {
                return "Task.Result deadlock ve await kullanimi";
            }

            if (ContainsAny(normalizedRaw, "paralel", "parallel"))
            {
                return "async await ve paralel programlama farki";
            }

            if (ContainsAny(normalizedRaw, "asenkron"))
            {
                return "asynchronous programming";
            }

            return "asenkron programlama ve hata ayiklama";
        }

        if (ContainsAny(normalizedRaw, "index", "indx", "indeks", "sorgu", "query", "optimizasyon", "optmzasyon"))
        {
            return "index ve sorgu optimizasyonu";
        }

        if (ContainsAny(normalizedRaw, "yas proble", "hiz proble", "age problem", "speed problem"))
        {
            return "yas problemleri ve hiz problemleri";
        }

        if (ContainsAny(normalizedRaw, "soruyu okuyunca", "ne yapacagimi", "okuma strateji"))
        {
            return "problem okuma stratejisi";
        }

        if (ContainsAny(normalizedRaw, "olasilik", "probability") &&
            ContainsAny(normalizedRaw, "kombinasyon", "combination"))
        {
            return "olasilik ve kombinasyon";
        }

        if (ContainsAny(normalizedRaw, "permutasyon", "kombinasyon", "combination", "permutation"))
        {
            return "permutasyon ve kombinasyon";
        }

        if (ContainsAny(normalizedRaw, "olasilik", "probability"))
        {
            return "olasilik";
        }

        if (ContainsAny(normalizedRaw, "sinav", "kpss", "yks", "tyt", "ayt"))
        {
            return string.IsNullOrWhiteSpace(normalizedCleaned) ? "sinav odakli konu calismasi" : Limit(cleaned, 90);
        }

        return string.IsNullOrWhiteSpace(normalizedCleaned) ? "temel kavramlar ve pratik" : Limit(cleaned, 90);
    }

    private static string BuildMainTopic(string raw, string normalizedRaw, string focus)
    {
        var cleaned = Regex.Replace(raw, @"\b(calismak|çalışmak|ogrenmek|öğrenmek|istiyorum|isterim|bana|anlat|study|learn|want|to)\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '.', ',', ':', ';', '-');
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return focus;
        }

        if (ContainsAny(normalizedRaw, "kpss"))
        {
            return "KPSS";
        }

        if (ContainsAny(normalizedRaw, "yks", "tyt", "ayt"))
        {
            if (ContainsAny(normalizedRaw, "matematik", "olasilik", "kombinasyon", "permutasyon"))
            {
                return ContainsAny(normalizedRaw, "tyt") ? "TYT Matematik" : "YKS Matematik";
            }

            return "YKS";
        }

        if (ContainsAny(normalizedRaw, "olasilik", "kombinasyon", "permutasyon", "matematik"))
        {
            return "Matematik";
        }

        if (ContainsAny(normalizedRaw, "problem", "problemler", "soruyu okuyunca", "denklem"))
        {
            return ContainsAny(normalizedRaw, "kpss") ? "KPSS" : "Problem solving";
        }

        return cleaned.Contains(focus, StringComparison.OrdinalIgnoreCase)
            ? cleaned
            : $"{cleaned} - {focus}";
    }

    private static string EnsureEnglishResearchIntent(string researchIntent, string mainTopic, string focusArea)
    {
        if (!string.IsNullOrWhiteSpace(researchIntent) && LooksEnglish(NormalizeForMatch(researchIntent)))
        {
            var trimmed = researchIntent.Trim();
            return ContainsAny(NormalizeForMatch(trimmed), "learning path", "practice", "prerequisite", "common mistake")
                ? trimmed
                : $"{trimmed} learning path";
        }

        var main = TranslateTopic(mainTopic);
        var focus = TranslateTopic(focusArea);
        var parts = new[] { main, focus, "learning path" }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(' ', parts);
    }

    private static string PreferSpecificMainTopic(string rawRequest, string modelMainTopic, string fallbackMainTopic)
    {
        var raw = NormalizeForMatch(rawRequest);
        var model = NormalizeForMatch(modelMainTopic);
        var fallback = string.IsNullOrWhiteSpace(fallbackMainTopic) ? modelMainTopic : fallbackMainTopic;
        var detectedLanguage = DetectProgrammingLanguage(raw);

        if (detectedLanguage is not null && !ContainsAny(model, NormalizeForMatch(detectedLanguage)))
        {
            return fallback;
        }

        if (ContainsAny(raw, "kpss"))
        {
            return "KPSS";
        }

        if (ContainsAny(raw, "ielts") && !ContainsAny(model, "ielts"))
        {
            return "IELTS English";
        }

        if (ContainsAny(raw, "olasilik", "kombinasyon", "permutasyon", "matematik", "tyt") &&
            (IsGenericMainTopic(model) || !ContainsAny(model, "math", "matematik", "probability", "olasilik", "combinatorics")))
        {
            return ContainsAny(raw, "tyt") ? "TYT Matematik" : "Matematik";
        }

        if (ContainsAny(raw, "problem", "problemler", "problemlerde") &&
            detectedLanguage is null &&
            !ContainsAny(model, "problem"))
        {
            return ContainsAny(raw, "kpss") ? "KPSS" : "Matematik problem cozme";
        }

        if (IsGenericMainTopic(model) && !string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return modelMainTopic;
    }

    private static string PreferSpecificFocusArea(string rawRequest, string modelFocusArea, string fallbackFocusArea)
    {
        var raw = NormalizeForMatch(rawRequest);
        var model = NormalizeForMatch(modelFocusArea);

        if (ContainsAny(raw, "dikkat", "hata") && !ContainsAny(model, "dikkat", "mistake", "attention"))
        {
            return fallbackFocusArea;
        }

        if (ContainsAny(raw, "hiz", "hizlan") && !ContainsAny(model, "hiz", "speed"))
        {
            return fallbackFocusArea;
        }

        if (ContainsAny(raw, "join", "explain", "execution") && !ContainsAny(model, "join", "plan", "execution"))
        {
            return fallbackFocusArea;
        }

        if (ContainsAny(raw, "groupby", "merge", "pivot") && !ContainsAny(model, "groupby", "merge", "pivot"))
        {
            return fallbackFocusArea;
        }

        var detectedLanguage = DetectProgrammingLanguage(raw);
        if (detectedLanguage is not null &&
            !ContainsAny(model, NormalizeForMatch(detectedLanguage)) &&
            ContainsAny(model, "javascript", "java script", "python", "java"))
        {
            return fallbackFocusArea;
        }

        return string.IsNullOrWhiteSpace(modelFocusArea) ? fallbackFocusArea : modelFocusArea;
    }

    private static string PreferSpecificResearchIntent(
        string rawRequest,
        string modelResearchIntent,
        string fallbackResearchIntent,
        string mainTopic,
        string focusArea)
    {
        var raw = NormalizeForMatch(rawRequest);
        var model = NormalizeForMatch(modelResearchIntent);
        var detectedLanguage = DetectProgrammingLanguage(raw);

        if (detectedLanguage is not null && !ContainsAny(model, NormalizeForMatch(detectedLanguage)))
        {
            return fallbackResearchIntent;
        }

        if (ContainsAny(raw, "c#", "c sharp", "csharp", "asenkron", "async", "await") &&
            ContainsAny(model, "javascript", "java script"))
        {
            return fallbackResearchIntent;
        }

        if (ContainsAny(raw, "kpss") && !ContainsAny(model, "kpss"))
        {
            return fallbackResearchIntent;
        }

        if (ContainsAny(raw, "paragraf") && !ContainsAny(model, "paragraph", "comprehension"))
        {
            return fallbackResearchIntent;
        }

        if (ContainsAny(raw, "dikkat", "hata") && !ContainsAny(model, "mistake", "attention", "error"))
        {
            return fallbackResearchIntent;
        }

        if (ContainsAny(raw, "index", "indx", "sorgu", "query") && !ContainsAny(model, "index", "query"))
        {
            return fallbackResearchIntent;
        }

        if (string.IsNullOrWhiteSpace(modelResearchIntent))
        {
            return EnsureEnglishResearchIntent(string.Empty, mainTopic, focusArea);
        }

        return modelResearchIntent;
    }

    private static bool IsGenericMainTopic(string modelMainTopic)
    {
        var model = NormalizeForMatch(modelMainTopic);
        return model is "programming"
            or "computer science"
            or "database management"
            or "data analysis"
            or "english language proficiency"
            or "mathematics"
            or "probability"
            or "problem solving";
    }

    private static string TranslateTopic(string value)
    {
        var text = value.Trim();
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["programlama"] = "programming",
            ["algoritmalar"] = "algorithms",
            ["algoritma"] = "algorithms",
            ["siralama"] = "sorting",
            ["çalışmak"] = "study",
            ["öğrenmek"] = "learn",
            ["veri yapilari"] = "data structures",
            ["veri yapıları"] = "data structures",
            ["temel kavramlar"] = "fundamentals",
            ["pratik"] = "practice",
            ["nesne yonelimli programlama"] = "object oriented programming",
            ["nesne yönelimli programlama"] = "object oriented programming",
            ["asenkron programlama"] = "asynchronous programming",
            ["hata ayiklama"] = "debugging",
            ["sınav"] = "exam",
            ["sinav"] = "exam"
        };

        foreach (var pair in replacements)
        {
            text = Regex.Replace(text, Regex.Escape(pair.Key), pair.Value, RegexOptions.IgnoreCase);
        }

        var asciiReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["programlamada"] = "programming",
            ["calismak"] = "study",
            ["ogrenmek"] = "learn",
            ["veritabani"] = "database",
            ["veri tabani"] = "database",
            ["indeksleri"] = "indexes",
            ["indeks"] = "index",
            ["sorgu"] = "query",
            ["optimizasyonu"] = "optimization",
            ["optmzasyon"] = "optimization",
            ["matematik"] = "math",
            ["olasilik"] = "probability",
            ["kombinasyon"] = "combinatorics",
            ["mulakat"] = "interview",
            ["yas problemleri"] = "age problems",
            ["hiz problemleri"] = "speed problems",
            ["okuma stratejisi"] = "reading strategy",
            ["ingilizce"] = "English",
            ["konusma"] = "speaking",
            ["gelistirmek"] = "improve",
            ["gelistirme"] = "improvement",
            ["asenkron"] = "asynchronous",
            ["problemleri"] = "problems",
            ["problem"] = "problem solving",
            ["kpss"] = "KPSS exam",
            ["paragraf"] = "paragraph",
            ["sorularinda"] = "questions",
            ["sorular"] = "questions",
            ["hizlanmak"] = "speed practice",
            ["hiz"] = "speed",
            ["dikkat"] = "attention",
            ["hatasi"] = "mistakes",
            ["hata"] = "mistake",
            ["taktikleri"] = "strategies",
            ["taktik"] = "strategy",
            ["yas"] = "age",
            ["konusurken"] = "speaking",
            ["akiciligim"] = "fluency",
            ["dusuk"] = "low",
            ["cevaplari"] = "answers",
            ["formulu"] = "formula",
            ["karistiriyorum"] = "confusion",
            ["permutasyon"] = "permutation",
            [" ve "] = " and "
        };

        foreach (var pair in asciiReplacements)
        {
            text = Regex.Replace(text, Regex.Escape(pair.Key), pair.Value, RegexOptions.IgnoreCase);
        }

        var typoFixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["jva"] = "java",
            ["jav"] = "java",
            ["algortima"] = "algoritma",
            ["algoritm"] = "algoritma",
            ["algoritmlar"] = "algoritmalar",
            ["calismk"] = "calismak",
            ["calisicam"] = "calismak",
            ["istiyom"] = "istiyorum",
            ["istiyrum"] = "istiyorum",
            ["indek"] = "index",
            ["indx"] = "index"
        };

        foreach (var pair in typoFixes)
        {
            text = Regex.Replace(text, $@"\b{Regex.Escape(pair.Key)}\b", pair.Value, RegexOptions.IgnoreCase);
        }

        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string BuildConfirmation(string mainTopic, string focusArea) =>
        string.IsNullOrWhiteSpace(focusArea)
            ? $"Sunu calismak istedigini anladim: {mainTopic}."
            : $"Sunu calismak istedigini anladim: {mainTopic} icinde {focusArea}.";

    private static bool IsUsable(StudyIntentPreviewResponse? value) =>
        value is not null &&
        !string.IsNullOrWhiteSpace(value.MainTopic) &&
        !string.IsNullOrWhiteSpace(value.FocusArea) &&
        !string.IsNullOrWhiteSpace(value.ResearchIntent);

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = Regex.Replace(value, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 3)] + "...";

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var cleaned = raw.Trim();
        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        return start >= 0 && end > start ? cleaned[start..(end + 1)] : cleaned;
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static bool LooksEnglish(string text) =>
        !ContainsAny(NormalizeForMatch(text), "calismak", "ogren", "istiyorum", "konu", "hakkinda", "algoritmalar", "veri yapilari", "sinav");

    private static string NormalizeForMatch(string value)
    {
        var text = value.ToLowerInvariant()
            .Replace('ç', 'c')
            .Replace('ğ', 'g')
            .Replace('ı', 'i')
            .Replace('ö', 'o')
            .Replace('ş', 's')
            .Replace('ü', 'u');

        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}

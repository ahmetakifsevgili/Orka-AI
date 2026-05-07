using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Korteks;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.SemanticKernel.Plugins;

namespace Orka.Infrastructure.Services;

public sealed class PlanDiagnosticService : IPlanDiagnosticService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan StateTtl = TimeSpan.FromHours(2);

    private readonly OrkaDbContext _db;
    private readonly IKorteksAgent _korteks;
    private readonly IPlanResearchCompressor _compressor;
    private readonly IAIAgentFactory _factory;
    private readonly IPlanDiagnosticStateStore _stateStore;
    private readonly IQuizAttemptRecorder _quizRecorder;
    private readonly IDeepPlanAgent _deepPlan;
    private readonly ILogger<PlanDiagnosticService> _logger;
    private readonly TavilySearchPlugin? _webSearch;
    private readonly WikipediaPlugin? _wikipedia;
    private readonly YouTubeTranscriptPlugin? _youtube;

    public PlanDiagnosticService(
        OrkaDbContext db,
        IKorteksAgent korteks,
        IPlanResearchCompressor compressor,
        IAIAgentFactory factory,
        IPlanDiagnosticStateStore stateStore,
        IQuizAttemptRecorder quizRecorder,
        IDeepPlanAgent deepPlan,
        ILogger<PlanDiagnosticService> logger,
        TavilySearchPlugin? webSearch = null,
        WikipediaPlugin? wikipedia = null,
        YouTubeTranscriptPlugin? youtube = null)
    {
        _db = db;
        _korteks = korteks;
        _compressor = compressor;
        _factory = factory;
        _stateStore = stateStore;
        _quizRecorder = quizRecorder;
        _deepPlan = deepPlan;
        _logger = logger;
        _webSearch = webSearch;
        _wikipedia = wikipedia;
        _youtube = youtube;
    }

    public async Task<StartPlanDiagnosticResponse> StartAsync(
        Guid userId,
        StartPlanDiagnosticRequest request,
        CancellationToken ct = default)
    {
        var topic = await _db.Topics.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TopicId && t.UserId == userId, ct);

        if (topic == null)
        {
            throw new InvalidOperationException("Topic not found for plan diagnostic start.");
        }

        var approvedResearchIntent = NormalizeApprovedIntent(request.ApprovedResearchIntent);
        if (string.IsNullOrWhiteSpace(approvedResearchIntent))
        {
            throw new InvalidOperationException("Approved study intent is required before learning research.");
        }

        var approvedMainTopic = CleanOrDefault(request.ApprovedMainTopic, topic.Title);
        var approvedFocusArea = CleanOrDefault(request.ApprovedFocusArea, "genel kapsam");
        var approvedStudyGoal = CleanOrDefault(request.ApprovedStudyGoal, "ogrenme ve pratik");
        var topicTitle = BuildApprovedTopicTitle(request.TopicTitle, approvedMainTopic, approvedFocusArea, topic.Title);
        var requestedQuestionCount = DetermineDiagnosticQuestionCount(approvedMainTopic, approvedFocusArea, approvedResearchIntent);
        var planRequestId = Guid.NewGuid();
        var quizRunId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var state = new PlanDiagnosticStateDto
        {
            PlanRequestId = planRequestId,
            UserId = userId,
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            TopicTitle = topicTitle,
            IntentRequestId = request.IntentRequestId,
            RawStudyRequest = CleanOrDefault(request.RawStudyRequest, topicTitle),
            ApprovedMainTopic = approvedMainTopic,
            ApprovedFocusArea = approvedFocusArea,
            ApprovedStudyGoal = approvedStudyGoal,
            ApprovedResearchIntent = approvedResearchIntent,
            UserLevel = string.IsNullOrWhiteSpace(request.UserLevel) ? topic.LanguageLevel ?? "Bilinmiyor" : request.UserLevel.Trim(),
            Status = PlanDiagnosticStatus.Researching,
            QuizRunId = quizRunId,
            CreatedAt = now,
            ExpiresAt = now.Add(StateTtl)
        };

        await _stateStore.SaveAsync(state, ct);

        try
        {
            var research = await BuildDirectLearningResearchAsync(
                approvedResearchIntent,
                topicTitle,
                approvedMainTopic,
                approvedFocusArea,
                request.TopicId,
                ct);
            var compressed = _compressor.Compress(research);
            var compressedBlock = _compressor.BuildPromptBlock(compressed);

            state.Status = PlanDiagnosticStatus.ResearchReady;
            state.CompressedResearchContextJson = JsonSerializer.Serialize(compressed, JsonOptions);
            state.CompressedResearchPromptBlock = compressedBlock;
            state.GroundingMode = compressed.GroundingMode;
            state.SourceCount = compressed.SourceCount;
            await _stateStore.SaveAsync(state, ct);

            var quizJson = await GenerateDiagnosticQuizFromStoredContextAsync(
                topicTitle,
                compressedBlock,
                requestedQuestionCount,
                ct);
            var questionCount = CountQuestions(quizJson);

            _db.QuizRuns.Add(new QuizRun
            {
                Id = quizRunId,
                UserId = userId,
                TopicId = request.TopicId,
                SessionId = request.SessionId,
                QuizType = "baseline",
                Status = "active",
                TotalQuestions = questionCount,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    planRequestId,
                    intentRequestId = request.IntentRequestId,
                    approvedMainTopic,
                    approvedFocusArea,
                    approvedResearchIntent
                }, JsonOptions),
                CreatedAt = now
            });
            await _db.SaveChangesAsync(ct);

            state.Status = PlanDiagnosticStatus.QuizPending;
            state.QuizQuestionCount = questionCount;
            await _stateStore.SaveAsync(state, ct);

            return new StartPlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                QuizRunId = state.QuizRunId,
                TopicId = state.TopicId,
                TopicTitle = state.TopicTitle,
                Status = state.Status,
                QuestionsJson = quizJson,
                GroundingMode = state.GroundingMode,
                SourceCount = state.SourceCount,
                QuizQuestionCount = state.QuizQuestionCount,
                IntentRequestId = state.IntentRequestId,
                ApprovedMainTopic = state.ApprovedMainTopic,
                ApprovedFocusArea = state.ApprovedFocusArea,
                ApprovedStudyGoal = state.ApprovedStudyGoal,
                ApprovedResearchIntent = state.ApprovedResearchIntent
            };
        }
        catch (Exception ex)
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = ex.Message;
            await _stateStore.SaveAsync(state, ct);
            _logger.LogWarning(ex, "[PlanDiagnostic] Start failed. PlanRequestId={PlanRequestId}", planRequestId);
            throw;
        }
    }

    public async Task<PlanDiagnosticAnswerResponse> RecordAnswerAsync(
        Guid userId,
        Guid planRequestId,
        RecordQuizAttemptRequest request,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, planRequestId, ct);
        request.QuizRunId = state.QuizRunId;
        request.TopicId = state.TopicId;
        request.SessionId ??= state.SessionId;

        await _quizRecorder.RecordAsync(userId, request, ct);

        state.AnsweredQuestionCount = await _db.QuizAttempts
            .CountAsync(a => a.UserId == userId && a.QuizRunId == state.QuizRunId, ct);

        if (state.QuizQuestionCount > 0 && state.AnsweredQuestionCount >= state.QuizQuestionCount)
        {
            state.Status = PlanDiagnosticStatus.QuizCompleted;
            state.QuizCompletedAt ??= DateTime.UtcNow;
        }

        await _stateStore.SaveAsync(state, ct);

        return new PlanDiagnosticAnswerResponse
        {
            PlanRequestId = state.PlanRequestId,
            QuizRunId = state.QuizRunId,
            Status = state.Status,
            AnsweredQuestionCount = state.AnsweredQuestionCount,
            QuizQuestionCount = state.QuizQuestionCount
        };
    }

    public async Task<FinalizePlanDiagnosticResponse> FinalizeAsync(
        Guid userId,
        FinalizePlanDiagnosticRequest request,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, request.PlanRequestId, ct);

        if (state.Status == PlanDiagnosticStatus.PlanGenerated && state.GeneratedPlanRootTopicId.HasValue)
        {
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = true,
                Message = "Plan was already generated.",
                GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
                GeneratedTopicIds = await GetGeneratedPlanTopicIdsAsync(userId, state.GeneratedPlanRootTopicId.Value, ct)
            };
        }

        state.AnsweredQuestionCount = await _db.QuizAttempts
            .CountAsync(a => a.UserId == userId && a.QuizRunId == state.QuizRunId, ct);

        if (state.QuizQuestionCount <= 0 || state.AnsweredQuestionCount < state.QuizQuestionCount)
        {
            state.Status = state.AnsweredQuestionCount > 0 ? PlanDiagnosticStatus.QuizPending : state.Status;
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = "Diagnostic quiz is incomplete."
            };
        }

        state.Status = PlanDiagnosticStatus.PlanGenerating;
        await _stateStore.SaveAsync(state, ct);

        var diagnosticSummary = await BuildCurrentDiagnosticSummaryAsync(userId, state.QuizRunId, ct);
        var planResult = await _deepPlan.GenerateAndSaveDeepPlanFromDiagnosticAsync(
            state.TopicId,
            state.TopicTitle,
            userId,
            state.CompressedResearchPromptBlock,
            diagnosticSummary,
            state.UserLevel);

        state.Status = PlanDiagnosticStatus.PlanGenerated;
        state.GeneratedPlanRootTopicId = state.TopicId;
        await _stateStore.SaveAsync(state, ct);

        return new FinalizePlanDiagnosticResponse
        {
            PlanRequestId = state.PlanRequestId,
            Status = state.Status,
            PlanGenerated = true,
            GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
            GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList()
        };
    }

    public async Task<FinalizePlanDiagnosticResponse> SkipAndGenerateAsync(
        Guid userId,
        Guid planRequestId,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, planRequestId, ct);

        state.AnsweredQuestionCount = await _db.QuizAttempts
            .CountAsync(a => a.UserId == userId && a.QuizRunId == state.QuizRunId, ct);

        if (state.Status == PlanDiagnosticStatus.PlanGenerated && state.GeneratedPlanRootTopicId.HasValue)
        {
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = true,
                Message = "Plan was already generated.",
                GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
                GeneratedTopicIds = await GetGeneratedPlanTopicIdsAsync(userId, state.GeneratedPlanRootTopicId.Value, ct)
            };
        }

        state.Status = PlanDiagnosticStatus.PlanGenerating;
        state.QuizCompletedAt ??= DateTime.UtcNow;
        await _stateStore.SaveAsync(state, ct);

        var diagnosticSummary = string.Join("\n", new[]
        {
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]",
            "Mode: StartFromZero",
            $"QuizRunId: {state.QuizRunId}",
            $"Answered: {state.AnsweredQuestionCount}",
            "Correct: 0",
            "Wrong: 0",
            "WeakConcepts: none",
            "MistakePatterns: none",
            "Instruction: The learner explicitly skipped the diagnostic quiz and chose to start from zero. Build a beginner-safe plan, but do not infer weak skills or record fake mistakes from skipped questions."
        });

        var planResult = await _deepPlan.GenerateAndSaveDeepPlanFromDiagnosticAsync(
            state.TopicId,
            state.TopicTitle,
            userId,
            state.CompressedResearchPromptBlock,
            diagnosticSummary,
            state.UserLevel);

        state.Status = PlanDiagnosticStatus.PlanGenerated;
        state.GeneratedPlanRootTopicId = state.TopicId;
        await _stateStore.SaveAsync(state, ct);

        return new FinalizePlanDiagnosticResponse
        {
            PlanRequestId = state.PlanRequestId,
            Status = state.Status,
            PlanGenerated = true,
            Message = "Diagnostic quiz skipped; beginner plan generated without fake quiz mistakes.",
            GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
            GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList()
        };
    }

    private async Task<string> GenerateDiagnosticQuizFromStoredContextAsync(
        string topicTitle,
        string compressedResearchPromptBlock,
        int requestedQuestionCount,
        CancellationToken ct)
    {
        var quizIntelligenceBrief = PlanIntelligenceBriefBuilder.BuildForDiagnosticQuiz(
            topicTitle,
            compressedResearchPromptBlock);

        var systemPrompt = $$"""
            Sen profesyonel bir 'Egitim Tanilama Uzmani' botusun.
            Gorevin: '{{topicTitle}}' konusunda {{requestedQuestionCount}} soruluk seviye tespit quiz'i uretmek.

            {{quizIntelligenceBrief}}

            KURALLAR:
            - Soru sayisi tam olarak {{requestedQuestionCount}} olacak; 15'ten az, 25'ten fazla olmayacak.
            - Ham arastirma raporu varsayma; sadece bu filtrelenmis brief'i ve konu basligini kullan.
            - Kaynak basliklarini veya web/video metinlerini soru kokune kopyalama.
            - Sorular onayli konu ve odak alaninda kalmali; baska teknoloji, sinav veya alan sizdirmamali.
            - Generic pipeline, "input -> transform", "tani sorusu" gibi ic sistem kalibi kullanma.
            - Orka IDE, sandbox veya urun arayuzu etiketlerini soru kokune, seceneklere ya da dogru cevaba yazma; quiz kavrami olcer, urun ozelligini degil.
            - conceptual, procedural, application, analysis ve misconception_probe soru tiplerini karisik kullan.
            - kolay, orta ve zor dagilimini dengeli kur.
            - Soru metinleri birbirinin kopyasi veya yakin tekrari olmasin.
            - En az 8 farkli conceptTag kullan.
            - En az 4 farkli questionType kullan.
            - En az 5 soru beklenen kavram yanilgisini hedeflesin.
            - Teknik konularda en az bir soru gercek kod parcasi, kod okuma veya hata ayiklama senaryosu icersin.
            - Her soru su alanlari icersin: question, options, correctAnswer, explanation, skillTag, difficulty, conceptTag, learningObjective, questionType, expectedMisconceptionCategory.

            SADECE JSON array dondur.
            """;

        var rawQuiz = await _factory.CompleteChatAsync(
            AgentRole.DeepPlan,
            systemPrompt,
            $"Konu: \"{topicTitle}\" icin tam {requestedQuestionCount} adet tanilayici baseline sorusu uret.",
            ct);
        try
        {
            var validatedQuiz = DiagnosticQuizQualityGate.EnsureQualityOrThrow(rawQuiz, topicTitle, requestedQuestionCount, out var quality);
            if (!quality.IsAcceptable)
            {
                _logger.LogWarning(
                    "[PlanDiagnostic] Diagnostic quiz quality failed. Topic={Topic} Failures={Failures}",
                    topicTitle,
                    string.Join(" | ", quality.Failures));
            }

            return validatedQuiz;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "[PlanDiagnostic] Diagnostic quiz provider output failed quality gate; using domain-aware fallback. Topic={Topic}",
                topicTitle);

            var fallback = DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint(topicTitle);
            var fallbackCount = DiagnosticQuizQualityGate.CountQuestions(fallback);
            if (fallbackCount is < 15 or > 25)
            {
                throw new InvalidOperationException($"Fallback diagnostic quiz is invalid; count={fallbackCount}.", ex);
            }

            return fallback;
        }
    }

    private async Task<KorteksResearchResultDto> BuildDirectLearningResearchAsync(
        string approvedResearchIntent,
        string topicTitle,
        string approvedMainTopic,
        string approvedFocusArea,
        Guid topicId,
        CancellationToken ct)
    {
        var retrievedAt = DateTimeOffset.UtcNow;
        var providerBlocks = new List<string>();
        var sources = new List<SourceEvidenceDto>();
        var failures = new List<string>();
        var warnings = new List<string>();

        var webQueries = BuildLearningResearchQueries(approvedResearchIntent, approvedMainTopic, approvedFocusArea);
        if (_webSearch != null)
        {
            var webResult = await TryProviderCallAsync(
                "WebSearch",
                () => _webSearch.SearchWebDeep(string.Join(", ", webQueries)),
                failures,
                ct);
            providerBlocks.Add(BuildProviderBlock("WebSearch", webResult));
            sources.AddRange(KorteksSourceEvidenceExtractor.Extract("WebSearch", "SearchWebDeep", webResult, retrievedAt));
            AddDegradedWarning("WebSearch", webResult, warnings);
        }
        else
        {
            warnings.Add("WebSearch plugin is not available in this runtime; learning research used internal curriculum synthesis.");
        }

        if (_wikipedia != null)
        {
            var wikiResult = await TryProviderCallAsync(
                "Wikipedia",
                () => _wikipedia.SearchWikipedia(approvedResearchIntent),
                failures,
                ct);
            providerBlocks.Add(BuildProviderBlock("Wikipedia", wikiResult));
            sources.AddRange(KorteksSourceEvidenceExtractor.Extract("Wikipedia", "SearchWikipedia", wikiResult, retrievedAt));
            AddDegradedWarning("Wikipedia", wikiResult, warnings);
        }
        else
        {
            warnings.Add("Wikipedia plugin is not available in this runtime.");
        }

        if (_youtube != null)
        {
            var youtubeResult = await TryProviderCallAsync(
                "YouTube",
                () => _youtube.SearchYouTubeVideos(approvedResearchIntent),
                failures,
                ct);
            providerBlocks.Add(BuildProviderBlock("YouTube", youtubeResult));
            sources.AddRange(KorteksSourceEvidenceExtractor.Extract("YouTube", "SearchYouTubeVideos", youtubeResult, retrievedAt));
            AddDegradedWarning("YouTube", youtubeResult, warnings);
        }
        else
        {
            warnings.Add("YouTube plugin is not available in this runtime.");
        }

        var deterministicBrief = BuildDeterministicLearningBrief(
            approvedResearchIntent,
            topicTitle,
            approvedMainTopic,
            approvedFocusArea);

        var systemPrompt = """
            You are Orka's Learning Research Synthesizer.
            You do not create the final quiz and you do not create the final study plan.
            Convert the approved study intent and available source snippets into a compact learning-research brief.

            Requirements:
            - Use the approved study intent, not the raw user sentence.
            - Prefer source-aware learning routes when source snippets are available.
            - If live sources are missing/degraded, be explicit and use conservative curriculum knowledge.
            - Extract prerequisites, sub-concepts, common mistakes, practice order, quiz scope, and recommended question count.
            - For programming topics, keep quiz material about language/concept knowledge; do not mention Orka IDE, sandbox, Visual Studio, or product UI inside quiz scope.
            - Do not invent citations. Do not claim current web grounding when providers are disabled.
            - Return a concise markdown brief with stable section headings.
            """;

        var userMessage = $$"""
            Approved study intent: {{approvedResearchIntent}}
            Main topic: {{approvedMainTopic}}
            Focus area: {{approvedFocusArea}}
            Display topic title: {{topicTitle}}

            Deterministic curriculum seed:
            {{deterministicBrief}}

            Provider notes:
            {{string.Join("\n\n", providerBlocks)}}

            Produce sections:
            [DIRECT LEARNING RESEARCH BRIEF]
            LearningRoute
            ReliableSources
            YouTubeLearningReferences
            Prerequisites
            SubConcepts
            CommonMistakes
            PracticeOrder
            QuizScope
            RecommendedQuestionCount
            PlanningNotes
            """;

        string report;
        try
        {
            report = await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, userMessage, ct);
            if (LooksLikeQuizJson(report) || !report.Contains("QuizScope", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Learning research synthesizer returned an invalid brief shape; deterministic curriculum brief was used.");
                report = deterministicBrief;
            }
        }
        catch (Exception ex)
        {
            warnings.Add("Learning research synthesizer failed; deterministic curriculum brief was used.");
            failures.Add($"LearningResearchSynthesizer: {ex.GetType().Name}");
            report = deterministicBrief;
        }

        sources = sources
            .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(12)
            .ToList();

        var groundingMode = sources.Count switch
        {
            > 2 => GroundingMode.SourceGrounded,
            > 0 => GroundingMode.PartialSourceGrounded,
            _ => GroundingMode.FallbackInternalKnowledge
        };

        return new KorteksResearchResultDto
        {
            Topic = approvedResearchIntent,
            TopicId = topicId,
            Report = report,
            GroundingMode = groundingMode,
            Sources = sources,
            ProviderFailures = failures,
            Warnings = warnings,
            IsFallback = sources.Count == 0,
            CreatedAt = retrievedAt
        };
    }

    private static string[] BuildLearningResearchQueries(
        string approvedResearchIntent,
        string approvedMainTopic,
        string approvedFocusArea)
    {
        var baseIntent = string.Join(' ', new[] { approvedResearchIntent, approvedMainTopic, approvedFocusArea }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        return
        [
            $"{baseIntent} learning path",
            $"{baseIntent} prerequisites common mistakes practice exercises",
            $"{baseIntent} tutorial course roadmap"
        ];
    }

    private static async Task<string> TryProviderCallAsync(
        string provider,
        Func<Task<string>> call,
        List<string> failures,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            return await call();
        }
        catch (Exception ex)
        {
            failures.Add($"{provider}: {ex.GetType().Name}");
            return $"[{provider}:degraded] {provider} unavailable during direct learning research.";
        }
    }

    private static string BuildProviderBlock(string provider, string? result)
    {
        var text = string.IsNullOrWhiteSpace(result)
            ? "[empty]"
            : result.Trim();
        if (text.Length > 2500)
        {
            text = text[..2500] + "...";
        }

        return $"[{provider}]\n{text}";
    }

    private static void AddDegradedWarning(string provider, string? result, List<string> warnings)
    {
        var marker = KorteksSourceEvidenceExtractor.FindDegradedMarker(result);
        if (!string.IsNullOrWhiteSpace(marker))
        {
            warnings.Add($"{provider} returned degraded marker: {marker}");
        }
    }

    private static bool LooksLikeQuizJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith("[", StringComparison.Ordinal) &&
               value.Contains("\"question\"", StringComparison.OrdinalIgnoreCase) &&
               value.Contains("\"options\"", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDeterministicLearningBrief(
        string approvedResearchIntent,
        string topicTitle,
        string approvedMainTopic,
        string approvedFocusArea)
    {
        if (LooksLikeJavaAlgorithmsIntent(approvedResearchIntent, topicTitle, approvedMainTopic, approvedFocusArea))
        {
            return $$"""
                [DIRECT LEARNING RESEARCH BRIEF]
                LearningRoute:
                1. Java array/list basics and iteration.
                2. Big-O intuition through simple loops, nested loops, and collection operations.
                3. Sorting and searching: Arrays.sort, Collections.sort, binary search prerequisites.
                4. Core data structures: ArrayList, LinkedList, Stack, Queue, HashSet, HashMap, PriorityQueue.
                5. Recursion, base cases, and call-stack reasoning.
                6. Graph basics: BFS with queue, DFS with recursion/stack.
                7. Practical patterns: two pointers, prefix sums, greedy checks, dynamic programming basics.

                ReliableSources:
                - Oracle Java Collections algorithms documentation is a stable reference for sorting/searching collection behavior.
                - Princeton Algorithms Part I is a strong Java-oriented algorithms/data structures curriculum reference.
                - OpenDSA/Open Data Structures are useful open learning references for practice and conceptual checks.

                YouTubeLearningReferences:
                - Use YouTube only as a teaching reference when configured. Prefer Java algorithms/data structures tutorials that show code traces and practice problems.

                Prerequisites:
                Java syntax, methods, loops, arrays, object basics, generics basics, and reading small code traces.

                SubConcepts:
                arrays, lists, sorting, searching, binary search precondition, Big-O, stack, queue, set, map, priority queue, recursion, BFS, DFS, greedy, dynamic programming.

                CommonMistakes:
                assuming binary search works on unsorted data; confusing HashMap with ordered maps; off-by-one loop bounds; ignoring base cases; treating every greedy idea as correct; mixing stack and queue behavior; memorizing Big-O without reading the code path.

                PracticeOrder:
                trace small arrays -> implement search -> compare linear/binary search -> sort custom objects with Comparator -> use stack/queue -> map/set frequency tasks -> recursion base cases -> BFS/DFS toy graph -> prefix sum/two pointer -> small DP table.

                QuizScope:
                Diagnostic questions must measure Java algorithm/data-structure understanding, not product UI usage. Include code reading, data-structure choice, complexity, and misconception probes.

                RecommendedQuestionCount:
                20

                PlanningNotes:
                Move already-known Java syntax quickly into practice. Spend more time on misconceptions found in quiz attempts, especially complexity, data-structure choice, and algorithm preconditions.
                """;
        }

        return $$"""
            [DIRECT LEARNING RESEARCH BRIEF]
            LearningRoute:
            Start from prerequisites, map the focus area into sub-concepts, then move from small examples to applied practice.

            ReliableSources:
            Live source grounding was unavailable or partial. Use conservative curriculum knowledge until provider-backed sources are available.

            YouTubeLearningReferences:
            Use as teaching references only when configured; do not treat video metadata as factual proof.

            Prerequisites:
            Identify vocabulary, prior skills, and basic examples needed before the learner starts the focus area.

            SubConcepts:
            Break "{{approvedResearchIntent}}" into measurable concept groups before quiz generation.

            CommonMistakes:
            Watch for memorized definitions, skipped prerequisites, confused terminology, and applying a rule outside its constraints.

            PracticeOrder:
            Concept check -> small worked example -> guided practice -> mixed practice -> error reflection.

            QuizScope:
            Questions should measure the approved intent directly and avoid internal product/system wording.

            RecommendedQuestionCount:
            20

            PlanningNotes:
            Use quiz results to fast-track known concepts and slow down on weak/misunderstood concepts.
            """;
    }

    private static bool LooksLikeJavaAlgorithmsIntent(params string[] values)
    {
        var text = string.Join(' ', values).ToLowerInvariant();
        return text.Contains("java", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("algoritma", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("algorithm", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("veri yapi", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("data structure", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<Guid>> GetGeneratedPlanTopicIdsAsync(Guid userId, Guid rootTopicId, CancellationToken ct)
    {
        var moduleIds = await _db.Topics.AsNoTracking()
            .Where(t => t.UserId == userId && t.ParentTopicId == rootTopicId && t.PlanIntent == "Module")
            .OrderBy(t => t.Order)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (moduleIds.Count == 0)
        {
            return [];
        }

        var lessonIds = await _db.Topics.AsNoTracking()
            .Where(t => t.UserId == userId && t.ParentTopicId.HasValue && moduleIds.Contains(t.ParentTopicId.Value))
            .OrderBy(t => t.Order)
            .Select(t => t.Id)
            .ToListAsync(ct);

        return lessonIds.Count > 0 ? lessonIds : moduleIds;
    }

    private async Task<string> BuildCurrentDiagnosticSummaryAsync(Guid userId, Guid quizRunId, CancellationToken ct)
    {
        var attempts = await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.QuizRunId == quizRunId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        if (attempts.Count == 0)
        {
            return "[PLAN DIAGNOSTIC QUIZ SUMMARY]\nNo answers were recorded.";
        }

        var correct = attempts.Where(a => a.IsCorrect).ToList();
        var wrong = attempts.Where(a => !a.IsCorrect).ToList();
        var knownSummary = correct
            .Select(ExtractWeakConcept)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();
        var conceptSummary = wrong
            .Select(ExtractWeakConcept)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();
        var fastTrack = knownSummary
            .Where(item => !conceptSummary.Any(weak => weak.StartsWith(item.Split(':')[0], StringComparison.OrdinalIgnoreCase)))
            .Take(8)
            .ToList();
        var mistakeSummary = wrong
            .Select(ExtractMistakePattern)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();
        var accuracyPercent = attempts.Count == 0
            ? 0
            : (int)Math.Round(attempts.Count(a => a.IsCorrect) * 100.0 / attempts.Count);
        var measuredLevel = accuracyPercent switch
        {
            >= 85 => "advanced",
            >= 65 => "intermediate",
            >= 40 => "developing",
            _ => "beginner"
        };

        return string.Join("\n", new[]
        {
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]",
            $"QuizRunId: {quizRunId}",
            $"Answered: {attempts.Count}",
            $"Correct: {attempts.Count(a => a.IsCorrect)}",
            $"Wrong: {wrong.Count}",
            $"AccuracyPercent: {accuracyPercent}",
            $"MeasuredLevel: {measuredLevel}",
            $"KnownConcepts: {(knownSummary.Count == 0 ? "none" : string.Join(" | ", knownSummary))}",
            $"FastTrackConcepts: {(fastTrack.Count == 0 ? "none" : string.Join(" | ", fastTrack))}",
            $"PracticeConcepts: {(knownSummary.Count == 0 ? "none" : string.Join(" | ", knownSummary.Take(8)))}",
            $"WeakConcepts: {(conceptSummary.Count == 0 ? "none" : string.Join(" | ", conceptSummary))}",
            $"MistakePatterns: {(mistakeSummary.Count == 0 ? "none" : string.Join(" | ", mistakeSummary))}",
            "Instruction: Move known concepts faster with short practice. Teach weak or mistaken concepts more slowly, logically, and with examples. Do not claim skipped or unanswered concepts are weaknesses."
        });
    }

    private static string? ExtractWeakConcept(QuizAttempt attempt)
    {
        var conceptTag = ExtractAttemptMetadata(attempt.SourceRefsJson, "conceptTag");
        if (!string.IsNullOrWhiteSpace(conceptTag))
        {
            return conceptTag.Trim();
        }

        var learningObjective = ExtractAttemptMetadata(attempt.SourceRefsJson, "learningObjective");
        if (!string.IsNullOrWhiteSpace(learningObjective))
        {
            return learningObjective.Trim();
        }

        if (!string.IsNullOrWhiteSpace(attempt.SkillTag))
        {
            return attempt.SkillTag.Trim();
        }

        if (!string.IsNullOrWhiteSpace(attempt.TopicPath))
        {
            var parts = attempt.TopicPath.Split(
                new[] { '/', '>', '|', '\\' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.LastOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(attempt.QuestionHash))
        {
            return attempt.QuestionHash.Trim();
        }

        return null;
    }

    private static string? ExtractAttemptMetadata(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string ExtractMistakePattern(QuizAttempt attempt)
    {
        var text = string.Join(" ",
            attempt.Explanation,
            attempt.CognitiveType,
            attempt.SourceRefsJson);

        foreach (var candidate in new[]
                 {
                     "Procedural",
                     "Reading",
                     "MisreadQuestion",
                     "Conceptual",
                     "Application",
                     "Careless"
                 })
        {
            if (text.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return string.IsNullOrWhiteSpace(attempt.CognitiveType)
            ? "IncorrectAnswer"
            : attempt.CognitiveType.Trim();
    }

    private async Task<PlanDiagnosticStateDto> RequireStateAsync(Guid userId, Guid planRequestId, CancellationToken ct)
    {
        var state = await _stateStore.GetAsync(planRequestId, ct);
        if (state == null || state.UserId != userId)
        {
            throw new InvalidOperationException("Plan diagnostic state was not found.");
        }

        return state;
    }

    private static string NormalizeApprovedIntent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 160 ? normalized : normalized[..160];
    }

    private static string CleanOrDefault(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string BuildApprovedTopicTitle(string? requestedTitle, string mainTopic, string focusArea, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(requestedTitle) && !LooksLikeRawStudyRequest(requestedTitle))
        {
            return CleanOrDefault(requestedTitle, fallback);
        }

        if (string.IsNullOrWhiteSpace(focusArea) || focusArea.Equals("genel kapsam", StringComparison.OrdinalIgnoreCase))
        {
            return CleanOrDefault(mainTopic, fallback);
        }

        return $"{mainTopic}: {focusArea}";
    }

    private static bool LooksLikeRawStudyRequest(string value)
    {
        var text = value.Trim().ToLowerInvariant();
        return text.Contains("calismak istiyorum", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ogrenmek istiyorum", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("istiyorum", StringComparison.OrdinalIgnoreCase);
    }

    private static int DetermineDiagnosticQuestionCount(string mainTopic, string focusArea, string researchIntent)
    {
        var text = $"{mainTopic} {focusArea} {researchIntent}".ToLowerInvariant();
        var broadSignals = new[]
        {
            "algorithm",
            "algoritma",
            "data structure",
            "veri yap",
            "programming",
            "programlama",
            "sql",
            "kpss",
            "yks",
            "curriculum",
            "roadmap"
        };

        var narrowSignals = new[]
        {
            "temel",
            "intro",
            "giris",
            "syntax",
            "tek konu",
            "single topic"
        };

        var score = broadSignals.Count(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase)) -
                    narrowSignals.Count(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));

        return Math.Clamp(18 + score * 2, 15, 25);
    }

    private static int CountQuestions(string quizJson)
    {
        try
        {
            var count = DiagnosticQuizQualityGate.CountQuestions(quizJson);
            return count > 0 ? count : 20;
        }
        catch
        {
            // The model response will still be returned; default to the intended diagnostic size.
        }

        return 20;
    }
}

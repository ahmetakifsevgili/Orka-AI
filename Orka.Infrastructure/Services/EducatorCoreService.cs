using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class EducatorCoreService : IEducatorCoreService
{
    private static readonly Regex DocCitationRegex = new(@"\[doc:[0-9a-fA-F-]{36}:p\d+\]", RegexOptions.Compiled);
    private static readonly Regex YouTubeIdRegex = new(@"\[youtube:([A-Za-z0-9_-]{6,})\]|VideoId:\s*`([^`]+)`|BestVideoId""\s*:\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex SentenceSplitRegex = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    private readonly IRedisMemoryService _redis;
    private readonly ILearningSignalService _signals;
    private readonly ILogger<EducatorCoreService> _logger;

    public EducatorCoreService(
        IRedisMemoryService redis,
        ILearningSignalService signals,
        ILogger<EducatorCoreService> logger)
    {
        _redis = redis;
        _signals = signals;
        _logger = logger;
    }

    public async Task<TeacherContext> BuildTeacherContextAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string question,
        string notebookContext,
        string wikiContext,
        string learningSignalContext,
        string? rawYouTubeContext,
        CancellationToken ct = default)
    {
        var sources = new List<SourceUsage>();
        if (!string.IsNullOrWhiteSpace(notebookContext))
        {
            sources.Add(new SourceUsage(
                "doc",
                "NotebookLM user documents",
                "Use exact [doc:sourceId:pN] tags for document-backed claims.",
                true,
                1));
        }

        if (!string.IsNullOrWhiteSpace(wikiContext))
        {
            sources.Add(new SourceUsage(
                "wiki",
                "Personal topic wiki",
                "Label wiki-grounded claims as Wiki context when no document tag exists.",
                true,
                2));
        }

        if (!string.IsNullOrWhiteSpace(learningSignalContext))
        {
            sources.Add(new SourceUsage(
                "learning-signal",
                "Student weakness and mastery signals",
                "Use signals only for personalization, not as factual evidence.",
                false,
                3));
        }

        TeachingReference? teachingReference = null;
        if (topicId.HasValue)
        {
            teachingReference = await NormalizeTeachingReferenceAsync(topicId.Value, rawYouTubeContext, ct);
            if (teachingReference != null)
            {
                sources.Add(new SourceUsage(
                    "youtube",
                    "YouTube educator reference",
                    "Use only for teaching flow, examples, analogies, and common mistakes unless explicitly cited.",
                    false,
                    5));

                await SafeRecordSignalAsync(
                    userId,
                    topicId,
                    sessionId,
                    LearningSignalTypes.YouTubeReferenceUsed,
                    payloadJson: JsonSerializer.Serialize(new
                    {
                        teachingReference.SourceId,
                        teachingReference.Status,
                        exampleCount = teachingReference.Examples.Count,
                        commonMistakeCount = teachingReference.CommonMistakes.Count
                    }),
                    ct: ct);
            }
        }

        if (!string.IsNullOrWhiteSpace(notebookContext))
        {
            await SafeRecordSignalAsync(
                userId,
                topicId,
                sessionId,
                LearningSignalTypes.NotebookSourceUsed,
                payloadJson: JsonSerializer.Serialize(new { questionLength = question.Length }),
                ct: ct);
        }

        var misconceptions = BuildMisconceptionSignals(question, learningSignalContext);
        foreach (var signal in misconceptions)
        {
            await SafeRecordSignalAsync(
                userId,
                topicId,
                sessionId,
                LearningSignalTypes.MisconceptionDetected,
                skillTag: signal.SkillTag,
                topicPath: signal.TopicPath,
                isPositive: false,
                payloadJson: JsonSerializer.Serialize(signal),
                ct: ct);
        }

        var quality = new EducatorQualityScore(
            RequiresCitationGuard: !string.IsNullOrWhiteSpace(notebookContext) || !string.IsNullOrWhiteSpace(wikiContext),
            HasNotebookContext: !string.IsNullOrWhiteSpace(notebookContext),
            HasWikiContext: !string.IsNullOrWhiteSpace(wikiContext),
            HasLearningSignals: !string.IsNullOrWhiteSpace(learningSignalContext),
            HasTeachingReference: teachingReference != null,
            Recommendation: BuildRecommendation(notebookContext, wikiContext, learningSignalContext, teachingReference));

        var promptBlock = BuildPromptBlock(sources, teachingReference, misconceptions, quality);
        return new TeacherContext(sources, teachingReference == null ? [] : [teachingReference], misconceptions, quality, promptBlock);
    }

    public async Task<TeachingReference?> NormalizeTeachingReferenceAsync(
        Guid topicId,
        string? rawYouTubeContext,
        CancellationToken ct = default)
    {
        var cacheKey = TeachingReferenceKey(topicId);

        try
        {
            var cached = await _redis.GetJsonAsync(cacheKey);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                var cachedReference = JsonSerializer.Deserialize<TeachingReference>(cached);
                if (cachedReference != null) return cachedReference;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[EducatorCore] Teaching reference cache read failed. TopicId={TopicId}", topicId);
        }

        if (string.IsNullOrWhiteSpace(rawYouTubeContext))
            return null;

        var reference = BuildTeachingReference(rawYouTubeContext);
        if (reference == null)
            return null;

        try
        {
            var payload = JsonSerializer.Serialize(reference);
            await _redis.SetJsonAsync(cacheKey, payload, TimeSpan.FromHours(24));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[EducatorCore] Teaching reference cache write failed. TopicId={TopicId}", topicId);
        }

        return reference;
    }

    public async Task RecordAnswerQualitySignalsAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string answer,
        TeacherContext context,
        CancellationToken ct = default)
    {
        if (context.QualityScore.HasNotebookContext && !DocCitationRegex.IsMatch(answer))
        {
            await SafeRecordSignalAsync(
                userId,
                topicId,
                sessionId,
                LearningSignalTypes.SourceCitationMissing,
                isPositive: false,
                payloadJson: JsonSerializer.Serialize(new
                {
                    reason = "notebook-context-present-without-doc-citation",
                    answerLength = answer.Length
                }),
                ct: ct);
        }

        if (context.TeachingReferences.Count > 0)
        {
            await SafeRecordSignalAsync(
                userId,
                topicId,
                sessionId,
                LearningSignalTypes.TeachingMoveApplied,
                isPositive: true,
                payloadJson: JsonSerializer.Serialize(new
                {
                    references = context.TeachingReferences.Select(r => new { r.SourceType, r.SourceId, r.Status }),
                    answerLength = answer.Length
                }),
                ct: ct);
        }
    }

    private static TeachingReference? BuildTeachingReference(string raw)
    {
        var status = raw.Contains("[youtube:disabled]", StringComparison.OrdinalIgnoreCase)
            ? "disabled"
            : raw.Contains("[youtube:degraded]", StringComparison.OrdinalIgnoreCase)
                ? "degraded"
                : "ready";

        var videoId = ExtractVideoId(raw);
        var transcript = ExtractTranscript(raw);
        if (string.IsNullOrWhiteSpace(transcript) && status == "ready")
            return null;

        var cleaned = CleanForAnalysis(transcript.Length > 0 ? transcript : raw);
        var flow = string.Join(" -> ", PickSentences(cleaned, [], 4));
        if (string.IsNullOrWhiteSpace(flow))
            flow = status == "ready" ? "Use the educator's sequence as a light teaching scaffold." : "No reliable transcript is available.";

        var examples = PickSentences(cleaned, ["ornek", "mesela", "diyelim", "example"], 4);
        var analogies = PickSentences(cleaned, ["gibi", "benzet", "dusun", "imagine", "like"], 3);
        var mistakes = PickSentences(cleaned, ["hata", "yanlis", "karistir", "dikkat", "mistake", "confus"], 4);
        var practice = BuildPracticeIdeas(examples, mistakes);

        return new TeachingReference(
            "youtube",
            string.IsNullOrWhiteSpace(videoId) ? "unknown" : videoId,
            status,
            flow,
            examples,
            analogies,
            mistakes,
            practice,
            DateTime.UtcNow);
    }

    private static string ExtractTranscript(string raw)
    {
        try
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("Transcript", out var transcript))
                    return transcript.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // Fall through to raw transcript parsing.
        }

        var markerIndex = raw.IndexOf("YouTube transcript", StringComparison.OrdinalIgnoreCase);
        return markerIndex >= 0 ? raw[markerIndex..] : raw;
    }

    private static string ExtractVideoId(string raw)
    {
        var match = YouTubeIdRegex.Match(raw);
        if (!match.Success) return string.Empty;
        return match.Groups.Cast<Group>().Skip(1).FirstOrDefault(g => g.Success && !string.IsNullOrWhiteSpace(g.Value))?.Value ?? string.Empty;
    }

    private static string CleanForAnalysis(string value)
    {
        var clean = Regex.Replace(value, @"https?://\S+", " ");
        clean = Regex.Replace(clean, @"\[[^\]]+\]", " ");
        clean = Regex.Replace(clean, @"\s+", " ");
        return clean.Trim();
    }

    private static IReadOnlyList<string> PickSentences(string text, string[] needles, int take)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var sentences = SentenceSplitRegex
            .Split(text)
            .Select(s => s.Trim())
            .Where(s => s.Length is > 25 and < 240)
            .ToList();

        if (needles.Length > 0)
        {
            sentences = sentences
                .Where(s => needles.Any(n => s.Contains(n, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        return sentences
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    private static IReadOnlyList<string> BuildPracticeIdeas(IReadOnlyList<string> examples, IReadOnlyList<string> mistakes)
    {
        var ideas = new List<string>();
        if (examples.Count > 0)
            ideas.Add("Turn the clearest example into a micro exercise with one answer check.");
        if (mistakes.Count > 0)
            ideas.Add("Ask a misconception-focused question before moving to the next step.");
        ideas.Add("Close the explanation with one short retrieval question.");
        return ideas.Take(4).ToList();
    }

    private static IReadOnlyList<MisconceptionSignal> BuildMisconceptionSignals(string question, string learningSignalContext)
    {
        var signals = new List<MisconceptionSignal>();
        var combined = $"{question}\n{learningSignalContext}";

        if (ContainsAny(combined, "anlamad", "karist", "zorland", "bilmiyorum", "confus", "stuck"))
        {
            signals.Add(new MisconceptionSignal(
                "unknown-or-active-confusion",
                "current-topic",
                "Student wording or learning summary indicates confusion.",
                "Return to the smallest missing step, give one concrete example, then ask a micro check question."));
        }

        return signals;
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string BuildRecommendation(
        string notebookContext,
        string wikiContext,
        string learningSignalContext,
        TeachingReference? teachingReference)
    {
        if (!string.IsNullOrWhiteSpace(notebookContext))
            return "Ground factual claims in user documents first and cite exact pages.";
        if (!string.IsNullOrWhiteSpace(wikiContext))
            return "Use Wiki as the main factual base and separate any outside knowledge.";
        if (!string.IsNullOrWhiteSpace(learningSignalContext))
            return "Adapt the explanation to weak skills before introducing new material.";
        if (teachingReference != null)
            return "Use the YouTube reference as teaching style guidance only.";
        return "Teach from first principles and state uncertainty when no source exists.";
    }

    private static string BuildPromptBlock(
        IReadOnlyList<SourceUsage> sources,
        TeachingReference? teachingReference,
        IReadOnlyList<MisconceptionSignal> misconceptions,
        EducatorQualityScore quality)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("[P6 EDUCATOR CORE - SOURCE PRIORITY AND TEACHING CONTRACT]");
        sb.AppendLine("Source priority: 1) user documents, 2) Wiki, 3) LearningSignal personalization, 4) Korteks/web, 5) YouTube pedagogy reference.");
        sb.AppendLine("Do not treat YouTube as a factual source by default. Use it for teaching flow, examples, analogies, and common mistakes.");
        sb.AppendLine("If a claim comes from a user document, include the exact [doc:sourceId:pN] tag on the sentence.");
        sb.AppendLine("If the answer is not supported by documents or Wiki, say that clearly before using general reasoning.");
        sb.AppendLine($"Quality recommendation: {quality.Recommendation}");

        if (sources.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Available teaching inputs:");
            foreach (var source in sources.OrderBy(s => s.Priority))
                sb.AppendLine($"- P{source.Priority} {source.Kind}: {source.Label}. Rule: {source.CitationRule}");
        }

        if (teachingReference != null)
        {
            sb.AppendLine();
            sb.AppendLine("[YOUTUBE TEACHING REFERENCE - PEDAGOGY ONLY]");
            sb.AppendLine($"Status: {teachingReference.Status}; Source: [youtube:{teachingReference.SourceId}]");
            sb.AppendLine($"Teaching flow: {teachingReference.TeachingFlow}");
            AppendList(sb, "Useful examples", teachingReference.Examples);
            AppendList(sb, "Analogies", teachingReference.Analogies);
            AppendList(sb, "Common mistakes", teachingReference.CommonMistakes);
            AppendList(sb, "Practice ideas", teachingReference.PracticeIdeas);
        }

        if (misconceptions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[ACTIVE MISCONCEPTION SIGNALS]");
            foreach (var signal in misconceptions)
                sb.AppendLine($"- {signal.SkillTag}: {signal.SuggestedTeachingMove}");
        }

        return sb.ToString();
    }

    private static void AppendList(StringBuilder sb, string label, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return;
        sb.AppendLine($"{label}:");
        foreach (var item in items.Take(4))
            sb.AppendLine($"- {item}");
    }

    private async Task SafeRecordSignalAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string signalType,
        string? skillTag = null,
        string? topicPath = null,
        int? score = null,
        bool? isPositive = null,
        string? payloadJson = null,
        CancellationToken ct = default)
    {
        try
        {
            await _signals.RecordSignalAsync(userId, topicId, sessionId, signalType, skillTag, topicPath, score, isPositive, payloadJson, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[EducatorCore] LearningSignal write skipped. Type={SignalType}", signalType);
        }
    }

    private static string TeachingReferenceKey(Guid topicId) => $"orka:v1:teacher-reference:{topicId}";
}

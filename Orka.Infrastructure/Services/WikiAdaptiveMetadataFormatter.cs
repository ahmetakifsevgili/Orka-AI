using System.Text;
using System.Text.Json;
using Orka.Core.DTOs;
using Orka.Core.Entities;

namespace Orka.Infrastructure.Services;

public static class WikiAdaptiveMetadataFormatter
{
    public static string BuildAdaptiveBlock(
        (double score, string weaknesses)? studentProfile,
        IEnumerable<QuizAttempt> failedAttempts,
        IEnumerable<LearningSignal> signals,
        IEnumerable<StudyRecommendationDto> recommendations,
        IEnumerable<RemediationPlan> remediationPlans)
    {
        var parts = new List<string>();

        if (studentProfile.HasValue)
        {
            parts.Add($"- Understanding score: {studentProfile.Value.score}/10");
            if (!string.IsNullOrWhiteSpace(studentProfile.Value.weaknesses))
                parts.Add($"- Redis weakness notes: {Trim(studentProfile.Value.weaknesses, 180)}");
        }

        var failed = failedAttempts.Take(5).ToList();
        if (failed.Count > 0)
        {
            parts.Add("- Recent failed quiz attempts:");
            parts.AddRange(failed.Select(a =>
            {
                var mistake = ExtractMistakeCategory(a.SourceRefsJson);
                var skill = FirstNonBlank(a.SkillTag, a.TopicPath, "unknown skill");
                var objective = FirstNonBlank(a.CognitiveType, a.Difficulty, "unspecified objective");
                return $"  - Skill={Trim(skill, 80)}; Objective={Trim(objective, 80)}; Mistake={Trim(mistake, 80)}; Question={Trim(a.Question, 140)}";
            }));
        }

        var recentSignals = signals.Take(8).ToList();
        if (recentSignals.Count > 0)
        {
            parts.Add("- Recent learning signals:");
            parts.AddRange(recentSignals.Select(s =>
                $"  - {Trim(s.SignalType, 60)}; Skill={Trim(s.SkillTag ?? s.TopicPath, 90)}; Score={s.Score?.ToString() ?? "n/a"}; Positive={s.IsPositive?.ToString() ?? "n/a"}"));
        }

        var activeRecommendations = recommendations.Where(r => !r.IsDone).Take(5).ToList();
        if (activeRecommendations.Count > 0)
        {
            parts.Add("- Open study recommendations:");
            parts.AddRange(activeRecommendations.Select(r =>
                $"  - {Trim(r.Title, 100)}: {Trim(r.Reason, 140)}"));
        }

        var pendingRemediation = remediationPlans
            .Where(r => !string.Equals(r.Status, "completed", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();
        if (pendingRemediation.Count > 0)
        {
            parts.Add("- Pending remediation plans:");
            parts.AddRange(pendingRemediation.Select(r =>
                $"  - Skill={Trim(r.SkillTag, 100)}; Status={Trim(r.Status, 40)}"));
        }

        if (parts.Count == 0) return string.Empty;

        return $"""

            [ADAPTIVE_WIKI_METADATA]
            {string.Join("\n", parts)}

            WIKI ADAPTATION RULES:
            - Include what the student appears to understand and what remains weak.
            - Repair misconceptions explicitly, but do not overfit to a single wrong answer.
            - Add short review/practice suggestions when recommendations or remediation plans exist.
            - Treat learning signals as personalization, not factual proof.
            """;
    }

    public static string BuildSourceBlock(IEnumerable<SourceChunk> chunks)
    {
        var items = chunks.Take(8).ToList();
        if (items.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("[SOURCE_BACKED_WIKI_CONTEXT]");
        sb.AppendLine("Use these document chunks only for source-backed claims. Preserve the exact [doc:sourceId:pN] tag on each factual sentence that uses a chunk.");
        foreach (var chunk in items)
        {
            sb.AppendLine($"- [doc:{chunk.LearningSourceId}:p{chunk.PageNumber}] {Trim(chunk.Text, 500)}");
        }

        return sb.ToString();
    }

    public static string EnsureSourceTagsPresent(string wikiMarkdown, string sourceBlock)
    {
        if (string.IsNullOrWhiteSpace(sourceBlock)) return wikiMarkdown;

        var tags = ExtractDocTags(sourceBlock).ToList();
        if (tags.Count == 0) return wikiMarkdown;

        var missing = tags
            .Where(tag => !wikiMarkdown.Contains(tag, StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();
        if (missing.Count == 0) return wikiMarkdown;

        return $"""
            {wikiMarkdown.TrimEnd()}

            ## Kaynaklı Notlar
            Bu wiki oluşturulurken kullanılan belge etiketleri: {string.Join(" ", missing)}
            """;
    }

    public static string ExtractMistakeCategory(string? sourceRefsJson)
    {
        if (string.IsNullOrWhiteSpace(sourceRefsJson)) return "not recorded";

        try
        {
            using var doc = JsonDocument.Parse(sourceRefsJson);
            if (TryRead(doc.RootElement, "mistakeCategory", out var value)) return value;
            if (TryRead(doc.RootElement, "MistakeCategory", out value)) return value;
            if (TryRead(doc.RootElement, "mistake", out value)) return value;
        }
        catch (JsonException)
        {
            return "not recorded";
        }

        return "not recorded";
    }

    private static bool TryRead(JsonElement root, string property, out string value)
    {
        value = string.Empty;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(property, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static IEnumerable<string> ExtractDocTags(string text)
    {
        const string prefix = "[doc:";
        var index = 0;
        while (index < text.Length)
        {
            var start = text.IndexOf(prefix, index, StringComparison.OrdinalIgnoreCase);
            if (start < 0) yield break;
            var end = text.IndexOf(']', start);
            if (end < 0) yield break;

            yield return text[start..(end + 1)];
            index = end + 1;
        }
    }

    private static string Trim(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value)) return "not recorded";
        var trimmed = value.Trim().Replace("\r", " ").Replace("\n", " ");
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }
}

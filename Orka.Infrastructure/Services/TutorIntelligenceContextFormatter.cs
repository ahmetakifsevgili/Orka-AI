using System.Text;
using System.Text.Json;
using Orka.Core.DTOs;
using Orka.Core.Entities;

namespace Orka.Infrastructure.Services;

public static class TutorIntelligenceContextFormatter
{
    public static string BuildPlanIntentHint(string? planIntent, string? category = null)
    {
        var intentName = !string.IsNullOrWhiteSpace(planIntent)
            ? planIntent.StartsWith("Plan:", StringComparison.OrdinalIgnoreCase)
                ? planIntent.Split(':', 2)[1].Trim()
                : planIntent.Trim()
            : !string.IsNullOrWhiteSpace(category) && category.StartsWith("Plan:", StringComparison.OrdinalIgnoreCase)
                ? category.Split(':', 2)[1].Trim()
                : null;

        if (string.IsNullOrWhiteSpace(intentName))
        {
            return string.Empty;
        }

        var mode = intentName.Equals("DeepDive", StringComparison.OrdinalIgnoreCase) ? "deeper conceptual explanation"
            : intentName.Equals("PracticeLab", StringComparison.OrdinalIgnoreCase) ? "step-by-step practice and runnable tasks"
            : intentName.Equals("QuickReview", StringComparison.OrdinalIgnoreCase) ? "retrieval practice and concise review"
            : intentName.Equals("Remediation", StringComparison.OrdinalIgnoreCase) ? "misconception repair before new material"
            : intentName.Equals("Assessment", StringComparison.OrdinalIgnoreCase) ? "check understanding before advancing"
            : intentName.Equals("Core", StringComparison.OrdinalIgnoreCase) ? "core explanation with one quick check"
            : "normal structured tutoring";

        return $"""

                [PLAN_INTENT_TEACHING_MODE]
                - Active topic planIntent: {intentName}
                - Backward category: {category ?? "(none)"}
                - Teaching mode: {mode}
                - Use this as style guidance only; do not change facts or invent missing learner signals.
                """;
    }

    public static string BuildFailedAttemptSummary(IEnumerable<QuizAttempt> attempts)
    {
        var items = attempts.Take(5).ToList();
        if (items.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("[QUIZ-DERIVED PERSONALIZATION - RECENT WRONG ANSWERS]");
        foreach (var attempt in items)
        {
            var skill = FirstNonBlank(attempt.SkillTag, attempt.TopicPath, "unknown skill");
            var mistake = ExtractMistakeCategory(attempt.SourceRefsJson);
            var cognitive = FirstNonBlank(attempt.CognitiveType, attempt.Difficulty, "unspecified");
            var topicPath = FirstNonBlank(attempt.TopicPath, attempt.Topic?.Title, "current topic");

            sb.AppendLine($"- Skill: {Trim(skill, 90)}; TopicPath: {Trim(topicPath, 120)}; Type: {Trim(cognitive, 60)}; Mistake: {Trim(mistake, 80)}");
            sb.AppendLine($"  Question: {Trim(attempt.Question, 180)}");
            if (!string.IsNullOrWhiteSpace(attempt.UserAnswer))
                sb.AppendLine($"  StudentAnswer: {Trim(attempt.UserAnswer, 120)}");
            if (!string.IsNullOrWhiteSpace(attempt.Explanation))
                sb.AppendLine($"  ExpectedRemediation: {Trim(attempt.Explanation, 180)}");
        }

        sb.AppendLine("[ACTION]: Start by repairing these weak concepts, then continue with a micro example and one check question.");
        return sb.ToString();
    }

    public static string BuildReviewPressureSummary(
        IEnumerable<StudyRecommendationDto> recommendations,
        IEnumerable<RemediationPlan> remediationPlans)
    {
        var activeRecommendations = recommendations
            .Where(r => !r.IsDone)
            .Take(5)
            .ToList();
        var pendingRemediation = remediationPlans
            .Where(r => !string.Equals(r.Status, "completed", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.CreatedAt)
            .Take(3)
            .ToList();

        if (activeRecommendations.Count == 0 && pendingRemediation.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("[REVIEW_AND_REMEDIATION_PRESSURE]");
        if (activeRecommendations.Count > 0)
        {
            sb.AppendLine("Open study recommendations:");
            foreach (var item in activeRecommendations)
            {
                sb.AppendLine($"- {Trim(item.Title, 100)} ({Trim(item.RecommendationType, 40)}): {Trim(item.Reason, 140)}");
                if (!string.IsNullOrWhiteSpace(item.ActionPrompt))
                    sb.AppendLine($"  Suggested move: {Trim(item.ActionPrompt, 160)}");
            }
        }

        if (pendingRemediation.Count > 0)
        {
            sb.AppendLine("Pending remediation plans:");
            foreach (var plan in pendingRemediation)
                sb.AppendLine($"- {Trim(plan.SkillTag, 100)}; Status={Trim(plan.Status, 40)}; Created={plan.CreatedAt:O}");
        }

        sb.AppendLine("[ACTION]: Use this only when the user asks what to review/practice next or when the current answer naturally touches the weak skill.");
        return sb.ToString();
    }

    public static string ExtractMistakeCategory(string? sourceRefsJson)
    {
        if (string.IsNullOrWhiteSpace(sourceRefsJson)) return "not recorded";

        try
        {
            using var doc = JsonDocument.Parse(sourceRefsJson);
            if (TryGetString(doc.RootElement, "mistakeCategory", out var category))
                return category;
            if (TryGetString(doc.RootElement, "MistakeCategory", out category))
                return category;
            if (TryGetString(doc.RootElement, "mistake", out category))
                return category;
        }
        catch (JsonException)
        {
            // SourceRefsJson is best-effort telemetry; never break Tutor context.
        }

        return "not recorded";
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string Trim(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value)) return "not recorded";
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }
}

using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs.Korteks;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class KorteksSynthesisService : IKorteksSynthesisService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly OrkaDbContext _db;
    private readonly IPlanResearchCompressor _compressor;
    private readonly ILogger<KorteksSynthesisService> _logger;

    public KorteksSynthesisService(
        OrkaDbContext db,
        IPlanResearchCompressor compressor,
        ILogger<KorteksSynthesisService> logger)
    {
        _db = db;
        _compressor = compressor;
        _logger = logger;
    }

    public async Task<KorteksResearchWorkflowDto> BuildAndSaveAsync(
        Guid userId,
        KorteksResearchResultDto researchResult,
        KorteksResearchSynthesisContextDto? context = null,
        CancellationToken ct = default)
    {
        context ??= new KorteksResearchSynthesisContextDto();
        await EnsureScopeAsync(userId, context.TopicId ?? researchResult.TopicId, context.SessionId, ct);

        var compressed = _compressor.Compress(researchResult);
        var sourceConfidence = DetermineSourceConfidence(researchResult.GroundingMode, compressed.SourceCount);
        var evidence = BuildEvidenceSummary(researchResult, compressed.SourceCount, sourceConfidence);
        var synthesis = BuildSynthesis(researchResult, compressed, sourceConfidence);
        var issues = BuildSafetyIssues(researchResult, compressed, evidence).ToArray();
        var contexts = BuildConsumerContexts(researchResult.Topic, synthesis, compressed, evidence, issues);
        var promptBlock = BuildWorkflowPromptBlock(contexts, evidence, issues);
        var now = DateTime.UtcNow;

        var entity = new KorteksResearchWorkflow
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = context.TopicId ?? researchResult.TopicId,
            SessionId = context.SessionId,
            PlanRequestId = context.PlanRequestId,
            ActiveLessonSnapshotId = context.ActiveLessonSnapshotId,
            StudentContextSnapshotId = context.StudentContextSnapshotId,
            Topic = Clean(researchResult.Topic, 256),
            ApprovedIntent = Clean(context.ApprovedIntent, 256),
            ApprovedMainTopic = Clean(context.ApprovedMainTopic, 256),
            ApprovedFocusArea = Clean(context.ApprovedFocusArea, 256),
            ApprovedStudyGoal = Clean(context.ApprovedStudyGoal, 256),
            Status = DetermineStatus(researchResult, evidence),
            WorkflowVersion = "korteks_synthesis_v1",
            GroundingMode = researchResult.GroundingMode.ToString(),
            SourceConfidence = sourceConfidence,
            SourceCount = compressed.SourceCount,
            ToolCallCount = researchResult.ProviderCalls.Count(c => c.Invoked),
            CanGroundTutorClaims = evidence.HasUrlBackedEvidence && !researchResult.IsFallback,
            EvidenceSummaryJson = JsonSerializer.Serialize(evidence, JsonOptions),
            SynthesisJson = JsonSerializer.Serialize(synthesis, JsonOptions),
            PlanContextJson = JsonSerializer.Serialize(contexts.Plan, JsonOptions),
            QuizContextJson = JsonSerializer.Serialize(contexts.Quiz, JsonOptions),
            TutorContextJson = JsonSerializer.Serialize(contexts.Tutor, JsonOptions),
            WikiContextJson = JsonSerializer.Serialize(contexts.Wiki, JsonOptions),
            SafetyIssuesJson = JsonSerializer.Serialize(issues, JsonOptions),
            PromptBlock = promptBlock,
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = now
        };

        _db.KorteksResearchWorkflows.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[KorteksSynthesis] Workflow saved. WorkflowRef={WorkflowRef} TopicRef={TopicRef} Mode={GroundingMode} Sources={SourceCount}",
            LogPrivacyGuard.SafeId(entity.Id, "workflow"),
            LogPrivacyGuard.SafeId(entity.TopicId, "topic"),
            entity.GroundingMode,
            entity.SourceCount);

        return ToDto(entity);
    }

    public async Task<KorteksResearchWorkflowDto?> GetWorkflowAsync(
        Guid userId,
        Guid workflowId,
        CancellationToken ct = default)
    {
        var entity = await _db.KorteksResearchWorkflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowId && w.UserId == userId && !w.IsDeleted, ct);

        return entity == null ? null : ToDto(entity);
    }

    public async Task<KorteksResearchWorkflowDto?> GetLatestWorkflowAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var query = _db.KorteksResearchWorkflows
            .AsNoTracking()
            .Where(w => w.UserId == userId && !w.IsDeleted);

        if (topicId.HasValue)
        {
            query = query.Where(w => w.TopicId == topicId.Value);
        }

        if (sessionId.HasValue)
        {
            query = query.Where(w => w.SessionId == sessionId.Value);
        }

        var entity = await query
            .OrderByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return entity == null ? null : ToDto(entity);
    }

    private async Task EnsureScopeAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
        if (topicId.HasValue)
        {
            var topicExists = await _db.Topics
                .AsNoTracking()
                .AnyAsync(t => t.Id == topicId.Value && t.UserId == userId && !t.IsArchived, ct);
            if (!topicExists)
            {
                throw new InvalidOperationException("Topic not found for Korteks synthesis.");
            }
        }

        if (sessionId.HasValue)
        {
            var sessionExists = await _db.Sessions
                .AsNoTracking()
                .AnyAsync(s => s.Id == sessionId.Value && s.UserId == userId, ct);
            if (!sessionExists)
            {
                throw new InvalidOperationException("Session not found for Korteks synthesis.");
            }
        }
    }

    private static KorteksEvidenceSummaryDto BuildEvidenceSummary(
        KorteksResearchResultDto result,
        int acceptedSourceCount,
        string sourceConfidence)
    {
        var successCount = result.ProviderCalls.Count(c => c.Invoked && c.Success);
        var failedCount = result.ProviderCalls.Count(c => c.Invoked && !c.Success) + result.ProviderFailures.Count;
        var hasUrlEvidence = acceptedSourceCount > 0 &&
                             result.GroundingMode is GroundingMode.SourceGrounded or GroundingMode.PartialSourceGrounded;

        return new KorteksEvidenceSummaryDto
        {
            GroundingStatus = hasUrlEvidence ? "source_aware" : "evidence_insufficient",
            SourceConfidence = sourceConfidence,
            SourceCount = acceptedSourceCount,
            SuccessfulToolCallCount = successCount,
            FailedToolCallCount = failedCount,
            HasUrlBackedEvidence = hasUrlEvidence,
            IsFallback = result.IsFallback
        };
    }

    private static KorteksResearchSynthesisDto BuildSynthesis(
        KorteksResearchResultDto result,
        CompressedPlanResearchContextDto compressed,
        string sourceConfidence)
    {
        var route = compressed.CurriculumMapHints.Count > 0
            ? compressed.CurriculumMapHints
            : compressed.KeyFacts.Take(6).ToList();

        var practice = route
            .Concat(compressed.PrerequisiteHints)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => $"Practice checkpoint: {v}")
            .Take(8)
            .ToList();

        var quizScope = compressed.PrerequisiteHints
            .Concat(compressed.LikelyMisconceptions)
            .Concat(route)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        return new KorteksResearchSynthesisDto
        {
            Topic = Clean(result.Topic, 256),
            SourceConfidence = sourceConfidence,
            KeyFacts = ToItems(compressed.KeyFacts, "fact", sourceConfidence),
            LearningRoute = ToItems(route, "learning_route", sourceConfidence),
            Prerequisites = ToItems(compressed.PrerequisiteHints, "prerequisite", sourceConfidence),
            Misconceptions = ToItems(compressed.LikelyMisconceptions, "misconception", sourceConfidence),
            PracticeOrder = ToItems(practice, "practice", sourceConfidence),
            QuizScope = ToItems(quizScope, "quiz_scope", sourceConfidence),
            TutorTeachingHints = ToItems(BuildTutorHints(compressed), "tutor_hint", sourceConfidence),
            WikiNotebookSeeds = ToItems(BuildWikiSeeds(compressed), "wiki_seed", sourceConfidence),
            Sources = compressed.TopSources.Take(8).ToArray(),
            ProviderWarnings = compressed.ProviderWarnings.Take(8).ToArray(),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static KorteksConsumerContextsDto BuildConsumerContexts(
        string topic,
        KorteksResearchSynthesisDto synthesis,
        CompressedPlanResearchContextDto compressed,
        KorteksEvidenceSummaryDto evidence,
        IReadOnlyCollection<KorteksSynthesisIssueDto> issues)
    {
        var baseMustNot = new[]
        {
            "Do not use raw provider prose as final teaching truth.",
            "Do not copy source/video titles as lesson titles.",
            "Do not present unsourced or fallback synthesis as verified evidence."
        };

        var planBrief = PlanIntelligenceBriefBuilder.BuildForPlan(topic, BuildCompressedPromptBlock(compressed, evidence, issues));
        var quizBrief = PlanIntelligenceBriefBuilder.BuildForDiagnosticQuiz(topic, BuildCompressedPromptBlock(compressed, evidence, issues));

        return new KorteksConsumerContextsDto
        {
            Plan = new KorteksConsumerContextDto
            {
                Consumer = "plan",
                UsagePolicy = "advisory_research_support",
                PromptBlock = planBrief,
                MustUse = ["Prefer concept graph, diagnostic profile, and active lesson state over research snippets."],
                MayUse = synthesis.LearningRoute.Select(i => i.Text).Take(8).ToArray(),
                MustNotUse = baseMustNot
            },
            Quiz = new KorteksConsumerContextDto
            {
                Consumer = "quiz",
                UsagePolicy = "scope_only_no_answer_leakage",
                PromptBlock = quizBrief,
                MustUse = ["Use this only for concept and misconception scope; quiz items must measure learner concepts."],
                MayUse = synthesis.QuizScope.Select(i => i.Text).Take(10).ToArray(),
                MustNotUse = baseMustNot.Concat(["Do not put Orka product labels, source names, or URLs into answer options."]).ToArray()
            },
            Tutor = new KorteksConsumerContextDto
            {
                Consumer = "tutor",
                UsagePolicy = evidence.HasUrlBackedEvidence ? "source_aware_teaching" : "fallback_caution_teaching",
                PromptBlock = BuildTutorPromptBlock(synthesis, evidence, issues),
                MustUse = ["Teach through the active concept and learner state; cite only URL-backed evidence."],
                MayUse = synthesis.TutorTeachingHints.Select(i => i.Text).Take(8).ToArray(),
                MustNotUse = baseMustNot
            },
            Wiki = new KorteksConsumerContextDto
            {
                Consumer = "wiki",
                UsagePolicy = "notebook_seed_not_auto_generation",
                PromptBlock = BuildWikiPromptBlock(synthesis, evidence, issues),
                MustUse = ["Treat these as notebook organization seeds, not as finished Wiki pages."],
                MayUse = synthesis.WikiNotebookSeeds.Select(i => i.Text).Take(8).ToArray(),
                MustNotUse = baseMustNot.Concat(["Do not create citations for sources that are not in the accepted source list."]).ToArray()
            }
        };
    }

    private static string BuildWorkflowPromptBlock(
        KorteksConsumerContextsDto contexts,
        KorteksEvidenceSummaryDto evidence,
        IReadOnlyCollection<KorteksSynthesisIssueDto> issues)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[KORTEKS RESEARCH SYNTHESIS CONTRACT v1]");
        sb.AppendLine($"GroundingStatus: {evidence.GroundingStatus}");
        sb.AppendLine($"SourceConfidence: {evidence.SourceConfidence}");
        sb.AppendLine($"SourceCount: {evidence.SourceCount}");
        sb.AppendLine($"SuccessfulToolCallCount: {evidence.SuccessfulToolCallCount}");
        if (issues.Count > 0)
        {
            sb.AppendLine("SafetyIssues:");
            foreach (var issue in issues.Take(8))
            {
                sb.AppendLine($"- {issue.Code}: {issue.UserSafeMessage}");
            }
        }

        sb.AppendLine(contexts.Plan.PromptBlock);
        return Trim(sb.ToString(), 7000);
    }

    private static string BuildCompressedPromptBlock(
        CompressedPlanResearchContextDto compressed,
        KorteksEvidenceSummaryDto evidence,
        IReadOnlyCollection<KorteksSynthesisIssueDto> issues)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]");
        sb.AppendLine($"Topic: {compressed.Topic}");
        sb.AppendLine($"GroundingMode: {compressed.GroundingMode}");
        sb.AppendLine($"SourceCount: {compressed.SourceCount}");
        sb.AppendLine($"SourceConfidence: {evidence.SourceConfidence}");
        sb.AppendLine($"KorteksSynthesisStatus: {evidence.GroundingStatus}");
        if (!string.IsNullOrWhiteSpace(compressed.FallbackWarning))
        {
            sb.AppendLine($"FallbackWarning: {compressed.FallbackWarning}");
        }

        AppendSection(sb, "TopSources", compressed.TopSources.Select(s => $"{s.Provider}: {s.Title} ({s.Url})"));
        AppendSection(sb, "KeyFacts", compressed.KeyFacts);
        AppendSection(sb, "WebFreshnessFacts", compressed.WebFreshnessFacts);
        AppendSection(sb, "YouTubeLearningReferences", compressed.YouTubeLearningReferences);
        AppendSection(sb, "CurriculumMapHints", compressed.CurriculumMapHints);
        AppendSection(sb, "PrerequisiteHints", compressed.PrerequisiteHints);
        AppendSection(sb, "LikelyMisconceptions", compressed.LikelyMisconceptions);
        AppendSection(sb, "KorteksSynthesisIssues", issues.Select(i => $"{i.Code}: {i.UserSafeMessage}"));
        sb.AppendLine("Instruction: Use this Korteks synthesis only as bounded research support. Active lesson state, diagnostic evidence, and source/citation rules have priority.");
        return Trim(sb.ToString(), 5200);
    }

    private static string BuildTutorPromptBlock(
        KorteksResearchSynthesisDto synthesis,
        KorteksEvidenceSummaryDto evidence,
        IReadOnlyCollection<KorteksSynthesisIssueDto> issues)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[KORTEKS SYNTHESIS FOR TUTOR]");
        sb.AppendLine($"Topic: {synthesis.Topic}");
        sb.AppendLine($"GroundingStatus: {evidence.GroundingStatus}");
        sb.AppendLine($"SourceConfidence: {evidence.SourceConfidence}");
        AppendSection(sb, "TeachingHints", synthesis.TutorTeachingHints.Select(i => i.Text));
        AppendSection(sb, "MisconceptionHints", synthesis.Misconceptions.Select(i => i.Text));
        AppendSection(sb, "AcceptedSources", synthesis.Sources.Select(s => $"{s.Title} ({s.Url})"));
        AppendSection(sb, "SafetyCautions", issues.Select(i => i.UserSafeMessage));
        sb.AppendLine("Rule: Cite only accepted URL-backed sources. If evidence is insufficient, say so and teach conservatively.");
        return Trim(sb.ToString(), 4000);
    }

    private static string BuildWikiPromptBlock(
        KorteksResearchSynthesisDto synthesis,
        KorteksEvidenceSummaryDto evidence,
        IReadOnlyCollection<KorteksSynthesisIssueDto> issues)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[KORTEKS SYNTHESIS FOR WIKI NOTEBOOK]");
        sb.AppendLine($"Topic: {synthesis.Topic}");
        sb.AppendLine($"GroundingStatus: {evidence.GroundingStatus}");
        AppendSection(sb, "NotebookSeeds", synthesis.WikiNotebookSeeds.Select(i => i.Text));
        AppendSection(sb, "SourceAnchors", synthesis.Sources.Select(s => $"{s.Title} ({s.Url})"));
        AppendSection(sb, "Cautions", issues.Select(i => i.UserSafeMessage));
        sb.AppendLine("Rule: This is a notebook organization seed, not final generated Wiki content.");
        return Trim(sb.ToString(), 3000);
    }

    private static IEnumerable<KorteksSynthesisIssueDto> BuildSafetyIssues(
        KorteksResearchResultDto result,
        CompressedPlanResearchContextDto compressed,
        KorteksEvidenceSummaryDto evidence)
    {
        if (!evidence.HasUrlBackedEvidence)
        {
            yield return new KorteksSynthesisIssueDto
            {
                Code = "evidence_insufficient",
                Severity = "warning",
                UserSafeMessage = "Korteks did not produce enough URL-backed source evidence; downstream consumers must avoid source-grounded claims."
            };
        }

        if (result.IsFallback)
        {
            yield return new KorteksSynthesisIssueDto
            {
                Code = "fallback_research",
                Severity = "warning",
                UserSafeMessage = "Research used fallback/internal knowledge and should be treated as advisory only."
            };
        }

        foreach (var warning in compressed.ProviderWarnings.Take(5))
        {
            yield return new KorteksSynthesisIssueDto
            {
                Code = "provider_warning",
                Severity = "info",
                UserSafeMessage = Clean(warning, 220)
            };
        }
    }

    private static IReadOnlyList<KorteksSynthesisItemDto> ToItems(
        IEnumerable<string> values,
        string kind,
        string confidence) =>
        values
            .Select(v => Clean(v, 220))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(v => new KorteksSynthesisItemDto
            {
                Kind = kind,
                Text = v,
                Confidence = confidence,
                EvidenceBasis = confidence is "high" or "medium" ? "source_aware_korteks" : "bounded_korteks_synthesis"
            })
            .ToArray();

    private static IEnumerable<string> BuildTutorHints(CompressedPlanResearchContextDto compressed)
    {
        foreach (var prerequisite in compressed.PrerequisiteHints.Take(4))
        {
            yield return $"Check prerequisite before explanation: {prerequisite}";
        }

        foreach (var misconception in compressed.LikelyMisconceptions.Take(4))
        {
            yield return $"Watch for misconception: {misconception}";
        }

        if (!string.IsNullOrWhiteSpace(compressed.FallbackWarning))
        {
            yield return compressed.FallbackWarning;
        }
    }

    private static IEnumerable<string> BuildWikiSeeds(CompressedPlanResearchContextDto compressed)
    {
        foreach (var route in compressed.CurriculumMapHints.Take(5))
        {
            yield return $"Notebook section candidate: {route}";
        }

        foreach (var fact in compressed.KeyFacts.Take(4))
        {
            yield return $"Evidence note candidate: {fact}";
        }
    }

    private static string DetermineStatus(KorteksResearchResultDto result, KorteksEvidenceSummaryDto evidence)
    {
        if (result.GroundingMode == GroundingMode.BlockedProvider)
        {
            return "blocked";
        }

        return evidence.HasUrlBackedEvidence ? "completed" : "degraded";
    }

    private static string DetermineSourceConfidence(GroundingMode mode, int sourceCount)
    {
        if (mode == GroundingMode.SourceGrounded && sourceCount >= 3)
        {
            return "high";
        }

        if (mode is GroundingMode.SourceGrounded or GroundingMode.PartialSourceGrounded && sourceCount > 0)
        {
            return "medium";
        }

        return "low";
    }

    private static void AppendSection(StringBuilder sb, string title, IEnumerable<string> items)
    {
        var list = items
            .Select(v => Clean(v, 220))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        if (list.Length == 0)
        {
            return;
        }

        sb.AppendLine($"{title}:");
        foreach (var item in list)
        {
            sb.AppendLine($"- {item}");
        }
    }

    private static KorteksResearchWorkflowDto ToDto(KorteksResearchWorkflow entity)
    {
        var evidence = Parse(entity.EvidenceSummaryJson, new KorteksEvidenceSummaryDto());
        var synthesis = Parse(entity.SynthesisJson, new KorteksResearchSynthesisDto());
        var plan = Parse(entity.PlanContextJson, new KorteksConsumerContextDto { Consumer = "plan" });
        var quiz = Parse(entity.QuizContextJson, new KorteksConsumerContextDto { Consumer = "quiz" });
        var tutor = Parse(entity.TutorContextJson, new KorteksConsumerContextDto { Consumer = "tutor" });
        var wiki = Parse(entity.WikiContextJson, new KorteksConsumerContextDto { Consumer = "wiki" });
        var issues = Parse<IReadOnlyList<KorteksSynthesisIssueDto>>(entity.SafetyIssuesJson, Array.Empty<KorteksSynthesisIssueDto>());

        return new KorteksResearchWorkflowDto
        {
            Id = entity.Id,
            TopicId = entity.TopicId,
            SessionId = entity.SessionId,
            PlanRequestId = entity.PlanRequestId,
            ActiveLessonSnapshotId = entity.ActiveLessonSnapshotId,
            StudentContextSnapshotId = entity.StudentContextSnapshotId,
            Topic = entity.Topic,
            ApprovedIntent = entity.ApprovedIntent,
            ApprovedMainTopic = entity.ApprovedMainTopic,
            ApprovedFocusArea = entity.ApprovedFocusArea,
            ApprovedStudyGoal = entity.ApprovedStudyGoal,
            Status = entity.Status,
            WorkflowVersion = entity.WorkflowVersion,
            GroundingMode = Enum.TryParse<GroundingMode>(entity.GroundingMode, out var mode) ? mode : GroundingMode.FallbackInternalKnowledge,
            SourceConfidence = entity.SourceConfidence,
            SourceCount = entity.SourceCount,
            ToolCallCount = entity.ToolCallCount,
            CanGroundTutorClaims = entity.CanGroundTutorClaims,
            EvidenceSummary = evidence,
            Synthesis = synthesis,
            ConsumerContexts = new KorteksConsumerContextsDto
            {
                Plan = plan,
                Quiz = quiz,
                Tutor = tutor,
                Wiki = wiki
            },
            SafetyIssues = issues,
            PromptBlock = entity.PromptBlock,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            CompletedAt = entity.CompletedAt
        };
    }

    private static T Parse<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string Clean(string? value, int max)
    {
        var normalized = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= max)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, max - 3)] + "...";
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..Math.Max(0, max - 40)] + "\n[truncated]";
}

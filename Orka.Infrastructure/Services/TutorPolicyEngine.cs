using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Chat;
using Orka.Core.Interfaces;
using Orka.Core.Services;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class TutorPolicyEngine : ITutorPolicyEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IConceptMasteryService _mastery;
    private readonly ITutorPolicyTraceService? _trace;
    private readonly ILogger<TutorPolicyEngine> _logger;

    public TutorPolicyEngine(
        OrkaDbContext db,
        IConceptMasteryService mastery,
        ILogger<TutorPolicyEngine> logger,
        ITutorPolicyTraceService? trace = null)
    {
        _db = db;
        _mastery = mastery;
        _trace = trace;
        _logger = logger;
    }

    public async Task<TutorPolicyContextDto> BuildAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        string notebookContext,
        string wikiContext,
        string learningSignalContext,
        CancellationToken ct = default)
    {
        try
        {
            var snapshot = topicId.HasValue
                ? await _db.ConceptGraphSnapshots.AsNoTracking()
                    .Where(s => s.UserId == userId && s.TopicId == topicId.Value)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync(ct)
                : null;
            var graph = snapshot == null ? null : SafeGraph(snapshot.GraphJson);
            var masteries = await _mastery.GetRecentMasteryAsync(userId, topicId, 12, ct);
            var weakMastery = masteries
                .OrderBy(m => m.MasteryScore)
                .ThenByDescending(m => m.Attempts)
                .FirstOrDefault();
            var activeConcept = weakMastery?.ConceptKey ??
                                graph?.Concepts.OrderBy(c => c.Order).FirstOrDefault()?.StableKey ??
                                string.Empty;
            var activeLabel = weakMastery?.Label ??
                              graph?.Concepts.FirstOrDefault(c => c.StableKey == activeConcept)?.Label ??
                              activeConcept;
            var learnerState = weakMastery == null
                ? "no concept mastery yet"
                : $"{weakMastery.PracticeReadiness}; mastery={weakMastery.MasteryScore:0}; confidence={weakMastery.Confidence:0.00}";
            var move = BuildTeachingMove(weakMastery, learningSignalContext, userMessage);
            var recentMistakes = await LoadRecentMistakesAsync(userId, topicId, ct);
            var sourceBundle = await LoadLatestSourceBundleAsync(userId, topicId, sessionId, ct);
            var sourceEvidence = BuildSourceEvidence(snapshot?.SourceConfidence, notebookContext, wikiContext, graph, sourceBundle);
            var hasWikiContext = !string.IsNullOrWhiteSpace(wikiContext) || sourceBundle?.EvidenceStatus == "wiki_backed";
            var sourceEvidenceCount = sourceBundle is { EvidenceStatus: "source_grounded" or "mixed" }
                ? Math.Max(sourceBundle.ChunkCount, CountReliableEvidence(sourceEvidence))
                : CountReliableEvidence(sourceEvidence);
            var evidenceQuality = await LoadEvidenceQualityAsync(userId, topicId, sourceEvidenceCount, snapshot?.SourceConfidence, ct);
            var groundingStatus = ResolveGroundingStatus(sourceBundle?.EvidenceStatus, sourceEvidenceCount, hasWikiContext, snapshot?.SourceConfidence);
            var directAnswerRisk = IsDirectAnswerRequest(userMessage) &&
                                   (weakMastery == null ||
                                    weakMastery.RemediationNeed == "high" ||
                                    weakMastery.PracticeReadiness == "evidence_insufficient");

            var context = new TutorPolicyContextDto
            {
                TopicId = topicId,
                ConceptGraphSnapshotId = snapshot?.Id,
                ActiveConceptKey = activeConcept,
                ActiveConceptLabel = activeLabel,
                LearnerState = learnerState,
                NextPedagogicalMove = move,
                GroundingStatus = groundingStatus,
                SourceEvidenceCount = sourceEvidenceCount,
                EvidenceQuality = evidenceQuality,
                DirectAnswerRisk = directAnswerRisk,
                SourceEvidence = sourceEvidence,
                RecentMistakes = recentMistakes,
            };
            if (context.GroundingStatus != "source_grounded" && UserAsksForSource(userMessage))
            {
                context.PolicyViolations.Add("source_claim_without_source_risk");
            }
            if (_trace != null)
            {
                var trace = await _trace.CreateTraceAsync(userId, topicId, sessionId, userMessage, context, ct);
                context.TraceId = trace.Id;
                context.PolicyViolations = trace.PolicyViolations.ToList();
            }
            context.PromptBlock = BuildPromptBlock(context);
            return context;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[TutorPolicy] Policy context fallback. UserRef={UserRef} TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(topicId, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
            var fallback = new TutorPolicyContextDto
            {
                TopicId = topicId,
                NextPedagogicalMove = "ask one short check question, then teach the smallest missing step"
            };
            fallback.PromptBlock = BuildPromptBlock(fallback);
            return fallback;
        }
    }

    private static string BuildPromptBlock(TutorPolicyContextDto context)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("[TUTOR POLICY ENGINE - CONCEPT GRAPH + LEARNER MODEL]");
        sb.AppendLine($"TutorPolicyTraceId: {context.TraceId?.ToString() ?? "none"}");
        sb.AppendLine($"ConceptGraphSnapshotId: {context.ConceptGraphSnapshotId?.ToString() ?? "none"}");
        sb.AppendLine($"ActiveConcept: {(string.IsNullOrWhiteSpace(context.ActiveConceptKey) ? "none" : $"{context.ActiveConceptKey} ({context.ActiveConceptLabel})")}");
        sb.AppendLine($"LearnerState: {context.LearnerState}");
        sb.AppendLine($"NextPedagogicalMove: {context.NextPedagogicalMove}");
        sb.AppendLine($"GroundingStatus: {context.GroundingStatus}; SourceEvidenceCount: {context.SourceEvidenceCount}");
        var evidencePolicy = TutorResponsePolicy.EvidencePolicyFor(context.EvidenceQuality, context.GroundingStatus, context.SourceEvidenceCount);
        sb.AppendLine($"EvidenceQuality: {context.EvidenceQuality?.Status ?? "unknown"}; EvidencePolicy: {evidencePolicy}");
        if (context.DirectAnswerRisk)
        {
            sb.AppendLine("DirectAnswerRisk: High; prefer hint-first scaffolding before final answers.");
        }
        if (evidencePolicy is "evidence_limited_caution" or "evidence_weak_verify" or "model_ok_no_source_claim" or "unknown_source_caution")
        {
            sb.AppendLine("EvidencePolicyRule: Use cautious wording; do not claim source-backed certainty when evidence is limited, weak, missing, or unknown.");
        }
        if (context.SourceEvidence.Count > 0)
        {
            sb.AppendLine("GroundingPolicy:");
            foreach (var source in context.SourceEvidence.Take(5))
            {
                sb.AppendLine($"- {source}");
            }
        }
        else
        {
            sb.AppendLine("GroundingPolicy: No reliable source evidence is active; do not imply source-backed claims.");
        }
        if (context.RecentMistakes.Count > 0)
        {
            sb.AppendLine("RecentMistakes:");
            foreach (var mistake in context.RecentMistakes.Take(5))
            {
                sb.AppendLine($"- {mistake}");
            }
        }
        if (context.PolicyViolations.Count > 0)
        {
            sb.AppendLine("PolicyRiskFlags:");
            foreach (var violation in context.PolicyViolations.Take(5))
            {
                sb.AppendLine($"- {violation}");
            }
        }
        sb.AppendLine("TeachingContract: Execute the next move with Socratic checks, hints, and short examples. Do not simply dump an answer when a learner model signal suggests remediation.");
        return sb.ToString();
    }

    private async Task<List<string>> LoadRecentMistakesAsync(Guid userId, Guid? topicId, CancellationToken ct)
    {
        var query = _db.QuizAttempts.AsNoTracking().Where(a => a.UserId == userId && !a.IsCorrect);
        if (topicId.HasValue)
        {
            query = query.Where(a => a.TopicId == topicId.Value);
        }

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(5)
            .Select(a => $"{a.SkillTag ?? a.TopicPath ?? "unknown"}: {a.CognitiveType ?? "incorrect answer"}")
            .ToListAsync(ct);
    }

    private async Task<EvidenceQualityDto> LoadEvidenceQualityAsync(Guid userId, Guid? topicId, int sourceEvidenceCount, string? sourceConfidence, CancellationToken ct)
    {
        if (topicId.HasValue)
        {
            var latestReport = await _db.SourceQualityReports
                .AsNoTracking()
                .Where(r => r.UserId == userId && r.TopicId == topicId.Value)
                .OrderByDescending(r => r.GeneratedAt)
                .Select(r => r.ReportJson)
                .FirstOrDefaultAsync(ct);

            if (TryReadEvidenceQuality(latestReport, out var cached))
                return cached;
        }

        var retrievalHealth = sourceEvidenceCount <= 0
            ? "no_source"
            : string.Equals(sourceConfidence, "low", StringComparison.OrdinalIgnoreCase) ? "low_confidence" : "healthy";
        var citationStatus = sourceEvidenceCount > 0 ? "unverified" : "unknown";
        return EvidenceQualityEvaluator.Build(
            sourceEvidenceCount,
            sourceEvidenceCount,
            sourceEvidenceCount,
            citationCoverage: 0m,
            unsupportedCitationCount: 0,
            citationMissingCount: 0,
            retrievalHealthStatus: retrievalHealth,
            citationCoverageStatus: citationStatus);
    }

    private static bool TryReadEvidenceQuality(string? reportJson, out EvidenceQualityDto evidenceQuality)
    {
        evidenceQuality = EvidenceQualityEvaluator.Unknown();
        if (string.IsNullOrWhiteSpace(reportJson))
            return false;

        try
        {
            var report = JsonSerializer.Deserialize<SourceQualityReportDto>(reportJson, JsonOptions);
            if (report?.EvidenceQuality == null)
                return false;

            evidenceQuality = report.EvidenceQuality;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<SourceEvidenceBundleProjection?> LoadLatestSourceBundleAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
        if (!topicId.HasValue)
        {
            return null;
        }

        return await _db.SourceEvidenceBundles
            .AsNoTracking()
            .Where(b =>
                b.UserId == userId &&
                !b.IsDeleted &&
                b.TopicId == topicId.Value &&
                (!sessionId.HasValue || b.SessionId == sessionId))
            .OrderByDescending(b => b.UpdatedAt)
            .Select(b => new SourceEvidenceBundleProjection(b.EvidenceStatus, b.ReadySourceCount, b.ChunkCount, b.StaleEvidenceCount, b.DeletedEvidenceCount))
            .FirstOrDefaultAsync(ct);
    }

    private static List<string> BuildSourceEvidence(string? sourceConfidence, string notebookContext, string wikiContext, ConceptGraphDto? graph, SourceEvidenceBundleProjection? sourceBundle)
    {
        var evidence = new List<string>();
        if (sourceBundle != null)
        {
            if (sourceBundle.EvidenceStatus is "source_grounded" or "mixed")
            {
                evidence.Add($"Source evidence lifecycle bundle is {sourceBundle.EvidenceStatus}; cite active document chunks only.");
            }
            else if (sourceBundle.EvidenceStatus == "wiki_backed")
            {
                evidence.Add("Wiki notebook evidence is active; label wiki-backed claims and avoid document-source claims.");
            }
            else if (sourceBundle.EvidenceStatus is "stale" or "degraded")
            {
                evidence.Add("Source evidence lifecycle is degraded/stale; avoid source-backed certainty until evidence is refreshed.");
            }
        }
        if (!string.IsNullOrWhiteSpace(notebookContext))
        {
            evidence.Add("Notebook document context is active; use exact [doc:sourceId:pN] citations for document-backed claims.");
        }
        if (!string.IsNullOrWhiteSpace(wikiContext))
        {
            evidence.Add("Personal Wiki context is active; label wiki-grounded claims when no document citation exists.");
        }
        if (graph?.SourceEvidence.Count > 0)
        {
            evidence.AddRange(graph.SourceEvidence.Take(3).Select(s => $"{s.Provider}: {s.Title}"));
        }
        if (evidence.Count == 0 && string.Equals(sourceConfidence, "low", StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add("Concept graph source confidence is low; stay conservative and avoid current-source claims.");
        }
        return evidence;
    }

    private static string ResolveGroundingStatus(string? bundleStatus, int sourceEvidenceCount, bool hasWikiContext, string? sourceConfidence)
    {
        if (bundleStatus is "source_grounded" or "mixed") return "source_grounded";
        if (bundleStatus == "wiki_backed") return "wiki_backed";
        if (bundleStatus is "stale" or "degraded") return bundleStatus;
        if (sourceEvidenceCount > 0) return "source_grounded";
        if (hasWikiContext) return "wiki_backed";
        return string.Equals(sourceConfidence, "low", StringComparison.OrdinalIgnoreCase) ? "low_source" : "model_only";
    }

    private static int CountReliableEvidence(IReadOnlyList<string> sourceEvidence) =>
        sourceEvidence.Count(e =>
            !e.Contains("source confidence is low", StringComparison.OrdinalIgnoreCase) &&
            !e.Contains("avoid current-source claims", StringComparison.OrdinalIgnoreCase) &&
            !e.Contains("avoid source-backed certainty", StringComparison.OrdinalIgnoreCase) &&
            !e.Contains("degraded/stale", StringComparison.OrdinalIgnoreCase) &&
            !e.Contains("Personal Wiki", StringComparison.OrdinalIgnoreCase) &&
            !e.Contains("Wiki notebook", StringComparison.OrdinalIgnoreCase) &&
            !e.Contains("wiki-grounded", StringComparison.OrdinalIgnoreCase));

    private static bool UserAsksForSource(string message) =>
        message.Contains("kaynak", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("source", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("citation", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("nereden", StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectAnswerRequest(string message) =>
        message.Contains("cevap", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("direkt", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("answer", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("sonucu", StringComparison.OrdinalIgnoreCase);

    private static string BuildTeachingMove(ConceptMasteryDto? mastery, string learningSignalContext, string userMessage)
    {
        if (userMessage.Contains("anlamad", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("zor", StringComparison.OrdinalIgnoreCase))
        {
            return "repair the active concept with a simpler explanation, one concrete example, and one micro-check";
        }
        if (mastery == null)
        {
            return "ask a short diagnostic check before choosing the depth";
        }
        if (mastery.RemediationNeed == "high")
        {
            return "repair misconception first, then give guided practice";
        }
        if (mastery.PracticeReadiness == "independent")
        {
            return "fast-track explanation and move to challenge practice";
        }
        if (!string.IsNullOrWhiteSpace(learningSignalContext))
        {
            return "connect the answer to the latest learning signal and ask a small retrieval question";
        }
        return "teach the next small step and verify understanding";
    }

    private sealed record SourceEvidenceBundleProjection(
        string EvidenceStatus,
        int ReadySourceCount,
        int ChunkCount,
        int StaleEvidenceCount,
        int DeletedEvidenceCount);

    private static ConceptGraphDto? SafeGraph(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ConceptGraphDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

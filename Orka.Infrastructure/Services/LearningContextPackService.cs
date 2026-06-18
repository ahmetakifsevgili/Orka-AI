using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class LearningContextPackService : ILearningContextPackService
{
    private const int MaxEstimatedTokens = 2_000;

    private readonly IOrkaLearningStateService _orkaLearningState;
    private readonly IActiveLessonSnapshotService _snapshots;
    private readonly ISourceEvidenceLifecycleService _sourceEvidence;

    public LearningContextPackService(
        IOrkaLearningStateService orkaLearningState,
        IActiveLessonSnapshotService snapshots,
        ISourceEvidenceLifecycleService sourceEvidence)
    {
        _orkaLearningState = orkaLearningState;
        _snapshots = snapshots;
        _sourceEvidence = sourceEvidence;
    }

    public async Task<LearningContextPackDto?> BuildPackAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sessionId = null,
        string? examCode = "KPSS",
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var state = await _orkaLearningState.BuildStateAsync(userId, topicId, sessionId, examCode, variantCode, ct);
        if (state == null)
        {
            return null;
        }

        var resolvedTopicId = state.TopicId ?? topicId;
        var resolvedSessionId = state.SessionId ?? sessionId;
        var activeSnapshot = await _snapshots.GetActiveLessonSnapshotAsync(userId, resolvedTopicId, resolvedSessionId, ct);
        var studentSnapshot = await _snapshots.GetStudentContextSnapshotAsync(userId, resolvedTopicId, resolvedSessionId, ct);
        var sourceBundle = resolvedTopicId.HasValue
            ? await _sourceEvidence.GetLatestSourceEvidenceBundleAsync(userId, resolvedTopicId.Value, resolvedSessionId, ct)
            : null;

        var blocks = BuildBlocks(state, activeSnapshot, studentSnapshot, sourceBundle);
        var warnings = BuildWarnings(state, activeSnapshot, studentSnapshot, sourceBundle);
        return new LearningContextPackDto
        {
            TopicId = resolvedTopicId,
            SessionId = resolvedSessionId,
            ScopeStatus = state.ScopeStatus,
            OrkaState = state,
            ActiveLessonSnapshot = activeSnapshot,
            StudentContextSnapshot = studentSnapshot,
            SourceEvidenceBundle = sourceBundle,
            Blocks = blocks,
            Warnings = warnings,
            EstimatedTokenCount = Math.Min(MaxEstimatedTokens, EstimateTokens(blocks, warnings)),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static IReadOnlyList<LearningContextPackBlockDto> BuildBlocks(
        OrkaLearningStateDto state,
        ActiveLessonSnapshotDto? activeSnapshot,
        StudentContextSnapshotDto? studentSnapshot,
        SourceEvidenceBundleDto? sourceBundle)
    {
        var blocks = new List<LearningContextPackBlockDto>
        {
            Block(
                "orka_state",
                state.PrimaryNextAction.Priority,
                $"Next action: {state.PrimaryNextAction.Label}; source health: {state.SourceHealth.Status}; signals: {state.SignalSummary.LearningSignalCount}.",
                priority: 100,
                metadata: new Dictionary<string, string>
                {
                    ["scopeStatus"] = state.ScopeStatus,
                    ["nextActionType"] = state.PrimaryNextAction.ActionType,
                    ["sourceHealth"] = state.SourceHealth.Status
                })
        };

        if (activeSnapshot != null)
        {
            blocks.Add(Block(
                "active_lesson_snapshot",
                activeSnapshot.Status,
                $"Active concept: {Fallback(activeSnapshot.ActiveConceptLabel, activeSnapshot.ActiveConceptKey, "unknown")}; learner state: {activeSnapshot.LearnerState}; remediation: {activeSnapshot.RemediationNeed}.",
                priority: 90,
                snapshotId: activeSnapshot.Id,
                expiresAt: activeSnapshot.ExpiresAt,
                metadata: new Dictionary<string, string>
                {
                    ["groundingMode"] = activeSnapshot.GroundingMode ?? "unknown",
                    ["sourceEvidenceCount"] = activeSnapshot.EvidenceSummary.SourceEvidenceCount.ToString(),
                    ["wikiEvidenceCount"] = activeSnapshot.EvidenceSummary.WikiEvidenceCount.ToString()
                }));
        }

        if (studentSnapshot != null)
        {
            blocks.Add(Block(
                "student_context_snapshot",
                studentSnapshot.ConfidenceStatus,
                $"Weak concepts: {studentSnapshot.WeakConcepts.Count}; remediation ready: {studentSnapshot.RemediationReady.Count}; source readiness: {studentSnapshot.SourceReadiness}.",
                priority: 80,
                snapshotId: studentSnapshot.Id,
                expiresAt: studentSnapshot.ExpiresAt,
                metadata: new Dictionary<string, string>
                {
                    ["sourceReadiness"] = studentSnapshot.SourceReadiness,
                    ["reviewPressureCount"] = studentSnapshot.ReviewPressure.Count.ToString()
                }));
        }

        if (sourceBundle != null)
        {
            blocks.Add(Block(
                "source_evidence_bundle",
                sourceBundle.EvidenceStatus,
                $"Ready sources: {sourceBundle.ReadySourceCount}/{sourceBundle.SourceCount}; chunks: {sourceBundle.ChunkCount}; citation coverage: {sourceBundle.CitationCoverage:0.##}.",
                priority: 70,
                snapshotId: sourceBundle.Id,
                expiresAt: sourceBundle.ExpiresAt,
                metadata: new Dictionary<string, string>
                {
                    ["bundleHash"] = sourceBundle.BundleHash,
                    ["unsupportedCitationCount"] = sourceBundle.UnsupportedCitationCount.ToString(),
                    ["staleEvidenceCount"] = sourceBundle.StaleEvidenceCount.ToString()
                }));
        }

        return blocks.OrderByDescending(b => b.Priority).ToArray();
    }

    private static IReadOnlyList<string> BuildWarnings(
        OrkaLearningStateDto state,
        ActiveLessonSnapshotDto? activeSnapshot,
        StudentContextSnapshotDto? studentSnapshot,
        SourceEvidenceBundleDto? sourceBundle)
    {
        var warnings = new List<string>();
        warnings.AddRange(state.SafetyWarnings.Take(4));
        warnings.AddRange(state.ConflictWarnings.Select(w => w.ConflictCode).Take(4));
        if (activeSnapshot == null) warnings.Add("active_lesson_snapshot_missing");
        if (studentSnapshot == null) warnings.Add("student_context_snapshot_missing");
        if (sourceBundle?.Warnings.Count > 0) warnings.AddRange(sourceBundle.Warnings.Take(4));
        if (sourceBundle == null && state.SignalSummary.SourceCount > 0) warnings.Add("source_bundle_missing");
        return warnings
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static LearningContextPackBlockDto Block(
        string type,
        string status,
        string summary,
        int priority,
        Guid? snapshotId = null,
        DateTime? expiresAt = null,
        IReadOnlyDictionary<string, string>? metadata = null) => new()
        {
            BlockType = type,
            Status = string.IsNullOrWhiteSpace(status) ? "unknown" : status,
            Summary = summary,
            Priority = priority,
            SnapshotId = snapshotId,
            ExpiresAt = expiresAt,
            Metadata = metadata ?? new Dictionary<string, string>()
        };

    private static int EstimateTokens(IReadOnlyList<LearningContextPackBlockDto> blocks, IReadOnlyList<string> warnings)
    {
        var chars = blocks.Sum(b =>
            b.BlockType.Length +
            b.Status.Length +
            b.Summary.Length +
            b.Metadata.Sum(kvp => kvp.Key.Length + kvp.Value.Length)) +
            warnings.Sum(w => w.Length);
        return Math.Max(1, (int)Math.Ceiling(chars / 4m));
    }

    private static string Fallback(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

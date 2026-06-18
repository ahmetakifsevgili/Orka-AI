using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Orka.Infrastructure.Services;

public sealed class LearningContextPackService : ILearningContextPackService
{
    private const string SchemaVersion = "orka.learning-context-pack.v1.1";
    private const int MaxEstimatedTokens = 2_000;
    private const int MaxSummaryChars = 320;
    private const int MaxMetadataValueChars = 180;
    private const int MaxWarningChars = 180;
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

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
        var bounded = BoundPack(blocks, warnings);
        var pack = new LearningContextPackDto
        {
            SchemaVersion = SchemaVersion,
            LearningStateVersion = state.LearningStateVersion,
            TopicId = resolvedTopicId,
            SessionId = resolvedSessionId,
            ScopeStatus = state.ScopeStatus,
            ContextWatermark = BuildWatermark(state, activeSnapshot, studentSnapshot, sourceBundle, bounded.Blocks),
            Blocks = bounded.Blocks,
            Warnings = bounded.Warnings,
            Trace = bounded.Trace,
            EstimatedTokenCount = bounded.Trace.EstimatedTokenCount,
            GeneratedAt = DateTimeOffset.UtcNow
        };
        pack = EnforceSerializedPayloadBudget(pack);
        pack.ContextWatermark = BuildWatermark(state, activeSnapshot, studentSnapshot, sourceBundle, pack.Blocks);
        RefreshSerializedTokenCounts(pack, pack.Trace.InitialEstimatedTokenCount);
        return pack;
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
                    ["nextActionPriority"] = state.PrimaryNextAction.Priority,
                    ["sourceHealth"] = state.SourceHealth.Status
                })
        };

        if (activeSnapshot != null)
        {
            var snapshotRef = new LearningContextPackRefDto
            {
                Kind = "active_lesson_snapshot",
                Id = activeSnapshot.Id.ToString("N"),
                Version = activeSnapshot.SnapshotVersion.ToString(),
                Status = activeSnapshot.Status,
                EvidenceStatus = activeSnapshot.EvidenceSummary.EvidenceStatus,
                UpdatedAt = activeSnapshot.UpdatedAt,
                ExpiresAt = activeSnapshot.ExpiresAt
            };
            blocks.Add(Block(
                "active_lesson_snapshot",
                activeSnapshot.Status,
                $"Active concept: {Fallback(activeSnapshot.ActiveConceptLabel, activeSnapshot.ActiveConceptKey, "unknown")}; learner state: {activeSnapshot.LearnerState}; remediation: {activeSnapshot.RemediationNeed}.",
                priority: 90,
                snapshotId: activeSnapshot.Id,
                snapshotRef: snapshotRef,
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
            var snapshotRef = new LearningContextPackRefDto
            {
                Kind = "student_context_snapshot",
                Id = studentSnapshot.Id.ToString("N"),
                Version = studentSnapshot.SnapshotVersion.ToString(),
                Status = studentSnapshot.ConfidenceStatus,
                EvidenceStatus = studentSnapshot.SourceReadiness,
                UpdatedAt = studentSnapshot.UpdatedAt,
                ExpiresAt = studentSnapshot.ExpiresAt
            };
            blocks.Add(Block(
                "student_context_snapshot",
                studentSnapshot.ConfidenceStatus,
                $"Weak concepts: {studentSnapshot.WeakConcepts.Count}; remediation ready: {studentSnapshot.RemediationReady.Count}; source readiness: {studentSnapshot.SourceReadiness}.",
                priority: 80,
                snapshotId: studentSnapshot.Id,
                snapshotRef: snapshotRef,
                expiresAt: studentSnapshot.ExpiresAt,
                metadata: new Dictionary<string, string>
                {
                    ["sourceReadiness"] = studentSnapshot.SourceReadiness,
                    ["reviewPressureCount"] = studentSnapshot.ReviewPressure.Count.ToString()
                }));
        }

        if (sourceBundle != null)
        {
            var sourceRef = new LearningContextPackRefDto
            {
                Kind = "source_evidence_bundle",
                Id = sourceBundle.Id.ToString("N"),
                Version = sourceBundle.BundleHash,
                Status = sourceBundle.EvidenceStatus,
                EvidenceStatus = sourceBundle.EvidenceStatus,
                UpdatedAt = sourceBundle.UpdatedAt,
                ExpiresAt = sourceBundle.ExpiresAt
            };
            blocks.Add(Block(
                "source_evidence_bundle",
                sourceBundle.EvidenceStatus,
                $"Ready sources: {sourceBundle.ReadySourceCount}/{sourceBundle.SourceCount}; chunks: {sourceBundle.ChunkCount}; citation coverage: {sourceBundle.CitationCoverage:0.##}.",
                priority: 70,
                snapshotId: sourceBundle.Id,
                sourceRef: sourceRef,
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
            .Select(w => Trim(w, MaxWarningChars))
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
        LearningContextPackRefDto? snapshotRef = null,
        LearningContextPackRefDto? sourceRef = null,
        DateTime? expiresAt = null,
        IReadOnlyDictionary<string, string>? metadata = null) => new()
        {
            BlockType = type,
            Status = string.IsNullOrWhiteSpace(status) ? "unknown" : status,
            Summary = Trim(summary, MaxSummaryChars),
            Priority = priority,
            SnapshotId = snapshotId,
            SnapshotRef = snapshotRef,
            SourceRef = sourceRef,
            ExpiresAt = expiresAt,
            Metadata = NormalizeMetadata(metadata)
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

    private static BoundPackResult BoundPack(
        IReadOnlyList<LearningContextPackBlockDto> blocks,
        IReadOnlyList<string> warnings)
    {
        var boundedBlocks = blocks.OrderByDescending(b => b.Priority).ToList();
        var boundedWarnings = warnings.ToList();
        var droppedBlocks = new List<LearningContextPackDroppedBlockDto>();
        var droppedWarnings = new List<LearningContextPackDroppedWarningDto>();
        var initialEstimate = EstimateTokens(boundedBlocks, boundedWarnings);

        while (EstimateTokens(boundedBlocks, boundedWarnings) > MaxEstimatedTokens && boundedWarnings.Count > 0)
        {
            droppedWarnings.Add(new LearningContextPackDroppedWarningDto
            {
                Warning = boundedWarnings[^1],
                Reason = "token_budget"
            });
            boundedWarnings.RemoveAt(boundedWarnings.Count - 1);
        }

        while (EstimateTokens(boundedBlocks, boundedWarnings) > MaxEstimatedTokens && boundedBlocks.Count > 1)
        {
            var dropped = boundedBlocks[^1];
            droppedBlocks.Add(new LearningContextPackDroppedBlockDto
            {
                BlockType = dropped.BlockType,
                Priority = dropped.Priority,
                Reason = "token_budget",
                EstimatedTokenCount = EstimateTokens(new[] { dropped }, Array.Empty<string>())
            });
            boundedBlocks.RemoveAt(boundedBlocks.Count - 1);
        }

        var finalEstimate = EstimateTokens(boundedBlocks, boundedWarnings);
        var trace = BuildTrace(boundedBlocks, initialEstimate, finalEstimate, droppedBlocks, droppedWarnings);

        return new BoundPackResult(boundedBlocks, boundedWarnings, trace);
    }

    private static LearningContextPackDto EnforceSerializedPayloadBudget(LearningContextPackDto pack)
    {
        var blocks = pack.Blocks.OrderByDescending(b => b.Priority).ToList();
        var warnings = pack.Warnings.ToList();
        var droppedBlocks = pack.Trace.DroppedBlocks.ToList();
        var droppedWarnings = pack.Trace.DroppedWarnings.ToList();
        var initialSerializedEstimate = EstimateSerializedTokens(pack);

        while (true)
        {
            pack.Blocks = blocks.ToArray();
            pack.Warnings = warnings.ToArray();
            pack.Trace = BuildTrace(pack.Blocks, initialSerializedEstimate, 0, droppedBlocks, droppedWarnings);
            RefreshSerializedTokenCounts(pack, initialSerializedEstimate);

            if (pack.EstimatedTokenCount <= MaxEstimatedTokens)
            {
                return pack;
            }

            if (warnings.Count > 0)
            {
                droppedWarnings.Add(new LearningContextPackDroppedWarningDto
                {
                    Warning = warnings[^1],
                    Reason = "serialized_payload_budget"
                });
                warnings.RemoveAt(warnings.Count - 1);
                continue;
            }

            if (blocks.Count > 1)
            {
                var dropped = blocks[^1];
                droppedBlocks.Add(new LearningContextPackDroppedBlockDto
                {
                    BlockType = dropped.BlockType,
                    Priority = dropped.Priority,
                    Reason = "serialized_payload_budget",
                    EstimatedTokenCount = EstimateSerializedTokens(new LearningContextPackDto
                    {
                        SchemaVersion = pack.SchemaVersion,
                        LearningStateVersion = pack.LearningStateVersion,
                        TopicId = pack.TopicId,
                        SessionId = pack.SessionId,
                        ScopeStatus = pack.ScopeStatus,
                        ContextWatermark = pack.ContextWatermark,
                        Blocks = new[] { dropped },
                        Warnings = Array.Empty<string>(),
                        Trace = new LearningContextPackTraceDto(),
                        GeneratedAt = pack.GeneratedAt
                    })
                });
                blocks.RemoveAt(blocks.Count - 1);
                continue;
            }

            if (droppedWarnings.Count > 0 || droppedBlocks.Count > 0)
            {
                droppedWarnings.Clear();
                droppedBlocks.Clear();
                continue;
            }

            return pack;
        }
    }

    private static LearningContextPackTraceDto BuildTrace(
        IReadOnlyList<LearningContextPackBlockDto> selectedBlocks,
        int initialEstimatedTokenCount,
        int estimatedTokenCount,
        IReadOnlyList<LearningContextPackDroppedBlockDto> droppedBlocks,
        IReadOnlyList<LearningContextPackDroppedWarningDto> droppedWarnings) => new()
        {
            TokenBudget = MaxEstimatedTokens,
            InitialEstimatedTokenCount = initialEstimatedTokenCount,
            EstimatedTokenCount = estimatedTokenCount,
            SelectedBlocks = selectedBlocks.Select(b => new LearningContextPackTraceBlockDto
            {
                BlockType = b.BlockType,
                Status = b.Status,
                Priority = b.Priority,
                EstimatedTokenCount = EstimateTokens(new[] { b }, Array.Empty<string>()),
                RefKind = b.SnapshotRef?.Kind ?? b.SourceRef?.Kind,
                RefId = b.SnapshotRef?.Id ?? b.SourceRef?.Id,
                RefVersion = b.SnapshotRef?.Version ?? b.SourceRef?.Version
            }).ToArray(),
            DroppedBlocks = droppedBlocks.ToArray(),
            DroppedWarnings = droppedWarnings.ToArray()
        };

    private static void RefreshSerializedTokenCounts(LearningContextPackDto pack, int initialEstimatedTokenCount)
    {
        var estimate = EstimateSerializedTokens(pack);
        pack.EstimatedTokenCount = estimate;
        pack.Trace.EstimatedTokenCount = estimate;
        pack.Trace.InitialEstimatedTokenCount = Math.Max(initialEstimatedTokenCount, estimate);

        var stableEstimate = EstimateSerializedTokens(pack);
        pack.EstimatedTokenCount = stableEstimate;
        pack.Trace.EstimatedTokenCount = stableEstimate;
        pack.Trace.InitialEstimatedTokenCount = Math.Max(initialEstimatedTokenCount, stableEstimate);
    }

    private static int EstimateSerializedTokens(LearningContextPackDto pack)
    {
        var json = JsonSerializer.Serialize(pack, PayloadJsonOptions);
        return Math.Max(1, (int)Math.Ceiling(json.Length / 4m));
    }

    private static string BuildWatermark(
        OrkaLearningStateDto state,
        ActiveLessonSnapshotDto? activeSnapshot,
        StudentContextSnapshotDto? studentSnapshot,
        SourceEvidenceBundleDto? sourceBundle,
        IReadOnlyList<LearningContextPackBlockDto> selectedBlocks)
    {
        var material = string.Join('\n', new[]
        {
            SchemaVersion,
            $"state:{state.LearningStateVersion}",
            $"scope:{state.ScopeStatus}",
            $"topic:{state.TopicId?.ToString("N") ?? "global"}",
            $"session:{state.SessionId?.ToString("N") ?? "none"}",
            $"next:{state.PrimaryNextAction.ActionType}|{state.PrimaryNextAction.Priority}|{state.PrimaryNextAction.TopicId?.ToString("N") ?? string.Empty}|{state.PrimaryNextAction.ConceptKey ?? string.Empty}|{string.Join(',', state.PrimaryNextAction.ReasonCodes.OrderBy(static r => r, StringComparer.Ordinal))}",
            $"source:{state.SourceHealth.Status}|{state.SignalSummary.SourceCount}|{state.SignalSummary.ReadySourceCount}|{state.SignalSummary.WikiPageCount}",
            $"signals:{state.SignalSummary.EvidenceCount}|{state.SignalSummary.QuizAttemptCount}|{state.SignalSummary.CorrectAttemptCount}|{state.SignalSummary.WrongAttemptCount}|{state.SignalSummary.BlankOrSkippedAttemptCount}|{state.SignalSummary.LearningSignalCount}|{state.SignalSummary.HasRealLearningData}",
            $"active:{RefWatermark(activeSnapshot)}",
            $"student:{RefWatermark(studentSnapshot)}",
            $"bundle:{RefWatermark(sourceBundle)}",
            $"blocks:{string.Join('|', selectedBlocks.Select(BlockWatermark))}"
        });

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"ctx_{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string RefWatermark(ActiveLessonSnapshotDto? snapshot) =>
        snapshot == null
            ? "missing"
            : $"{snapshot.Id:N}|{snapshot.SnapshotVersion}|{snapshot.Status}|{snapshot.EvidenceSummary.EvidenceStatus}|{snapshot.EvidenceSummary.SourceEvidenceCount}|{snapshot.EvidenceSummary.WikiEvidenceCount}|{snapshot.EvidenceSummary.RecentAttemptCount}|{snapshot.ActiveConceptKey ?? string.Empty}|{snapshot.RemediationNeed}|{snapshot.UpdatedAt:O}|{snapshot.ExpiresAt:O}";

    private static string RefWatermark(StudentContextSnapshotDto? snapshot) =>
        snapshot == null
            ? "missing"
            : $"{snapshot.Id:N}|{snapshot.SnapshotVersion}|{snapshot.ConfidenceStatus}|{snapshot.SourceReadiness}|{snapshot.WeakConcepts.Count}|{snapshot.RemediationReady.Count}|{snapshot.ReviewPressure.Count}|{snapshot.UpdatedAt:O}|{snapshot.ExpiresAt:O}";

    private static string RefWatermark(SourceEvidenceBundleDto? bundle) =>
        bundle == null
            ? "missing"
            : $"{bundle.Id:N}|{bundle.BundleHash}|{bundle.EvidenceStatus}|{bundle.SourceCount}|{bundle.ReadySourceCount}|{bundle.ChunkCount}|{bundle.CitationCoverage}|{bundle.UnsupportedCitationCount}|{bundle.StaleEvidenceCount}|{bundle.DeletedEvidenceCount}|{bundle.UpdatedAt:O}|{bundle.ExpiresAt:O}";

    private static string BlockWatermark(LearningContextPackBlockDto block) =>
        $"{block.BlockType}|{block.Status}|{block.Priority}|{block.SnapshotRef?.Kind ?? block.SourceRef?.Kind ?? string.Empty}|{block.SnapshotRef?.Id ?? block.SourceRef?.Id ?? string.Empty}|{block.SnapshotRef?.Version ?? block.SourceRef?.Version ?? string.Empty}";

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata == null || metadata.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in metadata.Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value)).Take(8))
        {
            var key = Trim(kvp.Key, 80);
            if (!result.ContainsKey(key))
            {
                result[key] = Trim(kvp.Value, MaxMetadataValueChars);
            }
        }

        return result;
    }

    private static string Trim(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string Fallback(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private sealed record BoundPackResult(
        IReadOnlyList<LearningContextPackBlockDto> Blocks,
        IReadOnlyList<string> Warnings,
        LearningContextPackTraceDto Trace);
}

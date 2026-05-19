using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class ConceptGraphQualityService : IConceptGraphQualityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService? _redis;
    private readonly ILogger<ConceptGraphQualityService> _logger;

    public ConceptGraphQualityService(
        OrkaDbContext db,
        IRedisMemoryService? redis,
        ILogger<ConceptGraphQualityService> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<ConceptGraphQualityDto> EvaluateAndSaveAsync(
        Guid userId,
        Guid? topicId,
        Guid planRequestId,
        ConceptGraphDto graph,
        CancellationToken ct = default)
    {
        var snapshotId = graph.SnapshotId ?? Guid.Empty;
        var concepts = graph.Concepts
            .Where(c => !string.IsNullOrWhiteSpace(c.StableKey) || !string.IsNullOrWhiteSpace(c.Label))
            .ToList();
        var conceptCount = concepts.Count;
        var normalizedLabels = concepts.Select(c => Normalize(c.Label)).Where(x => x.Length > 0).ToList();
        var duplicateCount = normalizedLabels.Count - normalizedLabels.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var duplicateRatio = Ratio(duplicateCount, Math.Max(normalizedLabels.Count, 1));
        var hasCycle = HasPrerequisiteCycle(graph);
        var orphanCount = CountOrphans(graph);
        var outcomeCoverage = Ratio(concepts.Count(c => c.LearningOutcomeKeys.Count > 0), Math.Max(conceptCount, 1));
        var misconceptionCoverage = Ratio(concepts.Count(c => c.Misconceptions.Count > 0), Math.Max(conceptCount, 1));
        var sourceEvidenceRatio = Ratio(
            concepts.Count(c => c.SourceEvidenceLabels.Count > 0) + (graph.SourceEvidence.Count > 0 ? 1 : 0),
            Math.Max(conceptCount, 1));
        sourceEvidenceRatio = Math.Min(1m, sourceEvidenceRatio);
        var possibleRelations = conceptCount <= 1 ? 1 : conceptCount * (conceptCount - 1);
        var relationDensity = Ratio(graph.Relations.Count, possibleRelations);

        var failures = new List<string>();
        if (conceptCount == 0) failures.Add("concept_count_zero");
        if (duplicateRatio > 0.15m) failures.Add("duplicate_ratio_high");
        if (hasCycle) failures.Add("prerequisite_cycle");
        if (conceptCount > 2 && orphanCount > Math.Ceiling(conceptCount * 0.30m)) failures.Add("orphan_concept_high");
        if (outcomeCoverage < 0.80m) failures.Add("outcome_coverage_low");
        if (misconceptionCoverage < 0.50m) failures.Add("misconception_coverage_low");
        if (sourceEvidenceRatio < 0.35m) failures.Add("source_evidence_low");

        var status = conceptCount == 0 || hasCycle
            ? "critical"
            : failures.Count == 0 ? "healthy" : "degraded";

        var entity = new ConceptGraphQualityRun
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = snapshotId,
            PlanRequestId = planRequestId,
            QualityStatus = status,
            ConceptCount = conceptCount,
            DuplicateRatio = duplicateRatio,
            HasPrerequisiteCycle = hasCycle,
            OrphanConceptCount = orphanCount,
            OutcomeCoverage = outcomeCoverage,
            MisconceptionCoverage = misconceptionCoverage,
            SourceEvidenceRatio = sourceEvidenceRatio,
            RelationDensity = relationDensity,
            FailuresJson = JsonSerializer.Serialize(failures, JsonOptions),
            CreatedAt = DateTime.UtcNow
        };

        _db.ConceptGraphQualityRuns.Add(entity);
        await _db.SaveChangesAsync(ct);

        var dto = ToDto(entity);
        await TryCacheAsync($"orka:v2:graph-quality:{snapshotId:N}", dto, TimeSpan.FromHours(6));
        return dto;
    }

    public async Task<ConceptGraphQualityDto?> GetLatestAsync(
        Guid userId,
        Guid? topicId,
        Guid? snapshotId = null,
        CancellationToken ct = default)
    {
        var query = _db.ConceptGraphQualityRuns.AsNoTracking().Where(q => q.UserId == userId);
        if (topicId.HasValue) query = query.Where(q => q.TopicId == topicId.Value);
        if (snapshotId.HasValue) query = query.Where(q => q.ConceptGraphSnapshotId == snapshotId.Value);

        var entity = await query.OrderByDescending(q => q.CreatedAt).FirstOrDefaultAsync(ct);
        return entity == null ? null : ToDto(entity);
    }

    private async Task TryCacheAsync(string key, ConceptGraphQualityDto dto, TimeSpan ttl)
    {
        if (_redis == null) return;

        try
        {
            await _redis.SetJsonAsync(key, JsonSerializer.Serialize(dto, JsonOptions), ttl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[GraphQuality] Redis write skipped. KeyRef={KeyRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeTextRef(key, "cache"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    private static ConceptGraphQualityDto ToDto(ConceptGraphQualityRun entity) => new()
    {
        Id = entity.Id,
        ConceptGraphSnapshotId = entity.ConceptGraphSnapshotId,
        PlanRequestId = entity.PlanRequestId,
        QualityStatus = entity.QualityStatus,
        ConceptCount = entity.ConceptCount,
        DuplicateRatio = entity.DuplicateRatio,
        HasPrerequisiteCycle = entity.HasPrerequisiteCycle,
        OrphanConceptCount = entity.OrphanConceptCount,
        OutcomeCoverage = entity.OutcomeCoverage,
        MisconceptionCoverage = entity.MisconceptionCoverage,
        SourceEvidenceRatio = entity.SourceEvidenceRatio,
        RelationDensity = entity.RelationDensity,
        Failures = DeserializeList(entity.FailuresJson),
        GeneratedAt = entity.CreatedAt
    };

    private static int CountOrphans(ConceptGraphDto graph)
    {
        var keys = graph.Concepts.Select(c => c.StableKey).Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Count <= 1) return 0;

        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relation in graph.Relations)
        {
            if (keys.Contains(relation.SourceConceptKey)) connected.Add(relation.SourceConceptKey);
            if (keys.Contains(relation.TargetConceptKey)) connected.Add(relation.TargetConceptKey);
        }

        foreach (var concept in graph.Concepts)
        {
            foreach (var prereq in concept.PrerequisiteKeys)
            {
                if (keys.Contains(concept.StableKey) && keys.Contains(prereq))
                {
                    connected.Add(concept.StableKey);
                    connected.Add(prereq);
                }
            }
        }

        return keys.Count(k => !connected.Contains(k));
    }

    private static bool HasPrerequisiteCycle(ConceptGraphDto graph)
    {
        var edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var relation in graph.Relations.Where(r => string.Equals(r.RelationType, "prerequisite", StringComparison.OrdinalIgnoreCase)))
        {
            if (!edges.TryGetValue(relation.SourceConceptKey, out var targets))
            {
                targets = [];
                edges[relation.SourceConceptKey] = targets;
            }
            targets.Add(relation.TargetConceptKey);
        }

        foreach (var concept in graph.Concepts)
        {
            foreach (var prereq in concept.PrerequisiteKeys)
            {
                if (!edges.TryGetValue(prereq, out var targets))
                {
                    targets = [];
                    edges[prereq] = targets;
                }
                targets.Add(concept.StableKey);
            }
        }

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in edges.Keys.ToList())
        {
            if (Visit(key)) return true;
        }
        return false;

        bool Visit(string key)
        {
            if (visited.Contains(key)) return false;
            if (!visiting.Add(key)) return true;
            if (edges.TryGetValue(key, out var targets))
            {
                foreach (var target in targets)
                {
                    if (Visit(target)) return true;
                }
            }
            visiting.Remove(key);
            visited.Add(key);
            return false;
        }
    }

    private static decimal Ratio(int numerator, int denominator) =>
        denominator <= 0 ? 0m : Math.Round(numerator / (decimal)denominator, 4);

    private static string Normalize(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

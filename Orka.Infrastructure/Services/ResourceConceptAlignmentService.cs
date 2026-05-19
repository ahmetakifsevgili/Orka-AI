using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class ResourceConceptAlignmentService : IResourceConceptAlignmentService
{
    private readonly OrkaDbContext _db;
    private readonly IEmbeddingService? _embedding;
    private readonly ILogger<ResourceConceptAlignmentService> _logger;

    public ResourceConceptAlignmentService(
        OrkaDbContext db,
        ILogger<ResourceConceptAlignmentService> logger,
        IEmbeddingService? embedding = null)
    {
        _db = db;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ResourceConceptAlignmentDto>> AlignGraphSourcesAsync(
        Guid userId,
        Guid? topicId,
        ConceptGraphDto graph,
        CancellationToken ct = default)
    {
        if (!graph.SnapshotId.HasValue || graph.SourceEvidence.Count == 0 || graph.Concepts.Count == 0)
        {
            return [];
        }

        var existing = await _db.ResourceConceptAlignments
            .Where(a => a.UserId == userId && a.ConceptGraphSnapshotId == graph.SnapshotId.Value)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            return existing.Select(ToDto).ToList();
        }

        var rows = new List<ResourceConceptAlignment>();
        foreach (var source in graph.SourceEvidence.Take(6))
        {
            var sourceTitle = source.Title ?? string.Empty;
            var sourceUri = source.Url ?? string.Empty;
            var sourceId = FirstNonBlank(source.Url, source.Title, source.Provider) ?? $"source:{rows.Count + 1}";
            foreach (var concept in graph.Concepts.OrderBy(c => c.Order).Take(10))
            {
                var score = await AlignmentScoreAsync(sourceTitle, sourceUri, source.Provider, concept, ct);
                var outcomeKey = concept.LearningOutcomeKeys.FirstOrDefault() ?? string.Empty;
                rows.Add(new ResourceConceptAlignment
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    TopicId = topicId,
                    ConceptGraphSnapshotId = graph.SnapshotId.Value,
                    SourceId = sourceId,
                    SourceTitle = sourceTitle,
                    SourceUri = sourceUri,
                    ConceptKey = concept.StableKey,
                    OutcomeKey = outcomeKey,
                    AlignmentScore = score,
                    EvidenceSnippet = BuildEvidenceSnippet(sourceTitle, concept.Label),
                    AlignmentStatus = score >= 0.65m ? "strong" : score >= 0.40m ? "weak" : "unverified",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        _db.ResourceConceptAlignments.AddRange(rows);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "[ResourceAlignment] Created {Count} source-concept alignments. SnapshotId={SnapshotId}",
            rows.Count,
            graph.SnapshotId);
        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ResourceConceptAlignmentDto>> GetRecentAsync(
        Guid userId,
        Guid? topicId,
        Guid? snapshotId = null,
        int take = 20,
        CancellationToken ct = default)
    {
        var query = _db.ResourceConceptAlignments.AsNoTracking().Where(a => a.UserId == userId);
        if (topicId.HasValue) query = query.Where(a => a.TopicId == topicId.Value);
        if (snapshotId.HasValue) query = query.Where(a => a.ConceptGraphSnapshotId == snapshotId.Value);
        var rows = await query.OrderByDescending(a => a.CreatedAt).Take(take).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    private async Task<decimal> AlignmentScoreAsync(string sourceTitle, string sourceUri, string? provider, LearningConceptDto concept, CancellationToken ct)
    {
        var lexical = AlignmentScore(sourceTitle, sourceUri, concept);
        if (_embedding == null) return lexical;

        try
        {
            var sourceText = $"{sourceTitle} {sourceUri} {provider}".Trim();
            var conceptText = $"{concept.Label} {concept.StableKey} {string.Join(' ', concept.LearningOutcomeKeys)} {string.Join(' ', concept.Misconceptions)}".Trim();
            if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(conceptText)) return lexical;

            var vectors = await _embedding.EmbedBatchAsync(new[] { sourceText, conceptText }, "search_document", ct);
            if (vectors.Length < 2) return lexical;
            var cosine = (decimal)_embedding.CosineSimilarity(vectors[0], vectors[1]);
            return Math.Round(Math.Clamp((cosine * 0.75m) + (lexical * 0.25m), 0m, 0.98m), 4);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                "[ResourceAlignment] Embedding score fallback used. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return lexical;
        }
    }

    private static decimal AlignmentScore(string sourceTitle, string sourceUri, LearningConceptDto concept)
    {
        var haystack = $"{sourceTitle} {sourceUri}".ToLowerInvariant();
        var labelTokens = concept.Label
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 4)
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .ToList();
        if (labelTokens.Count == 0) return 0.45m;
        var matches = labelTokens.Count(haystack.Contains);
        return matches == 0 ? 0.45m : Math.Min(0.90m, 0.55m + matches * 0.10m);
    }

    private static string BuildEvidenceSnippet(string sourceTitle, string conceptLabel)
    {
        if (string.IsNullOrWhiteSpace(sourceTitle))
        {
            return $"Source aligned by concept graph context for {conceptLabel}.";
        }

        return $"{sourceTitle} was linked as evidence for {conceptLabel}.";
    }

    private static ResourceConceptAlignmentDto ToDto(ResourceConceptAlignment entity) => new()
    {
        Id = entity.Id,
        ConceptGraphSnapshotId = entity.ConceptGraphSnapshotId,
        TopicId = entity.TopicId,
        SourceId = entity.SourceId,
        SourceTitle = entity.SourceTitle,
        SourceUri = entity.SourceUri,
        ConceptKey = entity.ConceptKey,
        OutcomeKey = entity.OutcomeKey,
        AlignmentScore = entity.AlignmentScore,
        EvidenceSnippet = entity.EvidenceSnippet,
        AlignmentStatus = entity.AlignmentStatus,
        CreatedAt = entity.CreatedAt
    };

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}

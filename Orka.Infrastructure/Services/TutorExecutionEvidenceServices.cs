using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class TutorMemoryFragmentService : ITutorMemoryFragmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IEmbeddingService? _embedding;
    private readonly IRedisMemoryService? _redis;

    public TutorMemoryFragmentService(OrkaDbContext db, IEmbeddingService? embedding = null, IRedisMemoryService? redis = null)
    {
        _db = db;
        _embedding = embedding;
        _redis = redis;
    }

    public async Task<TutorMemoryFragmentDto> RecordAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string fragmentType,
        string conceptKey,
        string content,
        string source = "tutor",
        decimal importance = 0.50m,
        CancellationToken ct = default)
    {
        string? embeddingJson = null;
        if (_embedding != null && !string.IsNullOrWhiteSpace(content))
        {
            try
            {
                embeddingJson = JsonSerializer.Serialize(await _embedding.EmbedAsync(content, "search_document", ct), JsonOptions);
            }
            catch
            {
                embeddingJson = null;
            }
        }

        var entity = new TutorMemoryFragment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            FragmentType = NormalizeFragmentType(fragmentType),
            ConceptKey = conceptKey ?? string.Empty,
            Content = content ?? string.Empty,
            EmbeddingJson = embeddingJson,
            Source = string.IsNullOrWhiteSpace(source) ? "tutor" : source,
            Importance = Math.Clamp(importance, 0m, 1m),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        _db.TutorMemoryFragments.Add(entity);
        await _db.SaveChangesAsync(ct);

        if (_redis != null && sessionId.HasValue)
        {
            await _redis.AddStreamEventAsync($"orka:v3:tutor-events:{sessionId.Value}", new Dictionary<string, string>
            {
                ["eventType"] = "tutor.memory.fragment.recorded",
                ["fragmentId"] = entity.Id.ToString(),
                ["fragmentType"] = entity.FragmentType,
                ["conceptKey"] = entity.ConceptKey
            }, TimeSpan.FromDays(2));
        }

        return ToDto(entity);
    }

    public async Task<IReadOnlyList<TutorMemoryFragmentDto>> RetrieveAsync(
        Guid userId,
        Guid? topicId,
        string query,
        int take = 8,
        CancellationToken ct = default)
    {
        var rows = await _db.TutorMemoryFragments
            .AsNoTracking()
            .Where(f => f.UserId == userId && (!topicId.HasValue || f.TopicId == topicId.Value))
            .OrderByDescending(f => f.Importance)
            .ThenByDescending(f => f.CreatedAt)
            .Take(80)
            .ToListAsync(ct);

        if (_embedding == null || string.IsNullOrWhiteSpace(query))
        {
            return rows.Take(take).Select(ToDto).ToList();
        }

        try
        {
            var queryVector = await _embedding.EmbedAsync(query, "search_query", ct);
            return rows
                .Select(row => new
                {
                    Row = row,
                    Score = TryDeserializeVector(row.EmbeddingJson, out var vector)
                        ? _embedding.CosineSimilarity(queryVector, vector)
                        : 0f
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Row.Importance)
                .Take(take)
                .Select(x => ToDto(x.Row))
                .ToList();
        }
        catch
        {
            return rows.Take(take).Select(ToDto).ToList();
        }
    }

    private static bool TryDeserializeVector(string? json, out float[] vector)
    {
        try
        {
            vector = string.IsNullOrWhiteSpace(json)
                ? []
                : JsonSerializer.Deserialize<float[]>(json, JsonOptions) ?? [];
            return vector.Length > 0;
        }
        catch
        {
            vector = [];
            return false;
        }
    }

    private static string NormalizeFragmentType(string value)
    {
        var clean = (value ?? string.Empty).Trim().ToLowerInvariant();
        return clean is "misconception" or "preference_signal" or "solved_example" or "source_note" or "affective_signal" or "unresolved_question"
            ? clean
            : "source_note";
    }

    private static TutorMemoryFragmentDto ToDto(TutorMemoryFragment entity) => new()
    {
        Id = entity.Id,
        UserId = entity.UserId,
        TopicId = entity.TopicId,
        SessionId = entity.SessionId,
        FragmentType = entity.FragmentType,
        ConceptKey = entity.ConceptKey,
        Content = entity.Content,
        Source = entity.Source,
        Importance = entity.Importance,
        CreatedAt = entity.CreatedAt
    };
}

public sealed class RagEvaluationService : IRagEvaluationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IAIAgentFactory _factory;
    private readonly ILogger<RagEvaluationService> _logger;

    public RagEvaluationService(OrkaDbContext db, IAIAgentFactory factory, ILogger<RagEvaluationService> logger)
    {
        _db = db;
        _factory = factory;
        _logger = logger;
    }

    public async Task<RagEvaluationRunDto> EvaluateTopicAsync(
        Guid userId,
        Guid? topicId,
        Guid? conceptGraphSnapshotId = null,
        CancellationToken ct = default)
    {
        var retrievalRuns = await _db.SourceRetrievalRuns
            .AsNoTracking()
            .Include(r => r.Items)
            .Where(r => r.UserId == userId && (!topicId.HasValue || r.TopicId == topicId.Value))
            .OrderByDescending(r => r.CreatedAt)
            .Take(30)
            .ToListAsync(ct);

        var citationChecks = await _db.SourceCitationChecks
            .AsNoTracking()
            .Where(c => c.UserId == userId && (!topicId.HasValue || c.TopicId == topicId.Value))
            .OrderByDescending(c => c.CreatedAt)
            .Take(80)
            .ToListAsync(ct);

        var runCount = retrievalRuns.Count;
        var emptyRate = runCount == 0 ? 1m : Math.Round(retrievalRuns.Count(r => r.IsEmpty || r.QualityStatus == "source_retrieval_empty") / (decimal)runCount, 4);
        var contextRelevance = runCount == 0 ? 0m : Math.Round(retrievalRuns.Average(r => r.AverageScore), 4);
        var checkCount = citationChecks.Count;
        var supported = citationChecks.Count(c => c.CheckStatus == "supported");
        var unsupported = citationChecks.Count(c => c.CheckStatus == "citation_unsupported");
        var missing = citationChecks.Count(c => c.CheckStatus == "citation_missing");
        var citationCoverage = checkCount == 0 ? 0m : Math.Round(supported / (decimal)checkCount, 4);
        var citationSupport = supported + unsupported == 0 ? citationCoverage : Math.Round(supported / (decimal)(supported + unsupported), 4);
        var faithfulness = Math.Round(((contextRelevance * 0.40m) + (citationCoverage * 0.40m) + (citationSupport * 0.20m)) * (1m - (emptyRate * 0.35m)), 4);
        var answerRelevance = Math.Round(contextRelevance * (1m - (emptyRate * 0.50m)), 4);
        var judge = await TryRunLlmJudgeAsync(retrievalRuns, citationChecks, ct);
        if (judge is not null)
        {
            faithfulness = Math.Round((faithfulness * 0.65m) + (judge.Faithfulness * 0.35m), 4);
            answerRelevance = Math.Round((answerRelevance * 0.65m) + (judge.AnswerRelevance * 0.35m), 4);
        }

        var status = runCount == 0 ? "unverified" :
            checkCount == 0 ? "degraded_eval" :
            faithfulness >= 0.70m && contextRelevance >= 0.55m && missing == 0 && unsupported == 0 ? "healthy" :
            faithfulness >= 0.40m ? "degraded" : "unverified";

        var run = new RagEvaluationRun
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = conceptGraphSnapshotId,
            QualityStatus = status,
            FaithfulnessScore = faithfulness,
            ContextRelevanceScore = contextRelevance,
            AnswerRelevanceScore = answerRelevance,
            CitationCoverageScore = citationCoverage,
            ItemCount = Math.Max(runCount, checkCount),
            ReportJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.rag-eval.v2",
                retrievalRunCount = runCount,
                citationCheckCount = checkCount,
                emptyRate,
                unsupported,
                missing,
                judgeMode = judge is null ? "deterministic_only_degraded_eval" : "deterministic_plus_llm_judge",
                metrics = new { faithfulness, contextRelevance, answerRelevance, citationCoverage, citationSupport }
            }, JsonOptions),
            CreatedAt = DateTime.UtcNow
        };
        _db.RagEvaluationRuns.Add(run);

        _db.RagEvaluationItems.Add(new RagEvaluationItem
        {
            Id = Guid.NewGuid(),
            RagEvaluationRunId = run.Id,
            UserId = userId,
            TopicId = topicId,
            Query = "source rag quality evaluation",
            Answer = "latest wiki/source answers with retrieval and citation checks",
            ContextJson = JsonSerializer.Serialize(retrievalRuns.Take(8).Select(r => new
            {
                r.Id,
                r.Query,
                r.RetrievalScope,
                r.RetrievedCount,
                r.AverageScore,
                r.QualityStatus,
                items = r.Items.OrderBy(i => i.Rank).Take(3).Select(i => new { i.SourceId, i.SourceChunkId, i.PageNumber, i.Rank, i.FusedScore })
            }), JsonOptions),
            ExpectedCitationCount = checkCount,
            CitationCount = supported,
            FaithfulnessScore = faithfulness,
            ContextRelevanceScore = contextRelevance,
            AnswerRelevanceScore = answerRelevance,
            CitationCoverageScore = citationCoverage,
            Status = status,
            Notes = judge is null
                ? "Internal deterministic RAG eval from SourceRetrievalRuns and SourceCitationChecks. LLM judge unavailable or skipped; eval remains degraded when citation evidence is insufficient."
                : "Hybrid RAG eval: deterministic retrieval/citation checks plus optional LLM judge for faithfulness and answer relevance.",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[RagEvaluation] Created run {RunId} Status={Status}", run.Id, status);
        return ToDto(run);
    }

    public async Task<RagEvaluationRunDto?> GetLatestAsync(Guid userId, Guid? topicId, CancellationToken ct = default)
    {
        var row = await _db.RagEvaluationRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.TopicId == topicId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return row == null ? null : ToDto(row);
    }

    private async Task<RagJudgeScore?> TryRunLlmJudgeAsync(
        IReadOnlyList<SourceRetrievalRun> retrievalRuns,
        IReadOnlyList<SourceCitationCheck> citationChecks,
        CancellationToken ct)
    {
        if (retrievalRuns.Count == 0 || citationChecks.Count == 0)
            return null;

        try
        {
            var contexts = retrievalRuns
                .SelectMany(r => r.Items.OrderBy(i => i.Rank).Take(2).Select(i => i.Snippet))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(8)
                .ToArray();
            var answers = citationChecks
                .Select(c => c.Answer)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct()
                .Take(3)
                .ToArray();

            if (contexts.Length == 0 || answers.Length == 0)
                return null;

            var raw = await _factory.CompleteChatAsync(
                AgentRole.Evaluator,
                """
                You are a strict RAG quality judge. Return only compact JSON:
                {"faithfulness":0.0,"answerRelevance":0.0}
                Scores are decimals from 0 to 1. Penalize unsupported claims and irrelevant answers.
                """,
                JsonSerializer.Serialize(new
                {
                    contexts,
                    answers,
                    citations = citationChecks.Take(12).Select(c => new { c.CitationId, c.CheckStatus, c.ClaimText })
                }, JsonOptions),
                ct);

            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start < 0 || end <= start) return null;

            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var root = doc.RootElement;
            var faithfulness = root.TryGetProperty("faithfulness", out var f) && f.TryGetDecimal(out var fv) ? fv : 0m;
            var answerRelevance = root.TryGetProperty("answerRelevance", out var a) && a.TryGetDecimal(out var av) ? av : 0m;
            return new RagJudgeScore(
                Math.Clamp(faithfulness, 0m, 1m),
                Math.Clamp(answerRelevance, 0m, 1m));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[RagEvaluation] Optional LLM judge skipped.");
            return null;
        }
    }

    private static RagEvaluationRunDto ToDto(RagEvaluationRun run) => new()
    {
        Id = run.Id,
        UserId = run.UserId,
        TopicId = run.TopicId,
        ConceptGraphSnapshotId = run.ConceptGraphSnapshotId,
        QualityStatus = run.QualityStatus,
        FaithfulnessScore = run.FaithfulnessScore,
        ContextRelevanceScore = run.ContextRelevanceScore,
        AnswerRelevanceScore = run.AnswerRelevanceScore,
        CitationCoverageScore = run.CitationCoverageScore,
        ItemCount = run.ItemCount,
        CreatedAt = run.CreatedAt
    };

    private sealed record RagJudgeScore(decimal Faithfulness, decimal AnswerRelevance);
}

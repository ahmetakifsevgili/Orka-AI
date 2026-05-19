using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Exceptions;
using Orka.Core.Interfaces;
using Orka.Core.Services;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Security;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public class LearningSourceService : ILearningSourceService
{
    private static readonly Regex DocCitationRegex = new(@"\[doc:(?<sourceId>[^:\]]+):p(?<page>\d+)(?::c(?<chunk>\d+))?\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly OrkaDbContext _db;
    private readonly FileExtractionService _extractor;
    private readonly UploadContentSafetyGuard _contentSafety;
    private readonly IEmbeddingService _embedding;
    private readonly IAIAgentFactory _factory;
    private readonly IWikiService _wikiService;
    private readonly IRedisMemoryService _redis;
    private readonly ISummarizerAgent _summarizer;
    private readonly ILearningSignalService _signals;
    private readonly ITopicScopeResolver _topicScopeResolver;
    private readonly ILogger<LearningSourceService> _logger;

    private const int ApproxChunkChars = 2200;
    private sealed record RankedSourceChunk(
        SourceChunk Chunk,
        decimal EmbeddingScore,
        decimal LexicalScore,
        decimal FusedScore,
        decimal TopicScopeBoost,
        int Rank,
        string QualityStatus,
        string Reason,
        string ScopeRelation);
    public LearningSourceService(
        OrkaDbContext db,
        FileExtractionService extractor,
        UploadContentSafetyGuard contentSafety,
        IEmbeddingService embedding,
        IAIAgentFactory factory,
        IWikiService wikiService,
        IRedisMemoryService redis,
        ISummarizerAgent summarizer,
        ILearningSignalService signals,
        ITopicScopeResolver topicScopeResolver,
        ILogger<LearningSourceService> logger)
    {
        _db = db;
        _extractor = extractor;
        _contentSafety = contentSafety;
        _embedding = embedding;
        _factory = factory;
        _wikiService = wikiService;
        _redis = redis;
        _summarizer = summarizer;
        _signals = signals;
        _topicScopeResolver = topicScopeResolver;
        _logger = logger;
    }

    public async Task<LearningSourceSummaryDto> UploadAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken ct = default)
    {
        if (!topicId.HasValue && !sessionId.HasValue)
            throw new InvalidOperationException("Kaynak için topicId veya sessionId zorunlu.");

        if (!topicId.HasValue && sessionId.HasValue)
        {
            topicId = await _db.Sessions
                .Where(s => s.Id == sessionId.Value && s.UserId == userId)
                .Select(s => s.TopicId)
                .FirstOrDefaultAsync(ct);
        }

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        _contentSafety.ValidateBytes(fileName, contentType, bytes);
        var uploadMb = BytesToMegabytes(bytes.LongLength);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("Kullanıcı bulunamadı.");
        if (user.StorageLimitMB > 0 && user.StorageUsedMB + uploadMb > user.StorageLimitMB)
        {
            throw new StorageQuotaExceededException();
        }

        await ValidateUploadBackpressureAsync(userId, topicId, ct);

        var extracted = _extractor.ExtractWithPages(fileName, bytes);
        var source = new LearningSource
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            SourceType = "document",
            Title = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            ContentType = contentType,
            FileSizeBytes = bytes.LongLength,
            PageCount = extracted.PageCount,
            Status = string.IsNullOrWhiteSpace(extracted.ErrorMessage) ? "ready" : "error",
            ErrorMessage = extracted.ErrorMessage,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var chunks = string.IsNullOrWhiteSpace(extracted.ErrorMessage)
            ? BuildChunks(source.Id, extracted.Pages).ToList()
            : new List<SourceChunk>();
        _contentSafety.ValidateChunkCount(chunks.Count);
        await ValidateEmbeddingQuotaAsync(userId, chunks.Count, ct);

        if (chunks.Count > 0)
        {
            try
            {
                var embeddings = await _embedding.EmbedBatchAsync(chunks.Select(c => c.Text), "search_document", ct);
                for (var i = 0; i < Math.Min(chunks.Count, embeddings.Length); i++)
                    chunks[i].EmbeddingJson = JsonSerializer.Serialize(embeddings[i]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "[NotebookLM] Embedding uretilemedi, lexical fallback kullanilacak. FileRef={FileRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeTextRef(fileName, "file"),
                    LogPrivacyGuard.SafeExceptionType(ex));
            }
        }

        source.ChunkCount = chunks.Count;
        _db.LearningSources.Add(source);
        _db.SourceChunks.AddRange(chunks);
        if (source.Status == "ready")
        {
            user.StorageUsedMB += uploadMb;
        }
        await _db.SaveChangesAsync(ct);

        if (topicId.HasValue)
        {
            _summarizer.InvalidateNotebookTools(topicId.Value);
            await _signals.RecordSignalAsync(
                userId,
                topicId,
                sessionId,
                LearningSignalTypes.SourceUploaded,
                payloadJson: JsonSerializer.Serialize(new
                {
                    sourceId = source.Id,
                    fileName,
                    source.Status,
                    source.PageCount,
                    source.ChunkCount
                }),
                ct: ct);
        }

        return ToSummary(source);
    }

    public async Task<IReadOnlyList<LearningSourceSummaryDto>> GetTopicSourcesAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default)
    {
        return await _db.LearningSources
            .Where(s => s.UserId == userId && s.TopicId == topicId)
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new LearningSourceSummaryDto(
                s.Id,
                s.TopicId,
                s.SessionId,
                s.SourceType,
                s.Title,
                s.FileName,
                s.PageCount,
                s.ChunkCount,
                s.Status,
                s.CreatedAt,
                s.IsDeleted,
                s.Version))
            .ToListAsync(ct);
    }

    public async Task<SourceNotebookDto?> GetTopicSourceNotebookAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default)
    {
        var topicExists = await _db.Topics
            .AsNoTracking()
            .AnyAsync(t => t.Id == topicId && t.UserId == userId, ct);
        if (!topicExists) return null;

        var sources = await _db.LearningSources
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId && !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return await BuildSourceNotebookDtoAsync(userId, topicId, sources, focusedSourceId: null, ct);
    }

    public async Task<SourceNotebookDto?> GetSourceNotebookAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken ct = default)
    {
        var source = await _db.LearningSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId && !s.IsDeleted, ct);
        if (source == null || !source.TopicId.HasValue) return null;

        var sources = await _db.LearningSources
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == source.TopicId.Value && !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return await BuildSourceNotebookDtoAsync(userId, source.TopicId.Value, sources, sourceId, ct);
    }

    public async Task<LearningSourceSummaryDto?> UpdateSourceAsync(
        Guid userId,
        Guid sourceId,
        string? title,
        CancellationToken ct = default)
    {
        var source = await _db.LearningSources
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId && !s.IsDeleted, ct);
        if (source == null) return null;

        if (!string.IsNullOrWhiteSpace(title))
            source.Title = title.Trim();
        source.Version += 1;
        source.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (source.TopicId.HasValue)
        {
            _summarizer.InvalidateNotebookTools(source.TopicId.Value);
            await _redis.BumpTopicVersionAsync(source.TopicId.Value, "source-updated");
        }
        return ToSummary(source);
    }

    public async Task<bool> DeleteSourceAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken ct = default)
    {
        var source = await _db.LearningSources
            .Include(s => s.Chunks)
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId && !s.IsDeleted, ct);
        if (source == null) return false;

        source.IsDeleted = true;
        source.Status = "deleted";
        source.DeletedAt = DateTime.UtcNow;
        source.DeletedByUserId = userId;
        source.Version += 1;
        source.UpdatedAt = DateTime.UtcNow;
        foreach (var chunk in source.Chunks)
            chunk.IsDeleted = true;

        await _db.SaveChangesAsync(ct);
        await RecalculateStorageUsedAsync(userId, ct);
        await _db.SaveChangesAsync(ct);
        if (source.TopicId.HasValue)
        {
            _summarizer.InvalidateNotebookTools(source.TopicId.Value);
            await _redis.BumpTopicVersionAsync(source.TopicId.Value, "source-deleted");
        }
        return true;
    }

    public async Task<SourcePageDto?> GetPageAsync(
        Guid userId,
        Guid sourceId,
        int pageNumber,
        CancellationToken ct = default)
    {
        var source = await _db.LearningSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId && !s.IsDeleted, ct);
        if (source == null) return null;

        var chunks = await _db.SourceChunks
            .AsNoTracking()
            .Where(c => c.LearningSourceId == sourceId && c.PageNumber == pageNumber && !c.IsDeleted)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => new SourceChunkDto(c.Id, c.PageNumber, c.ChunkIndex, string.Empty, null))
            .ToListAsync(ct);

        await _signals.RecordSignalAsync(
            userId,
            source.TopicId,
            source.SessionId,
            LearningSignalTypes.SourceOpened,
            payloadJson: JsonSerializer.Serialize(new
            {
                sourceId = source.Id,
                pageNumber,
                source.FileName
            }),
            ct: ct);

        return new SourcePageDto(source.Id, pageNumber, source.Title, chunks);
    }

    public async Task<SourceAskResultDto> AskAsync(
        Guid userId,
        Guid sourceId,
        string question,
        CancellationToken ct = default)
    {
        var source = await _db.LearningSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId && !s.IsDeleted, ct);
        if (source == null) throw new InvalidOperationException("Kaynak bulunamadı.");

        var ranked = await FindRelevantChunksAsync(userId, source.TopicId, sourceId, question, 8, ct);
        var retrievalRun = await RecordRetrievalRunAsync(
            userId,
            source.TopicId,
            source.SessionId,
            sourceId,
            question,
            "source_ask",
            8,
            ranked,
            ct);
        var chunks = ranked.Select(r => r.Chunk).ToList();
        var citations = chunks
            .Select(c => new SourceChunkDto(c.Id, c.PageNumber, c.ChunkIndex, c.Text, c.HighlightHint))
            .ToList();
        if (chunks.Count == 0)
        {
            await RecordSourceAskedAsync(userId, source, question, citationCount: 0, ct);
            await RecordCitationChecksAsync(userId, source.TopicId, source.SessionId, retrievalRun.Id, sourceId, NotebookSourceContextFormatter.SourceMissingAnswer(), ranked, ct);
            var missingEvidenceQuality = EvidenceQualityEvaluator.Build(
                sourceCount: 1,
                readySourceCount: IsReadySource(source.Status) ? 1 : 0,
                retrievedEvidenceCount: 0,
                citationCoverage: 0m,
                unsupportedCitationCount: 0,
                citationMissingCount: 1,
                retrievalHealthStatus: retrievalRun.QualityStatus,
                citationCoverageStatus: "citation_missing");
            return new SourceAskResultDto(
                NotebookSourceContextFormatter.SourceMissingAnswer(),
                citations,
                new SourceMetadataDto([], "source_retrieval_empty", "source_retrieval_empty", null, retrievalRun.Id, "empty", 0, 1, missingEvidenceQuality));
        }

        var wiki = source.TopicId.HasValue
            ? await SafeWikiAsync(source.TopicId.Value, userId)
            : string.Empty;
        var korteks = source.TopicId.HasValue
            ? await _redis.GetKorteksResearchReportAsync(source.TopicId.Value) ?? string.Empty
            : string.Empty;

        var sourceContext = NotebookSourceContextFormatter.BuildSourceContext(chunks);
        var systemPrompt = $"""
            Sen Orka AI'nin belgeye kilitli (Strict Document Grounding) öğrenme ajanısın.

            [TEMEL KURAL - SADECE BELGE]:
            Kullanıcı sana soru sorduğunda YALNIZCA aşağıdaki [BELGE PARÇALARI] kısmındaki bilgileri kullan.
            Eğer sorunun cevabı belgede yoksa, hiçbir şekilde dış bilgi, hafıza veya genel kültür kullanma.
            Cevap belgede yoksa şunu söyle: "Bu bilgi yüklenen belgede yer almıyor."
            Asla uydurma kaynak, sayfa numarası veya alıntı üretme.

            [KAYNAK ETİKETLEME KURALI]:
            Belgedeki her bilgiyi kullanırken cümlenin sonuna ilgili parçanın tam etiketini ekle.
            Örnek format: "Hücre zarı yarı geçirgendir [doc:{sourceId}:p3]."

            Dil Türkçe, öğretici ve öz olsun.
            """;

        var userPrompt = $$"""
            Kullanıcı sorusu:
            {{question}}

            [BELGE PARÇALARI]
            {{sourceContext}}
            """;

        var answer = await _factory.CompleteChatAsync(AgentRole.Tutor, systemPrompt, userPrompt, ct);
        var citationChecks = await RecordCitationChecksAsync(userId, source.TopicId, source.SessionId, retrievalRun.Id, sourceId, answer, ranked, ct);
        if (citationChecks.Any(c => c.CheckStatus == "citation_missing"))
        {
            await _signals.RecordSignalAsync(
                userId,
                source.TopicId,
                source.SessionId,
                LearningSignalTypes.SourceCitationMissing,
                payloadJson: JsonSerializer.Serialize(new
                {
                    sourceId,
                    question,
                    reason = "source-ask-answer-without-doc-citation"
                }),
                ct: ct);
        }

        await RecordSourceAskedAsync(userId, source, question, citations.Count, ct);

        var unsupportedCount = citationChecks.Count(c => c.CheckStatus == "citation_unsupported");
        var missingCount = citationChecks.Count(c => c.CheckStatus == "citation_missing");
        var supportedCount = citationChecks.Count(c => c.CheckStatus == "supported");
        var sourceQuality = missingCount > 0 ? "citation_missing" :
            unsupportedCount > 0 ? "citation_unsupported" :
            retrievalRun.QualityStatus;
        var citationCoverage = citationChecks.Count == 0 ? 0m : Math.Round(supportedCount / (decimal)citationChecks.Count, 4);
        var evidenceQuality = EvidenceQualityEvaluator.Build(
            sourceCount: 1,
            readySourceCount: IsReadySource(source.Status) ? 1 : 0,
            retrievedEvidenceCount: ranked.Count,
            citationCoverage: citationCoverage,
            unsupportedCitationCount: unsupportedCount,
            citationMissingCount: missingCount,
            retrievalHealthStatus: retrievalRun.QualityStatus,
            citationCoverageStatus: missingCount > 0 ? "citation_missing" : unsupportedCount > 0 ? "citation_unsupported" : "healthy");

        return new SourceAskResultDto(answer, citations, new SourceMetadataDto(
            citations.Select(c => new Orka.Core.DTOs.Chat.CitationDto(
                $"[doc:{sourceId}:p{c.PageNumber}]",
                "document",
                sourceId,
                c.PageNumber,
                source.Title,
                null,
                supportedCount > 0 ? 1.0 : 0.65,
                c.Id,
                source.TopicId,
                null,
                "direct-source",
                "source_direct")).ToList(),
            citations.Count > 0 ? "source_grounded" : "model_fallback",
            missingCount > 0 ? "citation_missing" : unsupportedCount > 0 ? "citation_unsupported" : null,
            citations.Count > 0 ? 1.0 : null,
            retrievalRun.Id,
            sourceQuality,
            unsupportedCount,
            missingCount,
            evidenceQuality));
    }

    public async Task<IReadOnlyList<TopicSourceEvidenceDto>> RetrieveTopicEvidenceAsync(
        Guid userId,
        Guid topicId,
        string question,
        int take = 8,
        Guid? sourceId = null,
        CancellationToken ct = default)
    {
        var requestedTopK = Math.Clamp(take, 1, 20);
        var retrievalScope = sourceId.HasValue ? "source_direct" : "wiki_topic_tree";
        var ranked = await FindRelevantChunksAsync(userId, topicId, sourceId, question, requestedTopK, ct);
        var run = await RecordRetrievalRunAsync(
            userId,
            topicId,
            null,
            sourceId,
            question,
            retrievalScope,
            requestedTopK,
            ranked,
            ct);

        var sourceTopicIds = ranked
            .Select(r => r.Chunk.LearningSource.TopicId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var sourceTopicTitles = sourceTopicIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _db.Topics
                .AsNoTracking()
                .Where(t => t.UserId == userId && sourceTopicIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Title })
                .ToDictionaryAsync(t => t.Id, t => t.Title, ct);

        return ranked.Select(r => new TopicSourceEvidenceDto
        {
            RetrievalRunId = run.Id,
            ChunkId = r.Chunk.Id,
            SourceId = r.Chunk.LearningSourceId,
            SourceTitle = r.Chunk.LearningSource.Title,
            FileName = r.Chunk.LearningSource.FileName,
            SourceTopicId = r.Chunk.LearningSource.TopicId,
            SourceTopicTitle = r.Chunk.LearningSource.TopicId.HasValue &&
                               sourceTopicTitles.TryGetValue(r.Chunk.LearningSource.TopicId.Value, out var title)
                ? title
                : null,
            ScopeRelation = r.ScopeRelation,
            RetrievalScope = run.RetrievalScope,
            PageNumber = r.Chunk.PageNumber,
            ChunkIndex = r.Chunk.ChunkIndex,
            Text = r.Chunk.Text,
            HighlightHint = r.Chunk.HighlightHint,
            Score = (double)Math.Round(r.FusedScore, 4),
            EmbeddingScore = r.EmbeddingScore,
            LexicalScore = r.LexicalScore,
            FusedScore = r.FusedScore,
            Rank = r.Rank,
            QualityStatus = r.QualityStatus
        }).ToList();
    }

    public async Task<string> BuildTopicGroundingContextAsync(
        Guid userId,
        Guid? topicId,
        string question,
        CancellationToken ct = default)
    {
        if (!topicId.HasValue) return string.Empty;

        var ranked = await FindRelevantChunksAsync(userId, topicId.Value, null, question, 5, ct);
        await RecordRetrievalRunAsync(
            userId,
            topicId.Value,
            null,
            null,
            question,
            "tutor_topic_tree",
            5,
            ranked,
            ct);
        if (ranked.Count == 0) return string.Empty;

        var chunks = ranked.Select(r => r.Chunk).ToList();

        return $$"""

            [NOTEBOOKLM BELGE BAĞLAMI - KAYNAK ZORUNLU]:
            Aşağıdaki belge parçalarını kullanırsan her ilgili cümlenin sonuna ilgili parçanın gerçek [doc:...:p...] etiketini aynen ekle.
            Belgede olmayan bilgiyi belgeye mal etme; gerekiyorsa Wiki/Korteks diye ayır.
            {{NotebookSourceContextFormatter.BuildSourceContext(chunks)}}
            """;
    }

    public async Task<SourceQualityReportDto> GetTopicQualityAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default)
    {
        var sourceStats = await _db.LearningSources
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId && !s.IsDeleted)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                SourceCount = g.Count(),
                ReadySourceCount = g.Count(s => s.Status == "ready")
            })
            .FirstOrDefaultAsync(ct);
        var sourceCount = sourceStats?.SourceCount ?? 0;
        var readySourceCount = sourceStats?.ReadySourceCount ?? 0;

        var recentRuns = await _db.SourceRetrievalRuns
            .AsNoTracking()
            .Include(r => r.Items)
            .Where(r => r.UserId == userId && r.TopicId == topicId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(30)
            .ToListAsync(ct);

        var recentChecks = await _db.SourceCitationChecks
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.TopicId == topicId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var latestReport = await _db.SourceQualityReports
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.TopicId == topicId)
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        var latestEvidenceAt = recentRuns.Select(r => r.CreatedAt)
            .Concat(recentChecks.Select(c => c.CreatedAt))
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        if (latestReport != null &&
            latestReport.GeneratedAt >= latestEvidenceAt &&
            TryDeserializeQualityReport(latestReport.ReportJson, out var cached))
        {
            HydrateEvidenceQuality(cached, sourceCount, readySourceCount);
            return cached;
        }

        var runCount = recentRuns.Count;
        var emptyCount = recentRuns.Count(r => r.IsEmpty || r.QualityStatus == "source_retrieval_empty" || r.QualityStatus == "empty");
        var avgContext = runCount == 0 ? 0m : Math.Round(recentRuns.Average(r => r.AverageScore), 4);
        var unsupported = recentChecks.Count(c => c.CheckStatus == "citation_unsupported");
        var missing = recentChecks.Count(c => c.CheckStatus == "citation_missing");
        var supported = recentChecks.Count(c => c.CheckStatus == "supported");
        var checkCount = recentChecks.Count;
        var citationCoverage = checkCount == 0 ? 0m : Math.Round(supported / (decimal)checkCount, 4);
        var retrievalHealth = runCount == 0 ? "unverified" :
            emptyCount == runCount ? "source_retrieval_empty" :
            recentRuns.Any(r => r.QualityStatus == "low_confidence") ? "low_confidence" :
            "healthy";
        var citationStatus = checkCount == 0 ? "unverified" :
            missing > 0 ? "citation_missing" :
            unsupported > 0 ? "citation_unsupported" :
            "healthy";
        var supportStatus = checkCount == 0 ? "unverified" :
            unsupported > 0 ? "citation_unsupported" :
            supported > 0 ? "supported" :
            "unverified";
        var quality = retrievalHealth == "healthy" && citationStatus == "healthy"
            ? "healthy"
            : runCount == 0 ? "unverified" : "degraded";
        var retrievedEvidenceCount = recentRuns.Sum(r => r.RetrievedCount);
        var evidenceQuality = EvidenceQualityEvaluator.Build(
            sourceCount,
            readySourceCount,
            retrievedEvidenceCount,
            citationCoverage,
            unsupported,
            missing,
            retrievalHealth,
            citationStatus);

        var dto = new SourceQualityReportDto
        {
            Id = Guid.NewGuid(),
            TopicId = topicId,
            QualityStatus = quality,
            RetrievalHealthStatus = retrievalHealth,
            CitationCoverageStatus = citationStatus,
            CitationSupportStatus = supportStatus,
            RetrievalRunCount = runCount,
            EmptyRunCount = emptyCount,
            CitationCheckCount = checkCount,
            UnsupportedCitationCount = unsupported,
            CitationMissingCount = missing,
            AverageContextRelevance = avgContext,
            CitationCoverage = citationCoverage,
            EvidenceQuality = evidenceQuality,
            GeneratedAt = DateTimeOffset.UtcNow,
            RecentRetrievalRuns = recentRuns.Take(8).Select(ToRetrievalRunDto).ToArray(),
            RecentCitationChecks = recentChecks.Take(12).Select(ToCitationCheckDto).ToArray()
        };

        _db.SourceQualityReports.Add(new SourceQualityReport
        {
            Id = dto.Id,
            UserId = userId,
            TopicId = topicId,
            QualityStatus = dto.QualityStatus,
            RetrievalHealthStatus = dto.RetrievalHealthStatus,
            CitationCoverageStatus = dto.CitationCoverageStatus,
            CitationSupportStatus = dto.CitationSupportStatus,
            RetrievalRunCount = dto.RetrievalRunCount,
            EmptyRunCount = dto.EmptyRunCount,
            CitationCheckCount = dto.CitationCheckCount,
            UnsupportedCitationCount = dto.UnsupportedCitationCount,
            CitationMissingCount = dto.CitationMissingCount,
            AverageContextRelevance = dto.AverageContextRelevance,
            CitationCoverage = dto.CitationCoverage,
            ReportJson = JsonSerializer.Serialize(dto),
            GeneratedAt = dto.GeneratedAt.UtcDateTime,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        return dto;
    }

    private async Task<List<RankedSourceChunk>> FindRelevantChunksAsync(
        Guid userId,
        Guid? topicId,
        Guid? sourceId,
        string question,
        int take,
        CancellationToken ct)
    {
        var query = _db.SourceChunks
            .AsNoTracking()
            .Include(c => c.LearningSource)
            .Where(c =>
                c.LearningSource.UserId == userId &&
                !c.LearningSource.IsDeleted &&
                c.LearningSource.Status == "ready" &&
                !c.IsDeleted);

        TopicScope? topicScope = null;
        if (sourceId.HasValue)
        {
            query = query.Where(c => c.LearningSourceId == sourceId.Value);
        }
        else if (topicId.HasValue)
        {
            topicScope = await _topicScopeResolver.ResolveAsync(userId, topicId.Value, ct);
            if (!topicScope.IsValid) return [];

            var scopedTopicIds = new[] { topicScope.CurrentTopicId }
                .Concat(topicScope.AncestorTopicIds)
                .Concat(topicScope.DescendantTopicIds)
                .Distinct()
                .ToArray();

            query = query.Where(c => c.LearningSource.TopicId.HasValue && scopedTopicIds.Contains(c.LearningSource.TopicId.Value));
        }

        var chunks = await query
            .OrderBy(c => c.LearningSource.TopicId)
            .ThenBy(c => c.LearningSource.CreatedAt)
            .ThenBy(c => c.LearningSourceId)
            .ThenBy(c => c.PageNumber)
            .ThenBy(c => c.ChunkIndex)
            .ThenBy(c => c.Id)
            .Take(400)
            .ToListAsync(ct);
        if (chunks.Count == 0) return [];

        float[]? qEmbedding = null;
        try
        {
            qEmbedding = await _embedding.EmbedAsync(question, "search_query", ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                "[NotebookLM] Query embedding uretilemedi, lexical scoring kullanilacak. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
        }

        var scored = chunks
            .Select(c => ScoreChunk(c, question, qEmbedding, topicScope, sourceId.HasValue))
            .ToList();

        var selected = sourceId.HasValue || topicScope is null
            ? OrderByScoreStable(scored).Take(take)
            : ApplyScopedQuota(scored, take);

        return selected
            .Take(take)
            .Select((x, i) => x with { Rank = i + 1 })
            .ToList();
    }

    private static IReadOnlyList<RankedSourceChunk> ApplyScopedQuota(
        IReadOnlyCollection<RankedSourceChunk> candidates,
        int take)
    {
        if (take <= 0 || candidates.Count == 0)
            return [];

        var selected = new List<RankedSourceChunk>(take);
        var selectedIds = new HashSet<Guid>();

        var current = OrderByScoreStable(candidates.Where(c => c.ScopeRelation == "current")).ToList();
        var ancestors = OrderByScoreStable(candidates.Where(c => c.ScopeRelation == "ancestor")).ToList();
        var descendants = OrderByScoreStable(candidates.Where(c => c.ScopeRelation == "descendant")).ToList();
        var other = OrderByScoreStable(candidates.Where(c =>
            c.ScopeRelation is not "current" and not "ancestor" and not "descendant")).ToList();

        var currentQuota = current.Count == 0 ? 0 : Math.Min(current.Count, Math.Max(1, (int)Math.Ceiling(take * 0.40m)));
        AddFrom(current, currentQuota);

        var ancestorQuota = ancestors.Count == 0 || selected.Count >= take
            ? 0
            : Math.Min(ancestors.Count, Math.Min(take - selected.Count, Math.Max(1, (int)Math.Floor(take * 0.30m))));
        AddFrom(ancestors, ancestorQuota);

        if (selected.Count < take)
        {
            AddFrom(descendants, take - selected.Count);
        }

        if (selected.Count < take)
        {
            AddFrom(other, take - selected.Count);
        }

        if (selected.Count < take)
        {
            AddFrom(OrderByScoreStable(candidates).ToList(), take - selected.Count);
        }

        return selected;

        void AddFrom(IEnumerable<RankedSourceChunk> source, int count)
        {
            var remaining = count;
            foreach (var item in source)
            {
                if (selected.Count >= take || remaining <= 0)
                    break;

                if (!selectedIds.Add(item.Chunk.Id))
                    continue;

                selected.Add(item);
                remaining--;
            }
        }
    }

    private static IOrderedEnumerable<RankedSourceChunk> OrderByScoreStable(IEnumerable<RankedSourceChunk> candidates) =>
        candidates
            .OrderBy(x => ScopeRelationPriority(x.ScopeRelation))
            .ThenByDescending(x => x.FusedScore + x.TopicScopeBoost)
            .ThenByDescending(x => x.FusedScore)
            .ThenBy(x => x.Chunk.PageNumber)
            .ThenBy(x => x.Chunk.ChunkIndex)
            .ThenBy(x => x.Chunk.LearningSource.CreatedAt)
            .ThenBy(x => x.Chunk.LearningSourceId)
            .ThenBy(x => x.Chunk.Id);

    private static int ScopeRelationPriority(string relation) => relation switch
    {
        "direct-source" => 0,
        "current" => 0,
        "ancestor" => 1,
        "descendant" => 2,
        _ => 3
    };

    private async Task ValidateUploadBackpressureAsync(Guid userId, Guid? topicId, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-1);
        var recentUploads = await _db.LearningSources
            .AsNoTracking()
            .CountAsync(s => s.UserId == userId && !s.IsDeleted && s.CreatedAt >= since, ct);
        if (recentUploads >= _contentSafety.Options.MaxUploadsPerUserPerHour)
            throw Core.Exceptions.ContentSafetyException.TooManyRequests("Kaynak yukleme limiti asildi.");

        if (topicId.HasValue)
        {
            var topicSources = await _db.LearningSources
                .AsNoTracking()
                .CountAsync(s => s.UserId == userId && s.TopicId == topicId.Value && !s.IsDeleted, ct);
            if (topicSources >= _contentSafety.Options.MaxSourcesPerTopic)
                throw Core.Exceptions.ContentSafetyException.TooManyRequests("Konu kaynak limiti asildi.");
        }
    }

    private async Task ValidateEmbeddingQuotaAsync(Guid userId, int newChunkCount, CancellationToken ct)
    {
        if (newChunkCount <= 0) return;

        var today = DateTime.UtcNow.Date;
        var usedChunks = await _db.LearningSources
            .AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDeleted && s.CreatedAt >= today)
            .SumAsync(s => s.ChunkCount, ct);

        if (usedChunks + newChunkCount > _contentSafety.Options.MaxEmbeddingChunksPerUserPerDay)
            throw Core.Exceptions.ContentSafetyException.TooManyRequests("Gunluk embedding kotasi asildi.");
    }

    private RankedSourceChunk ScoreChunk(SourceChunk chunk, string question, float[]? qEmbedding, TopicScope? topicScope, bool isDirectSource)
    {
        var lexical = (decimal)Math.Round(NotebookSourceContextFormatter.ScoreLexical(chunk, question), 4);
        var embedding = 0m;
        if (qEmbedding != null && !string.IsNullOrWhiteSpace(chunk.EmbeddingJson))
        {
            try
            {
                var emb = JsonSerializer.Deserialize<float[]>(chunk.EmbeddingJson);
                if (emb is { Length: > 0 } && emb.Length == qEmbedding.Length)
                    embedding = (decimal)Math.Round(_embedding.CosineSimilarity(qEmbedding, emb), 4);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(
                    "[NotebookLM] Chunk embedding deserialize edilemedi. ChunkRef={ChunkRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeId(chunk.Id, "chunk"),
                    LogPrivacyGuard.SafeExceptionType(ex));
            }
        }

        var fused = embedding > 0m
            ? Math.Round((embedding * 0.75m) + (lexical * 0.25m), 4)
            : lexical;
        var quality = fused >= 0.65m ? "healthy" :
            fused >= 0.35m ? "degraded" :
            "low_confidence";
        var reason = embedding > 0m ? "embedding_lexical_fusion" : "lexical_fallback";
        return new RankedSourceChunk(
            chunk,
            embedding,
            lexical,
            fused,
            CalculateTopicScopeBoost(chunk, topicScope),
            0,
            quality,
            reason,
            ResolveScopeRelation(chunk, topicScope, isDirectSource));
    }

    private static string ResolveScopeRelation(SourceChunk chunk, TopicScope? topicScope, bool isDirectSource)
    {
        if (isDirectSource)
            return "direct-source";

        if (topicScope is null || !chunk.LearningSource.TopicId.HasValue)
            return "unknown";

        var sourceTopicId = chunk.LearningSource.TopicId.Value;
        if (sourceTopicId == topicScope.CurrentTopicId)
            return "current";
        if (topicScope.AncestorTopicIds.Contains(sourceTopicId))
            return "ancestor";
        if (topicScope.DescendantTopicIds.Contains(sourceTopicId))
            return "descendant";

        return "unknown";
    }

    private static decimal CalculateTopicScopeBoost(SourceChunk chunk, TopicScope? topicScope)
    {
        if (topicScope is null || !chunk.LearningSource.TopicId.HasValue)
            return 0m;

        var sourceTopicId = chunk.LearningSource.TopicId.Value;
        if (sourceTopicId == topicScope.CurrentTopicId)
            return 0.05m;
        if (topicScope.AncestorTopicIds.Contains(sourceTopicId))
            return 0.02m;
        if (topicScope.DescendantTopicIds.Contains(sourceTopicId))
            return 0.01m;

        return 0m;
    }

    private async Task<SourceRetrievalRun> RecordRetrievalRunAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        Guid? sourceId,
        string query,
        string scope,
        int requestedTopK,
        IReadOnlyList<RankedSourceChunk> ranked,
        CancellationToken ct)
    {
        var maxScore = ranked.Count == 0 ? 0m : ranked.Max(r => r.FusedScore);
        var averageScore = ranked.Count == 0 ? 0m : Math.Round(ranked.Average(r => r.FusedScore), 4);
        var qualityStatus = ranked.Count == 0 ? "source_retrieval_empty" :
            ranked.All(r => r.QualityStatus == "low_confidence") ? "low_confidence" :
            ranked.Any(r => r.QualityStatus == "healthy") ? "healthy" :
            "degraded";

        var run = new SourceRetrievalRun
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            SourceId = sourceId,
            Query = TrimForStorage(query, 2000),
            RetrievalScope = scope,
            Provider = "orka-source",
            RequestedTopK = requestedTopK,
            RetrievedCount = ranked.Count,
            IsEmpty = ranked.Count == 0,
            MaxScore = maxScore,
            AverageScore = averageScore,
            QualityStatus = qualityStatus,
            Reason = ranked.Count == 0 ? "no_matching_source_chunks" : string.Join(",", ranked.Select(r => r.Reason).Distinct().Take(3)),
            MetadataJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.source-retrieval.v1",
                requestedTopK,
                scoreModel = "embedding_lexical_fusion",
                lowConfidenceThreshold = 0.35m,
                healthyThreshold = 0.65m,
                scopeRelations = ranked.Select(r => r.ScopeRelation).Distinct().ToArray(),
                sourceTopicIds = ranked
                    .Select(r => r.Chunk.LearningSource.TopicId)
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .Distinct()
                    .ToArray()
            }),
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };

        _db.SourceRetrievalRuns.Add(run);
        foreach (var item in ranked)
        {
            _db.SourceRetrievalItems.Add(new SourceRetrievalItem
            {
                Id = Guid.NewGuid(),
                SourceRetrievalRunId = run.Id,
                UserId = userId,
                TopicId = topicId,
                SourceId = item.Chunk.LearningSourceId,
                SourceChunkId = item.Chunk.Id,
                PageNumber = item.Chunk.PageNumber,
                ChunkIndex = item.Chunk.ChunkIndex,
                Rank = item.Rank,
                EmbeddingScore = item.EmbeddingScore,
                LexicalScore = item.LexicalScore,
                FusedScore = item.FusedScore,
                QualityStatus = item.QualityStatus,
                Reason = item.Reason,
                Snippet = TrimForStorage(item.Chunk.HighlightHint ?? item.Chunk.Text, 600),
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
        return run;
    }

    private async Task<IReadOnlyList<SourceCitationCheck>> RecordCitationChecksAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        Guid? retrievalRunId,
        Guid? expectedSourceId,
        string answer,
        IReadOnlyList<RankedSourceChunk> ranked,
        CancellationToken ct)
    {
        var checks = new List<SourceCitationCheck>();
        var matches = DocCitationRegex.Matches(answer ?? string.Empty);
        if (matches.Count == 0)
        {
            checks.Add(new SourceCitationCheck
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                SessionId = sessionId,
                SourceRetrievalRunId = retrievalRunId,
                SourceId = expectedSourceId,
                CitationId = string.Empty,
                SourceType = "document",
                Answer = TrimForStorage(answer, 4000),
                ClaimText = string.Empty,
                CheckStatus = "citation_missing",
                Confidence = 0m,
                Reason = "answer_contains_no_document_citation",
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            foreach (Match match in matches)
            {
                var citationId = match.Value;
                var sourceText = match.Groups["sourceId"].Value;
                var page = int.TryParse(match.Groups["page"].Value, out var pageNumber) ? pageNumber : (int?)null;
                var chunk = int.TryParse(match.Groups["chunk"].Value, out var chunkIndex) ? chunkIndex : (int?)null;
                var sourceGuid = Guid.TryParse(sourceText, out var parsedSourceId) ? parsedSourceId : (Guid?)null;
                var supported = sourceGuid.HasValue && page.HasValue && ranked.Any(r =>
                    r.Chunk.LearningSourceId == sourceGuid.Value &&
                    r.Chunk.PageNumber == page.Value &&
                    (!chunk.HasValue || r.Chunk.ChunkIndex == chunk.Value));
                var matched = supported
                    ? ranked.First(r => r.Chunk.LearningSourceId == sourceGuid!.Value &&
                                        r.Chunk.PageNumber == page!.Value &&
                                        (!chunk.HasValue || r.Chunk.ChunkIndex == chunk.Value))
                    : null;

                checks.Add(new SourceCitationCheck
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    TopicId = topicId,
                    SessionId = sessionId,
                    SourceRetrievalRunId = retrievalRunId,
                    SourceId = sourceGuid ?? expectedSourceId,
                    SourceChunkId = matched?.Chunk.Id,
                    CitationId = citationId,
                    SourceType = "document",
                    PageNumber = page,
                    ChunkIndex = chunk,
                    Answer = TrimForStorage(answer, 4000),
                    ClaimText = ExtractClaimNearCitation(answer ?? string.Empty, match.Index),
                    CheckStatus = supported ? "supported" : "citation_unsupported",
                    Confidence = supported ? matched!.FusedScore : 0m,
                    Reason = supported ? "citation_matches_retrieved_chunk" : "citation_not_in_retrieved_evidence",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        _db.SourceCitationChecks.AddRange(checks);
        await _db.SaveChangesAsync(ct);
        return checks;
    }

    private static SourceRetrievalRunDto ToRetrievalRunDto(SourceRetrievalRun run) => new()
    {
        Id = run.Id,
        TopicId = run.TopicId,
        SessionId = run.SessionId,
        SourceId = run.SourceId,
        Query = run.Query,
        RetrievalScope = run.RetrievalScope,
        RequestedTopK = run.RequestedTopK,
        RetrievedCount = run.RetrievedCount,
        IsEmpty = run.IsEmpty,
        MaxScore = run.MaxScore,
        AverageScore = run.AverageScore,
        QualityStatus = run.QualityStatus,
        Reason = run.Reason,
        CreatedAt = run.CreatedAt,
        Items = run.Items
            .OrderBy(i => i.Rank)
            .Select(ToRetrievalItemDto)
            .ToArray()
    };

    private static bool TryDeserializeQualityReport(string? json, out SourceQualityReportDto dto)
    {
        try
        {
            dto = string.IsNullOrWhiteSpace(json)
                ? new SourceQualityReportDto()
                : JsonSerializer.Deserialize<SourceQualityReportDto>(json) ?? new SourceQualityReportDto();
            return !string.IsNullOrWhiteSpace(dto.QualityStatus);
        }
        catch
        {
            dto = new SourceQualityReportDto();
            return false;
        }
    }

    private static void HydrateEvidenceQuality(SourceQualityReportDto dto, int sourceCount, int readySourceCount)
    {
        dto.EvidenceQuality ??= EvidenceQualityEvaluator.Build(
            sourceCount,
            readySourceCount,
            dto.RecentRetrievalRuns?.Sum(r => r.RetrievedCount) ?? dto.RetrievalRunCount,
            dto.CitationCoverage,
            dto.UnsupportedCitationCount,
            dto.CitationMissingCount,
            dto.RetrievalHealthStatus,
            dto.CitationCoverageStatus);
    }

    private static bool IsReadySource(string? status) =>
        string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase);

    private static SourceRetrievalItemDto ToRetrievalItemDto(SourceRetrievalItem item) => new()
    {
        Id = item.Id,
        SourceRetrievalRunId = item.SourceRetrievalRunId,
        SourceId = item.SourceId,
        SourceChunkId = item.SourceChunkId,
        PageNumber = item.PageNumber,
        ChunkIndex = item.ChunkIndex,
        Rank = item.Rank,
        EmbeddingScore = item.EmbeddingScore,
        LexicalScore = item.LexicalScore,
        FusedScore = item.FusedScore,
        QualityStatus = item.QualityStatus,
        Reason = item.Reason,
        Snippet = item.Snippet
    };

    private static SourceCitationCheckDto ToCitationCheckDto(SourceCitationCheck check) => new()
    {
        Id = check.Id,
        SourceRetrievalRunId = check.SourceRetrievalRunId,
        SourceId = check.SourceId,
        SourceChunkId = check.SourceChunkId,
        CitationId = check.CitationId,
        SourceType = check.SourceType,
        PageNumber = check.PageNumber,
        ChunkIndex = check.ChunkIndex,
        CheckStatus = check.CheckStatus,
        Confidence = check.Confidence,
        Reason = check.Reason,
        CreatedAt = check.CreatedAt
    };

    private static string ExtractClaimNearCitation(string answer, int citationIndex)
    {
        if (string.IsNullOrWhiteSpace(answer)) return string.Empty;
        var start = Math.Max(0, citationIndex - 240);
        var length = Math.Min(answer.Length - start, 320);
        return TrimForStorage(answer.Substring(start, length), 320);
    }

    private static string TrimForStorage(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var clean = value.Trim();
        return clean.Length <= maxChars ? clean : clean[..maxChars];
    }

    private static IEnumerable<SourceChunk> BuildChunks(Guid sourceId, IReadOnlyList<ExtractedPage> pages)
    {
        var chunkIndex = 0;
        foreach (var page in pages)
        {
            var normalized = NormalizeWhitespace(page.Text);
            for (var start = 0; start < normalized.Length; start += ApproxChunkChars)
            {
                var len = Math.Min(ApproxChunkChars, normalized.Length - start);
                var text = normalized.Substring(start, len).Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                yield return new SourceChunk
                {
                    Id = Guid.NewGuid(),
                    LearningSourceId = sourceId,
                    PageNumber = page.PageNumber,
                    ChunkIndex = chunkIndex++,
                    Text = text,
                    HighlightHint = text.Length > 180 ? text[..180] : text,
                    CreatedAt = DateTime.UtcNow
                };
            }
        }
    }

    private async Task<SourceNotebookDto> BuildSourceNotebookDtoAsync(
        Guid userId,
        Guid topicId,
        IReadOnlyList<LearningSource> sources,
        Guid? focusedSourceId,
        CancellationToken ct)
    {
        var topicTitle = await _db.Topics
            .AsNoTracking()
            .Where(t => t.Id == topicId && t.UserId == userId)
            .Select(t => t.Title)
            .FirstOrDefaultAsync(ct);

        var sourceIds = sources.Select(s => s.Id).ToHashSet();
        var latestBundle = await _db.SourceEvidenceBundles
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.TopicId == topicId && !b.IsDeleted)
            .OrderByDescending(b => b.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var linkedPages = await _db.WikiPages
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.TopicId == topicId && !p.IsDeleted)
            .Where(p => p.PageType == "orkalm_source" || p.PageType == "source_note")
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new SourceNotebookWikiPageDto
            {
                Id = p.Id,
                Title = p.Title,
                PageKey = p.PageKey,
                PageType = p.PageType,
                SourceReadiness = p.SourceReadiness,
                EvidenceStatus = p.EvidenceStatus
            })
            .ToListAsync(ct);

        var packs = await _db.LearningNotebookPacks
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.TopicId == topicId && !p.IsDeleted)
            .Where(p =>
                p.PackType == "source_digest" ||
                p.PackType == "source_notebook" ||
                p.SafeMetadataJson.Contains("sourceSurface"))
            .OrderByDescending(p => p.UpdatedAt)
            .Take(40)
            .ToListAsync(ct);

        if (focusedSourceId.HasValue)
        {
            packs = packs
                .Where(p => TryReadPackSourceId(p.SafeMetadataJson) == focusedSourceId.Value)
                .ToList();
        }

        var packRefs = packs
            .Select(p => new SourceNotebookPackRefDto
            {
                Id = p.Id,
                PackType = p.PackType,
                PackStatus = p.PackStatus,
                Title = p.Title,
                SourceId = TryReadPackSourceId(p.SafeMetadataJson),
                WikiPageId = p.WikiPageId,
                SourceReadiness = p.SourceReadiness,
                EvidenceStatus = p.EvidenceStatus,
                UpdatedAt = new DateTimeOffset(p.UpdatedAt, TimeSpan.Zero)
            })
            .ToList();

        var sourceDtos = sources.Select(source =>
        {
            var sourceReadiness = ResolveSourceReadiness(source);
            var evidenceStatus = ResolveSourceEvidenceStatus(source, latestBundle);
            var page = FindLinkedSourcePage(linkedPages, source);
            var latestPack = packRefs.FirstOrDefault(p => p.SourceId == source.Id);
            return new SourceNotebookSourceDto
            {
                Id = source.Id,
                TopicId = source.TopicId,
                SessionId = source.SessionId,
                Title = SafeDisplay(source.Title, 120),
                FileName = SafeFileName(source.FileName, 120),
                Status = SafeStatus(source.Status),
                SourceReadiness = sourceReadiness,
                EvidenceStatus = evidenceStatus,
                PageCount = source.PageCount,
                ChunkCount = source.ChunkCount,
                CitationCoverage = source.Status == "ready" && source.ChunkCount > 0
                    ? Math.Max(latestBundle?.CitationCoverage ?? 0m, 0.65m)
                    : 0m,
                LinkedWikiPageId = page?.Id,
                LinkedWikiPageTitle = page?.Title,
                LatestPackId = latestPack?.Id,
                Warnings = BuildSourceWarnings(source, latestBundle),
                CreatedAt = new DateTimeOffset(source.CreatedAt, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(source.UpdatedAt, TimeSpan.Zero)
            };
        }).ToList();

        var readyCount = sources.Count(s => string.Equals(s.Status, "ready", StringComparison.OrdinalIgnoreCase) && s.ChunkCount > 0);
        var warnings = BuildNotebookWarnings(sources, latestBundle);
        var focused = focusedSourceId.HasValue ? sources.FirstOrDefault(s => s.Id == focusedSourceId.Value) : null;
        var title = focused != null
            ? $"{SafeDisplay(focused.Title, 120)} source notebook"
            : $"{SafeDisplay(topicTitle, 120)} source notebook";

        return new SourceNotebookDto
        {
            TopicId = topicId,
            SourceId = focusedSourceId,
            Surface = focusedSourceId.HasValue ? "source" : "source_collection",
            Title = string.IsNullOrWhiteSpace(title) ? "OrkaLM source notebook" : title,
            SourceReadiness = ResolveNotebookReadiness(sources, latestBundle),
            EvidenceStatus = latestBundle?.EvidenceStatus ?? (readyCount > 0 ? "source_grounded" : "evidence_insufficient"),
            SourceCount = sources.Count,
            ReadySourceCount = readyCount,
            ChunkCount = sources.Sum(s => s.ChunkCount),
            CitationCoverage = latestBundle?.CitationCoverage ?? (readyCount > 0 ? 0.65m : 0m),
            Warnings = warnings,
            Sources = focusedSourceId.HasValue
                ? sourceDtos.Where(s => s.Id == focusedSourceId.Value).ToList()
                : sourceDtos,
            LinkedWikiPages = linkedPages,
            Packs = packRefs,
            NextActions = BuildSourceNotebookActions(readyCount, latestBundle?.EvidenceStatus),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static SourceNotebookWikiPageDto? FindLinkedSourcePage(
        IReadOnlyList<SourceNotebookWikiPageDto> pages,
        LearningSource source)
    {
        var sourceKey = source.Id.ToString("N");
        return pages.FirstOrDefault(p =>
            p.PageKey.Contains(sourceKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Title, source.Title, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<NotebookStudioNextActionDto> BuildSourceNotebookActions(int readySourceCount, string? evidenceStatus)
    {
        if (readySourceCount <= 0)
        {
            return new[]
            {
                new NotebookStudioNextActionDto
                {
                    ActionType = "add_source",
                    UserSafeLabel = "Add or repair a source before claiming source-backed notes.",
                    Priority = "high"
                }
            };
        }

        var actions = new List<NotebookStudioNextActionDto>
        {
            new() { ActionType = "source_digest", UserSafeLabel = "Create a source digest.", Priority = "high" },
            new() { ActionType = "study_guide", UserSafeLabel = "Turn this source into a study guide.", Priority = "high" },
            new() { ActionType = "audio_script", UserSafeLabel = "Create a safe audio script or overview.", Priority = "normal" },
            new() { ActionType = "review_quiz", UserSafeLabel = "Start a source-based review quiz.", Priority = "normal" },
            new() { ActionType = "slide_deck_outline", UserSafeLabel = "Build a source-backed slide outline.", Priority = "normal" }
        };

        if (string.Equals(evidenceStatus, "stale", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evidenceStatus, "degraded", StringComparison.OrdinalIgnoreCase))
        {
            actions.Insert(0, new NotebookStudioNextActionDto
            {
                ActionType = "refresh_evidence",
                UserSafeLabel = "Refresh source evidence before relying on citations.",
                Priority = "high"
            });
        }

        return actions;
    }

    private static IReadOnlyList<string> BuildNotebookWarnings(IReadOnlyList<LearningSource> sources, SourceEvidenceBundle? bundle)
    {
        var warnings = new List<string>();
        if (sources.Count == 0) warnings.Add("No uploaded sources are available for this notebook.");
        if (sources.All(s => !string.Equals(s.Status, "ready", StringComparison.OrdinalIgnoreCase) || s.ChunkCount <= 0))
            warnings.Add("No ready source evidence is available; source-backed claims are disabled.");
        if (bundle == null) warnings.Add("No source evidence bundle has been built yet.");
        else
        {
            if (bundle.StaleEvidenceCount > 0) warnings.Add("Some source evidence is stale.");
            if (bundle.DeletedEvidenceCount > 0) warnings.Add("Some source evidence points to deleted sources.");
            if (bundle.UnsupportedCitationCount > 0) warnings.Add("Some citations need review.");
            if (string.Equals(bundle.EvidenceStatus, "evidence_insufficient", StringComparison.OrdinalIgnoreCase))
                warnings.Add("Evidence is insufficient for source-grounded output.");
        }

        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> BuildSourceWarnings(LearningSource source, SourceEvidenceBundle? bundle)
    {
        var warnings = new List<string>();
        if (!string.Equals(source.Status, "ready", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Source is not ready.");
        if (source.ChunkCount <= 0)
            warnings.Add("Source has no indexed chunks.");
        if (string.Equals(source.Status, "stale", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Source is stale and should be refreshed.");
        if (bundle is { EvidenceStatus: "evidence_insufficient" })
            warnings.Add("Current evidence bundle is insufficient.");
        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ResolveNotebookReadiness(IReadOnlyList<LearningSource> sources, SourceEvidenceBundle? bundle)
    {
        if (bundle != null && !string.IsNullOrWhiteSpace(bundle.EvidenceStatus))
            return bundle.EvidenceStatus;
        return sources.Any(s => string.Equals(s.Status, "ready", StringComparison.OrdinalIgnoreCase) && s.ChunkCount > 0)
            ? "source_grounded"
            : "evidence_insufficient";
    }

    private static string ResolveSourceEvidenceStatus(LearningSource source, SourceEvidenceBundle? bundle)
    {
        if (!string.Equals(source.Status, "ready", StringComparison.OrdinalIgnoreCase) || source.ChunkCount <= 0)
            return "evidence_insufficient";
        return bundle?.EvidenceStatus ?? "source_grounded";
    }

    private static string ResolveSourceReadiness(LearningSource source)
    {
        if (source.IsDeleted) return "deleted";
        if (string.Equals(source.Status, "ready", StringComparison.OrdinalIgnoreCase) && source.ChunkCount > 0) return "source_grounded";
        if (string.Equals(source.Status, "stale", StringComparison.OrdinalIgnoreCase)) return "stale";
        if (string.Equals(source.Status, "error", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source.Status, "failed", StringComparison.OrdinalIgnoreCase)) return "degraded";
        return "evidence_insufficient";
    }

    private static string SafeStatus(string? status)
    {
        var safe = NormalizeWhitespace(status ?? "pending").ToLowerInvariant();
        return safe.Length > 40 ? safe[..40] : safe;
    }

    private static string SafeDisplay(string? value, int maxChars)
    {
        var safe = NormalizeWhitespace(value ?? string.Empty);
        return safe.Length <= maxChars ? safe : safe[..maxChars];
    }

    private static string SafeFileName(string? fileName, int maxChars)
    {
        var safe = Path.GetFileName(fileName ?? string.Empty);
        return SafeDisplay(safe, maxChars);
    }

    private static Guid? TryReadPackSourceId(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("sourceId", out var sourceId) &&
                sourceId.ValueKind == JsonValueKind.String &&
                Guid.TryParse(sourceId.GetString(), out var parsed))
            {
                return parsed;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private async Task<string> SafeWikiAsync(Guid topicId, Guid userId)
    {
        try
        {
            return await _wikiService.GetWikiFullContentAsync(topicId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[NotebookLM] Wiki context okunamadi. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(topicId, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    private static string NormalizeWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private async Task RecalculateStorageUsedAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return;

        var usedBytes = await _db.LearningSources
            .Where(s => s.UserId == userId && !s.IsDeleted && s.Status == "ready")
            .SumAsync(s => (long?)s.FileSizeBytes, ct) ?? 0L;

        user.StorageUsedMB = Math.Max(0d, BytesToMegabytes(usedBytes));
    }

    private static double BytesToMegabytes(long bytes) =>
        bytes <= 0 ? 0d : bytes / 1024d / 1024d;

    private static string TrimForPrompt(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value)) return "(yok)";
        return value.Length > maxChars ? value[..maxChars] + "\n[...kırpıldı]" : value;
    }

    private static LearningSourceSummaryDto ToSummary(LearningSource s) =>
        new(s.Id, s.TopicId, s.SessionId, s.SourceType, s.Title, s.FileName, s.PageCount, s.ChunkCount, s.Status, s.CreatedAt, s.IsDeleted, s.Version);

    private async Task RecordSourceAskedAsync(
        Guid userId,
        LearningSource source,
        string question,
        int citationCount,
        CancellationToken ct)
    {
        await _signals.RecordSignalAsync(
            userId,
            source.TopicId,
            source.SessionId,
            LearningSignalTypes.SourceAsked,
            payloadJson: JsonSerializer.Serialize(new
            {
                sourceId = source.Id,
                question,
                citationCount
            }),
            ct: ct);
    }
}

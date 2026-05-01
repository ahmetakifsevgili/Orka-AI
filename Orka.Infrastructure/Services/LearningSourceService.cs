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
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class LearningSourceService : ILearningSourceService
{
    private readonly OrkaDbContext _db;
    private readonly FileExtractionService _extractor;
    private readonly IEmbeddingService _embedding;
    private readonly IAIAgentFactory _factory;
    private readonly IWikiService _wikiService;
    private readonly IRedisMemoryService _redis;
    private readonly ISummarizerAgent _summarizer;
    private readonly ILearningSignalService _signals;
    private readonly ILogger<LearningSourceService> _logger;

    private const int ApproxChunkChars = 2200;
    private const int MaxContextChars = 7000;

    public LearningSourceService(
        OrkaDbContext db,
        FileExtractionService extractor,
        IEmbeddingService embedding,
        IAIAgentFactory factory,
        IWikiService wikiService,
        IRedisMemoryService redis,
        ISummarizerAgent summarizer,
        ILearningSignalService signals,
        ILogger<LearningSourceService> logger)
    {
        _db = db;
        _extractor = extractor;
        _embedding = embedding;
        _factory = factory;
        _wikiService = wikiService;
        _redis = redis;
        _summarizer = summarizer;
        _signals = signals;
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
        var uploadMb = bytes.Length / 1024d / 1024d;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("Kullanici bulunamadi.");
        if (user.StorageLimitMB > 0 && user.StorageUsedMB + uploadMb > user.StorageLimitMB)
        {
            throw new StorageQuotaExceededException();
        }

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
            PageCount = extracted.PageCount,
            Status = string.IsNullOrWhiteSpace(extracted.ErrorMessage) ? "ready" : "error",
            ErrorMessage = extracted.ErrorMessage,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var chunks = string.IsNullOrWhiteSpace(extracted.ErrorMessage)
            ? BuildChunks(source.Id, extracted.Pages).ToList()
            : new List<SourceChunk>();

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
                _logger.LogWarning(ex, "[NotebookLM] Embedding üretilemedi, lexical fallback kullanılacak. File={File}", fileName);
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
                s.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<SourcePageDto?> GetPageAsync(
        Guid userId,
        Guid sourceId,
        int pageNumber,
        CancellationToken ct = default)
    {
        var source = await _db.LearningSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId, ct);
        if (source == null) return null;

        var chunks = await _db.SourceChunks
            .AsNoTracking()
            .Where(c => c.LearningSourceId == sourceId && c.PageNumber == pageNumber)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => new SourceChunkDto(c.Id, c.PageNumber, c.ChunkIndex, c.Text, c.HighlightHint))
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
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId, ct);
        if (source == null) throw new InvalidOperationException("Kaynak bulunamadı.");

        var chunks = await FindRelevantChunksAsync(userId, source.TopicId, sourceId, question, 8, ct);
        var citations = chunks
            .Select(c => new SourceChunkDto(c.Id, c.PageNumber, c.ChunkIndex, c.Text, c.HighlightHint))
            .ToList();

        var wiki = source.TopicId.HasValue
            ? await SafeWikiAsync(source.TopicId.Value, userId)
            : string.Empty;
        var korteks = source.TopicId.HasValue
            ? await _redis.GetKorteksResearchReportAsync(source.TopicId.Value) ?? string.Empty
            : string.Empty;

        var sourceContext = BuildSourceContext(chunks);
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
        if (!Regex.IsMatch(answer, $@"\[doc:{sourceId}:p\d+\]", RegexOptions.IgnoreCase))
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

        await _signals.RecordSignalAsync(
            userId,
            source.TopicId,
            source.SessionId,
            LearningSignalTypes.SourceAsked,
            payloadJson: JsonSerializer.Serialize(new
            {
                sourceId = source.Id,
                question,
                citationCount = citations.Count
            }),
            ct: ct);

        return new SourceAskResultDto(answer, citations);
    }

    public async Task<string> BuildTopicGroundingContextAsync(
        Guid userId,
        Guid? topicId,
        string question,
        CancellationToken ct = default)
    {
        if (!topicId.HasValue) return string.Empty;

        var chunks = await FindRelevantChunksAsync(userId, topicId.Value, null, question, 5, ct);
        if (chunks.Count == 0) return string.Empty;

        return $$"""

            [NOTEBOOKLM BELGE BAĞLAMI - KAYNAK ZORUNLU]:
            Aşağıdaki belge parçalarını kullanırsan her ilgili cümlenin sonuna ilgili parçanın gerçek [doc:...:p...] etiketini aynen ekle.
            Belgede olmayan bilgiyi belgeye mal etme; gerekiyorsa Wiki/Korteks diye ayır.
            {{BuildSourceContext(chunks)}}
            """;
    }

    private async Task<List<SourceChunk>> FindRelevantChunksAsync(
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
            .Where(c => c.LearningSource.UserId == userId);

        if (sourceId.HasValue)
            query = query.Where(c => c.LearningSourceId == sourceId.Value);
        else if (topicId.HasValue)
            query = query.Where(c => c.LearningSource.TopicId == topicId.Value);

        var chunks = await query.Take(400).ToListAsync(ct);
        if (chunks.Count == 0) return [];

        float[]? qEmbedding = null;
        try
        {
            qEmbedding = await _embedding.EmbedAsync(question, "search_query", ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[NotebookLM] Query embedding üretilemedi, lexical scoring kullanılacak.");
        }

        return chunks
            .Select(c => new { Chunk = c, Score = ScoreChunk(c, question, qEmbedding) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.PageNumber)
            .Take(take)
            .Select(x => x.Chunk)
            .ToList();
    }

    private float ScoreChunk(SourceChunk chunk, string question, float[]? qEmbedding)
    {
        if (qEmbedding != null && !string.IsNullOrWhiteSpace(chunk.EmbeddingJson))
        {
            try
            {
                var emb = JsonSerializer.Deserialize<float[]>(chunk.EmbeddingJson);
                if (emb is { Length: > 0 } && emb.Length == qEmbedding.Length)
                    return _embedding.CosineSimilarity(qEmbedding, emb);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "[NotebookLM] Chunk embedding deserialize edilemedi. ChunkId={ChunkId}", chunk.Id);
            }
        }

        var words = question
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 2)
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .ToList();
        if (words.Count == 0) return 0;

        var text = chunk.Text.ToLowerInvariant();
        return words.Count(w => text.Contains(w, StringComparison.Ordinal)) / (float)words.Count;
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

    private static string BuildSourceContext(IEnumerable<SourceChunk> chunks)
    {
        var sb = new StringBuilder();
        foreach (var chunk in chunks)
        {
            sb.AppendLine($"[doc:{chunk.LearningSourceId}:p{chunk.PageNumber}] chunk:{chunk.ChunkIndex}");
            sb.AppendLine(chunk.Text);
            sb.AppendLine();
            if (sb.Length > MaxContextChars) break;
        }
        return sb.ToString();
    }

    private async Task<string> SafeWikiAsync(Guid topicId, Guid userId)
    {
        try
        {
            return await _wikiService.GetWikiFullContentAsync(topicId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[NotebookLM] Wiki context okunamadı. TopicId={TopicId}", topicId);
            return string.Empty;
        }
    }

    private static string NormalizeWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string TrimForPrompt(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value)) return "(yok)";
        return value.Length > maxChars ? value[..maxChars] + "\n[...kırpıldı]" : value;
    }

    private static LearningSourceSummaryDto ToSummary(LearningSource s) =>
        new(s.Id, s.TopicId, s.SessionId, s.SourceType, s.Title, s.FileName, s.PageCount, s.ChunkCount, s.Status, s.CreatedAt);
}

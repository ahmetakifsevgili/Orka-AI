using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class SourceQuestionService : ISourceQuestionService
{
    private const int MaxAnswerLength = 3_200;
    private const int MaxQuestionTraceLength = 600;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] BlockedMarkers =
    [
        "rawPrompt", "hiddenPrompt", "systemPrompt", "rawProviderPayload", "rawSourceChunk",
        "rawToolPayload", "debugTrace", "localPath", "apiKey", "secret", "answerKey",
        "correctAnswer", "stackTrace"
    ];

    private readonly OrkaDbContext _db;
    private readonly ILearningSourceService _sources;
    private readonly ISourceConceptLinkingService _sourceConceptLinks;
    private readonly IWikiLearningTraceWriter? _traceWriter;
    private readonly ILearningRuntimeTelemetryService? _telemetry;

    public SourceQuestionService(
        OrkaDbContext db,
        ILearningSourceService sources,
        ISourceConceptLinkingService sourceConceptLinks,
        IWikiLearningTraceWriter? traceWriter = null,
        ILearningRuntimeTelemetryService? telemetry = null)
    {
        _db = db;
        _sources = sources;
        _sourceConceptLinks = sourceConceptLinks;
        _traceWriter = traceWriter;
        _telemetry = telemetry;
    }

    public async Task<SourceQuestionResponseDto?> AskAsync(
        Guid userId,
        SourceQuestionRequestDto request,
        CancellationToken ct = default)
    {
        if (request.SourceId.HasValue)
        {
            return await AskSourceAsync(userId, request.SourceId.Value, request, ct);
        }

        if (request.TopicId.HasValue)
        {
            return await AskTopicSourcesAsync(userId, request.TopicId.Value, request, ct);
        }

        var explicitSource = (request.SourceIds ?? Array.Empty<Guid>()).FirstOrDefault(id => id != Guid.Empty);
        return explicitSource == Guid.Empty
            ? null
            : await AskSourceAsync(userId, explicitSource, request, ct);
    }

    public async Task<SourceQuestionResponseDto?> AskSourceAsync(
        Guid userId,
        Guid sourceId,
        SourceQuestionRequestDto request,
        CancellationToken ct = default)
    {
        var source = await _db.LearningSources.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId && !s.IsDeleted, ct);
        if (source == null) return null;

        request.SourceId = sourceId;
        request.TopicId ??= source.TopicId;
        request.Mode = NormalizeMode(request.Mode, "selected_source");
        return await AskResolvedSourceAsync(userId, source, request, ct);
    }

    public async Task<SourceQuestionResponseDto?> AskTopicSourcesAsync(
        Guid userId,
        Guid topicId,
        SourceQuestionRequestDto request,
        CancellationToken ct = default)
    {
        var ownsTopic = await _db.Topics.AsNoTracking()
            .AnyAsync(t => t.Id == topicId && t.UserId == userId && !t.IsArchived, ct);
        if (!ownsTopic) return null;

        request.TopicId = topicId;
        request.Mode = NormalizeMode(request.Mode, "source_collection");
        var explicitIds = (request.SourceIds ?? Array.Empty<Guid>()).Where(id => id != Guid.Empty).Distinct().ToArray();
        var query = _db.LearningSources.AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId && !s.IsDeleted);
        if (explicitIds.Length > 0)
        {
            query = query.Where(s => explicitIds.Contains(s.Id));
        }

        var sources = await query
            .OrderByDescending(s => s.Status == "ready")
            .ThenByDescending(s => s.ChunkCount)
            .ThenByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);

        var source = sources.FirstOrDefault(s => IsReadySource(s.Status) && s.ChunkCount > 0) ?? sources.FirstOrDefault();
        if (source == null)
        {
            return BuildNoSourceResponse(topicId, request);
        }

        var response = await AskResolvedSourceAsync(userId, source, request, ct);
        if (response != null && sources.Count > 1)
        {
            response.Warnings = response.Warnings
                .Concat(["source_collection_primary_source_only"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            response.NextActions = response.NextActions
                .Concat(["multi_source_compare_backlog"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return response;
    }

    private async Task<SourceQuestionResponseDto> AskResolvedSourceAsync(
        Guid userId,
        LearningSource source,
        SourceQuestionRequestDto request,
        CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        await RecordTelemetryAsync(userId, source.TopicId, source.SessionId, "source_question_requested", "started", null, ct);
        var related = await LoadRelatedConceptsAsync(userId, source, request, ct);
        var bundle = source.TopicId.HasValue
            ? await LoadLatestBundleAsync(userId, source.TopicId.Value, ct)
            : null;

        if (!IsReadySource(source.Status))
        {
            var degraded = BuildDegradedResponse(source, request, related, "source_not_ready", "degraded");
            await WriteTraceIfRequestedAsync(userId, source, request, degraded, ct);
            await RecordTelemetryAsync(userId, source.TopicId, source.SessionId, "source_question_degraded", "degraded", started.ElapsedMilliseconds, ct);
            return degraded;
        }

        SourceAskResultDto legacy;
        try
        {
            legacy = await _sources.AskAsync(userId, source.Id, request.Question.Trim(), ct);
        }
        catch
        {
            var failed = BuildDegradedResponse(source, request, related, "source_question_failed", "failed");
            await RecordTelemetryAsync(userId, source.TopicId, source.SessionId, "source_question_degraded", "failed", started.ElapsedMilliseconds, ct);
            return failed;
        }

        var warnings = new List<string>();
        if (request.Mode is "source_collection" or "wiki_page_sources" or "linked_concept_sources")
        {
            warnings.Add("source_collection_answer_uses_primary_source");
        }
        if (bundle is { EvidenceStatus: not "source_grounded" and not "mixed" })
        {
            warnings.Add("evidence_status_not_source_grounded");
        }

        var safeAnswer = SanitizeText(legacy.Answer, MaxAnswerLength, out var blockedAnswerTerms);
        if (blockedAnswerTerms.Count > 0)
        {
            safeAnswer = "Kaynak yaniti guvenlik nedeniyle kisitlandi; kaynak defterini yenileyip tekrar dene.";
            warnings.Add("unsafe_answer_redacted");
        }

        var citations = BuildSafeCitations(source, legacy, warnings);
        var missingCitations = legacy.Metadata?.CitationMissingCount ?? 0;
        var unsupportedCitations = legacy.Metadata?.UnsupportedCitationCount ?? 0;
        if (missingCitations > 0) warnings.Add("citation_missing");
        if (unsupportedCitations > 0) warnings.Add("citation_unsupported");
        if (citations.Count == 0) warnings.Add("citation_missing");

        var sourceBasis = ResolveSourceBasis(source, legacy, citations.Count, warnings);
        var response = new SourceQuestionResponseDto
        {
            Answer = safeAnswer,
            SourceBasis = sourceBasis,
            EvidenceStatus = sourceBasis == "source_grounded" ? "source_grounded" : sourceBasis,
            SourceReadiness = ResolveReadiness(source, sourceBasis),
            Citations = citations,
            RelatedConcepts = related.Links.Take(8).ToArray(),
            RelatedWikiPages = related.Links.Where(l => l.WikiPageId.HasValue).Take(8).ToArray(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Safety = new SourceQuestionSafetyDto
            {
                Status = blockedAnswerTerms.Count == 0 ? "safe" : "degraded",
                BlockedTerms = blockedAnswerTerms,
                RawPayloadRemoved = true
            },
            Context = new SourceQuestionContextDto
            {
                SourceId = source.Id,
                SourceTitle = SafeDisplay(source.Title, 160),
                TopicId = source.TopicId,
                WikiPageId = request.WikiPageId,
                WikiPageTitle = await LoadWikiPageTitleAsync(userId, request.WikiPageId, ct),
                RelatedConcepts = related.Links.Take(8).ToArray(),
                RelatedWikiPages = related.Links.Where(l => l.WikiPageId.HasValue).Take(8).ToArray()
            },
            NextActions = BuildNextActions(sourceBasis, citations.Count, related.Links.Count)
        };

        var trace = await WriteTraceIfRequestedAsync(userId, source, request, response, ct);
        response.TraceBlockId = trace?.Id;
        await RecordTelemetryAsync(userId, source.TopicId, source.SessionId,
            response.SourceBasis == "source_grounded" ? "source_question_answered" : "source_question_degraded",
            response.SourceBasis,
            started.ElapsedMilliseconds,
            ct);
        return response;
    }

    private static SourceQuestionResponseDto BuildNoSourceResponse(Guid topicId, SourceQuestionRequestDto request) =>
        new()
        {
            Answer = "Bu kaynak defterinde soru sorulabilecek hazir kaynak yok.",
            SourceBasis = "evidence_insufficient",
            EvidenceStatus = "evidence_insufficient",
            SourceReadiness = "evidence_insufficient",
            Warnings = ["no_source_available"],
            Context = new SourceQuestionContextDto { TopicId = topicId, WikiPageId = request.WikiPageId },
            NextActions = ["add_source", "refresh_source_evidence"]
        };

    private static SourceQuestionResponseDto BuildDegradedResponse(
        LearningSource source,
        SourceQuestionRequestDto request,
        SourceConceptLinkSummaryDto related,
        string warning,
        string status) =>
        new()
        {
            Answer = "Bu kaynak su anda guvenilir source-grounded yanit icin hazir degil. Kaynak durumunu yenile veya baska bir kaynak sec.",
            SourceBasis = status == "failed" ? "degraded" : "evidence_insufficient",
            EvidenceStatus = status == "failed" ? "degraded" : "evidence_insufficient",
            SourceReadiness = status == "failed" ? "degraded" : "evidence_insufficient",
            RelatedConcepts = related.Links.Take(8).ToArray(),
            RelatedWikiPages = related.Links.Where(l => l.WikiPageId.HasValue).Take(8).ToArray(),
            Warnings = [warning],
            Safety = new SourceQuestionSafetyDto { Status = status, RawPayloadRemoved = true },
            Context = new SourceQuestionContextDto
            {
                SourceId = source.Id,
                SourceTitle = SafeDisplay(source.Title, 160),
                TopicId = source.TopicId,
                WikiPageId = request.WikiPageId,
                RelatedConcepts = related.Links.Take(8).ToArray(),
                RelatedWikiPages = related.Links.Where(l => l.WikiPageId.HasValue).Take(8).ToArray()
            },
            NextActions = ["refresh_source_evidence", "create_source_pack"]
        };

    private async Task<SourceConceptLinkSummaryDto> LoadRelatedConceptsAsync(
        Guid userId,
        LearningSource source,
        SourceQuestionRequestDto request,
        CancellationToken ct)
    {
        try
        {
            if (request.WikiPageId.HasValue)
            {
                var pageLinks = await _sourceConceptLinks.GetWikiPageSourceLinksAsync(userId, request.WikiPageId.Value, ct);
                if (pageLinks != null && pageLinks.Links.Count > 0) return pageLinks;
            }

            var summary = await _sourceConceptLinks.GetSourceConceptLinksAsync(userId, source.Id, ct);
            return summary ?? new SourceConceptLinkSummaryDto
            {
                TopicId = source.TopicId,
                SourceId = source.Id,
                Title = source.Title,
                SourceReadiness = "evidence_insufficient",
                EvidenceStatus = "evidence_insufficient"
            };
        }
        catch
        {
            return new SourceConceptLinkSummaryDto
            {
                TopicId = source.TopicId,
                SourceId = source.Id,
                Title = source.Title,
                SourceReadiness = "degraded",
                EvidenceStatus = "degraded",
                Warnings = ["related_concepts_unavailable"]
            };
        }
    }

    private async Task<WikiBlockDto?> WriteTraceIfRequestedAsync(
        Guid userId,
        LearningSource source,
        SourceQuestionRequestDto request,
        SourceQuestionResponseDto response,
        CancellationToken ct)
    {
        if (!request.WriteWikiTrace || _traceWriter == null) return null;

        try
        {
            var question = SanitizeText(request.Question, MaxQuestionTraceLength, out _);
            if (!string.IsNullOrWhiteSpace(question))
            {
                await _traceWriter.RecordStudentQuestionAsync(new WikiLearningTraceRequestDto
                {
                    UserId = userId,
                    TopicId = source.TopicId,
                    SessionId = source.SessionId,
                    ActiveWikiPageId = request.WikiPageId,
                    SourceId = source.Id,
                    TraceType = "student_question",
                    Title = "Source question",
                    SafeContent = question,
                    SourceBasis = "student_manual",
                    CreatedBy = "orkalm_source_question",
                    MetadataJson = BuildTraceMetadata(response.SourceBasis, response.Citations.Count, response.Warnings)
                }, ct);
            }

            var answerTrace = response.SourceBasis == "source_grounded" ? "source_note" : "tutor_explanation";
            var answerBlock = response.SourceBasis == "source_grounded"
                ? await _traceWriter.RecordSourceNoteAsync(new WikiLearningTraceRequestDto
                {
                    UserId = userId,
                    TopicId = source.TopicId,
                    SessionId = source.SessionId,
                    ActiveWikiPageId = request.WikiPageId,
                    SourceId = source.Id,
                    TraceType = answerTrace,
                    Title = "Source answer",
                    SafeContent = response.Answer,
                    SourceBasis = "source_grounded",
                    CreatedBy = "orkalm_source_question",
                    MetadataJson = BuildTraceMetadata(response.SourceBasis, response.Citations.Count, response.Warnings)
                }, ct)
                : await _traceWriter.RecordTutorExplanationAsync(new WikiLearningTraceRequestDto
                {
                    UserId = userId,
                    TopicId = source.TopicId,
                    SessionId = source.SessionId,
                    ActiveWikiPageId = request.WikiPageId,
                    SourceId = source.Id,
                    TraceType = answerTrace,
                    Title = "Source answer",
                    SafeContent = response.Answer,
                    SourceBasis = response.SourceBasis == "mixed" ? "model_assisted" : response.SourceBasis,
                    CreatedBy = "orkalm_source_question",
                    MetadataJson = BuildTraceMetadata(response.SourceBasis, response.Citations.Count, response.Warnings)
                }, ct);

            if (answerBlock != null)
            {
                await RecordTelemetryAsync(userId, source.TopicId, source.SessionId, "source_question_trace_written", "ok", null, ct);
            }

            return answerBlock;
        }
        catch
        {
            return null;
        }
    }

    private async Task<SourceEvidenceBundle?> LoadLatestBundleAsync(Guid userId, Guid topicId, CancellationToken ct) =>
        await _db.SourceEvidenceBundles.AsNoTracking()
            .Where(b => b.UserId == userId && b.TopicId == topicId && !b.IsDeleted)
            .OrderByDescending(b => b.UpdatedAt)
            .ThenByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<string?> LoadWikiPageTitleAsync(Guid userId, Guid? pageId, CancellationToken ct)
    {
        if (!pageId.HasValue) return null;
        return await _db.WikiPages.AsNoTracking()
            .Where(p => p.Id == pageId.Value && p.UserId == userId && !p.IsDeleted)
            .Select(p => p.Title)
            .FirstOrDefaultAsync(ct);
    }

    private static List<SourceQuestionCitationDto> BuildSafeCitations(
        LearningSource source,
        SourceAskResultDto legacy,
        List<string> warnings)
    {
        var metadata = legacy.Metadata?.Citations ?? [];
        var citations = new List<SourceQuestionCitationDto>();
        foreach (var citation in legacy.Citations.Take(8))
        {
            var citationId = $"[doc:{source.Id}:p{citation.PageNumber}]";
            var matching = metadata.FirstOrDefault(c => string.Equals(c.CitationId, citationId, StringComparison.OrdinalIgnoreCase));
            var label = $"Page {citation.PageNumber}";
            var safeLabel = SanitizeText(label, 120, out var blocked);
            if (blocked.Count > 0)
            {
                warnings.Add("citation_label_redacted");
                safeLabel = $"Page {citation.PageNumber}";
            }

            citations.Add(new SourceQuestionCitationDto
            {
                CitationId = citationId,
                SourceId = source.Id,
                SourceChunkId = citation.Id,
                PageNumber = citation.PageNumber,
                ChunkIndex = citation.ChunkIndex,
                Label = safeLabel,
                SourceTitle = SafeDisplay(source.Title, 160),
                SupportStatus = legacy.Metadata?.FallbackReason ?? "supported",
                Confidence = matching?.Confidence ?? legacy.Metadata?.SourceConfidence
            });
        }

        return citations;
    }

    private static string ResolveSourceBasis(
        LearningSource source,
        SourceAskResultDto legacy,
        int citationCount,
        IReadOnlyCollection<string> warnings)
    {
        if (!IsReadySource(source.Status)) return "degraded";
        if (citationCount == 0) return "evidence_insufficient";
        if (warnings.Any(w => w.Contains("citation_missing", StringComparison.OrdinalIgnoreCase))) return "evidence_insufficient";
        if (warnings.Any(w => w.Contains("citation_unsupported", StringComparison.OrdinalIgnoreCase))) return "mixed";
        var grounding = legacy.Metadata?.GroundingMode;
        return string.Equals(grounding, "source_grounded", StringComparison.OrdinalIgnoreCase)
            ? "source_grounded"
            : "mixed";
    }

    private static string ResolveReadiness(LearningSource source, string sourceBasis)
    {
        if (!IsReadySource(source.Status)) return "degraded";
        return sourceBasis is "source_grounded" or "mixed" ? "source_ready" : "evidence_insufficient";
    }

    private static IReadOnlyList<string> BuildNextActions(string sourceBasis, int citationCount, int relatedConceptCount)
    {
        var actions = new List<string> { "create_source_pack" };
        if (sourceBasis != "source_grounded") actions.Add("refresh_source_evidence");
        if (citationCount > 0) actions.Add("open_citation");
        if (relatedConceptCount > 0) actions.Add("open_related_concept");
        actions.Add("write_to_wiki");
        return actions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string BuildTraceMetadata(string sourceBasis, int citationCount, IReadOnlyList<string> warnings) =>
        JsonSerializer.Serialize(new
        {
            sourceBasis,
            citationCount,
            warnings = warnings.Take(6).ToArray()
        }, JsonOptions);

    private async Task RecordTelemetryAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string operation,
        string status,
        long? latencyMs,
        CancellationToken ct)
    {
        if (_telemetry == null) return;
        try
        {
            await _telemetry.RecordEventAsync(userId, new LearningRuntimeEventRequestDto
            {
                TopicId = topicId,
                SessionId = sessionId,
                Category = "orkalm_source_question",
                Operation = operation,
                Status = status,
                Severity = status is "failed" or "degraded" ? "warning" : "info",
                LatencyMs = latencyMs,
                SafeMessage = "Source question event recorded without raw question, answer, or source text.",
                SafeMetadata = new Dictionary<string, string>
                {
                    ["sourceBasis"] = status,
                    ["rawContentStored"] = "false"
                }
            }, ct);
        }
        catch
        {
            // Telemetry must never block source Q&A.
        }
    }

    private static bool IsReadySource(string? status) =>
        string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "processed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "indexed", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMode(string? mode, string fallback)
    {
        var normalized = (mode ?? fallback).Trim().ToLowerInvariant();
        return normalized is "selected_source" or "source_collection" or "wiki_page_sources" or "linked_concept_sources"
            ? normalized
            : fallback;
    }

    private static string SafeDisplay(string? value, int maxLength)
    {
        var safe = SanitizeText(value ?? string.Empty, maxLength, out _);
        return string.IsNullOrWhiteSpace(safe) ? "Source" : safe;
    }

    private static string SanitizeText(string? value, int maxLength, out IReadOnlyList<string> blockedTerms)
    {
        var blocked = new List<string>();
        var text = value ?? string.Empty;
        foreach (var marker in BlockedMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                blocked.Add(marker);
                text = Regex.Replace(text, Regex.Escape(marker), "[redacted]", RegexOptions.IgnoreCase);
            }
        }

        text = text.Replace("\0", string.Empty).Trim();
        blockedTerms = blocked.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}

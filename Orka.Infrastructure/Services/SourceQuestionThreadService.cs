using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class SourceQuestionThreadService : ISourceQuestionThreadService
{
    private const int MaxThreads = 40;
    private const int MaxTurns = 20;
    private const int MaxQuestionLength = 800;
    private const int MaxAnswerSummaryLength = 1_200;
    private const string ArtifactType = "source_question_thread";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] BlockedMarkers =
    [
        "rawPrompt", "hiddenPrompt", "systemPrompt", "rawProviderPayload", "rawSourceChunk",
        "rawToolPayload", "debugTrace", "localPath", "apiKey", "secret", "answerKey",
        "correctAnswer", "stackTrace"
    ];

    private readonly OrkaDbContext _db;
    private readonly ISourceQuestionService _sourceQuestions;
    private readonly IWikiLearningTraceWriter? _traceWriter;
    private readonly ILearningRuntimeTelemetryService? _telemetry;

    public SourceQuestionThreadService(
        OrkaDbContext db,
        ISourceQuestionService sourceQuestions,
        IWikiLearningTraceWriter? traceWriter = null,
        ILearningRuntimeTelemetryService? telemetry = null)
    {
        _db = db;
        _sourceQuestions = sourceQuestions;
        _traceWriter = traceWriter;
        _telemetry = telemetry;
    }

    public async Task<SourceStudySummaryDto> GetStudySummaryAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sourceId = null,
        Guid? wikiPageId = null,
        CancellationToken ct = default)
    {
        var topicMissing = topicId.HasValue &&
            !await _db.Topics.AsNoTracking().AnyAsync(t => t.Id == topicId.Value && t.UserId == userId && !t.IsArchived, ct);
        var sourceMissing = sourceId.HasValue &&
            !await _db.LearningSources.AsNoTracking().AnyAsync(s => s.Id == sourceId.Value && s.UserId == userId && !s.IsDeleted, ct);
        var pageMissing = wikiPageId.HasValue &&
            !await _db.WikiPages.AsNoTracking().AnyAsync(p =>
                p.Id == wikiPageId.Value && p.UserId == userId && !p.IsDeleted &&
                (!topicId.HasValue || p.TopicId == topicId.Value), ct);

        if (topicMissing || sourceMissing || pageMissing)
        {
            return new SourceStudySummaryDto
            {
                TopicId = topicId,
                SourceId = sourceId,
                WikiPageId = wikiPageId,
                StudyStatus = "not_found",
                SourceReadiness = "evidence_insufficient",
                EvidenceStatus = "evidence_insufficient",
                RecommendedNextAction = "select_source",
                NextActions = ["select_source"],
                Warnings = ["source_study_context_not_found"]
            };
        }

        var sourceQuery = _db.LearningSources.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDeleted);
        if (topicId.HasValue) sourceQuery = sourceQuery.Where(s => s.TopicId == topicId.Value);
        if (sourceId.HasValue) sourceQuery = sourceQuery.Where(s => s.Id == sourceId.Value);
        var sources = await sourceQuery
            .OrderByDescending(s => s.Status == "ready")
            .ThenByDescending(s => s.UpdatedAt)
            .Take(40)
            .ToListAsync(ct);
        var sourceIds = sources.Select(s => s.Id).ToArray();

        var threads = await ListThreadsAsync(userId, topicId, sourceId, wikiPageId, ct);
        var turns = threads.Items.SelectMany(t => t.Turns).ToArray();
        var reviewed = turns.Count(t => t.ReviewStatus == "supported");
        var needsReview = turns.Count(t => t.ReviewStatus is "needs_review" or "not_checked" or "missing_citation" or "unsupported");
        var degraded = turns.Count(t => t.ReviewStatus is "stale" or "degraded" ||
                                       t.SourceBasis is "degraded" ||
                                       t.EvidenceStatus is "degraded" or "stale");
        var threadCitationWarnings = turns.Count(t => t.ReviewStatus is "missing_citation" or "unsupported" or "needs_review" ||
                                                     t.Warnings.Any(w => w.Contains("citation", StringComparison.OrdinalIgnoreCase)));

        var citationQuery = _db.SourceCitationChecks.AsNoTracking().Where(c => c.UserId == userId);
        if (sourceIds.Length > 0)
        {
            citationQuery = citationQuery.Where(c => c.SourceId.HasValue && sourceIds.Contains(c.SourceId.Value));
        }
        else if (topicId.HasValue)
        {
            var topicSourceIds = await _db.LearningSources.AsNoTracking()
                .Where(s => s.UserId == userId && s.TopicId == topicId.Value && !s.IsDeleted)
                .Select(s => s.Id)
                .ToArrayAsync(ct);
            citationQuery = citationQuery.Where(c => c.SourceId.HasValue && topicSourceIds.Contains(c.SourceId.Value));
        }
        else
        {
            citationQuery = citationQuery.Where(c => false);
        }

        var citationChecks = await citationQuery.Take(240).ToListAsync(ct);
        var citationWarnings = citationChecks.Count(c => IsCitationWarning(c.CheckStatus));

        var relatedConceptCount = await CountRelatedConceptsAsync(userId, topicId, sourceIds, wikiPageId, ct);
        var warnings = BuildStudyWarnings(sources, threads.Items, turns, citationWarnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        var studyStatus = ResolveStudyStatus(sources, turns, warnings);
        var nextActions = BuildStudyNextActions(sources.Count, turns.Length, needsReview, degraded, citationWarnings, relatedConceptCount)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var recommended = nextActions.FirstOrDefault() ?? "add_source";

        await RecordTelemetryAsync(userId, topicId, "source_study_summary_viewed", studyStatus, turns.Length, ct);
        return new SourceStudySummaryDto
        {
            TopicId = topicId ?? sources.FirstOrDefault(s => s.TopicId.HasValue)?.TopicId,
            SourceId = sourceId,
            WikiPageId = wikiPageId,
            SourceCount = sources.Count,
            ThreadCount = threads.Count,
            TurnCount = turns.Length,
            ReviewedCount = reviewed,
            NeedsReviewCount = needsReview,
            DegradedCount = degraded,
            CitationWarningCount = threadCitationWarnings + citationWarnings,
            RelatedConceptCount = relatedConceptCount,
            ComparedSourceCount = sources.Count >= 2 ? sources.Count : 0,
            SourceReadiness = ResolveAggregateSourceReadiness(sources),
            EvidenceStatus = ResolveAggregateEvidenceStatus(sources, turns),
            StudyStatus = studyStatus,
            RecommendedNextAction = recommended,
            NextActions = nextActions,
            RecentQuestions = turns.OrderByDescending(t => t.CreatedAt).Select(t => SanitizeText(t.Question, 180, out _)).Where(NotBlank).Take(6).ToArray(),
            Warnings = warnings,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<SourceQuestionThreadListDto> ListThreadsAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sourceId = null,
        Guid? wikiPageId = null,
        CancellationToken ct = default)
    {
        var artifacts = await _db.LearningArtifacts.AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsDeleted && a.ArtifactType == ArtifactType)
            .OrderByDescending(a => a.UpdatedAt)
            .Take(120)
            .ToListAsync(ct);

        var items = artifacts
            .Select(TryMap)
            .Where(dto => dto != null)
            .Select(dto => dto!)
            .Where(dto => !topicId.HasValue || dto.TopicId == topicId.Value)
            .Where(dto => !sourceId.HasValue || dto.SourceIds.Contains(sourceId.Value))
            .Where(dto => !wikiPageId.HasValue || dto.WikiPageId == wikiPageId.Value)
            .Take(MaxThreads)
            .ToArray();

        await RecordTelemetryAsync(userId, topicId, "source_question_thread_viewed", "list", items.Length, ct);
        return new SourceQuestionThreadListDto { Count = items.Length, Items = items };
    }

    public async Task<SourceQuestionThreadDto?> GetThreadAsync(Guid userId, Guid threadId, CancellationToken ct = default)
    {
        var artifact = await LoadThreadArtifactAsync(userId, threadId, tracking: false, ct);
        if (artifact == null) return null;

        await RecordTelemetryAsync(userId, artifact.TopicId, "source_question_thread_viewed", "detail", null, ct);
        return TryMap(artifact);
    }

    public async Task<SourceQuestionThreadDto?> CreateThreadAsync(
        Guid userId,
        SourceQuestionThreadRequestDto request,
        CancellationToken ct = default)
    {
        var context = await ResolveContextAsync(userId, request.TopicId, request.SourceId, request.SourceIds, request.WikiPageId, ct);
        if (context == null) return null;

        var title = BuildThreadTitle(request.Title, request.InitialQuestion, context.SourceTitles);
        var state = new ThreadState
        {
            TopicId = context.TopicId,
            SourceIds = context.SourceIds,
            WikiPageId = request.WikiPageId,
            ConceptKey = SanitizeText(request.ConceptKey, 160, out _),
            Title = title,
            Status = "active",
            SourceBasis = "evidence_insufficient",
            EvidenceStatus = "evidence_insufficient",
            SourceReadiness = "evidence_insufficient",
            CitationReviewStatus = "not_checked",
            Warnings = context.Warnings,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var entity = new LearningArtifact
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = context.TopicId,
            WikiNotebookSectionKey = "orkalm_source_question_thread",
            ConceptKey = state.ConceptKey,
            ArtifactType = ArtifactType,
            ArtifactStatus = "active",
            Origin = "source",
            RenderFormat = "plain_text",
            Title = title,
            SafeContent = BuildSafeContent(state),
            ContentJson = SerializeState(state),
            SourceBasis = "evidence_insufficient",
            SafetyWarningsJson = SerializeStrings(state.Warnings),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.LearningArtifacts.Add(entity);
        await _db.SaveChangesAsync(ct);
        await RecordTelemetryAsync(userId, context.TopicId, "source_question_thread_created", "active", null, ct);

        if (!string.IsNullOrWhiteSpace(request.InitialQuestion))
        {
            return await AskFollowUpAsync(userId, entity.Id, new SourceQuestionFollowUpRequestDto
            {
                Question = request.InitialQuestion,
                IncludeLearnerContext = request.IncludeLearnerContext,
                WriteWikiTrace = request.WriteWikiTrace
            }, ct);
        }

        return TryMap(entity);
    }

    public async Task<SourceQuestionThreadDto?> AskFollowUpAsync(
        Guid userId,
        Guid threadId,
        SourceQuestionFollowUpRequestDto request,
        CancellationToken ct = default)
    {
        var artifact = await LoadThreadArtifactAsync(userId, threadId, tracking: true, ct);
        if (artifact == null || string.IsNullOrWhiteSpace(request.Question)) return null;

        var state = ParseState(artifact);
        var safeQuestion = SanitizeText(request.Question, MaxQuestionLength, out var blockedQuestionTerms);
        if (string.IsNullOrWhiteSpace(safeQuestion)) return TryMap(artifact);

        if (state.Turns.Any(t => string.Equals(Normalize(t.Question), Normalize(safeQuestion), StringComparison.OrdinalIgnoreCase)))
        {
            return TryMap(artifact);
        }

        var askRequest = new SourceQuestionRequestDto
        {
            TopicId = state.TopicId,
            SourceId = state.SourceIds.Count == 1 ? state.SourceIds[0] : null,
            SourceIds = state.SourceIds,
            WikiPageId = state.WikiPageId,
            Question = BuildFollowUpQuestion(safeQuestion, state),
            Mode = state.SourceIds.Count > 1 ? "source_collection" : "selected_source",
            IncludeLearnerContext = request.IncludeLearnerContext,
            WriteWikiTrace = false
        };

        SourceQuestionResponseDto? answer;
        try
        {
            answer = await _sourceQuestions.AskAsync(userId, askRequest, ct);
        }
        catch
        {
            answer = null;
        }

        var turn = answer == null
            ? BuildDegradedTurn(safeQuestion, blockedQuestionTerms)
            : BuildTurn(safeQuestion, answer, blockedQuestionTerms);

        state.Turns = state.Turns.Concat([turn]).TakeLast(MaxTurns).ToArray();
        ApplyAggregateState(state);
        SaveState(artifact, state);
        await _db.SaveChangesAsync(ct);
        await RecordTelemetryAsync(userId, state.TopicId, "source_question_followup_asked", turn.SourceBasis, state.Turns.Count, ct);

        if (request.WriteWikiTrace)
        {
            await WriteWikiTraceAsync(userId, threadId, ct);
        }

        return await GetThreadAsync(userId, threadId, ct);
    }

    public async Task<SourceQuestionThreadDto?> UpdateReviewAsync(
        Guid userId,
        Guid threadId,
        SourceQuestionReviewStateDto request,
        CancellationToken ct = default)
    {
        var artifact = await LoadThreadArtifactAsync(userId, threadId, tracking: true, ct);
        if (artifact == null) return null;

        var state = ParseState(artifact);
        var requested = NormalizeReviewStatus(request.ReviewStatus);
        var warnings = request.Warnings.Select(w => SanitizeText(w, 120, out _)).Where(NotBlank).Take(8).ToArray();

        foreach (var turn in state.Turns.Where(t => !request.TurnId.HasValue || t.TurnId == request.TurnId.Value))
        {
            var allowed = requested;
            if (requested == "supported" && (turn.SourceBasis != "source_grounded" || turn.Citations.Count == 0))
            {
                allowed = "needs_review";
                turn.Warnings = turn.Warnings.Concat(["supported_review_requires_source_grounded_citation"]).Concat(warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .ToArray();
            }
            else
            {
                turn.Warnings = turn.Warnings.Concat(warnings).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
            }

            turn.ReviewStatus = allowed;
        }

        ApplyAggregateState(state);
        SaveState(artifact, state);
        await _db.SaveChangesAsync(ct);
        await RecordTelemetryAsync(userId, state.TopicId, "source_question_thread_reviewed", state.CitationReviewStatus, null, ct);
        return TryMap(artifact);
    }

    public async Task<WikiBlockDto?> WriteWikiTraceAsync(Guid userId, Guid threadId, CancellationToken ct = default)
    {
        if (_traceWriter == null) return null;
        var artifact = await LoadThreadArtifactAsync(userId, threadId, tracking: true, ct);
        if (artifact == null) return null;

        var state = ParseState(artifact);
        var turn = state.Turns.LastOrDefault();
        if (turn == null || !state.TopicId.HasValue) return null;

        try
        {
            await _traceWriter.RecordStudentQuestionAsync(new WikiLearningTraceRequestDto
            {
                UserId = userId,
                TopicId = state.TopicId,
                ActiveWikiPageId = state.WikiPageId,
                SourceId = state.SourceIds.FirstOrDefault(id => id != Guid.Empty),
                ConceptKey = state.ConceptKey,
                TraceType = "student_question",
                Title = "Source Q&A follow-up",
                SafeContent = turn.Question,
                SourceBasis = "student_manual",
                CreatedBy = "orkalm_source_question_thread",
                MetadataJson = BuildTraceMetadata(state, turn)
            }, ct);

            var answerBlock = turn.SourceBasis == "source_grounded"
                ? await _traceWriter.RecordSourceNoteAsync(new WikiLearningTraceRequestDto
                {
                    UserId = userId,
                    TopicId = state.TopicId,
                    ActiveWikiPageId = state.WikiPageId,
                    SourceId = state.SourceIds.FirstOrDefault(id => id != Guid.Empty),
                    ConceptKey = state.ConceptKey,
                    TraceType = "source_note",
                    Title = "Reviewed source Q&A answer",
                    SafeContent = turn.SafeAnswerSummary,
                    SourceBasis = "source_grounded",
                    CreatedBy = "orkalm_source_question_thread",
                    MetadataJson = BuildTraceMetadata(state, turn)
                }, ct)
                : await _traceWriter.RecordTutorExplanationAsync(new WikiLearningTraceRequestDto
                {
                    UserId = userId,
                    TopicId = state.TopicId,
                    ActiveWikiPageId = state.WikiPageId,
                    SourceId = state.SourceIds.FirstOrDefault(id => id != Guid.Empty),
                    ConceptKey = state.ConceptKey,
                    TraceType = "tutor_explanation",
                    Title = "Source Q&A answer needing review",
                    SafeContent = turn.SafeAnswerSummary,
                    SourceBasis = turn.SourceBasis == "mixed" ? "model_assisted" : turn.SourceBasis,
                    CreatedBy = "orkalm_source_question_thread",
                    MetadataJson = BuildTraceMetadata(state, turn)
                }, ct);

            if (answerBlock != null)
            {
                turn.TraceBlockId = answerBlock.Id;
                state.Warnings = state.Warnings.Concat(["wiki_trace_written"]).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
                SaveState(artifact, state);
                await _db.SaveChangesAsync(ct);
                await RecordTelemetryAsync(userId, state.TopicId, "source_question_thread_wiki_trace_written", "ok", null, ct);
            }

            return answerBlock;
        }
        catch
        {
            return null;
        }
    }

    private async Task<ThreadContext?> ResolveContextAsync(
        Guid userId,
        Guid? topicId,
        Guid? sourceId,
        IReadOnlyList<Guid> sourceIds,
        Guid? wikiPageId,
        CancellationToken ct)
    {
        if (topicId.HasValue)
        {
            var ownsTopic = await _db.Topics.AsNoTracking().AnyAsync(t => t.Id == topicId.Value && t.UserId == userId && !t.IsArchived, ct);
            if (!ownsTopic) return null;
        }

        if (wikiPageId.HasValue)
        {
            var ownsPage = await _db.WikiPages.AsNoTracking().AnyAsync(p =>
                p.Id == wikiPageId.Value && p.UserId == userId && !p.IsDeleted &&
                (!topicId.HasValue || p.TopicId == topicId.Value), ct);
            if (!ownsPage) return null;
        }

        var ids = sourceIds.Concat(sourceId.HasValue ? [sourceId.Value] : Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(8)
            .ToArray();

        var query = _db.LearningSources.AsNoTracking().Where(s => s.UserId == userId && !s.IsDeleted);
        if (topicId.HasValue) query = query.Where(s => s.TopicId == topicId.Value);
        if (ids.Length > 0) query = query.Where(s => ids.Contains(s.Id));

        var sources = await query.OrderByDescending(s => s.UpdatedAt).Take(8).ToListAsync(ct);
        if (ids.Length > 0 && sources.Count != ids.Length) return null;
        if (ids.Length == 0 && !topicId.HasValue) return null;

        return new ThreadContext(
            topicId ?? sources.FirstOrDefault(s => s.TopicId.HasValue)?.TopicId,
            sources.Select(s => s.Id).ToArray(),
            sources.Select(s => SanitizeText(s.Title, 160, out _)).Where(NotBlank).ToArray(),
            sources.Count == 0 ? ["no_source_available"] : []);
    }

    private async Task<LearningArtifact?> LoadThreadArtifactAsync(Guid userId, Guid threadId, bool tracking, CancellationToken ct)
    {
        var query = tracking ? _db.LearningArtifacts : _db.LearningArtifacts.AsNoTracking();
        return await query.FirstOrDefaultAsync(a =>
            a.Id == threadId &&
            a.UserId == userId &&
            !a.IsDeleted &&
            a.ArtifactType == ArtifactType, ct);
    }

    private SourceQuestionThreadDto? TryMap(LearningArtifact artifact)
    {
        try
        {
            var state = ParseState(artifact);
            return new SourceQuestionThreadDto
            {
                ThreadId = artifact.Id,
                TopicId = state.TopicId ?? artifact.TopicId,
                SourceIds = state.SourceIds,
                WikiPageId = state.WikiPageId,
                ConceptKey = state.ConceptKey,
                Title = state.Title,
                Status = state.Status,
                SourceBasis = state.SourceBasis,
                EvidenceStatus = state.EvidenceStatus,
                SourceReadiness = state.SourceReadiness,
                CitationReviewStatus = state.CitationReviewStatus,
                LinkedConcepts = state.Turns.SelectMany(t => t.RelatedConcepts).GroupBy(l => l.ConceptKey).Select(g => g.First()).Take(12).ToArray(),
                LinkedWikiPages = state.Turns.SelectMany(t => t.RelatedWikiPages).Where(l => l.WikiPageId.HasValue).GroupBy(l => l.WikiPageId).Select(g => g.First()).Take(12).ToArray(),
                Warnings = state.Warnings,
                Turns = state.Turns,
                CreatedAt = artifact.CreatedAt,
                UpdatedAt = artifact.UpdatedAt
            };
        }
        catch
        {
            return null;
        }
    }

    private static SourceQuestionTurnDto BuildTurn(
        string safeQuestion,
        SourceQuestionResponseDto answer,
        IReadOnlyList<string> blockedQuestionTerms)
    {
        var summary = SanitizeText(answer.Answer, MaxAnswerSummaryLength, out var blockedAnswerTerms);
        if (blockedAnswerTerms.Count > 0)
        {
            summary = "Source Q&A answer was redacted because unsafe internal payload markers were detected.";
        }

        var warnings = answer.Warnings
            .Concat(blockedQuestionTerms.Count > 0 ? ["question_payload_redacted"] : Array.Empty<string>())
            .Concat(blockedAnswerTerms.Count > 0 ? ["answer_payload_redacted"] : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        return new SourceQuestionTurnDto
        {
            TurnId = Guid.NewGuid(),
            Question = blockedQuestionTerms.Count > 0 ? "Question was redacted because it contained unsafe internal markers." : safeQuestion,
            SafeAnswerSummary = summary,
            SourceBasis = answer.SourceBasis,
            EvidenceStatus = answer.EvidenceStatus,
            Citations = answer.Citations.Take(8).ToArray(),
            RelatedConcepts = answer.RelatedConcepts.Take(8).ToArray(),
            RelatedWikiPages = answer.RelatedWikiPages.Take(8).ToArray(),
            ReviewStatus = ReviewStatusFor(answer),
            Warnings = warnings,
            TraceBlockId = answer.TraceBlockId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static SourceQuestionTurnDto BuildDegradedTurn(string safeQuestion, IReadOnlyList<string> blockedQuestionTerms) =>
        new()
        {
            TurnId = Guid.NewGuid(),
            Question = blockedQuestionTerms.Count > 0 ? "Question was redacted because it contained unsafe internal markers." : safeQuestion,
            SafeAnswerSummary = "Source Q&A could not be completed safely. Review source readiness and try again.",
            SourceBasis = "degraded",
            EvidenceStatus = "degraded",
            ReviewStatus = "needs_review",
            Warnings = ["source_question_thread_answer_failed"],
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static void ApplyAggregateState(ThreadState state)
    {
        var turns = state.Turns;
        state.SourceBasis = turns.LastOrDefault()?.SourceBasis ?? "evidence_insufficient";
        state.EvidenceStatus = turns.LastOrDefault()?.EvidenceStatus ?? "evidence_insufficient";
        state.SourceReadiness = state.EvidenceStatus is "source_grounded" or "mixed" ? "source_ready" :
            state.EvidenceStatus is "degraded" or "stale" ? "degraded" : "evidence_insufficient";
        state.CitationReviewStatus = AggregateReviewStatus(turns);
        state.Status = state.CitationReviewStatus switch
        {
            "supported" => "reviewed",
            "unsupported" or "missing_citation" or "needs_review" => "needs_review",
            "stale" or "degraded" => "degraded",
            _ => "active"
        };
        state.Warnings = state.Warnings
            .Concat(turns.SelectMany(t => t.Warnings))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
        state.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string AggregateReviewStatus(IReadOnlyList<SourceQuestionTurnDto> turns)
    {
        if (turns.Count == 0) return "not_checked";
        if (turns.Any(t => t.ReviewStatus is "unsupported")) return "unsupported";
        if (turns.Any(t => t.ReviewStatus is "missing_citation")) return "missing_citation";
        if (turns.Any(t => t.ReviewStatus is "stale" or "degraded")) return "stale";
        if (turns.Any(t => t.ReviewStatus is "needs_review" or "not_checked")) return "needs_review";
        return "supported";
    }

    private static string ReviewStatusFor(SourceQuestionResponseDto answer)
    {
        if (answer.SourceBasis is "degraded" || answer.EvidenceStatus is "degraded" or "stale") return "degraded";
        if (answer.Warnings.Any(w => w.Contains("citation_unsupported", StringComparison.OrdinalIgnoreCase))) return "unsupported";
        if (answer.Warnings.Any(w => w.Contains("citation_missing", StringComparison.OrdinalIgnoreCase)) || answer.Citations.Count == 0) return "missing_citation";
        if (answer.SourceBasis == "source_grounded") return "supported";
        return "needs_review";
    }

    private static void SaveState(LearningArtifact artifact, ThreadState state)
    {
        artifact.Title = state.Title;
        artifact.ArtifactStatus = state.Status;
        artifact.SourceBasis = state.SourceBasis == "source_grounded" ? "source_grounded" :
            state.SourceBasis == "wiki_backed" ? "wiki_backed" :
            state.SourceBasis == "tool_evidence" ? "tool_evidence" :
            state.SourceBasis == "code_output" ? "code_output" :
            state.SourceBasis == "evidence_insufficient" ? "evidence_insufficient" : "model_assisted";
        artifact.SafeContent = BuildSafeContent(state);
        artifact.ContentJson = SerializeState(state);
        artifact.SafetyWarningsJson = SerializeStrings(state.Warnings);
        artifact.UpdatedAt = DateTime.UtcNow;
    }

    private static string BuildSafeContent(ThreadState state)
    {
        var questions = state.Turns
            .TakeLast(4)
            .Select(t => $"- {SanitizeText(t.Question, 180, out _)} [{t.ReviewStatus}]");
        return string.Join("\n", new[]
        {
            $"# {state.Title}",
            $"Status: {state.Status}",
            $"Source basis: {state.SourceBasis}",
            $"Citation review: {state.CitationReviewStatus}",
            "Recent questions:",
            string.Join("\n", questions)
        }).Trim();
    }

    private static string BuildFollowUpQuestion(string question, ThreadState state)
    {
        var prior = state.Turns
            .TakeLast(3)
            .Select(t => $"Previous safe summary: Q={t.Question}; basis={t.SourceBasis}; review={t.ReviewStatus}; answer={t.SafeAnswerSummary}")
            .Select(s => SanitizeText(s, 500, out _))
            .Where(NotBlank)
            .ToArray();

        if (prior.Length == 0) return question;
        return $"{question}\n\nSafe prior source Q&A context only:\n{string.Join("\n", prior)}";
    }

    private static string BuildThreadTitle(string? requested, string? initialQuestion, IReadOnlyList<string> sourceTitles)
    {
        var basis = !string.IsNullOrWhiteSpace(requested)
            ? requested
            : !string.IsNullOrWhiteSpace(initialQuestion)
                ? initialQuestion
                : sourceTitles.FirstOrDefault() ?? "Source Q&A thread";
        return SanitizeText(basis, 120, out var blocked).Trim() is { Length: > 0 } safe && blocked.Count == 0
            ? safe
            : "Source Q&A thread";
    }

    private static string NormalizeReviewStatus(string? status)
    {
        var key = Normalize(status);
        return key is "supported" or "unsupported" or "missing_citation" or "needs_review" or "stale" or "not_checked" or "mixed"
            ? key
            : "needs_review";
    }

    private static string BuildTraceMetadata(ThreadState state, SourceQuestionTurnDto turn) =>
        JsonSerializer.Serialize(new
        {
            threadStatus = state.Status,
            sourceBasis = turn.SourceBasis,
            reviewStatus = turn.ReviewStatus,
            citationCount = turn.Citations.Count,
            warnings = turn.Warnings.Take(6).ToArray()
        }, JsonOptions);

    private static ThreadState ParseState(LearningArtifact artifact)
    {
        if (!string.IsNullOrWhiteSpace(artifact.ContentJson))
        {
            var parsed = JsonSerializer.Deserialize<ThreadState>(artifact.ContentJson, JsonOptions);
            if (parsed != null)
            {
                parsed.Title = string.IsNullOrWhiteSpace(parsed.Title) ? artifact.Title : parsed.Title;
                parsed.TopicId ??= artifact.TopicId;
                return parsed;
            }
        }

        return new ThreadState
        {
            TopicId = artifact.TopicId,
            Title = artifact.Title,
            Status = artifact.ArtifactStatus,
            SourceBasis = artifact.SourceBasis,
            EvidenceStatus = "evidence_insufficient",
            SourceReadiness = "evidence_insufficient",
            CitationReviewStatus = "not_checked",
            CreatedAt = artifact.CreatedAt,
            UpdatedAt = artifact.UpdatedAt
        };
    }

    private static string SerializeState(ThreadState state) => JsonSerializer.Serialize(state, JsonOptions);
    private static string SerializeStrings(IReadOnlyList<string> values) => JsonSerializer.Serialize(values, JsonOptions);

    private async Task<int> CountRelatedConceptsAsync(
        Guid userId,
        Guid? topicId,
        IReadOnlyList<Guid> sourceIds,
        Guid? wikiPageId,
        CancellationToken ct)
    {
        var query = _db.WikiLinks.AsNoTracking()
            .Where(l => l.UserId == userId && !l.IsDeleted)
            .Where(l => l.LinkType == "source_supports" ||
                        l.LinkType == "source_mentions" ||
                        l.LinkType == "source_reviews" ||
                        l.LinkType == "source_remediates");
        if (topicId.HasValue) query = query.Where(l => l.TopicId == topicId.Value);
        if (wikiPageId.HasValue) query = query.Where(l => l.TargetPageId == wikiPageId.Value || l.SourcePageId == wikiPageId.Value);
        var rows = await query
            .Select(l => new { l.TargetPageId, l.SourcePageId, l.MetadataJson })
            .Take(120)
            .ToListAsync(ct);

        if (sourceIds.Count == 0)
        {
            return rows.Select(r => r.TargetPageId).Where(id => id.HasValue).Distinct().Count();
        }

        return rows
            .Where(r => SourceIdsMatch(r.MetadataJson, sourceIds))
            .Select(r => r.TargetPageId)
            .Where(id => id.HasValue)
            .Distinct()
            .Count();
    }

    private static bool SourceIdsMatch(string? metadataJson, IReadOnlyList<Guid> sourceIds)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty("sourceId", out var sourceElement)) return false;
            return Guid.TryParse(sourceElement.GetString(), out var parsed) && sourceIds.Contains(parsed);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> BuildStudyWarnings(
        IReadOnlyList<LearningSource> sources,
        IReadOnlyList<SourceQuestionThreadDto> threads,
        IReadOnlyList<SourceQuestionTurnDto> turns,
        int citationWarnings)
    {
        var warnings = new List<string>();
        if (sources.Count == 0) warnings.Add("no_source_available");
        if (threads.Count == 0) warnings.Add("source_question_thread_empty");
        if (turns.Any(t => t.ReviewStatus is "needs_review" or "not_checked")) warnings.Add("source_question_review_needed");
        if (turns.Any(t => t.ReviewStatus is "missing_citation" or "unsupported")) warnings.Add("source_question_citation_warning");
        if (turns.Any(t => t.SourceBasis is "degraded" || t.EvidenceStatus is "degraded" or "stale")) warnings.Add("source_question_thread_degraded");
        if (citationWarnings > 0) warnings.Add("citation_review_needed");
        if (sources.Any(s => !string.Equals(s.Status, "ready", StringComparison.OrdinalIgnoreCase))) warnings.Add("source_readiness_degraded");
        warnings.AddRange(threads.SelectMany(t => t.Warnings).Select(w => SanitizeText(w, 120, out _)).Where(NotBlank).Take(8));
        return warnings;
    }

    private static IReadOnlyList<string> BuildStudyNextActions(
        int sourceCount,
        int turnCount,
        int needsReview,
        int degraded,
        int citationWarnings,
        int relatedConceptCount)
    {
        var actions = new List<string>();
        if (sourceCount == 0)
        {
            actions.Add("add_source");
            return actions;
        }

        if (turnCount == 0) actions.Add("ask_source_question");
        if (needsReview > 0 || citationWarnings > 0) actions.Add("review_citations");
        if (degraded > 0) actions.Add("refresh_source_evidence");
        if (sourceCount >= 2) actions.Add("compare_sources");
        if (relatedConceptCount == 0) actions.Add("sync_source_concepts");
        actions.Add("create_source_pack");
        actions.Add("write_safe_note_to_wiki");
        return actions;
    }

    private static string ResolveStudyStatus(
        IReadOnlyList<LearningSource> sources,
        IReadOnlyList<SourceQuestionTurnDto> turns,
        IReadOnlyList<string> warnings)
    {
        if (sources.Count == 0) return "empty";
        if (turns.Count == 0) return "ready";
        if (warnings.Any(w => w.Contains("degraded", StringComparison.OrdinalIgnoreCase))) return "degraded";
        if (warnings.Any(w => w.Contains("review", StringComparison.OrdinalIgnoreCase) ||
                              w.Contains("citation", StringComparison.OrdinalIgnoreCase))) return "needs_review";
        return "reviewed";
    }

    private static string ResolveAggregateSourceReadiness(IReadOnlyList<LearningSource> sources)
    {
        if (sources.Count == 0) return "evidence_insufficient";
        if (sources.Any(s => !string.Equals(s.Status, "ready", StringComparison.OrdinalIgnoreCase))) return "degraded";
        return "source_ready";
    }

    private static string ResolveAggregateEvidenceStatus(
        IReadOnlyList<LearningSource> sources,
        IReadOnlyList<SourceQuestionTurnDto> turns)
    {
        if (turns.Any(t => t.EvidenceStatus is "degraded" or "stale")) return "degraded";
        if (turns.Any(t => t.EvidenceStatus == "source_grounded")) return "source_grounded";
        if (turns.Any(t => t.EvidenceStatus == "mixed")) return "mixed";
        if (sources.Count > 0 && sources.All(s => string.Equals(s.Status, "ready", StringComparison.OrdinalIgnoreCase))) return "mixed";
        return "evidence_insufficient";
    }

    private static bool IsCitationWarning(string? status)
    {
        var key = Normalize(status);
        return key is "unsupported" or "missing" or "missing_citation" or "needs_review" or "stale" or "not_checked" or "degraded";
    }

    private async Task RecordTelemetryAsync(
        Guid userId,
        Guid? topicId,
        string operation,
        string status,
        int? count,
        CancellationToken ct)
    {
        if (_telemetry == null) return;
        try
        {
            await _telemetry.RecordEventAsync(userId, new LearningRuntimeEventRequestDto
            {
                TopicId = topicId,
                Category = "orkalm_source_question_memory",
                Operation = operation,
                Status = status,
                Severity = status is "failed" or "degraded" ? "warning" : "info",
                SafeMessage = "Source Q&A thread event recorded without raw source text, prompts, provider payloads, or answers.",
                SafeMetadata = new Dictionary<string, string>
                {
                    ["rawContentStored"] = "false",
                    ["count"] = count?.ToString() ?? "0"
                }
            }, ct);
        }
        catch
        {
            // Telemetry must never block source Q&A memory.
        }
    }

    private static string SanitizeText(string? value, int maxLength, out IReadOnlyList<string> blockedTerms)
    {
        var blocked = new List<string>();
        var safe = value ?? string.Empty;
        foreach (var marker in BlockedMarkers)
        {
            if (safe.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                blocked.Add(marker);
                safe = Regex.Replace(safe, Regex.Escape(marker), "[redacted]", RegexOptions.IgnoreCase);
            }
        }

        if (Regex.IsMatch(safe, @"[A-Za-z]:\\|\\\\"))
        {
            blocked.Add("localPath");
            safe = Regex.Replace(safe, @"[A-Za-z]:\\[^\s]+|\\\\[^\s]+", "[redacted-path]");
        }

        safe = Regex.Replace(safe, @"\s+", " ").Trim();
        blockedTerms = blocked.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return safe.Length <= maxLength ? safe : safe[..maxLength].Trim();
    }

    private static string Normalize(string? value) =>
        Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"[^a-z0-9_]+", "_").Trim('_');

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    private sealed class ThreadState
    {
        public Guid? TopicId { get; set; }
        public IReadOnlyList<Guid> SourceIds { get; set; } = Array.Empty<Guid>();
        public Guid? WikiPageId { get; set; }
        public string? ConceptKey { get; set; }
        public string Title { get; set; } = "Source Q&A thread";
        public string Status { get; set; } = "active";
        public string SourceBasis { get; set; } = "evidence_insufficient";
        public string EvidenceStatus { get; set; } = "evidence_insufficient";
        public string SourceReadiness { get; set; } = "evidence_insufficient";
        public string CitationReviewStatus { get; set; } = "not_checked";
        public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
        public IReadOnlyList<SourceQuestionTurnDto> Turns { get; set; } = Array.Empty<SourceQuestionTurnDto>();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed record ThreadContext(
        Guid? TopicId,
        IReadOnlyList<Guid> SourceIds,
        IReadOnlyList<string> SourceTitles,
        IReadOnlyList<string> Warnings);
}

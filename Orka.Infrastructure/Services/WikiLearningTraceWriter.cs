using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class WikiLearningTraceWriter : IWikiLearningTraceWriter
{
    private static readonly string[] BlockedMarkers =
    [
        "rawPrompt",
        "hiddenPrompt",
        "systemPrompt",
        "rawProviderPayload",
        "rawSourceChunk",
        "rawToolPayload",
        "debugTrace",
        "localPath",
        "apiKey",
        "secret",
        "answerKey",
        "correctAnswer",
        "stackTrace"
    ];

    private readonly OrkaDbContext _db;
    private readonly IWikiService _wikiService;
    private readonly ITopicScopeResolver? _topicScopeResolver;
    private readonly ILogger<WikiLearningTraceWriter> _logger;

    public WikiLearningTraceWriter(
        OrkaDbContext db,
        IWikiService wikiService,
        ILogger<WikiLearningTraceWriter> logger,
        ITopicScopeResolver? topicScopeResolver = null)
    {
        _db = db;
        _wikiService = wikiService;
        _logger = logger;
        _topicScopeResolver = topicScopeResolver;
    }

    public Task<WikiBlockDto?> RecordTutorExplanationAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default) =>
        RecordAsync(request, TraceTypeOrDefault(request, "tutor_explanation"), DefaultSourceBasis(request.SourceBasis, "tutor_generated"), ct);

    public Task<WikiBlockDto?> RecordStudentQuestionAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default) =>
        RecordAsync(request, TraceTypeOrDefault(request, "student_question"), DefaultSourceBasis(request.SourceBasis, "student_manual"), ct);

    public Task<WikiBlockDto?> RecordQuizResultAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default) =>
        RecordAsync(request, TraceTypeOrDefault(request, "quiz_result"), DefaultSourceBasis(request.SourceBasis, "assessment_verified"), ct);

    public Task<WikiBlockDto?> RecordMisconceptionAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default) =>
        RecordAsync(request, TraceTypeOrDefault(request, "misconception_note"), DefaultSourceBasis(request.SourceBasis, "assessment_signal"), ct);

    public Task<WikiBlockDto?> RecordRepairNoteAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default) =>
        RecordAsync(request, TraceTypeOrDefault(request, "repair_note"), DefaultSourceBasis(request.SourceBasis, "tutor_generated"), ct);

    public Task<WikiBlockDto?> RecordWorkedExampleAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default) =>
        RecordAsync(request, TraceTypeOrDefault(request, "worked_example"), DefaultSourceBasis(request.SourceBasis, "tutor_generated"), ct);

    public Task<WikiBlockDto?> RecordSourceNoteAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default) =>
        RecordAsync(request, TraceTypeOrDefault(request, "source_note"), DefaultSourceBasis(request.SourceBasis, "source_grounded"), ct);

    public Task<WikiBlockDto?> RecordArtifactLinkAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default) =>
        RecordAsync(request, TraceTypeOrDefault(request, "artifact_link"), DefaultSourceBasis(request.SourceBasis, "wiki_backed"), ct);

    public Task<WikiBlockDto?> RecordManualNoteAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default) =>
        RecordAsync(request, TraceTypeOrDefault(request, "manual_note"), DefaultSourceBasis(request.SourceBasis, "student_manual"), ct);

    private async Task<WikiBlockDto?> RecordAsync(
        WikiLearningTraceRequestDto request,
        string blockType,
        string sourceBasis,
        CancellationToken ct)
    {
        if (request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.SafeContent)) return null;

        try
        {
            var page = await ResolvePageAsync(request, ct);
            if (page == null) return null;

            var safeTitle = SanitizeTraceText(request.Title, 240);
            var safeContent = SanitizeTraceText(request.SafeContent, 2400);
            if (string.IsNullOrWhiteSpace(safeContent)) return null;

            var normalizedType = NormalizeBlockType(blockType);
            var duplicate = await FindDuplicateAsync(page.Id, normalizedType, safeTitle, safeContent, request, ct);
            if (duplicate != null) return ToDto(duplicate);

            return await _wikiService.AddWikiBlockAsync(page.Id, request.UserId, new CreateWikiBlockRequestDto
            {
                BlockType = blockType,
                Title = safeTitle,
                Content = safeContent,
                Source = SanitizeTraceText(request.CreatedBy, 80),
                SourceBasis = NormalizeSourceBasis(sourceBasis),
                ConceptKey = SanitizeTraceText(request.ConceptKey, 180) ?? page.ConceptKey,
                MisconceptionKey = SanitizeTraceText(request.MisconceptionKey, 180),
                QuizAttemptId = request.QuizAttemptId,
                SourceEvidenceBundleId = request.SourceEvidenceBundleId,
                LearningArtifactId = request.LearningArtifactId,
                TutorTurnStateId = request.TutorTurnStateId,
                Visibility = string.IsNullOrWhiteSpace(request.Visibility)
                    ? DefaultVisibility(blockType)
                    : request.Visibility!
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[WikiTrace] Trace write failed. UserRef={UserRef} TopicRef={TopicRef} TraceType={TraceType} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(request.UserId, "usr"),
                LogPrivacyGuard.SafeId(request.TopicId, "topic"),
                LogPrivacyGuard.SafeMessage(blockType, 80),
                LogPrivacyGuard.SafeExceptionType(ex));
            return null;
        }
    }

    private async Task<WikiPage?> ResolvePageAsync(WikiLearningTraceRequestDto request, CancellationToken ct)
    {
        if (request.ActiveWikiPageId.HasValue)
        {
            var activePage = await _db.WikiPages.FirstOrDefaultAsync(p =>
                p.Id == request.ActiveWikiPageId.Value &&
                p.UserId == request.UserId &&
                !p.IsDeleted &&
                (!request.TopicId.HasValue || p.TopicId == request.TopicId.Value), ct);
            if (activePage != null) return activePage;
        }

        var topicIds = await ResolveTopicIdsAsync(request.UserId, request.TopicId, ct);
        if (topicIds.Count == 0) return null;

        var pageQuery = _db.WikiPages
            .Where(p => p.UserId == request.UserId && topicIds.Contains(p.TopicId) && !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.ConceptKey))
        {
            var concept = request.ConceptKey.Trim();
            var conceptPage = await pageQuery
                .OrderByDescending(p => p.TopicId == request.TopicId)
                .ThenBy(p => p.OrderIndex)
                .FirstOrDefaultAsync(p => p.ConceptKey != null && p.ConceptKey == concept, ct);
            if (conceptPage != null) return conceptPage;
        }

        if (request.SourceId.HasValue)
        {
            var sourceKey = request.SourceId.Value.ToString("N");
            var sourcePage = await pageQuery
                .OrderBy(p => p.OrderIndex)
                .FirstOrDefaultAsync(p =>
                    p.PageType == "source_note" ||
                    p.PageType == "orkalm_source" ||
                    p.PageKey.Contains(sourceKey), ct);
            if (sourcePage != null) return sourcePage;
        }

        var root = await pageQuery
            .OrderByDescending(p => p.TopicId == request.TopicId)
            .ThenBy(p => p.OrderIndex)
            .FirstOrDefaultAsync(p => p.PageType == "topic_root", ct);
        if (root != null) return root;

        var first = await pageQuery
            .OrderByDescending(p => p.TopicId == request.TopicId)
            .ThenBy(p => p.OrderIndex)
            .FirstOrDefaultAsync(ct);
        if (first != null) return first;

        return await CreateFallbackPageAsync(request, ct);
    }

    private async Task<List<Guid>> ResolveTopicIdsAsync(Guid userId, Guid? topicId, CancellationToken ct)
    {
        if (!topicId.HasValue) return [];
        if (_topicScopeResolver != null)
        {
            var scope = await _topicScopeResolver.ResolveAsync(userId, topicId.Value, ct);
            if (scope.IsValid && scope.TreeTopicIds.Count > 0)
            {
                return scope.TreeTopicIds.ToList();
            }
        }

        var ownsTopic = await _db.Topics.AnyAsync(t => t.Id == topicId.Value && t.UserId == userId && !t.IsArchived, ct);
        return ownsTopic ? [topicId.Value] : [];
    }

    private async Task<WikiPage?> CreateFallbackPageAsync(WikiLearningTraceRequestDto request, CancellationToken ct)
    {
        if (!request.TopicId.HasValue) return null;
        var topic = await _db.Topics.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TopicId.Value && t.UserId == request.UserId && !t.IsArchived, ct);
        if (topic == null) return null;

        var now = DateTime.UtcNow;
        var conceptKey = SanitizeTraceText(request.ConceptKey, 180);
        var title = !string.IsNullOrWhiteSpace(conceptKey)
            ? HumanizeConceptKey(conceptKey)
            : topic.Title;
        var pageType = !string.IsNullOrWhiteSpace(conceptKey) ? "concept" : "topic_root";
        var pageKey = !string.IsNullOrWhiteSpace(conceptKey)
            ? $"concept:{CleanPageKey(conceptKey)}"
            : $"topic:{topic.Id:N}";

        var existing = await _db.WikiPages.FirstOrDefaultAsync(p =>
            p.UserId == request.UserId &&
            p.TopicId == topic.Id &&
            p.PageKey == pageKey &&
            !p.IsDeleted, ct);
        if (existing != null) return existing;

        var order = await _db.WikiPages
            .Where(p => p.UserId == request.UserId && p.TopicId == topic.Id && !p.IsDeleted)
            .MaxAsync(p => (int?)p.OrderIndex, ct) ?? 0;

        var page = new WikiPage
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            TopicId = topic.Id,
            SessionId = request.SessionId,
            PlanStepId = request.PlanStepId,
            PageKey = pageKey,
            PageType = pageType,
            ConceptKey = conceptKey,
            ParentConceptKey = SanitizeTraceText(request.ParentConceptKey, 180),
            Title = title,
            SourceReadiness = "evidence_insufficient",
            EvidenceStatus = "evidence_insufficient",
            SafeSummary = $"Learning notes for {title}",
            Status = "learning",
            OrderIndex = order + 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.WikiPages.Add(page);
        await _db.SaveChangesAsync(ct);
        return page;
    }

    private async Task<WikiBlock?> FindDuplicateAsync(
        Guid pageId,
        WikiBlockType blockType,
        string? safeTitle,
        string safeContent,
        WikiLearningTraceRequestDto request,
        CancellationToken ct)
    {
        var query = _db.WikiBlocks
            .AsNoTracking()
            .Where(b => b.WikiPageId == pageId && b.BlockType == blockType && !b.IsDeleted);

        if (request.QuizAttemptId.HasValue)
        {
            var quizDuplicate = await query.FirstOrDefaultAsync(b => b.QuizAttemptId == request.QuizAttemptId.Value, ct);
            if (quizDuplicate != null) return quizDuplicate;
        }

        if (request.LearningArtifactId.HasValue)
        {
            var artifactDuplicate = await query.FirstOrDefaultAsync(b => b.LearningArtifactId == request.LearningArtifactId.Value, ct);
            if (artifactDuplicate != null) return artifactDuplicate;
        }

        if (request.TutorTurnStateId.HasValue)
        {
            var tutorDuplicate = await query.FirstOrDefaultAsync(b => b.TutorTurnStateId == request.TutorTurnStateId.Value, ct);
            if (tutorDuplicate != null) return tutorDuplicate;
        }

        var normalizedContent = NormalizeForDedupe(safeContent);
        var normalizedTitle = NormalizeForDedupe(safeTitle ?? string.Empty);
        var conceptKey = SanitizeTraceText(request.ConceptKey, 180);
        var since = DateTime.UtcNow.AddHours(-12);
        var candidates = await query
            .Where(b => b.CreatedAt >= since)
            .OrderByDescending(b => b.CreatedAt)
            .Take(24)
            .ToListAsync(ct);

        return candidates.FirstOrDefault(b =>
            string.Equals(b.ConceptKey ?? string.Empty, conceptKey ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(normalizedTitle) || NormalizeForDedupe(b.Title) == normalizedTitle) &&
            NormalizeForDedupe(b.Content) == normalizedContent);
    }

    private static string? SanitizeTraceText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sanitized = value.Trim();
        foreach (var marker in BlockedMarkers)
        {
            sanitized = Regex.Replace(sanitized, Regex.Escape(marker), "[redacted]", RegexOptions.IgnoreCase);
        }

        sanitized = Regex.Replace(sanitized, @"[A-Za-z]:\\[^\s]+", "[redacted_path]");
        sanitized = Regex.Replace(sanitized, @"\s+", " ");
        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    private static string NormalizeForDedupe(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");

    private static string DefaultSourceBasis(string requested, string fallback) =>
        string.IsNullOrWhiteSpace(requested)
            ? fallback
            : requested;

    private static string TraceTypeOrDefault(WikiLearningTraceRequestDto request, string fallback) =>
        string.IsNullOrWhiteSpace(request.TraceType) || request.TraceType == "manual_note"
            ? fallback
            : request.TraceType;

    private static string NormalizeSourceBasis(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "model_assisted" : value.Trim().ToLowerInvariant();
        return key is "source_grounded" or "wiki_backed" or "tool_evidence" or "code_output" or "model_assisted" or
            "evidence_insufficient" or "student_manual" or "tutor_generated" or "assessment_verified" or "assessment_signal"
            ? key
            : "model_assisted";
    }

    private static string DefaultVisibility(string blockType) =>
        blockType is "repair_note" or "misconception_note" ? "highlighted" : "normal";

    private static WikiBlockType NormalizeBlockType(string value)
    {
        var key = Regex.Replace(value.Trim(), @"[\s\-]+", "_").ToLowerInvariant();
        return key switch
        {
            "tutor_explanation" => WikiBlockType.TutorExplanation,
            "student_question" => WikiBlockType.StudentQuestion,
            "source_note" => WikiBlockType.SourceNote,
            "source_excerpt_summary" => WikiBlockType.SourceExcerptSummary,
            "worked_example" => WikiBlockType.WorkedExample,
            "misconception_note" => WikiBlockType.MisconceptionNote,
            "repair_note" => WikiBlockType.RepairNote,
            "quiz_result" => WikiBlockType.QuizResult,
            "quiz_review" => WikiBlockType.QuizReview,
            "artifact_link" => WikiBlockType.ArtifactLink,
            "checkpoint" => WikiBlockType.Checkpoint,
            "manual_note" => WikiBlockType.ManualNote,
            _ => WikiBlockType.ManualNote
        };
    }

    private static string CleanPageKey(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9\-_:.]+", "-").Trim('-');

    private static string HumanizeConceptKey(string conceptKey)
    {
        var cleaned = conceptKey.Replace('_', ' ').Replace('-', ' ').Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Concept notes" : cleaned;
    }

    private static WikiBlockDto ToDto(WikiBlock block) => new()
    {
        Id = block.Id,
        WikiPageId = block.WikiPageId,
        BlockType = ToSnakeCase(block.BlockType.ToString()),
        Title = block.Title,
        Content = block.Content,
        Source = block.Source,
        SourceBasis = block.SourceBasis,
        ConceptKey = block.ConceptKey,
        MisconceptionKey = block.MisconceptionKey,
        QuizAttemptId = block.QuizAttemptId,
        SourceEvidenceBundleId = block.SourceEvidenceBundleId,
        LearningArtifactId = block.LearningArtifactId,
        TutorTurnStateId = block.TutorTurnStateId,
        Visibility = block.Visibility,
        SafetyWarnings = ParseSafetyWarnings(block.SafetyWarningsJson),
        OrderIndex = block.OrderIndex,
        CreatedAt = block.CreatedAt,
        UpdatedAt = block.UpdatedAt
    };

    private static string ToSnakeCase(string value) =>
        Regex.Replace(value, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();

    private static IReadOnlyList<string> ParseSafetyWarnings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch
        {
            return new[] { "invalid_safety_warning_payload" };
        }
    }
}

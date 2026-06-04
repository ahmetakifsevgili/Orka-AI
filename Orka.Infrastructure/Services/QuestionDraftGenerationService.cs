using System.Text.RegularExpressions;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed partial class QuestionDraftGenerationService : IQuestionDraftGenerationService
{
    private const int DefaultDesiredCount = 3;
    private const int MaxDesiredCount = 5;
    private const int MaxStatementLength = 220;
    private const string SupportedQuestionType = "multiple_choice";

    private static readonly string[] OfficialClaimMarkers =
    [
        "official curriculum",
        "official simulation",
        "resmi mufredat",
        "resmi müfredat",
        "resmi sinav",
        "resmi sınav",
        "resmi kapsam",
        "osym",
        "ösym",
        "meb"
    ];

    private readonly IQuestionImportService _questionImports;

    public QuestionDraftGenerationService(IQuestionImportService questionImports)
    {
        _questionImports = questionImports;
    }

    public async Task<QuestionDraftPreviewDto> PreviewDraftGenerationAsync(
        Guid userId,
        QuestionDraftGenerationRequestDto request,
        CancellationToken ct = default)
    {
        var issues = ValidateRequest(request);
        var statements = issues.Any(i => i.Severity == "error")
            ? new List<string>()
            : ExtractStatements(request.Source).Take(NormalizedDesiredCount(request.DesiredCount)).ToList();

        if (!issues.Any(i => i.Severity == "error") && statements.Count == 0)
        {
            issues.Add(Error("source_context_required", "Source context must include at least one usable statement."));
        }

        if (issues.Any(i => i.Severity == "error"))
        {
            return InvalidPreview(request, issues);
        }

        var items = statements
            .Select((statement, index) => BuildImportItem(request, statement, index))
            .ToList();

        var importPreview = await _questionImports.PreviewImportAsync(
            userId,
            new QuestionImportRequestDto { Items = items },
            ct);

        return ToDraftPreview(importPreview, request.DesiredCount);
    }

    public async Task<QuestionDraftApprovalResultDto> ApproveDraftsToQuestionBankAsync(
        Guid userId,
        QuestionDraftApprovalDto request,
        CancellationToken ct = default)
    {
        var result = await _questionImports.ApproveImportAsync(
            userId,
            new QuestionImportApprovalDto { ImportPreviewId = request.DraftPreviewId },
            ct);

        return new QuestionDraftApprovalResultDto
        {
            DraftPreviewId = request.DraftPreviewId,
            ImportPreviewId = result.ImportPreviewId,
            Status = result.Status,
            CreatedCount = result.CreatedCount,
            SkippedCount = result.SkippedCount,
            RejectedCount = result.RejectedCount,
            CreatedQuestionIds = result.CreatedQuestionIds,
            Issues = result.Issues.Select(ToDraftIssue).ToList()
        };
    }

    public async Task<QuestionDraftPreviewDto?> GetDraftGenerationPreviewAsync(
        Guid userId,
        Guid draftPreviewId,
        CancellationToken ct = default)
    {
        var importPreview = await _questionImports.GetImportPreviewAsync(userId, draftPreviewId, ct);
        return importPreview is null ? null : ToDraftPreview(importPreview, importPreview.TotalCount);
    }

    private static List<QuestionDraftGenerationIssueDto> ValidateRequest(QuestionDraftGenerationRequestDto request)
    {
        var issues = new List<QuestionDraftGenerationIssueDto>();
        var context = request.Context;
        var source = request.Source;

        if (context.ExamDefinitionId is null && string.IsNullOrWhiteSpace(context.ExamCode))
        {
            issues.Add(Error("exam_context_required", "Exam context is required for draft generation."));
        }

        if (!string.Equals(Clean(request.QuestionType), SupportedQuestionType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Error("unsupported_generation_question_type", "Only multiple_choice draft generation is supported."));
        }

        if (request.DesiredCount > MaxDesiredCount)
        {
            issues.Add(Error("desired_count_exceeds_cap", $"Desired count cannot exceed {MaxDesiredCount}."));
        }

        if (request.DesiredCount < 0)
        {
            issues.Add(Error("desired_count_invalid", "Desired count cannot be negative."));
        }

        if (string.IsNullOrWhiteSpace(source.SourceTitle))
        {
            issues.Add(Error("source_title_required", "Source title is required."));
        }

        if (string.IsNullOrWhiteSpace(source.SourceText)
            && !source.StructuredSourceContext.Any(s => !string.IsNullOrWhiteSpace(s)))
        {
            issues.Add(Error("source_context_required", "Source text or structured source context is required."));
        }

        if (!string.IsNullOrWhiteSpace(source.SourceUrl)
            && !Uri.TryCreate(source.SourceUrl.Trim(), UriKind.Absolute, out _))
        {
            issues.Add(Error("source_url_invalid", "Source URL is invalid."));
        }

        var claimText = string.Join(
            " ",
            source.SourceTitle,
            source.SourceText,
            string.Join(" ", source.StructuredSourceContext));
        if (ContainsOfficialClaimMarker(claimText))
        {
            issues.Add(Error("official_claim_requires_verified_source", "Official-source claims require verified source metadata."));
        }

        return issues;
    }

    private static QuestionImportItemDto BuildImportItem(
        QuestionDraftGenerationRequestDto request,
        string statement,
        int index)
    {
        var context = request.Context;
        var source = request.Source;
        var clipped = Clip(statement, MaxStatementLength);
        var tags = new[]
            {
                "generated_draft",
                "source_grounded",
                "requires_review",
                SafeTag(context.ExamCode),
                SafeTag(context.TopicCode),
                SafeTag(context.OutcomeCode)
            }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new QuestionImportItemDto
        {
            ExternalId = $"DRAFT_{index + 1}",
            ExamDefinitionId = context.ExamDefinitionId,
            ExamVariantId = context.ExamVariantId,
            ExamSectionId = context.ExamSectionId,
            ExamSubjectId = context.ExamSubjectId,
            ExamTopicId = context.ExamTopicId,
            ExamOutcomeId = context.ExamOutcomeId,
            ExamCode = context.ExamCode,
            VariantCode = context.VariantCode,
            SectionCode = context.SectionCode,
            SubjectCode = context.SubjectCode,
            TopicCode = context.TopicCode,
            OutcomeCode = context.OutcomeCode,
            LearningTopicId = context.LearningTopicId,
            ConceptGraphSnapshotId = context.ConceptGraphSnapshotId,
            LearningConceptId = context.LearningConceptId,
            AssessmentItemId = context.AssessmentItemId,
            QuizRunId = context.QuizRunId,
            PlanRequestId = context.PlanRequestId,
            ConceptKey = CleanOptional(context.ConceptKey),
            ConceptLabel = CleanOptional(context.ConceptLabel),
            MisconceptionTarget = CleanOptional(context.MisconceptionTarget),
            EvidenceExpected = CleanOptional(context.EvidenceExpected),
            ScoringRuleJson = CleanOptional(context.ScoringRuleJson),
            CalibrationStatus = CleanOptional(context.CalibrationStatus),
            VisualReadinessStatus = CleanOptional(context.VisualReadinessStatus),
            QuestionBankSource = CleanOptional(context.QuestionBankSource),
            QuestionType = SupportedQuestionType,
            Stem = ExpectedDraftStem(clipped),
            Difficulty = Clean(request.Difficulty, "medium"),
            CognitiveSkill = Clean(request.CognitiveSkill, "reading_comprehension"),
            SourceOrigin = Clean(source.SourceOrigin, "source_grounded_draft"),
            LicenseStatus = Clean(source.LicenseStatus, "unknown"),
            SourceTitle = CleanOptional(source.SourceTitle),
            SourceUrl = CleanOptional(source.SourceUrl),
            Explanation = "This draft is grounded in the supplied source context and requires content review before use.",
            Tags = tags,
            Options =
            [
                new QuestionImportOptionDto { OptionKey = "A", Text = clipped, IsCorrect = true, SortOrder = 0 },
                new QuestionImportOptionDto { OptionKey = "B", Text = "Draft distractor placeholder: review against the source before publishing.", IsCorrect = false, SortOrder = 1 },
                new QuestionImportOptionDto { OptionKey = "C", Text = "Draft alternative placeholder: replace with a validated misconception option.", IsCorrect = false, SortOrder = 2 }
            ]
        };
    }

    private static QuestionDraftPreviewDto InvalidPreview(
        QuestionDraftGenerationRequestDto request,
        List<QuestionDraftGenerationIssueDto> issues)
    {
        return new QuestionDraftPreviewDto
        {
            Status = "rejected",
            TotalRequested = request.DesiredCount,
            GeneratedCount = 0,
            AcceptedDraftCount = 0,
            RejectedCount = 0,
            WarningCount = issues.Count(i => i.Severity == "warning"),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow,
            Issues = issues
        };
    }

    private static QuestionDraftPreviewDto ToDraftPreview(QuestionImportPreviewDto importPreview, int totalRequested)
    {
        return new QuestionDraftPreviewDto
        {
            Id = importPreview.Id,
            ImportPreviewId = importPreview.Id,
            Status = importPreview.Status,
            TotalRequested = totalRequested <= 0 ? DefaultDesiredCount : totalRequested,
            GeneratedCount = importPreview.TotalCount,
            AcceptedDraftCount = importPreview.AcceptedCount,
            RejectedCount = importPreview.RejectedCount,
            WarningCount = importPreview.WarningCount + importPreview.Items.Count(i => i.Status == "accepted") * GenerationWarnings().Count,
            CreatedAt = importPreview.CreatedAt,
            ExpiresAt = importPreview.ExpiresAt,
            Items = importPreview.Items.Select(ToDraftPreviewItem).ToList()
        };
    }

    private static QuestionDraftPreviewItemDto ToDraftPreviewItem(QuestionImportPreviewItemDto item)
    {
        var candidate = item.NormalizedQuestion is null ? null : ToCandidate(item.ExternalId, item.NormalizedQuestion);
        var issues = item.Issues.Select(ToDraftIssue).ToList();
        if (candidate is not null)
        {
            issues.AddRange(GenerationWarnings());
        }

        return new QuestionDraftPreviewItemDto
        {
            Id = item.Id,
            RowIndex = item.RowIndex,
            ExternalId = item.ExternalId,
            Status = item.Status,
            IsDuplicate = item.IsDuplicate,
            DuplicateQuestionId = item.DuplicateQuestionId,
            CreatedQuestionId = item.CreatedQuestionId,
            Candidate = candidate,
            Issues = issues
        };
    }

    private static QuestionDraftCandidateDto ToCandidate(string? externalId, CreateQuestionDto question)
    {
        return new QuestionDraftCandidateDto
        {
            ExternalId = externalId,
            QuestionType = question.QuestionType,
            Stem = question.Stem,
            Difficulty = question.Difficulty,
            CognitiveSkill = question.CognitiveSkill,
            LicenseStatus = question.LicenseStatus,
            SourceOrigin = question.SourceOrigin,
            SourceTitle = question.SourceTitle,
            SourceUrl = question.SourceUrl,
            LearningTopicId = question.LearningTopicId,
            ConceptGraphSnapshotId = question.ConceptGraphSnapshotId,
            LearningConceptId = question.LearningConceptId,
            AssessmentItemId = question.AssessmentItemId,
            QuizRunId = question.QuizRunId,
            PlanRequestId = question.PlanRequestId,
            ConceptKey = question.ConceptKey,
            ConceptLabel = question.ConceptLabel,
            MisconceptionTarget = question.MisconceptionTarget,
            EvidenceExpected = question.EvidenceExpected,
            ScoringRuleJson = question.ScoringRuleJson,
            CalibrationStatus = question.CalibrationStatus,
            VisualReadinessStatus = question.VisualReadinessStatus,
            QuestionBankSource = question.QuestionBankSource,
            Explanation = question.Explanation,
            Options = question.Options
                .OrderBy(o => o.SortOrder)
                .Select(o => new QuestionDraftOptionDto
                {
                    OptionKey = o.OptionKey,
                    Text = o.Text,
                    IsCorrect = o.IsCorrect,
                    SortOrder = o.SortOrder,
                    Rationale = o.Rationale,
                    MisconceptionKey = o.MisconceptionKey,
                    DiagnosticSignalJson = o.DiagnosticSignalJson
                })
                .ToList(),
            Tags = question.Tags.Select(t => t.Tag).ToList()
        };
    }

    private static List<QuestionDraftGenerationIssueDto> GenerationWarnings() =>
    [
        Warning("generated_draft_requires_review", "Generated draft requires human content review."),
        Warning("deterministic_stub_generator", "Draft was produced by a deterministic local generator."),
        Warning("review_distractors_before_publish", "Distractors must be reviewed before publication.")
    ];

    private static QuestionDraftGenerationIssueDto ToDraftIssue(QuestionImportValidationIssueDto issue) => new()
    {
        Code = issue.Code,
        Severity = issue.Severity,
        Message = issue.Message
    };

    private static IEnumerable<string> ExtractStatements(QuestionDraftGenerationSourceDto source)
    {
        var structured = source.StructuredSourceContext
            .Select(s => CleanOptional(s))
            .Where(s => !string.IsNullOrWhiteSpace(s));

        var sourceText = CleanOptional(source.SourceText);
        var textStatements = string.IsNullOrWhiteSpace(sourceText)
            ? Enumerable.Empty<string>()
            : SentenceSplitRegex()
                .Split(sourceText)
                .Select(s => CleanOptional(s))
                .Where(s => !string.IsNullOrWhiteSpace(s));

        return structured.Concat(textStatements)
            .Select(s => Clip(s!, MaxStatementLength))
            .Where(s => s.Length >= 12)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static string ExpectedDraftStem(string sourceStatement) =>
        $"Kaynaga gore asagidaki ifade hangi kaynak bilgisine dayanir? {sourceStatement}";

    private static int NormalizedDesiredCount(int desiredCount) =>
        desiredCount <= 0 ? DefaultDesiredCount : Math.Min(desiredCount, MaxDesiredCount);

    private static bool ContainsOfficialClaimMarker(string value) =>
        OfficialClaimMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string? SafeTag(string? value)
    {
        var cleaned = CleanOptional(value);
        return string.IsNullOrWhiteSpace(cleaned)
            ? null
            : Regex.Replace(cleaned.ToLowerInvariant(), "[^a-z0-9_\\-]+", "_");
    }

    private static string Clean(string? value, string fallback = "") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? CleanOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Clip(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd();

    private static QuestionDraftGenerationIssueDto Error(string code, string message) => new()
    {
        Code = code,
        Severity = "error",
        Message = message
    };

    private static QuestionDraftGenerationIssueDto Warning(string code, string message) => new()
    {
        Code = code,
        Severity = "warning",
        Message = message
    };

    [GeneratedRegex("[\\r\\n.!?;]+")]
    private static partial Regex SentenceSplitRegex();
}

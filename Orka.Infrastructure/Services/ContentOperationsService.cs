using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class ContentOperationsService : IContentOperationsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> ReviewStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "authoring",
        "editorial_review",
        "pedagogy_review",
        "accessibility_review",
        "source_review",
        "approved",
        "published",
        "rejected",
        "retired"
    };

    private static readonly Dictionary<string, string> NextStage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["authoring"] = "editorial_review",
        ["editorial_review"] = "pedagogy_review",
        ["pedagogy_review"] = "accessibility_review",
        ["accessibility_review"] = "source_review",
        ["source_review"] = "approved",
        ["approved"] = "published"
    };

    private static readonly HashSet<string> SafePublishLicenseStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "user_provided",
        "licensed",
        "open"
    };

    private readonly OrkaDbContext _db;
    private readonly IQuestionBankService _questionBank;

    public ContentOperationsService(OrkaDbContext db, IQuestionBankService questionBank)
    {
        _db = db;
        _questionBank = questionBank;
    }

    public async Task<QuestionReviewWorkflowDto?> GetWorkflowAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default)
    {
        if (!await CanSeeQuestionAsync(userId, questionId, ct))
        {
            return null;
        }

        var workflow = await LoadWorkflowAsync(questionId, ct);
        return workflow is null ? null : ToDto(workflow);
    }

    public async Task<QuestionReviewWorkflowDto?> SubmitQuestionForReviewAsync(
        Guid userId,
        Guid questionId,
        SubmitQuestionReviewDto request,
        CancellationToken ct = default)
    {
        var question = await LoadQuestionForMutationAsync(userId, questionId, ct);
        if (question is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var workflow = await LoadWorkflowAsync(questionId, ct);
        if (workflow is null)
        {
            workflow = new QuestionReviewWorkflow
            {
                QuestionItemId = question.Id,
                OwnerUserId = question.OwnerUserId ?? userId,
                CurrentStage = "editorial_review",
                Status = "open",
                CreatedByUserId = userId,
                UpdatedByUserId = userId,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.QuestionReviewWorkflows.Add(workflow);
        }
        else
        {
            workflow.CurrentStage = "editorial_review";
            workflow.Status = "open";
            workflow.UpdatedByUserId = userId;
            workflow.UpdatedAt = now;
        }

        if (question.QualityStatus == "draft")
        {
            question.QualityStatus = "needs_review";
            question.UpdatedAt = now;
        }

        AddEvent(workflow, question.Id, userId, "submitted", fromStage: "authoring", toStage: "editorial_review", safeNote: request.SafeNote, now: now);
        await AddVersionAsync(userId, question.Id, "submitted_for_review", ct);
        await _db.SaveChangesAsync(ct);
        return ToDto((await LoadWorkflowAsync(questionId, ct))!);
    }

    public async Task<QuestionReviewWorkflowDto?> AssignReviewerAsync(
        Guid userId,
        Guid questionId,
        AssignQuestionReviewerDto request,
        CancellationToken ct = default)
    {
        var question = await LoadQuestionForMutationAsync(userId, questionId, ct);
        if (question is null)
        {
            return null;
        }

        var workflow = await EnsureWorkflowAsync(userId, question, ct);
        if (request.AssignedReviewerUserId is { } reviewerId
            && !await _db.Users.AsNoTracking().AnyAsync(u => u.Id == reviewerId, ct))
        {
            throw new ArgumentException("assigned_reviewer_not_found");
        }

        var now = DateTime.UtcNow;
        workflow.AssignedReviewerUserId = request.AssignedReviewerUserId;
        workflow.UpdatedByUserId = userId;
        workflow.UpdatedAt = now;
        AddEvent(workflow, question.Id, userId, "assigned", safeNote: request.SafeNote, now: now);
        await _db.SaveChangesAsync(ct);
        return ToDto((await LoadWorkflowAsync(questionId, ct))!);
    }

    public async Task<QuestionReviewWorkflowDto?> AdvanceReviewStageAsync(
        Guid userId,
        Guid questionId,
        AdvanceQuestionReviewStageDto request,
        CancellationToken ct = default)
    {
        var question = await LoadQuestionForMutationAsync(userId, questionId, ct);
        if (question is null)
        {
            return null;
        }

        var workflow = await EnsureWorkflowAsync(userId, question, ct);
        var toStage = NormalizeStage(request.ToStage);
        if (!ReviewStages.Contains(toStage))
        {
            throw new ArgumentException("review_stage_not_supported");
        }

        var fromStage = workflow.CurrentStage;
        if (!NextStage.TryGetValue(fromStage, out var expected) || !expected.Equals(toStage, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("invalid_review_stage_transition");
        }

        if (toStage == "approved")
        {
            var readiness = await BuildReadinessAsync(userId, question, workflow, requireApprovedWorkflow: false, requireApprovedQuality: false, ct);
            if (readiness.BlockingIssues.Count > 0)
            {
                workflow.Status = "blocked";
                workflow.UpdatedByUserId = userId;
                workflow.UpdatedAt = DateTime.UtcNow;
                AddEvent(workflow, question.Id, userId, "validation_failed", fromStage, toStage, "publish readiness has blocking issues", request.SafeNote, workflow.UpdatedAt);
                await SaveReadinessSnapshotAsync(userId, readiness, ct);
                await _db.SaveChangesAsync(ct);
                throw new ArgumentException("publish_readiness_has_blocking_issues");
            }
        }

        var now = DateTime.UtcNow;
        workflow.CurrentStage = toStage;
        workflow.Status = toStage == "approved" ? "approved" : "open";
        workflow.CompletedAt = toStage == "approved" ? now : null;
        workflow.UpdatedByUserId = userId;
        workflow.UpdatedAt = now;

        if (toStage == "approved")
        {
            question.QualityStatus = "approved";
            question.UpdatedAt = now;
            AddEvent(workflow, question.Id, userId, "approved", fromStage, toStage, safeNote: request.SafeNote, now: now);
            await AddVersionAsync(userId, question.Id, "approved", ct);
        }
        else
        {
            AddEvent(workflow, question.Id, userId, "stage_changed", fromStage, toStage, safeNote: request.SafeNote, now: now);
        }

        await _db.SaveChangesAsync(ct);
        return ToDto((await LoadWorkflowAsync(questionId, ct))!);
    }

    public async Task<QuestionReviewWorkflowDto?> RejectQuestionAsync(
        Guid userId,
        Guid questionId,
        RejectQuestionReviewDto request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("question_rejection_reason_required");
        }

        var question = await LoadQuestionForMutationAsync(userId, questionId, ct);
        if (question is null)
        {
            return null;
        }

        var workflow = await EnsureWorkflowAsync(userId, question, ct);
        var now = DateTime.UtcNow;
        var fromStage = workflow.CurrentStage;
        workflow.CurrentStage = "rejected";
        workflow.Status = "rejected";
        workflow.CompletedAt = now;
        workflow.UpdatedByUserId = userId;
        workflow.UpdatedAt = now;
        question.QualityStatus = "rejected";
        question.UpdatedAt = now;
        AddEvent(workflow, question.Id, userId, "rejected", fromStage, "rejected", reason: request.Reason, now: now);
        await AddVersionAsync(userId, question.Id, "rejected", ct);
        await _db.SaveChangesAsync(ct);
        return ToDto((await LoadWorkflowAsync(questionId, ct))!);
    }

    public async Task<QuestionReviewWorkflowDto?> RetireQuestionAsync(
        Guid userId,
        Guid questionId,
        RetireQuestionDto request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("question_retire_reason_required");
        }

        var question = await LoadQuestionForMutationAsync(userId, questionId, ct);
        if (question is null)
        {
            return null;
        }

        var workflow = await EnsureWorkflowAsync(userId, question, ct);
        var now = DateTime.UtcNow;
        var fromStage = workflow.CurrentStage;
        workflow.CurrentStage = "retired";
        workflow.Status = "retired";
        workflow.CompletedAt = now;
        workflow.UpdatedByUserId = userId;
        workflow.UpdatedAt = now;
        question.QualityStatus = "retired";
        question.UpdatedAt = now;
        AddEvent(workflow, question.Id, userId, "retired", fromStage, "retired", reason: request.Reason, now: now);
        await AddVersionAsync(userId, question.Id, "retired", ct);
        await _db.SaveChangesAsync(ct);
        return ToDto((await LoadWorkflowAsync(questionId, ct))!);
    }

    public async Task<QuestionPublishReadinessDto?> GetPublishReadinessAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default)
    {
        var question = await LoadVisibleQuestionAsync(userId, questionId, ct);
        if (question is null)
        {
            return null;
        }

        var workflow = await LoadWorkflowAsync(questionId, ct);
        var readiness = await BuildReadinessAsync(userId, question, workflow, requireApprovedWorkflow: true, requireApprovedQuality: true, ct);
        await SaveReadinessSnapshotAsync(userId, readiness, ct);
        await _db.SaveChangesAsync(ct);
        return readiness;
    }

    public async Task<QuestionItemDto?> PublishApprovedQuestionAsync(
        Guid userId,
        Guid questionId,
        PublishQuestionContentDto request,
        CancellationToken ct = default)
    {
        var question = await LoadQuestionForMutationAsync(userId, questionId, ct);
        if (question is null)
        {
            return null;
        }

        var workflow = await LoadWorkflowAsync(questionId, ct);
        var readiness = await BuildReadinessAsync(userId, question, workflow, requireApprovedWorkflow: true, requireApprovedQuality: true, ct);
        if (readiness.BlockingIssues.Count > 0)
        {
            await SaveReadinessSnapshotAsync(userId, readiness, ct);
            await _db.SaveChangesAsync(ct);
            throw new ArgumentException("publish_readiness_has_blocking_issues");
        }

        var published = await _questionBank.PublishQuestionAsync(userId, questionId, ct);
        if (published is null)
        {
            return null;
        }

        workflow!.CurrentStage = "published";
        workflow.Status = "approved";
        workflow.UpdatedByUserId = userId;
        workflow.UpdatedAt = DateTime.UtcNow;
        AddEvent(workflow, question.Id, userId, "published", "approved", "published", safeNote: request.SafeNote, now: workflow.UpdatedAt);
        await AddVersionAsync(userId, question.Id, "published", ct);
        await _db.SaveChangesAsync(ct);
        return published;
    }

    public async Task<IReadOnlyList<QuestionContentVersionDto>> GetQuestionVersionsAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default)
    {
        if (!await CanSeeQuestionAsync(userId, questionId, ct))
        {
            return [];
        }

        return await _db.QuestionContentVersions
            .AsNoTracking()
            .Where(v => v.QuestionItemId == questionId && !v.IsDeleted)
            .OrderBy(v => v.VersionNumber)
            .Select(v => new QuestionContentVersionDto
            {
                Id = v.Id,
                QuestionItemId = v.QuestionItemId,
                VersionNumber = v.VersionNumber,
                CreatedAt = v.CreatedAt,
                Reason = v.Reason
            })
            .ToListAsync(ct);
    }

    private async Task<QuestionReviewWorkflow> EnsureWorkflowAsync(Guid userId, QuestionItem question, CancellationToken ct)
    {
        var workflow = await LoadWorkflowAsync(question.Id, ct);
        if (workflow is not null)
        {
            return workflow;
        }

        workflow = new QuestionReviewWorkflow
        {
            QuestionItemId = question.Id,
            OwnerUserId = question.OwnerUserId ?? userId,
            CurrentStage = "authoring",
            Status = "open",
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.QuestionReviewWorkflows.Add(workflow);
        AddEvent(workflow, question.Id, userId, "created", toStage: "authoring", now: workflow.CreatedAt);
        return workflow;
    }

    private async Task<QuestionItem?> LoadQuestionForMutationAsync(Guid userId, Guid questionId, CancellationToken ct)
    {
        var question = await LoadVisibleQuestionAsync(userId, questionId, ct);
        if (question is null)
        {
            return null;
        }

        if (question.OwnerUserId is null && !await IsAdminAsync(userId, ct))
        {
            throw new ArgumentException("system_global_content_mutation_requires_admin");
        }

        if (question.OwnerUserId is not null && question.OwnerUserId != userId)
        {
            return null;
        }

        return question;
    }

    private Task<bool> IsAdminAsync(Guid userId, CancellationToken ct) =>
        _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsAdmin, ct);

    private Task<bool> CanSeeQuestionAsync(Guid userId, Guid questionId, CancellationToken ct) =>
        _db.QuestionItems
            .AsNoTracking()
            .AnyAsync(q => q.Id == questionId && !q.IsDeleted && (q.OwnerUserId == null || q.OwnerUserId == userId), ct);

    private Task<QuestionItem?> LoadVisibleQuestionAsync(Guid userId, Guid questionId, CancellationToken ct) =>
        _db.QuestionItems
            .Include(q => q.Options)
                .ThenInclude(o => o.ContentBlocks.Where(b => !b.IsDeleted))
                    .ThenInclude(b => b.Asset)
            .Include(q => q.ContentBlocks.Where(b => !b.IsDeleted))
                .ThenInclude(b => b.Asset)
            .Include(q => q.Tags)
            .Include(q => q.Explanations)
            .Include(q => q.OutcomeLinks)
            .FirstOrDefaultAsync(q => q.Id == questionId && !q.IsDeleted && (q.OwnerUserId == null || q.OwnerUserId == userId), ct);

    private Task<QuestionReviewWorkflow?> LoadWorkflowAsync(Guid questionId, CancellationToken ct) =>
        _db.QuestionReviewWorkflows
            .Include(w => w.Events.Where(e => !e.IsDeleted).OrderBy(e => e.CreatedAt))
            .FirstOrDefaultAsync(w => w.QuestionItemId == questionId && !w.IsDeleted, ct);

    private async Task<QuestionPublishReadinessDto> BuildReadinessAsync(
        Guid userId,
        QuestionItem question,
        QuestionReviewWorkflow? workflow,
        bool requireApprovedWorkflow,
        bool requireApprovedQuality,
        CancellationToken ct)
    {
        var blocking = new List<QuestionPublishIssueDto>();
        var warnings = new List<QuestionPublishIssueDto>();

        if (question.OwnerUserId is null && !await IsAdminAsync(userId, ct))
        {
            blocking.Add(Issue("system_global_content_mutation_requires_admin", "ownership", "System/global content can only be published through an admin path."));
        }

        if (workflow is null)
        {
            blocking.Add(Issue("workflow_required", "workflow", "Question must have a review workflow before publishing."));
        }
        else if (requireApprovedWorkflow)
        {
            if (workflow.CurrentStage is "rejected" or "retired" || workflow.Status is "rejected" or "retired")
            {
                blocking.Add(Issue($"workflow_{workflow.CurrentStage}_blocks_publish", "workflow", "Rejected or retired content cannot be published."));
            }

            if (workflow.CurrentStage != "approved" && workflow.CurrentStage != "published")
            {
                blocking.Add(Issue("workflow_approval_required", "workflow", "Question workflow must be approved before publishing."));
            }
        }

        if (requireApprovedQuality && question.QualityStatus != "approved")
        {
            blocking.Add(Issue("approved_quality_status_required", "quality", "Question quality status must be approved before publishing."));
        }

        if (question.QualityStatus is "rejected" or "retired")
        {
            blocking.Add(Issue($"quality_status_{question.QualityStatus}_blocks_publish", "quality", "Rejected or retired questions cannot be published."));
        }

        var activeBlocks = question.ContentBlocks.Where(b => !b.IsDeleted).ToList();
        if (string.IsNullOrWhiteSpace(question.Stem) && activeBlocks.Count == 0)
        {
            blocking.Add(Issue("question_stem_or_content_required", "shape", "Question needs a stem or renderable content block."));
        }

        if (question.QuestionType == "multiple_choice")
        {
            var options = question.Options.ToList();
            if (options.Count < 2)
            {
                blocking.Add(Issue("multiple_choice_minimum_two_options", "shape", "Multiple-choice questions need at least two options."));
            }

            if (options.Count(o => o.IsCorrect) != 1)
            {
                blocking.Add(Issue("multiple_choice_single_correct_option_required", "shape", "Multiple-choice questions need exactly one correct option."));
            }

            if (options.Any(o => string.IsNullOrWhiteSpace(o.OptionKey)
                                 || (string.IsNullOrWhiteSpace(o.Text)
                                     && o.ContentBlocks.All(b => b.IsDeleted || !HasRenderableOptionBlock(b)))))
            {
                blocking.Add(Issue("multiple_choice_option_text_or_content_required", "shape", "Each option needs text or renderable option content."));
            }

            var incorrectOptions = options.Where(o => !o.IsCorrect).ToList();
            var missingDistractorRationale = incorrectOptions.Count(o => string.IsNullOrWhiteSpace(o.Rationale));
            var missingDiagnosticSignal = incorrectOptions.Count(o =>
                string.IsNullOrWhiteSpace(o.MisconceptionKey) &&
                string.IsNullOrWhiteSpace(o.DiagnosticSignalJson));
            if (IsProfessionalPracticeQuestion(question))
            {
                if (missingDistractorRationale > 0)
                {
                    blocking.Add(Issue("distractor_rationale_required", "pedagogy", "Professional diagnostic questions need a rationale for every distractor."));
                }

                if (missingDiagnosticSignal > 0)
                {
                    blocking.Add(Issue("distractor_diagnostic_signal_required", "pedagogy", "Professional diagnostic distractors need a misconception key or diagnostic signal."));
                }
            }
            else if (missingDistractorRationale > 0 || missingDiagnosticSignal > 0)
            {
                warnings.Add(Issue("distractor_diagnostic_metadata_missing", "pedagogy", "Distractor rationales or diagnostic signals are missing, so this item is not yet professional diagnostic-ready.", severity: "warning"));
            }
        }

        AddProfessionalBindingIssues(question, blocking, warnings);

        if (!SafePublishLicenseStatuses.Contains(question.LicenseStatus))
        {
            blocking.Add(Issue("safe_license_required", "license", "Question license status must be user_provided, licensed, or open before publishing."));
        }

        if (!string.IsNullOrWhiteSpace(question.SourceUrl) && !IsValidUrl(question.SourceUrl))
        {
            blocking.Add(Issue("valid_source_url_required", "source", "Question source URL must be a valid HTTP(S) URL."));
        }

        foreach (var accessibilityIssue in ValidateAccessibility(question))
        {
            blocking.Add(Issue(accessibilityIssue.Code, "accessibility", accessibilityIssue.Code));
        }

        if (string.IsNullOrWhiteSpace(question.SourceTitle))
        {
            warnings.Add(Issue("source_title_missing", "source", "Source title is missing.", severity: "warning"));
        }

        if (string.IsNullOrWhiteSpace(question.Explanation)
            && question.Explanations.All(e => e.IsDeleted || string.IsNullOrWhiteSpace(e.ExplanationText)))
        {
            warnings.Add(Issue("explanation_missing", "pedagogy", "Explanation is missing.", severity: "warning"));
        }

        if (question.Tags.Any(t => t.Tag.Contains("generated_draft", StringComparison.OrdinalIgnoreCase))
            || question.SourceOrigin.Contains("generated", StringComparison.OrdinalIgnoreCase)
            || question.SourceOrigin.Contains("import", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(Issue("imported_or_generated_content_requires_review", "review", "Imported or generated draft content should be reviewed before publishing.", severity: "warning"));
        }

        var analyticsSignals = await _db.QuestionQualityReviewSignals
            .AsNoTracking()
            .Where(s => s.QuestionItemId == question.Id
                        && !s.IsDeleted
                        && s.ResolvedAt == null
                        && s.Severity != "info")
            .OrderByDescending(s => s.CreatedAt)
            .Take(5)
            .ToListAsync(ct);
        foreach (var signal in analyticsSignals)
        {
            warnings.Add(Issue($"analytics_{signal.SignalType}", "analytics", signal.Message, severity: "warning"));
        }

        var recommended = workflow is null ? "authoring" :
            workflow.CurrentStage == "published" ? "published" :
            blocking.Count > 0 ? workflow.CurrentStage :
            NextStage.GetValueOrDefault(workflow.CurrentStage, "approved");

        return new QuestionPublishReadinessDto
        {
            QuestionItemId = question.Id,
            WorkflowId = workflow?.Id,
            WorkflowStage = workflow?.CurrentStage,
            WorkflowStatus = workflow?.Status,
            IsReadyToPublish = blocking.Count == 0,
            RecommendedNextReviewStage = recommended,
            BlockingIssues = blocking,
            WarningIssues = warnings
        };
    }

    private static void AddProfessionalBindingIssues(
        QuestionItem question,
        List<QuestionPublishIssueDto> blocking,
        List<QuestionPublishIssueDto> warnings)
    {
        var professional = IsProfessionalPracticeQuestion(question);
        var target = professional ? blocking : warnings;
        var severity = professional ? "blocking" : "warning";
        var messageSuffix = professional
            ? "Professional diagnostic/practice questions cannot be published without this binding."
            : "This item can remain curated content, but it is not eligible for KG-bound professional practice until this is added.";

        AddIfMissing(question.AssessmentItemId.HasValue, "assessment_item_binding_required", "assessment", "Assessment item binding is missing. " + messageSuffix);
        AddIfMissing(question.ConceptGraphSnapshotId.HasValue, "concept_graph_binding_required", "knowledge_graph", "Concept graph snapshot binding is missing. " + messageSuffix);
        AddIfMissing(question.LearningConceptId.HasValue, "learning_concept_binding_required", "knowledge_graph", "Learning concept binding is missing. " + messageSuffix);
        AddIfMissing(!string.IsNullOrWhiteSpace(question.ConceptKey), "concept_key_required", "knowledge_graph", "Concept key is missing. " + messageSuffix);
        AddIfMissing(!string.IsNullOrWhiteSpace(question.EvidenceExpected), "evidence_expected_required", "assessment", "Evidence expectation is missing. " + messageSuffix);
        AddIfMissing(!string.IsNullOrWhiteSpace(question.ScoringRuleJson), "scoring_rule_required", "assessment", "Server-side scoring rule metadata is missing. " + messageSuffix);

        var visualReady = question.VisualReadinessStatus is "not_required" or "ready" or "validated";
        AddIfMissing(visualReady, "visual_readiness_required", "accessibility", "Visual readiness must be not_required, ready, or validated. " + messageSuffix);

        void AddIfMissing(bool ok, string code, string area, string message)
        {
            if (ok)
            {
                return;
            }

            target.Add(Issue(code, area, message, severity));
        }
    }

    private static bool IsProfessionalPracticeQuestion(QuestionItem question) =>
        string.Equals(question.QuestionBankSource, "diagnostic_assessment_item", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(question.QualityStatus, "diagnostic_ready", StringComparison.OrdinalIgnoreCase);

    private async Task SaveReadinessSnapshotAsync(Guid userId, QuestionPublishReadinessDto readiness, CancellationToken ct)
    {
        _db.QuestionPublishReadinessSnapshots.Add(new QuestionPublishReadinessSnapshot
        {
            QuestionItemId = readiness.QuestionItemId,
            WorkflowId = readiness.WorkflowId,
            IsReadyToPublish = readiness.IsReadyToPublish,
            BlockingIssuesJson = JsonSerializer.Serialize(readiness.BlockingIssues, JsonOptions),
            WarningIssuesJson = JsonSerializer.Serialize(readiness.WarningIssues, JsonOptions),
            CheckedAt = DateTime.UtcNow,
            CheckedByUserId = userId
        });
        await Task.CompletedTask;
    }

    private async Task AddVersionAsync(Guid userId, Guid questionId, string reason, CancellationToken ct)
    {
        var latestVersion = await _db.QuestionContentVersions
            .Where(v => v.QuestionItemId == questionId && !v.IsDeleted)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;

        var snapshot = await _questionBank.GetQuestionAsync(userId, questionId, ct);
        _db.QuestionContentVersions.Add(new QuestionContentVersion
        {
            QuestionItemId = questionId,
            VersionNumber = latestVersion + 1,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions),
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            Reason = reason
        });
    }

    private void AddEvent(
        QuestionReviewWorkflow workflow,
        Guid questionId,
        Guid actorUserId,
        string eventType,
        string? fromStage = null,
        string? toStage = null,
        string? reason = null,
        string? safeNote = null,
        DateTime? now = null)
    {
        var reviewEvent = new QuestionReviewEvent
        {
            QuestionReviewWorkflowId = workflow.Id,
            QuestionItemId = questionId,
            ActorUserId = actorUserId,
            EventType = eventType,
            FromStage = fromStage,
            ToStage = toStage,
            Reason = SafeText(reason),
            SafeNote = SafeText(safeNote),
            CreatedAt = now ?? DateTime.UtcNow
        };
        _db.QuestionReviewEvents.Add(reviewEvent);
    }

    private static IReadOnlyList<QuestionAccessibilityValidationDto> ValidateAccessibility(QuestionItem question)
    {
        var issues = new List<QuestionAccessibilityValidationDto>();
        foreach (var block in question.ContentBlocks.Where(b => !b.IsDeleted))
        {
            var requiresTextAlternative = block.BlockType is "image" or "chart" or "table";
            var hasTextAlternative = HasTextAlternative(block.AltText, block.Caption, block.Asset?.AltText, block.Asset?.Caption);
            if (requiresTextAlternative && !hasTextAlternative)
            {
                issues.Add(new QuestionAccessibilityValidationDto
                {
                    TargetType = "question_content_block",
                    TargetId = block.Id,
                    Code = $"{block.BlockType}_requires_alt_text_or_caption",
                    Severity = "error"
                });
            }

            if (block.BlockType == "formula"
                && string.IsNullOrWhiteSpace(block.Text)
                && string.IsNullOrWhiteSpace(block.AltText)
                && string.IsNullOrWhiteSpace(block.Asset?.AltText))
            {
                issues.Add(new QuestionAccessibilityValidationDto
                {
                    TargetType = "question_content_block",
                    TargetId = block.Id,
                    Code = "formula_requires_accessible_text",
                    Severity = "error"
                });
            }

            if (block.Asset is { } asset)
            {
                ValidateAsset(asset, issues);
            }
        }

        foreach (var block in question.Options.SelectMany(o => o.ContentBlocks).Where(b => !b.IsDeleted))
        {
            var requiresTextAlternative = block.BlockType is "image" or "table";
            var hasTextAlternative = HasTextAlternative(block.AltText, block.Caption, block.Asset?.AltText, block.Asset?.Caption);
            if (requiresTextAlternative && !hasTextAlternative)
            {
                issues.Add(new QuestionAccessibilityValidationDto
                {
                    TargetType = "question_option_content_block",
                    TargetId = block.Id,
                    Code = $"{block.BlockType}_option_requires_alt_text",
                    Severity = "error"
                });
            }

            if (block.BlockType == "formula"
                && string.IsNullOrWhiteSpace(block.Text)
                && string.IsNullOrWhiteSpace(block.AltText)
                && string.IsNullOrWhiteSpace(block.Asset?.AltText))
            {
                issues.Add(new QuestionAccessibilityValidationDto
                {
                    TargetType = "question_option_content_block",
                    TargetId = block.Id,
                    Code = "formula_option_requires_accessible_text",
                    Severity = "error"
                });
            }

            if (block.Asset is { } asset)
            {
                ValidateAsset(asset, issues);
            }
        }

        return issues;
    }

    private static void ValidateAsset(QuestionAsset asset, List<QuestionAccessibilityValidationDto> issues)
    {
        if (asset.AssetType == "image" && string.IsNullOrWhiteSpace(asset.AltText))
        {
            issues.Add(new QuestionAccessibilityValidationDto
            {
                TargetType = "question_asset",
                TargetId = asset.Id,
                Code = "image_asset_requires_alt_text",
                Severity = "error"
            });
        }

        if (!SafePublishLicenseStatuses.Contains(asset.LicenseStatus))
        {
            issues.Add(new QuestionAccessibilityValidationDto
            {
                TargetType = "question_asset",
                TargetId = asset.Id,
                Code = "asset_requires_safe_license_status",
                Severity = "error"
            });
        }
    }

    private static bool HasRenderableOptionBlock(QuestionOptionContentBlock block) =>
        !string.IsNullOrWhiteSpace(block.Text) || !string.IsNullOrWhiteSpace(block.ContentJson) || block.AssetId is not null;

    private static bool HasTextAlternative(params string?[] values) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value));

    private static QuestionPublishIssueDto Issue(string code, string area, string message, string severity = "blocking") => new()
    {
        Code = code,
        Severity = severity,
        Area = area,
        Message = message
    };

    private static string NormalizeStage(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string? SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 512 ? trimmed : trimmed[..512];
    }

    private static bool IsValidUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static QuestionReviewWorkflowDto ToDto(QuestionReviewWorkflow workflow) => new()
    {
        Id = workflow.Id,
        QuestionItemId = workflow.QuestionItemId,
        CurrentStage = workflow.CurrentStage,
        Status = workflow.Status,
        HasAssignedReviewer = workflow.AssignedReviewerUserId is not null,
        CreatedAt = workflow.CreatedAt,
        UpdatedAt = workflow.UpdatedAt,
        CompletedAt = workflow.CompletedAt,
        Events = workflow.Events
            .Where(e => !e.IsDeleted)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new QuestionReviewEventDto
            {
                Id = e.Id,
                EventType = e.EventType,
                FromStage = e.FromStage,
                ToStage = e.ToStage,
                Reason = e.Reason,
                SafeNote = e.SafeNote,
                CreatedAt = e.CreatedAt
            })
            .ToList()
    };
}

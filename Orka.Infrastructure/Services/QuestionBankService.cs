using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orka.Infrastructure.Services;

public sealed class QuestionBankService : IQuestionBankService
{
    private static readonly JsonSerializerOptions BankJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> AllowedQuestionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "multiple_choice",
        "paragraph",
        "math_problem",
        "grammar",
        "vocabulary",
        "reading_comprehension"
    };

    private static readonly HashSet<string> AllowedDifficulties = new(StringComparer.OrdinalIgnoreCase)
    {
        "easy",
        "medium",
        "hard"
    };

    private static readonly HashSet<string> AllowedQualityStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft",
        "needs_review",
        "approved",
        "published",
        "rejected",
        "diagnostic_ready"
    };

    private static readonly HashSet<string> AllowedVisualReadinessStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "not_required",
        "needs_validation",
        "ready",
        "validated",
        "rejected"
    };

    private static readonly HashSet<string> AllowedLicenseStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "unknown",
        "user_provided",
        "licensed",
        "open",
        "restricted"
    };

    private static readonly HashSet<string> SafePublishLicenseStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "user_provided",
        "licensed",
        "open"
    };

    private static readonly HashSet<string> AllowedAssetTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image",
        "audio",
        "table_json",
        "chart_json",
        "formula",
        "document_reference"
    };

    private static readonly HashSet<string> AllowedQuestionBlockTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text",
        "passage",
        "image",
        "table",
        "chart",
        "formula",
        "code",
        "callout"
    };

    private static readonly HashSet<string> AllowedOptionBlockTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text",
        "image",
        "formula",
        "table"
    };

    private static readonly HashSet<string> AllowedStimulusTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "passage",
        "image",
        "table",
        "chart",
        "formula",
        "mixed"
    };

    private readonly OrkaDbContext _db;

    public QuestionBankService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<QuestionItemDto>> GetQuestionsAsync(
        Guid userId,
        QuestionBankFilterDto filters,
        CancellationToken ct = default)
    {
        var take = Math.Clamp(filters.Take <= 0 ? 50 : filters.Take, 1, 100);
        var requestedQuestionType = !string.IsNullOrWhiteSpace(filters.QuestionType)
            ? NormalizeBounded(filters.QuestionType, AllowedQuestionTypes, "multiple_choice")
            : null;
        var requestedDifficulty = !string.IsNullOrWhiteSpace(filters.Difficulty)
            ? NormalizeBounded(filters.Difficulty, AllowedDifficulties, "medium")
            : null;
        var query = VisibleQuestions(userId);

        if (filters.ExamDefinitionId is { } examDefinitionId)
        {
            query = query.Where(q => q.ExamDefinitionId == examDefinitionId);
        }

        if (filters.ExamVariantId is { } examVariantId)
        {
            query = query.Where(q => q.ExamVariantId == examVariantId);
        }

        if (filters.ExamSectionId is { } examSectionId)
        {
            query = query.Where(q => q.ExamSectionId == examSectionId);
        }

        if (filters.ExamSubjectId is { } examSubjectId)
        {
            query = query.Where(q => q.ExamSubjectId == examSubjectId);
        }

        if (filters.ExamTopicId is { } examTopicId)
        {
            query = query.Where(q => q.ExamTopicId == examTopicId);
        }

        if (filters.ExamOutcomeId is { } examOutcomeId)
        {
            query = query.Where(q => q.ExamOutcomeId == examOutcomeId || q.OutcomeLinks.Any(l => !l.IsDeleted && l.ExamOutcomeId == examOutcomeId));
        }

        if (!string.IsNullOrWhiteSpace(filters.QualityStatus))
        {
            var qualityStatus = NormalizeBounded(filters.QualityStatus, AllowedQualityStatuses, "draft");
            query = query.Where(q => q.QualityStatus == qualityStatus);
        }

        if (filters.LearningTopicId is { } learningTopicId)
        {
            query = query.Where(q => q.LearningTopicId == learningTopicId);
        }

        if (filters.ConceptGraphSnapshotId is { } snapshotId)
        {
            query = query.Where(q => q.ConceptGraphSnapshotId == snapshotId);
        }

        if (filters.LearningConceptId is { } learningConceptId)
        {
            query = query.Where(q => q.LearningConceptId == learningConceptId);
        }

        if (filters.AssessmentItemId is { } assessmentItemId)
        {
            query = query.Where(q => q.AssessmentItemId == assessmentItemId);
        }

        if (filters.QuizRunId is { } quizRunId)
        {
            query = query.Where(q => q.QuizRunId == quizRunId);
        }

        if (filters.PlanRequestId is { } planRequestId)
        {
            query = query.Where(q => q.PlanRequestId == planRequestId);
        }

        if (!string.IsNullOrWhiteSpace(filters.ConceptKey))
        {
            var conceptKey = filters.ConceptKey.Trim();
            query = query.Where(q => q.ConceptKey == conceptKey);
        }

        if (!string.IsNullOrWhiteSpace(filters.QuestionType))
        {
            query = query.Where(q => q.QuestionType == requestedQuestionType);
        }

        if (!string.IsNullOrWhiteSpace(filters.Difficulty))
        {
            query = query.Where(q => q.Difficulty == requestedDifficulty);
        }

        var questions = await query
            .OrderByDescending(q => q.OwnerUserId == userId)
            .ThenByDescending(q => q.UpdatedAt)
            .ThenBy(q => q.Id)
            .Take(take)
            .ToListAsync(ct);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        var isAdmin = user?.IsAdmin ?? false;

        var dtos = questions
            .Select(q => ToDto(q, stripAnswerKey: ShouldStripAnswerKey(q, userId, isAdmin)))
            .ToList();

        if (filters.IncludeDiagnosticItems && dtos.Count < take)
        {
            var diagnosticItems = await GetDiagnosticQuestionBankItemsAsync(
                userId,
                filters,
                requestedQuestionType,
                requestedDifficulty,
                take - dtos.Count,
                ct);
            dtos.AddRange(diagnosticItems);
        }

        return dtos;
    }

    public async Task<QuestionItemDto?> GetQuestionAsync(Guid userId, Guid questionId, CancellationToken ct = default)
    {
        var question = await VisibleQuestions(userId).FirstOrDefaultAsync(q => q.Id == questionId, ct);
        if (question is not null)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            var isAdmin = user?.IsAdmin ?? false;
            return ToDto(question, stripAnswerKey: ShouldStripAnswerKey(question, userId, isAdmin));
        }

        var diagnosticItem = await _db.AssessmentItems
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == questionId
                                         && item.UserId == userId
                                         && item.GeneratedQuestionJson != null
                                         && item.GeneratedQuestionJson != string.Empty, ct);

        if (diagnosticItem is null)
        {
            return null;
        }

        var stat = await _db.AssessmentItemStats
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.AssessmentItemId == questionId, ct);

        return ToDiagnosticQuestionBankDto(diagnosticItem, stat);
    }

    public async Task<QuestionItemDto> CreateQuestionAsync(
        Guid userId,
        CreateQuestionDto request,
        CancellationToken ct = default)
    {
        var links = await ResolveVisibleExamLinksAsync(
            userId,
            request.ExamDefinitionId,
            request.ExamVariantId,
            request.ExamSectionId,
            request.ExamSubjectId,
            request.ExamTopicId,
            request.ExamOutcomeId,
            request.OutcomeLinks,
            ct);
        var binding = await ResolveQuestionBindingAsync(
            userId,
            request.LearningTopicId,
            request.ConceptGraphSnapshotId,
            request.LearningConceptId,
            request.AssessmentItemId,
            request.ConceptKey,
            request.ConceptLabel,
            request.MisconceptionTarget,
            request.EvidenceExpected,
            request.ScoringRuleJson,
            request.QuizRunId,
            request.PlanRequestId,
            ct);

        var now = DateTime.UtcNow;
        var question = new QuestionItem
        {
            OwnerUserId = userId,
            ExamDefinitionId = links.ExamDefinitionId,
            ExamVariantId = links.ExamVariantId,
            ExamSectionId = links.ExamSectionId,
            ExamSubjectId = links.ExamSubjectId,
            ExamTopicId = links.ExamTopicId,
            ExamOutcomeId = links.ExamOutcomeId,
            LearningTopicId = binding.LearningTopicId,
            ConceptGraphSnapshotId = binding.ConceptGraphSnapshotId,
            LearningConceptId = binding.LearningConceptId,
            AssessmentItemId = binding.AssessmentItemId,
            QuizRunId = binding.QuizRunId,
            PlanRequestId = binding.PlanRequestId,
            QuestionBankSource = Clean(request.QuestionBankSource, binding.AssessmentItemId.HasValue ? "diagnostic_assessment_item" : "curated_question_item"),
            ConceptKey = binding.ConceptKey,
            ConceptLabel = binding.ConceptLabel,
            MisconceptionTarget = binding.MisconceptionTarget,
            EvidenceExpected = binding.EvidenceExpected,
            ScoringRuleJson = binding.ScoringRuleJson,
            CalibrationStatus = SafeOptional(request.CalibrationStatus),
            VisualReadinessStatus = NormalizeBounded(request.VisualReadinessStatus, AllowedVisualReadinessStatuses, "not_required"),
            QuestionType = NormalizeBounded(request.QuestionType, AllowedQuestionTypes, "multiple_choice"),
            Stem = Clean(request.Stem),
            Difficulty = NormalizeBounded(request.Difficulty, AllowedDifficulties, "medium"),
            CognitiveSkill = Clean(request.CognitiveSkill, "conceptual"),
            QualityStatus = "draft",
            LicenseStatus = NormalizeBounded(request.LicenseStatus, AllowedLicenseStatuses, "unknown"),
            SourceOrigin = Clean(request.SourceOrigin, "manual"),
            SourceTitle = SafeOptional(request.SourceTitle),
            SourceUrl = SafeOptional(request.SourceUrl),
            Explanation = CleanOptional(request.Explanation),
            CreatedAt = now,
            UpdatedAt = now
        };

        ApplyChildren(question, request.Options, request.Explanations, request.Tags, links.OutcomeLinks, now);
        await ApplyQuestionContentBlocksAsync(userId, question, request.ContentBlocks, now, ct);
        await ApplyStimulusLinksAsync(userId, question, request.Stimuli, ct);
        var validation = ValidateQuestion(question, forPublish: false);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(" | ", validation.Errors));
        }

        _db.QuestionItems.Add(question);
        await _db.SaveChangesAsync(ct);
        return (await GetQuestionAsync(userId, question.Id, ct))!;
    }

    public async Task<QuestionItemDto?> UpdateQuestionAsync(
        Guid userId,
        Guid questionId,
        UpdateQuestionDto request,
        CancellationToken ct = default)
    {
        var question = await OwnedQuestionForMutation(userId, questionId).FirstOrDefaultAsync(ct);
        if (question is null)
        {
            return null;
        }

        var links = await ResolveVisibleExamLinksAsync(
            userId,
            question.ExamDefinitionId,
            request.ExamVariantId ?? question.ExamVariantId,
            request.ExamSectionId ?? question.ExamSectionId,
            request.ExamSubjectId ?? question.ExamSubjectId,
            request.ExamTopicId ?? question.ExamTopicId,
            request.ExamOutcomeId ?? question.ExamOutcomeId,
            request.OutcomeLinks ?? question.OutcomeLinks.Select(l => new QuestionOutcomeLinkDto
            {
                ExamOutcomeId = l.ExamOutcomeId,
                IsPrimary = l.IsPrimary,
                LinkStrength = l.LinkStrength
            }).ToList(),
            ct);
        var binding = await ResolveQuestionBindingAsync(
            userId,
            request.LearningTopicId ?? question.LearningTopicId,
            request.ConceptGraphSnapshotId ?? question.ConceptGraphSnapshotId,
            request.LearningConceptId ?? question.LearningConceptId,
            request.AssessmentItemId ?? question.AssessmentItemId,
            request.ConceptKey ?? question.ConceptKey,
            request.ConceptLabel ?? question.ConceptLabel,
            request.MisconceptionTarget ?? question.MisconceptionTarget,
            request.EvidenceExpected ?? question.EvidenceExpected,
            request.ScoringRuleJson ?? question.ScoringRuleJson,
            request.QuizRunId ?? question.QuizRunId,
            request.PlanRequestId ?? question.PlanRequestId,
            ct);

        question.ExamVariantId = links.ExamVariantId;
        question.ExamSectionId = links.ExamSectionId;
        question.ExamSubjectId = links.ExamSubjectId;
        question.ExamTopicId = links.ExamTopicId;
        question.ExamOutcomeId = links.ExamOutcomeId;
        question.LearningTopicId = binding.LearningTopicId;
        question.ConceptGraphSnapshotId = binding.ConceptGraphSnapshotId;
        question.LearningConceptId = binding.LearningConceptId;
        question.AssessmentItemId = binding.AssessmentItemId;
        question.QuizRunId = binding.QuizRunId;
        question.PlanRequestId = binding.PlanRequestId;
        question.QuestionBankSource = request.QuestionBankSource is null
            ? question.QuestionBankSource
            : Clean(request.QuestionBankSource, binding.AssessmentItemId.HasValue ? "diagnostic_assessment_item" : "curated_question_item");
        question.ConceptKey = binding.ConceptKey;
        question.ConceptLabel = binding.ConceptLabel;
        question.MisconceptionTarget = binding.MisconceptionTarget;
        question.EvidenceExpected = binding.EvidenceExpected;
        question.ScoringRuleJson = binding.ScoringRuleJson;
        question.CalibrationStatus = request.CalibrationStatus is null ? question.CalibrationStatus : SafeOptional(request.CalibrationStatus);
        question.VisualReadinessStatus = request.VisualReadinessStatus is null
            ? question.VisualReadinessStatus
            : NormalizeBounded(request.VisualReadinessStatus, AllowedVisualReadinessStatuses, "not_required");
        question.QuestionType = NormalizeBounded(request.QuestionType ?? question.QuestionType, AllowedQuestionTypes, "multiple_choice");
        question.Stem = request.Stem is null ? question.Stem : Clean(request.Stem);
        question.Difficulty = NormalizeBounded(request.Difficulty ?? question.Difficulty, AllowedDifficulties, "medium");
        question.CognitiveSkill = request.CognitiveSkill is null ? question.CognitiveSkill : Clean(request.CognitiveSkill, "conceptual");
        question.QualityStatus = NormalizeBounded(request.QualityStatus ?? question.QualityStatus, AllowedQualityStatuses, "draft");
        question.LicenseStatus = NormalizeBounded(request.LicenseStatus ?? question.LicenseStatus, AllowedLicenseStatuses, "unknown");
        question.SourceOrigin = request.SourceOrigin is null ? question.SourceOrigin : Clean(request.SourceOrigin, "manual");
        question.SourceTitle = request.SourceTitle is null ? question.SourceTitle : SafeOptional(request.SourceTitle);
        question.SourceUrl = request.SourceUrl is null ? question.SourceUrl : SafeOptional(request.SourceUrl);
        question.Explanation = request.Explanation is null ? question.Explanation : CleanOptional(request.Explanation);
        question.UpdatedAt = DateTime.UtcNow;

        if (request.Options is not null || request.Explanations is not null || request.Tags is not null || request.OutcomeLinks is not null)
        {
            question.Options.Clear();
            question.Explanations.Clear();
            question.Tags.Clear();
            question.OutcomeLinks.Clear();
            ApplyChildren(
                question,
                request.Options ?? [],
                request.Explanations ?? [],
                request.Tags ?? [],
                links.OutcomeLinks,
                question.UpdatedAt);
        }
        else if (request.ExamOutcomeId is not null)
        {
            question.OutcomeLinks.Clear();
            ApplyOutcomeLinks(question, links.OutcomeLinks, question.UpdatedAt);
        }

        if (request.ContentBlocks is not null)
        {
            question.ContentBlocks.Clear();
            await ApplyQuestionContentBlocksAsync(userId, question, request.ContentBlocks, question.UpdatedAt, ct);
        }

        if (request.Stimuli is not null)
        {
            question.StimulusLinks.Clear();
            await ApplyStimulusLinksAsync(userId, question, request.Stimuli, ct);
        }

        var validation = ValidateQuestion(question, forPublish: false);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(" | ", validation.Errors));
        }

        await _db.SaveChangesAsync(ct);
        return await GetQuestionAsync(userId, question.Id, ct);
    }

    public async Task<QuestionItemDto?> SubmitForReviewAsync(Guid userId, Guid questionId, CancellationToken ct = default)
    {
        var question = await OwnedQuestionForMutation(userId, questionId).FirstOrDefaultAsync(ct);
        if (question is null)
        {
            return null;
        }

        var validation = ValidateQuestion(question, forPublish: false);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(" | ", validation.Errors));
        }

        question.QualityStatus = "needs_review";
        question.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await GetQuestionAsync(userId, question.Id, ct);
    }

    public async Task<QuestionItemDto?> PublishQuestionAsync(Guid userId, Guid questionId, CancellationToken ct = default)
    {
        var question = await OwnedQuestionForMutation(userId, questionId).FirstOrDefaultAsync(ct);
        if (question is null)
        {
            return null;
        }

        await EnsureVisibleExamTreeAsync(question, userId, ct);
        var validation = ValidateQuestion(question, forPublish: true);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(" | ", validation.Errors));
        }

        question.QualityStatus = "published";
        question.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await GetQuestionAsync(userId, question.Id, ct);
    }

    public async Task<bool> SoftDeleteQuestionAsync(Guid userId, Guid questionId, CancellationToken ct = default)
    {
        var question = await OwnedQuestionForMutation(userId, questionId).FirstOrDefaultAsync(ct);
        if (question is null)
        {
            return false;
        }

        question.IsDeleted = true;
        question.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<QuestionAssetDto> CreateAssetAsync(
        Guid userId,
        CreateQuestionAssetDto request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.StorageKey))
        {
            throw new ArgumentException("question_asset_storage_key_required");
        }

        if (!IsSafeStorageKey(request.StorageKey))
        {
            throw new ArgumentException("question_asset_storage_key_must_be_relative");
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("question_asset_file_name_required");
        }

        if (string.IsNullOrWhiteSpace(request.Sha256Hash))
        {
            throw new ArgumentException("question_asset_hash_required");
        }

        if (!IsValidOptionalUrl(request.SourceUrl))
        {
            throw new ArgumentException("question_asset_source_url_invalid");
        }

        await EnsureSourceVisibleAsync(userId, request.SourceRegistryItemId, ct);

        var now = DateTime.UtcNow;
        var asset = new QuestionAsset
        {
            OwnerUserId = userId,
            AssetType = NormalizeBounded(request.AssetType, AllowedAssetTypes, "image"),
            StorageKey = Clean(request.StorageKey),
            FileName = Clean(request.FileName),
            MimeType = Clean(request.MimeType, "application/octet-stream"),
            SizeBytes = Math.Max(0, request.SizeBytes),
            Sha256Hash = Clean(request.Sha256Hash).ToLowerInvariant(),
            SourceRegistryItemId = request.SourceRegistryItemId,
            SourceTitle = SafeOptional(request.SourceTitle),
            SourceUrl = SafeOptional(request.SourceUrl),
            LicenseStatus = NormalizeBounded(request.LicenseStatus, AllowedLicenseStatuses, "unknown"),
            VerificationStatus = Clean(request.VerificationStatus, "unverified"),
            AltText = SafeOptional(request.AltText),
            Caption = SafeOptional(request.Caption),
            LongDescription = SafeOptional(request.LongDescription),
            GenerationProvider = SafeOptional(request.GenerationProvider),
            GenerationModel = SafeOptional(request.GenerationModel),
            RenderStrategy = SafeOptional(request.RenderStrategy),
            GenerationPromptHash = SafeOptional(request.GenerationPromptHash),
            ValidationReportJson = SafeContentJson(request.ValidationReportJson),
            VisualReadinessStatus = Clean(request.VisualReadinessStatus, "needs_validation"),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.QuestionAssets.Add(asset);
        await _db.SaveChangesAsync(ct);
        return ToDto(asset);
    }

    public async Task<QuestionAssetDto?> GetAssetAsync(Guid userId, Guid assetId, CancellationToken ct = default)
    {
        var asset = await VisibleAssets(userId).FirstOrDefaultAsync(a => a.Id == assetId, ct);
        return asset is null ? null : ToDto(asset);
    }

    public async Task<QuestionStimulusDto> CreateStimulusAsync(
        Guid userId,
        CreateQuestionStimulusDto request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("question_stimulus_title_required");
        }

        await EnsureSourceVisibleAsync(userId, request.SourceRegistryItemId, ct);
        await EnsureCurriculumNodeVisibleAsync(userId, request.CurriculumNodeId, ct);

        var now = DateTime.UtcNow;
        var stimulus = new QuestionStimulus
        {
            OwnerUserId = userId,
            Title = Clean(request.Title),
            StimulusType = NormalizeBounded(request.StimulusType, AllowedStimulusTypes, "passage"),
            ContentText = SafeOptional(request.ContentText),
            ContentJson = SafeContentJson(request.ContentJson),
            SourceRegistryItemId = request.SourceRegistryItemId,
            CurriculumNodeId = request.CurriculumNodeId,
            VerificationStatus = Clean(request.VerificationStatus, "unverified"),
            LicenseStatus = NormalizeBounded(request.LicenseStatus, AllowedLicenseStatuses, "unknown"),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.QuestionStimuli.Add(stimulus);
        await _db.SaveChangesAsync(ct);
        return ToDto(stimulus, sortOrder: 0);
    }

    public async Task<QuestionItemDto?> AttachStimulusAsync(
        Guid userId,
        Guid questionId,
        QuestionStimulusLinkDto request,
        CancellationToken ct = default)
    {
        var question = await OwnedQuestionForMutation(userId, questionId).FirstOrDefaultAsync(ct);
        if (question is null)
        {
            return null;
        }

        var stimulus = await VisibleStimuli(userId).FirstOrDefaultAsync(s => s.Id == request.QuestionStimulusId, ct);
        if (stimulus is null)
        {
            throw new ArgumentException("question_stimulus_not_visible");
        }

        if (question.StimulusLinks.All(l => l.QuestionStimulusId != stimulus.Id))
        {
            _db.QuestionStimulusLinks.Add(new QuestionStimulusLink
            {
                QuestionItemId = question.Id,
                QuestionStimulusId = stimulus.Id,
                SortOrder = request.SortOrder
            });
        }

        question.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await GetQuestionAsync(userId, question.Id, ct);
    }

    public async Task<QuestionItemDto?> AddQuestionContentBlockAsync(
        Guid userId,
        Guid questionId,
        CreateQuestionContentBlockDto request,
        CancellationToken ct = default)
    {
        var question = await OwnedQuestionForMutation(userId, questionId).FirstOrDefaultAsync(ct);
        if (question is null)
        {
            return null;
        }

        _db.QuestionContentBlocks.Add(await BuildQuestionContentBlockAsync(userId, question.Id, request, DateTime.UtcNow, ct));
        question.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await GetQuestionAsync(userId, question.Id, ct);
    }

    public async Task<QuestionItemDto?> AddOptionContentBlockAsync(
        Guid userId,
        Guid optionId,
        CreateQuestionOptionContentBlockDto request,
        CancellationToken ct = default)
    {
        var option = await _db.QuestionOptions
            .Include(o => o.QuestionItem)
            .ThenInclude(q => q.ContentBlocks)
            .Include(o => o.QuestionItem)
            .ThenInclude(q => q.StimulusLinks)
            .Include(o => o.ContentBlocks)
            .FirstOrDefaultAsync(o => o.Id == optionId
                                      && !o.QuestionItem.IsDeleted
                                      && o.QuestionItem.OwnerUserId == userId, ct);

        if (option is null)
        {
            return null;
        }

        _db.QuestionOptionContentBlocks.Add(await BuildOptionContentBlockAsync(userId, option.Id, request, DateTime.UtcNow, ct));
        option.QuestionItem.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await GetQuestionAsync(userId, option.QuestionItemId, ct);
    }

    private async Task<IReadOnlyList<QuestionItemDto>> GetDiagnosticQuestionBankItemsAsync(
        Guid userId,
        QuestionBankFilterDto filters,
        string? requestedQuestionType,
        string? requestedDifficulty,
        int take,
        CancellationToken ct)
    {
        if (take <= 0 || HasExamTreeFilter(filters) || HasCuratedOnlyQualityFilter(filters))
        {
            return [];
        }

        var query = _db.AssessmentItems
            .AsNoTracking()
            .Where(item => item.UserId == userId
                           && item.GeneratedQuestionJson != null
                           && item.GeneratedQuestionJson != string.Empty
                           && !_db.QuestionItems.Any(q => !q.IsDeleted && q.AssessmentItemId == item.Id));

        if (filters.AssessmentItemId is { } assessmentItemId)
        {
            query = query.Where(item => item.Id == assessmentItemId);
        }

        if (filters.LearningTopicId is { } learningTopicId)
        {
            query = query.Where(item => item.TopicId == learningTopicId);
        }

        if (filters.ConceptGraphSnapshotId is { } snapshotId)
        {
            query = query.Where(item => item.ConceptGraphSnapshotId == snapshotId);
        }

        if (filters.LearningConceptId is { } learningConceptId)
        {
            query = query.Where(item => item.LearningConceptId == learningConceptId);
        }

        if (filters.QuizRunId is { } quizRunId)
        {
            query = query.Where(item => item.QuizRunId == quizRunId);
        }

        if (filters.PlanRequestId is { } planRequestId)
        {
            query = query.Where(item => item.PlanRequestId == planRequestId);
        }

        if (!string.IsNullOrWhiteSpace(filters.ConceptKey))
        {
            var conceptKey = filters.ConceptKey.Trim();
            query = query.Where(item => item.ConceptKey == conceptKey);
        }

        var candidates = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Order)
            .ThenBy(item => item.Id)
            .Take(Math.Min(take * 4, 100))
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return [];
        }

        var candidateIds = candidates.Select(item => item.Id).ToList();
        var stats = await _db.AssessmentItemStats
            .AsNoTracking()
            .Where(stat => stat.UserId == userId && candidateIds.Contains(stat.AssessmentItemId))
            .ToDictionaryAsync(stat => stat.AssessmentItemId, ct);

        return candidates
            .Select(item => ToDiagnosticQuestionBankDto(item, stats.GetValueOrDefault(item.Id)))
            .Where(dto => requestedQuestionType is null || dto.QuestionType == requestedQuestionType)
            .Where(dto => requestedDifficulty is null || dto.Difficulty == requestedDifficulty)
            .Take(take)
            .ToList();
    }

    private static bool HasExamTreeFilter(QuestionBankFilterDto filters) =>
        filters.ExamDefinitionId is not null ||
        filters.ExamVariantId is not null ||
        filters.ExamSectionId is not null ||
        filters.ExamSubjectId is not null ||
        filters.ExamTopicId is not null ||
        filters.ExamOutcomeId is not null;

    private static bool HasCuratedOnlyQualityFilter(QuestionBankFilterDto filters)
    {
        if (string.IsNullOrWhiteSpace(filters.QualityStatus))
        {
            return false;
        }

        return !filters.QualityStatus.Equals("diagnostic_ready", StringComparison.OrdinalIgnoreCase);
    }

    private IQueryable<QuestionItem> VisibleQuestions(Guid userId) =>
        _db.QuestionItems
            .AsNoTracking()
            .Include(q => q.Options)
            .ThenInclude(o => o.ContentBlocks.Where(b => !b.IsDeleted))
            .ThenInclude(b => b.Asset)
            .Include(q => q.ContentBlocks.Where(b => !b.IsDeleted))
            .ThenInclude(b => b.Asset)
            .Include(q => q.StimulusLinks)
            .ThenInclude(l => l.QuestionStimulus)
            .Include(q => q.Explanations)
            .Include(q => q.Tags)
            .Include(q => q.OutcomeLinks)
            .Where(q => !q.IsDeleted && (q.OwnerUserId == null || q.OwnerUserId == userId));

    private IQueryable<QuestionItem> OwnedQuestionForMutation(Guid userId, Guid questionId) =>
        _db.QuestionItems
            .Include(q => q.Options)
            .ThenInclude(o => o.ContentBlocks)
            .ThenInclude(b => b.Asset)
            .Include(q => q.ContentBlocks)
            .ThenInclude(b => b.Asset)
            .Include(q => q.StimulusLinks)
            .Include(q => q.Explanations)
            .Include(q => q.Tags)
            .Include(q => q.OutcomeLinks)
            .Where(q => q.Id == questionId
                        && !q.IsDeleted
                        && q.OwnerUserId == userId
                        && q.QuestionBankSource != "diagnostic_assessment_item");

    private async Task<ResolvedExamLinks> ResolveVisibleExamLinksAsync(
        Guid userId,
        Guid examDefinitionId,
        Guid? examVariantId,
        Guid? examSectionId,
        Guid? examSubjectId,
        Guid? examTopicId,
        Guid? examOutcomeId,
        IReadOnlyList<QuestionOutcomeLinkDto> outcomeLinks,
        CancellationToken ct)
    {
        var definition = await _db.ExamDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == examDefinitionId
                                      && !d.IsDeleted
                                      && (d.OwnerUserId == null || d.OwnerUserId == userId), ct);

        if (definition is null)
        {
            throw new ArgumentException("Linked exam definition is not visible.");
        }

        if (examVariantId is { } variantId)
        {
            var variantOk = await _db.ExamVariants.AsNoTracking()
                .AnyAsync(v => v.Id == variantId && !v.IsDeleted && v.ExamDefinitionId == examDefinitionId, ct);
            if (!variantOk) throw new ArgumentException("Linked exam variant is not visible.");
        }

        if (examSectionId is { } sectionId)
        {
            var section = await _db.ExamSections.AsNoTracking()
                .Include(s => s.ExamVariant)
                .FirstOrDefaultAsync(s => s.Id == sectionId && !s.IsDeleted, ct);
            if (section is null || section.ExamVariant.ExamDefinitionId != examDefinitionId || (examVariantId is not null && section.ExamVariantId != examVariantId))
            {
                throw new ArgumentException("Linked exam section is not visible.");
            }
        }

        if (examSubjectId is { } subjectId)
        {
            var subject = await _db.ExamSubjects.AsNoTracking()
                .Include(s => s.ExamSection)
                .ThenInclude(s => s.ExamVariant)
                .FirstOrDefaultAsync(s => s.Id == subjectId && !s.IsDeleted, ct);
            if (subject is null
                || subject.ExamSection.ExamVariant.ExamDefinitionId != examDefinitionId
                || (examSectionId is not null && subject.ExamSectionId != examSectionId))
            {
                throw new ArgumentException("Linked exam subject is not visible.");
            }
        }

        if (examTopicId is { } topicId)
        {
            var topic = await _db.ExamTopics.AsNoTracking()
                .Include(t => t.ExamSubject)
                .ThenInclude(s => s.ExamSection)
                .ThenInclude(s => s.ExamVariant)
                .FirstOrDefaultAsync(t => t.Id == topicId && !t.IsDeleted, ct);
            if (topic is null
                || topic.ExamSubject.ExamSection.ExamVariant.ExamDefinitionId != examDefinitionId
                || (examSubjectId is not null && topic.ExamSubjectId != examSubjectId))
            {
                throw new ArgumentException("Linked exam topic is not visible.");
            }
        }

        var resolvedOutcomeLinks = outcomeLinks
            .Where(l => l.ExamOutcomeId != Guid.Empty)
            .GroupBy(l => l.ExamOutcomeId)
            .Select((group, index) => new QuestionOutcomeLinkDto
            {
                ExamOutcomeId = group.Key,
                IsPrimary = group.Any(l => l.IsPrimary) || index == 0,
                LinkStrength = group.Max(l => l.LinkStrength <= 0 ? 1.0m : l.LinkStrength)
            })
            .ToList();

        if (examOutcomeId is { } primaryOutcomeId && resolvedOutcomeLinks.All(l => l.ExamOutcomeId != primaryOutcomeId))
        {
            resolvedOutcomeLinks.Insert(0, new QuestionOutcomeLinkDto
            {
                ExamOutcomeId = primaryOutcomeId,
                IsPrimary = true,
                LinkStrength = 1.0m
            });
        }

        foreach (var link in resolvedOutcomeLinks)
        {
            await EnsureOutcomeVisibleAsync(examDefinitionId, examTopicId, link.ExamOutcomeId, ct);
        }

        return new ResolvedExamLinks(
            examDefinitionId,
            examVariantId,
            examSectionId,
            examSubjectId,
            examTopicId,
            examOutcomeId,
            resolvedOutcomeLinks);
    }

    private async Task EnsureOutcomeVisibleAsync(Guid examDefinitionId, Guid? examTopicId, Guid outcomeId, CancellationToken ct)
    {
        var outcome = await _db.ExamOutcomes.AsNoTracking()
            .Include(o => o.ExamTopic)
            .ThenInclude(t => t.ExamSubject)
            .ThenInclude(s => s.ExamSection)
            .ThenInclude(s => s.ExamVariant)
            .FirstOrDefaultAsync(o => o.Id == outcomeId && !o.IsDeleted && !o.ExamTopic.IsDeleted, ct);

        if (outcome is null
            || outcome.ExamTopic.ExamSubject.ExamSection.ExamVariant.ExamDefinitionId != examDefinitionId
            || (examTopicId is not null && outcome.ExamTopicId != examTopicId))
        {
            throw new ArgumentException("Linked exam outcome is not visible.");
        }
    }

    private async Task EnsureVisibleExamTreeAsync(QuestionItem question, Guid userId, CancellationToken ct)
    {
        await ResolveVisibleExamLinksAsync(
            userId,
            question.ExamDefinitionId,
            question.ExamVariantId,
            question.ExamSectionId,
            question.ExamSubjectId,
            question.ExamTopicId,
            question.ExamOutcomeId,
            question.OutcomeLinks.Select(l => new QuestionOutcomeLinkDto
            {
                ExamOutcomeId = l.ExamOutcomeId,
                IsPrimary = l.IsPrimary,
                LinkStrength = l.LinkStrength
            }).ToList(),
            ct);
    }

    private static void ApplyChildren(
        QuestionItem question,
        IReadOnlyList<QuestionOptionDto> options,
        IReadOnlyList<QuestionExplanationDto> explanations,
        IReadOnlyList<QuestionTagDto> tags,
        IReadOnlyList<QuestionOutcomeLinkDto> outcomeLinks,
        DateTime now)
    {
        foreach (var option in options.OrderBy(o => o.SortOrder).ThenBy(o => o.OptionKey, StringComparer.OrdinalIgnoreCase))
        {
            question.Options.Add(new QuestionOption
            {
                OptionKey = Clean(option.OptionKey),
                Text = Clean(option.Text),
                IsCorrect = option.IsCorrect,
                Rationale = SafeOptional(option.Rationale),
                MisconceptionKey = SafeOptional(option.MisconceptionKey),
                DiagnosticSignalJson = SafeAssessmentMetadataJson(option.DiagnosticSignalJson),
                SortOrder = option.SortOrder
            });
        }

        foreach (var explanation in explanations.Where(e => !string.IsNullOrWhiteSpace(e.ExplanationText)))
        {
            question.Explanations.Add(new QuestionExplanation
            {
                ExplanationText = Clean(explanation.ExplanationText),
                SourceTitle = SafeOptional(explanation.SourceTitle),
                SourceUrl = SafeOptional(explanation.SourceUrl),
                Visibility = Clean(explanation.Visibility, "authoring"),
                IsSafeForLearners = explanation.IsSafeForLearners,
                CreatedAt = now
            });
        }

        foreach (var tag in tags.Select(t => CleanOptional(t.Tag)).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            question.Tags.Add(new QuestionTag
            {
                Tag = tag,
                CreatedAt = now
            });
        }

        ApplyOutcomeLinks(question, outcomeLinks, now);
    }

    private static void ApplyOutcomeLinks(QuestionItem question, IReadOnlyList<QuestionOutcomeLinkDto> outcomeLinks, DateTime now)
    {
        foreach (var link in outcomeLinks)
        {
            question.OutcomeLinks.Add(new QuestionOutcomeLink
            {
                ExamOutcomeId = link.ExamOutcomeId,
                IsPrimary = link.IsPrimary,
                LinkStrength = link.LinkStrength <= 0 ? 1.0m : link.LinkStrength,
                CreatedAt = now
            });
        }
    }

    private async Task ApplyQuestionContentBlocksAsync(
        Guid userId,
        QuestionItem question,
        IReadOnlyList<CreateQuestionContentBlockDto> blocks,
        DateTime now,
        CancellationToken ct)
    {
        foreach (var block in blocks.OrderBy(b => b.SortOrder))
        {
            question.ContentBlocks.Add(await BuildQuestionContentBlockAsync(userId, question.Id, block, now, ct));
        }
    }

    private async Task<QuestionContentBlock> BuildQuestionContentBlockAsync(
        Guid userId,
        Guid questionId,
        CreateQuestionContentBlockDto request,
        DateTime now,
        CancellationToken ct)
    {
        await EnsureAssetVisibleAsync(userId, request.AssetId, ct);
        return new QuestionContentBlock
        {
            QuestionItemId = questionId,
            BlockType = NormalizeBounded(request.BlockType, AllowedQuestionBlockTypes, "text"),
            Text = SafeOptional(request.Text),
            ContentJson = SafeContentJson(request.ContentJson),
            AssetId = request.AssetId,
            SortOrder = request.SortOrder,
            AltText = SafeOptional(request.AltText),
            Caption = SafeOptional(request.Caption),
            LongDescription = SafeOptional(request.LongDescription),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private async Task<QuestionOptionContentBlock> BuildOptionContentBlockAsync(
        Guid userId,
        Guid optionId,
        CreateQuestionOptionContentBlockDto request,
        DateTime now,
        CancellationToken ct)
    {
        await EnsureAssetVisibleAsync(userId, request.AssetId, ct);
        return new QuestionOptionContentBlock
        {
            QuestionOptionId = optionId,
            BlockType = NormalizeBounded(request.BlockType, AllowedOptionBlockTypes, "text"),
            Text = SafeOptional(request.Text),
            ContentJson = SafeContentJson(request.ContentJson),
            AssetId = request.AssetId,
            SortOrder = request.SortOrder,
            AltText = SafeOptional(request.AltText),
            Caption = SafeOptional(request.Caption),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private async Task ApplyStimulusLinksAsync(
        Guid userId,
        QuestionItem question,
        IReadOnlyList<QuestionStimulusLinkDto> stimuli,
        CancellationToken ct)
    {
        foreach (var item in stimuli
                     .Where(s => s.QuestionStimulusId != Guid.Empty)
                     .GroupBy(s => s.QuestionStimulusId)
                     .Select(g => g.OrderBy(s => s.SortOrder).First()))
        {
            var stimulus = await VisibleStimuli(userId).FirstOrDefaultAsync(s => s.Id == item.QuestionStimulusId, ct);
            if (stimulus is null)
            {
                throw new ArgumentException("question_stimulus_not_visible");
            }

            question.StimulusLinks.Add(new QuestionStimulusLink
            {
                QuestionItemId = question.Id,
                QuestionStimulusId = item.QuestionStimulusId,
                SortOrder = item.SortOrder
            });
        }
    }

    private IQueryable<QuestionAsset> VisibleAssets(Guid userId) =>
        _db.QuestionAssets
            .AsNoTracking()
            .Where(a => !a.IsDeleted && (a.OwnerUserId == null || a.OwnerUserId == userId));

    private IQueryable<QuestionStimulus> VisibleStimuli(Guid userId) =>
        _db.QuestionStimuli
            .AsNoTracking()
            .Where(s => !s.IsDeleted && (s.OwnerUserId == null || s.OwnerUserId == userId));

    private async Task EnsureAssetVisibleAsync(Guid userId, Guid? assetId, CancellationToken ct)
    {
        if (assetId is null)
        {
            return;
        }

        if (!await VisibleAssets(userId).AnyAsync(a => a.Id == assetId, ct))
        {
            throw new ArgumentException("question_asset_not_visible");
        }
    }

    private async Task EnsureSourceVisibleAsync(Guid userId, Guid? sourceId, CancellationToken ct)
    {
        if (sourceId is null)
        {
            return;
        }

        var visible = await _db.SourceRegistryItems
            .AsNoTracking()
            .AnyAsync(s => s.Id == sourceId && !s.IsDeleted && (s.OwnerUserId == null || s.OwnerUserId == userId), ct);
        if (!visible)
        {
            throw new ArgumentException("source_not_visible");
        }
    }

    private async Task EnsureCurriculumNodeVisibleAsync(Guid userId, Guid? curriculumNodeId, CancellationToken ct)
    {
        if (curriculumNodeId is null)
        {
            return;
        }

        var visible = await _db.CurriculumNodes
            .AsNoTracking()
            .Include(n => n.CurriculumVersion)
            .AnyAsync(n => n.Id == curriculumNodeId
                           && !n.IsDeleted
                           && !n.CurriculumVersion.IsDeleted
                           && (n.CurriculumVersion.OwnerUserId == null || n.CurriculumVersion.OwnerUserId == userId), ct);
        if (!visible)
        {
            throw new ArgumentException("curriculum_node_not_visible");
        }
    }

    private static QuestionValidationResultDto ValidateQuestion(QuestionItem question, bool forPublish)
    {
        var result = new QuestionValidationResultDto();

        var activeQuestionBlocks = question.ContentBlocks.Where(b => !b.IsDeleted).ToList();
        if (string.IsNullOrWhiteSpace(question.Stem) && activeQuestionBlocks.Count == 0)
        {
            result.Errors.Add("question_stem_required");
        }

        if (question.QuestionType == "multiple_choice")
        {
            var activeOptions = question.Options.ToList();
            if (activeOptions.Count < 2)
            {
                result.Errors.Add("multiple_choice_minimum_two_options");
            }

            if (activeOptions.Any(o => string.IsNullOrWhiteSpace(o.OptionKey)
                                       || (string.IsNullOrWhiteSpace(o.Text)
                                           && o.ContentBlocks.All(b => b.IsDeleted || !HasRenderableOptionBlock(b)))))
            {
                result.Errors.Add("multiple_choice_option_text_required");
            }

            var correctCount = activeOptions.Count(o => o.IsCorrect);
            if (correctCount == 0)
            {
                result.Errors.Add("multiple_choice_correct_option_required");
            }
            else if (correctCount > 1)
            {
                result.Errors.Add("multiple_choice_single_correct_option_required");
            }
        }

        if (forPublish)
        {
            if (question.QualityStatus != "approved")
            {
                result.Errors.Add("publish_requires_approved_quality_status");
            }

            if (!SafePublishLicenseStatuses.Contains(question.LicenseStatus))
            {
                result.Errors.Add("publish_requires_safe_license_status");
            }

            if (!string.IsNullOrWhiteSpace(question.SourceUrl)
                && !Uri.TryCreate(question.SourceUrl, UriKind.Absolute, out _))
            {
                result.Errors.Add("publish_requires_valid_source_url");
            }

            foreach (var issue in ValidateAccessibility(question))
            {
                result.Accessibility.Add(issue);
                result.Errors.Add(issue.Code);
            }

            AddProfessionalPracticePublishErrors(question, activeQuestionBlocks, result.Errors);
        }
        else
        {
            foreach (var issue in ValidateAccessibility(question))
            {
                result.Accessibility.Add(issue);
                result.Warnings.Add(issue.Code);
            }
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    private static void AddProfessionalPracticePublishErrors(
        QuestionItem question,
        IReadOnlyCollection<QuestionContentBlock> activeQuestionBlocks,
        List<string> errors)
    {
        if (!IsProfessionalPracticeQuestion(question))
        {
            return;
        }

        AddIfMissing(question.AssessmentItemId.HasValue, "assessment_item_binding_required", errors);
        AddIfMissing(question.ConceptGraphSnapshotId.HasValue, "concept_graph_binding_required", errors);
        AddIfMissing(question.LearningConceptId.HasValue, "learning_concept_binding_required", errors);
        AddIfMissing(!string.IsNullOrWhiteSpace(question.ConceptKey), "concept_key_required", errors);
        AddIfMissing(!string.IsNullOrWhiteSpace(question.EvidenceExpected), "evidence_expected_required", errors);
        AddIfMissing(!string.IsNullOrWhiteSpace(question.ScoringRuleJson), "scoring_rule_required", errors);
        AddIfMissing(
            question.VisualReadinessStatus is "not_required" or "ready" or "validated",
            "visual_readiness_required",
            errors);

        if (question.QuestionType == "multiple_choice")
        {
            var incorrectOptions = question.Options.Where(o => !o.IsCorrect).ToList();
            AddIfMissing(
                incorrectOptions.Count > 0 && incorrectOptions.All(o => !string.IsNullOrWhiteSpace(o.Rationale)),
                "distractor_rationale_required",
                errors);
            AddIfMissing(
                incorrectOptions.Count > 0 && incorrectOptions.All(o =>
                    !string.IsNullOrWhiteSpace(o.MisconceptionKey) ||
                    !string.IsNullOrWhiteSpace(o.DiagnosticSignalJson)),
                "distractor_diagnostic_signal_required",
                errors);
        }

        if (activeQuestionBlocks.Any(block => block.BlockType is "image" or "chart" or "table" or "formula"))
        {
            AddIfMissing(
                question.VisualReadinessStatus is "ready" or "validated",
                "visual_readiness_required",
                errors);
        }
    }

    private static void AddIfMissing(bool condition, string code, List<string> errors)
    {
        if (!condition && !errors.Contains(code, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(code);
        }
    }

    private static bool IsProfessionalPracticeQuestion(QuestionItem question) =>
        string.Equals(question.QuestionBankSource, "diagnostic_assessment_item", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(question.QualityStatus, "diagnostic_ready", StringComparison.OrdinalIgnoreCase);

    private static bool HasRenderableOptionBlock(QuestionOptionContentBlock block)
    {
        return !string.IsNullOrWhiteSpace(block.Text)
               || !string.IsNullOrWhiteSpace(block.ContentJson)
               || block.AssetId is not null;
    }

    private static IReadOnlyList<QuestionAccessibilityValidationDto> ValidateAccessibility(QuestionItem question)
    {
        var issues = new List<QuestionAccessibilityValidationDto>();

        foreach (var block in question.ContentBlocks.Where(b => !b.IsDeleted))
        {
            ValidateQuestionBlockAccessibility(block, issues);
        }

        foreach (var optionBlock in question.Options.SelectMany(o => o.ContentBlocks).Where(b => !b.IsDeleted))
        {
            ValidateOptionBlockAccessibility(optionBlock, issues);
        }

        return issues;
    }

    private static void ValidateQuestionBlockAccessibility(
        QuestionContentBlock block,
        List<QuestionAccessibilityValidationDto> issues)
    {
        var requiresTextAlternative = block.BlockType is "image" or "chart" or "table";
        var hasTextAlternative = !string.IsNullOrWhiteSpace(block.AltText)
                                 || !string.IsNullOrWhiteSpace(block.Caption)
                                 || !string.IsNullOrWhiteSpace(block.Asset?.AltText)
                                 || !string.IsNullOrWhiteSpace(block.Asset?.Caption);

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
            ValidateAssetAccessibility(asset, "question_asset", issues);
        }
    }

    private static void ValidateOptionBlockAccessibility(
        QuestionOptionContentBlock block,
        List<QuestionAccessibilityValidationDto> issues)
    {
        var requiresTextAlternative = block.BlockType is "image" or "table";
        var hasTextAlternative = !string.IsNullOrWhiteSpace(block.AltText)
                                 || !string.IsNullOrWhiteSpace(block.Caption)
                                 || !string.IsNullOrWhiteSpace(block.Asset?.AltText)
                                 || !string.IsNullOrWhiteSpace(block.Asset?.Caption);

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
            ValidateAssetAccessibility(asset, "question_asset", issues);
        }
    }

    private static void ValidateAssetAccessibility(
        QuestionAsset asset,
        string targetType,
        List<QuestionAccessibilityValidationDto> issues)
    {
        if (asset.AssetType == "image" && string.IsNullOrWhiteSpace(asset.AltText))
        {
            issues.Add(new QuestionAccessibilityValidationDto
            {
                TargetType = targetType,
                TargetId = asset.Id,
                Code = "image_asset_requires_alt_text",
                Severity = "error"
            });
        }

        if (!SafePublishLicenseStatuses.Contains(asset.LicenseStatus))
        {
            issues.Add(new QuestionAccessibilityValidationDto
            {
                TargetType = targetType,
                TargetId = asset.Id,
                Code = "asset_requires_safe_license_status",
                Severity = "error"
            });
        }
    }

    private static bool ShouldStripAnswerKey(QuestionItem question, Guid userId, bool isAdmin) =>
        !isAdmin && (question.OwnerUserId is null ||
                     string.Equals(question.QuestionBankSource, "diagnostic_assessment_item", StringComparison.OrdinalIgnoreCase));

    private static QuestionItemDto ToDiagnosticQuestionBankDto(AssessmentItem item, AssessmentItemStat? stat)
    {
        var projection = ParseGeneratedDiagnosticQuestion(item);
        var tags = new List<QuestionTagDto>();
        if (!string.IsNullOrWhiteSpace(item.ConceptKey))
        {
            tags.Add(new QuestionTagDto { Tag = $"concept:{item.ConceptKey}" });
        }

        if (!string.IsNullOrWhiteSpace(item.CognitiveSkill))
        {
            tags.Add(new QuestionTagDto { Tag = $"skill:{item.CognitiveSkill}" });
        }

        if (!string.IsNullOrWhiteSpace(item.MisconceptionTarget))
        {
            tags.Add(new QuestionTagDto { Tag = $"misconception:{item.MisconceptionTarget}" });
        }

        return new QuestionItemDto
        {
            Id = item.Id,
            OwnershipState = "user",
            QuestionBankSource = "diagnostic_assessment_item",
            ExamDefinitionId = Guid.Empty,
            LearningTopicId = item.TopicId,
            ConceptGraphSnapshotId = item.ConceptGraphSnapshotId,
            LearningConceptId = item.LearningConceptId,
            AssessmentItemId = item.Id,
            QuizRunId = item.QuizRunId,
            PlanRequestId = item.PlanRequestId,
            ConceptKey = string.IsNullOrWhiteSpace(item.ConceptKey) ? null : item.ConceptKey,
            ConceptLabel = string.IsNullOrWhiteSpace(item.ConceptLabel) ? null : item.ConceptLabel,
            MisconceptionTarget = string.IsNullOrWhiteSpace(item.MisconceptionTarget) ? null : item.MisconceptionTarget,
            EvidenceExpected = string.IsNullOrWhiteSpace(item.EvidenceExpected) ? null : item.EvidenceExpected,
            ScoringRuleJson = string.IsNullOrWhiteSpace(item.ScoringRuleJson) ? null : item.ScoringRuleJson,
            CalibrationStatus = stat?.CalibrationStatus ?? "uncalibrated",
            VisualReadinessStatus = "not_required",
            QuestionType = NormalizeDiagnosticQuestionType(item.QuestionType, projection.Options.Count),
            Stem = projection.Stem,
            Difficulty = NormalizeDiagnosticDifficulty(item.Difficulty),
            CognitiveSkill = Clean(item.CognitiveSkill, "conceptual"),
            QualityStatus = "diagnostic_ready",
            LicenseStatus = "user_provided",
            SourceOrigin = "diagnostic_engine",
            SourceTitle = "Orka diagnostic assessment item",
            Explanation = string.Empty,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.CreatedAt,
            Options = projection.Options.ToList(),
            Tags = tags,
            Validation = new QuestionValidationResultDto()
        };
    }

    private static QuestionItemDto ToDto(QuestionItem question, bool stripAnswerKey = false) => new()
    {
        Id = question.Id,
        OwnershipState = question.OwnerUserId is null ? "system" : "user",
        QuestionBankSource = string.IsNullOrWhiteSpace(question.QuestionBankSource) ? "curated_question_item" : question.QuestionBankSource,
        ExamDefinitionId = question.ExamDefinitionId,
        ExamVariantId = question.ExamVariantId,
        ExamSectionId = question.ExamSectionId,
        ExamSubjectId = question.ExamSubjectId,
        ExamTopicId = question.ExamTopicId,
        ExamOutcomeId = question.ExamOutcomeId,
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
        QuestionType = question.QuestionType,
        Stem = question.Stem,
        Difficulty = question.Difficulty,
        CognitiveSkill = question.CognitiveSkill,
        QualityStatus = question.QualityStatus,
        LicenseStatus = question.LicenseStatus,
        SourceOrigin = question.SourceOrigin,
        SourceTitle = question.SourceTitle,
        SourceUrl = question.SourceUrl,
        Explanation = stripAnswerKey ? string.Empty : question.Explanation ?? string.Empty,
        CreatedAt = question.CreatedAt,
        UpdatedAt = question.UpdatedAt,
        Options = question.Options
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.OptionKey)
            .Select(o => new QuestionOptionDto
            {
                Id = o.Id,
                OptionKey = o.OptionKey,
                Text = o.Text,
                IsCorrect = stripAnswerKey ? false : o.IsCorrect,
                Rationale = stripAnswerKey ? null : o.Rationale,
                MisconceptionKey = stripAnswerKey ? null : o.MisconceptionKey,
                DiagnosticSignalJson = stripAnswerKey ? null : LearnerSafeContentJson.SanitizeAssessmentMetadata(o.DiagnosticSignalJson),
                SortOrder = o.SortOrder,
                ContentBlocks = o.ContentBlocks
                    .Where(b => !b.IsDeleted)
                    .OrderBy(b => b.SortOrder)
                    .Select(block => ToDto(block, stripAnswerKey))
                    .ToList()
            })
            .ToList(),
        Explanations = question.Explanations
            .Where(e => !e.IsDeleted)
            .Where(e => !stripAnswerKey || e.IsSafeForLearners)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new QuestionExplanationDto
            {
                Id = e.Id,
                ExplanationText = e.ExplanationText,
                SourceTitle = e.SourceTitle,
                SourceUrl = e.SourceUrl,
                Visibility = e.Visibility,
                IsSafeForLearners = e.IsSafeForLearners
            })
            .ToList(),
        Tags = question.Tags
            .OrderBy(t => t.Tag)
            .Select(t => new QuestionTagDto { Id = t.Id, Tag = t.Tag })
            .ToList(),
        OutcomeLinks = question.OutcomeLinks
            .Where(l => !l.IsDeleted)
            .OrderByDescending(l => l.IsPrimary)
            .ThenByDescending(l => l.LinkStrength)
            .Select(l => new QuestionOutcomeLinkDto
            {
                Id = l.Id,
                ExamOutcomeId = l.ExamOutcomeId,
                IsPrimary = l.IsPrimary,
                LinkStrength = l.LinkStrength
            })
            .ToList(),
        ContentBlocks = question.ContentBlocks
            .Where(b => !b.IsDeleted)
            .OrderBy(b => b.SortOrder)
            .Select(block => ToDto(block, stripAnswerKey))
            .ToList(),
        Stimuli = question.StimulusLinks
            .Where(l => !l.QuestionStimulus.IsDeleted)
            .OrderBy(l => l.SortOrder)
            .Select(l => ToDto(l.QuestionStimulus, l.SortOrder, stripAnswerKey))
            .ToList(),
        Validation = stripAnswerKey
            ? new QuestionValidationResultDto()
            : ValidateQuestion(question, forPublish: question.QualityStatus == "approved")
    };

    private static QuestionContentBlockDto ToDto(QuestionContentBlock block, bool stripAnswerKey = false) => new()
    {
        Id = block.Id,
        BlockType = block.BlockType,
        Text = block.Text,
        ContentJson = stripAnswerKey ? LearnerSafeContentJson.Sanitize(block.ContentJson) : block.ContentJson,
        AssetId = block.AssetId,
        Asset = block.Asset is null || block.Asset.IsDeleted ? null : ToDto(block.Asset),
        SortOrder = block.SortOrder,
        AltText = block.AltText,
        Caption = block.Caption,
        LongDescription = block.LongDescription
    };

    private static QuestionOptionContentBlockDto ToDto(QuestionOptionContentBlock block, bool stripAnswerKey = false) => new()
    {
        Id = block.Id,
        BlockType = block.BlockType,
        Text = block.Text,
        ContentJson = stripAnswerKey ? LearnerSafeContentJson.Sanitize(block.ContentJson) : block.ContentJson,
        AssetId = block.AssetId,
        Asset = block.Asset is null || block.Asset.IsDeleted ? null : ToDto(block.Asset),
        SortOrder = block.SortOrder,
        AltText = block.AltText,
        Caption = block.Caption
    };

    private static QuestionAssetDto ToDto(QuestionAsset asset) => new()
    {
        Id = asset.Id,
        OwnershipState = asset.OwnerUserId is null ? "system" : "user",
        AssetType = asset.AssetType,
        StorageKey = asset.StorageKey,
        FileName = asset.FileName,
        MimeType = asset.MimeType,
        SizeBytes = asset.SizeBytes,
        Sha256Hash = asset.Sha256Hash,
        SourceRegistryItemId = asset.SourceRegistryItemId,
        SourceTitle = asset.SourceTitle,
        SourceUrl = asset.SourceUrl,
        LicenseStatus = asset.LicenseStatus,
        VerificationStatus = asset.VerificationStatus,
        AltText = asset.AltText,
        Caption = asset.Caption,
        LongDescription = asset.LongDescription,
        GenerationProvider = asset.GenerationProvider,
        GenerationModel = asset.GenerationModel,
        RenderStrategy = asset.RenderStrategy,
        GenerationPromptHash = asset.GenerationPromptHash,
        ValidationReportJson = LearnerSafeContentJson.Sanitize(asset.ValidationReportJson),
        VisualReadinessStatus = asset.VisualReadinessStatus,
        CreatedAt = asset.CreatedAt,
        UpdatedAt = asset.UpdatedAt
    };

    private static QuestionStimulusDto ToDto(QuestionStimulus stimulus, int sortOrder, bool stripAnswerKey = false) => new()
    {
        Id = stimulus.Id,
        OwnershipState = stimulus.OwnerUserId is null ? "system" : "user",
        Title = stimulus.Title,
        StimulusType = stimulus.StimulusType,
        ContentText = stimulus.ContentText,
        ContentJson = stripAnswerKey ? LearnerSafeContentJson.Sanitize(stimulus.ContentJson) : stimulus.ContentJson,
        SourceRegistryItemId = stimulus.SourceRegistryItemId,
        CurriculumNodeId = stimulus.CurriculumNodeId,
        VerificationStatus = stimulus.VerificationStatus,
        LicenseStatus = stimulus.LicenseStatus,
        SortOrder = sortOrder,
        CreatedAt = stimulus.CreatedAt,
        UpdatedAt = stimulus.UpdatedAt
    };

    private static DiagnosticQuestionProjection ParseGeneratedDiagnosticQuestion(AssessmentItem item)
    {
        if (string.IsNullOrWhiteSpace(item.GeneratedQuestionJson))
        {
            return new DiagnosticQuestionProjection(FallbackDiagnosticStem(item), []);
        }

        try
        {
            var node = JsonNode.Parse(item.GeneratedQuestionJson);
            PublicTextNormalizer.RepairJsonStrings(node);

            var question = node switch
            {
                JsonObject obj => obj,
                JsonArray array => array.OfType<JsonObject>().FirstOrDefault(),
                _ => null
            };

            if (question is null)
            {
                return new DiagnosticQuestionProjection(FallbackDiagnosticStem(item), []);
            }

            var stem = FirstJsonString(question, "question", "stem", "prompt", "title");
            var options = ParseDiagnosticOptions(question["options"] as JsonArray);
            return new DiagnosticQuestionProjection(Clean(stem, FallbackDiagnosticStem(item)), options);
        }
        catch
        {
            return new DiagnosticQuestionProjection(FallbackDiagnosticStem(item), []);
        }
    }

    private static List<QuestionOptionDto> ParseDiagnosticOptions(JsonArray? options)
    {
        if (options is null || options.Count == 0)
        {
            return [];
        }

        var parsed = new List<QuestionOptionDto>();
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var fallbackKey = ((char)('A' + Math.Min(parsed.Count, 25))).ToString();
            var key = fallbackKey;
            var text = string.Empty;

            if (option is JsonValue)
            {
                text = option.GetValue<string?>() ?? string.Empty;
            }
            else if (option is JsonObject obj)
            {
                key = FirstJsonString(obj, "id", "optionId", "key", "label", "value") ?? fallbackKey;
                text = FirstJsonString(obj, "text", "value", "label", "option", "id") ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            parsed.Add(new QuestionOptionDto
            {
                OptionKey = Clean(key, fallbackKey),
                Text = Clean(text),
                IsCorrect = false,
                SortOrder = parsed.Count
            });
        }

        return parsed;
    }

    private static string? FirstJsonString(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!obj.TryGetPropertyValue(key, out var node) || node is null)
            {
                continue;
            }

            if (node is JsonValue value)
            {
                try
                {
                    var text = value.GetValue<string?>();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
                catch
                {
                    var text = value.ToJsonString(BankJsonOptions).Trim('"');
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            var serialized = node.ToJsonString(BankJsonOptions);
            if (!string.IsNullOrWhiteSpace(serialized) && serialized != "null")
            {
                return serialized;
            }
        }

        return null;
    }

    private static string FallbackDiagnosticStem(AssessmentItem item)
    {
        var concept = !string.IsNullOrWhiteSpace(item.ConceptLabel)
            ? item.ConceptLabel
            : !string.IsNullOrWhiteSpace(item.ConceptKey)
                ? item.ConceptKey
                : "this concept";

        return $"Diagnostic check for {concept}.";
    }

    private static string NormalizeDiagnosticQuestionType(string? value, int optionCount)
    {
        if (optionCount >= 2)
        {
            return "multiple_choice";
        }

        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant()
            .Replace("-", "_")
            .Replace(" ", "_");

        return AllowedQuestionTypes.Contains(normalized) ? normalized : "paragraph";
    }

    private static string NormalizeDiagnosticDifficulty(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "easy" or "kolay" or "foundation" or "foundational" or "beginner" or "baseline" or "intro")
        {
            return "easy";
        }

        if (normalized is "hard" or "zor" or "advanced" or "challenge" or "challenging" or "expert")
        {
            return "hard";
        }

        return "medium";
    }

    private static string NormalizeBounded(string? value, HashSet<string> allowed, string fallback)
    {
        var normalized = CleanOptional(value).Trim().ToLowerInvariant();
        return allowed.Contains(normalized) ? normalized : fallback;
    }

    private static string Clean(string? value, string fallback = "") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string CleanOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string? SafeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SafeContentJson(string? value) =>
        LearnerSafeContentJson.Sanitize(value);

    private static string? SafeAssessmentMetadataJson(string? value) =>
        LearnerSafeContentJson.SanitizeAssessmentMetadata(value);

    private static bool IsValidOptionalUrl(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
               || Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private static bool IsSafeStorageKey(string value)
    {
        var cleaned = value.Trim();
        return !string.IsNullOrWhiteSpace(cleaned)
               && !cleaned.Contains("..", StringComparison.Ordinal)
               && !cleaned.Contains(@":\", StringComparison.Ordinal)
               && !cleaned.StartsWith("\\", StringComparison.Ordinal)
               && !cleaned.StartsWith("/", StringComparison.Ordinal);
    }

    private async Task<QuestionProfessionalBinding> ResolveQuestionBindingAsync(
        Guid userId,
        Guid? learningTopicId,
        Guid? conceptGraphSnapshotId,
        Guid? learningConceptId,
        Guid? assessmentItemId,
        string? conceptKey,
        string? conceptLabel,
        string? misconceptionTarget,
        string? evidenceExpected,
        string? scoringRuleJson,
        Guid? quizRunId,
        Guid? planRequestId,
        CancellationToken ct)
    {
        if (learningTopicId.HasValue)
        {
            var topicExists = await _db.Topics
                .AsNoTracking()
                .AnyAsync(t => t.Id == learningTopicId.Value && t.UserId == userId, ct);
            if (!topicExists)
            {
                throw new ArgumentException("Learning topic is not visible for this user.");
            }
        }

        AssessmentItem? assessmentItem = null;
        if (assessmentItemId.HasValue)
        {
            assessmentItem = await _db.AssessmentItems
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == assessmentItemId.Value && item.UserId == userId, ct);
            if (assessmentItem is null)
            {
                throw new ArgumentException("Assessment item is not visible for this user.");
            }

            learningTopicId = EnsureSameGuid(learningTopicId, assessmentItem.TopicId, "learningTopicId");
            conceptGraphSnapshotId = EnsureSameGuid(conceptGraphSnapshotId, assessmentItem.ConceptGraphSnapshotId, "conceptGraphSnapshotId");
            learningConceptId = EnsureSameGuid(learningConceptId, assessmentItem.LearningConceptId, "learningConceptId");
            quizRunId = EnsureSameGuid(quizRunId, assessmentItem.QuizRunId, "quizRunId");
            planRequestId = EnsureSameGuid(planRequestId, assessmentItem.PlanRequestId, "planRequestId");
            conceptKey = FirstNonEmpty(conceptKey, assessmentItem.ConceptKey);
            conceptLabel = FirstNonEmpty(conceptLabel, assessmentItem.ConceptLabel);
            misconceptionTarget = FirstNonEmpty(misconceptionTarget, assessmentItem.MisconceptionTarget);
            evidenceExpected = FirstNonEmpty(evidenceExpected, assessmentItem.EvidenceExpected);
            scoringRuleJson = FirstNonEmpty(scoringRuleJson, assessmentItem.ScoringRuleJson);
        }

        if (conceptGraphSnapshotId.HasValue)
        {
            var snapshot = await _db.ConceptGraphSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == conceptGraphSnapshotId.Value && s.UserId == userId, ct);
            if (snapshot is null)
            {
                throw new ArgumentException("Concept graph snapshot is not visible for this user.");
            }

            learningTopicId = EnsureSameGuid(learningTopicId, snapshot.TopicId, "learningTopicId");
        }

        if (learningConceptId.HasValue)
        {
            var concept = await _db.LearningConcepts
                .AsNoTracking()
                .Where(c => c.Id == learningConceptId.Value)
                .Join(
                    _db.ConceptGraphSnapshots.AsNoTracking().Where(s => s.UserId == userId),
                    concept => concept.ConceptGraphSnapshotId,
                    snapshot => snapshot.Id,
                    (concept, snapshot) => new { Concept = concept, Snapshot = snapshot })
                .FirstOrDefaultAsync(ct);
            if (concept is null)
            {
                throw new ArgumentException("Learning concept is not visible for this user.");
            }

            conceptGraphSnapshotId = EnsureSameGuid(conceptGraphSnapshotId, concept.Concept.ConceptGraphSnapshotId, "conceptGraphSnapshotId");
            learningTopicId = EnsureSameGuid(learningTopicId, concept.Snapshot.TopicId, "learningTopicId");
            conceptKey = FirstNonEmpty(conceptKey, concept.Concept.StableKey);
            conceptLabel = FirstNonEmpty(conceptLabel, concept.Concept.Label);
        }

        return new QuestionProfessionalBinding(
            learningTopicId,
            conceptGraphSnapshotId,
            learningConceptId,
            assessmentItemId,
            quizRunId,
            planRequestId,
            SafeOptional(conceptKey),
            SafeOptional(conceptLabel),
            SafeOptional(misconceptionTarget),
            SafeOptional(evidenceExpected),
            SafeAssessmentMetadataJson(scoringRuleJson));
    }

    private static Guid? EnsureSameGuid(Guid? existing, Guid? candidate, string fieldName)
    {
        if (!candidate.HasValue)
        {
            return existing;
        }

        if (existing.HasValue && existing.Value != candidate.Value)
        {
            throw new ArgumentException($"Question professional binding has conflicting {fieldName}.");
        }

        return candidate;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record QuestionProfessionalBinding(
        Guid? LearningTopicId,
        Guid? ConceptGraphSnapshotId,
        Guid? LearningConceptId,
        Guid? AssessmentItemId,
        Guid? QuizRunId,
        Guid? PlanRequestId,
        string? ConceptKey,
        string? ConceptLabel,
        string? MisconceptionTarget,
        string? EvidenceExpected,
        string? ScoringRuleJson);

    private sealed record ResolvedExamLinks(
        Guid ExamDefinitionId,
        Guid? ExamVariantId,
        Guid? ExamSectionId,
        Guid? ExamSubjectId,
        Guid? ExamTopicId,
        Guid? ExamOutcomeId,
        IReadOnlyList<QuestionOutcomeLinkDto> OutcomeLinks);

    private sealed record DiagnosticQuestionProjection(string Stem, IReadOnlyList<QuestionOptionDto> Options);
}

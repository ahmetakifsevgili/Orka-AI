using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class QuestionBankService : IQuestionBankService
{
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

        if (!string.IsNullOrWhiteSpace(filters.QuestionType))
        {
            var questionType = NormalizeBounded(filters.QuestionType, AllowedQuestionTypes, "multiple_choice");
            query = query.Where(q => q.QuestionType == questionType);
        }

        if (!string.IsNullOrWhiteSpace(filters.Difficulty))
        {
            var difficulty = NormalizeBounded(filters.Difficulty, AllowedDifficulties, "medium");
            query = query.Where(q => q.Difficulty == difficulty);
        }

        var questions = await query
            .OrderByDescending(q => q.OwnerUserId == userId)
            .ThenByDescending(q => q.UpdatedAt)
            .ThenBy(q => q.Id)
            .Take(take)
            .ToListAsync(ct);

        return questions.Select(ToDto).ToList();
    }

    public async Task<QuestionItemDto?> GetQuestionAsync(Guid userId, Guid questionId, CancellationToken ct = default)
    {
        var question = await VisibleQuestions(userId).FirstOrDefaultAsync(q => q.Id == questionId, ct);
        return question is null ? null : ToDto(question);
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

        question.ExamVariantId = links.ExamVariantId;
        question.ExamSectionId = links.ExamSectionId;
        question.ExamSubjectId = links.ExamSubjectId;
        question.ExamTopicId = links.ExamTopicId;
        question.ExamOutcomeId = links.ExamOutcomeId;
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
            ContentJson = SafeOptional(request.ContentJson),
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
            .Where(q => q.Id == questionId && !q.IsDeleted && q.OwnerUserId == userId);

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
            ContentJson = SafeOptional(request.ContentJson),
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
            ContentJson = SafeOptional(request.ContentJson),
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

    private static QuestionItemDto ToDto(QuestionItem question) => new()
    {
        Id = question.Id,
        OwnershipState = question.OwnerUserId is null ? "system" : "user",
        ExamDefinitionId = question.ExamDefinitionId,
        ExamVariantId = question.ExamVariantId,
        ExamSectionId = question.ExamSectionId,
        ExamSubjectId = question.ExamSubjectId,
        ExamTopicId = question.ExamTopicId,
        ExamOutcomeId = question.ExamOutcomeId,
        QuestionType = question.QuestionType,
        Stem = question.Stem,
        Difficulty = question.Difficulty,
        CognitiveSkill = question.CognitiveSkill,
        QualityStatus = question.QualityStatus,
        LicenseStatus = question.LicenseStatus,
        SourceOrigin = question.SourceOrigin,
        SourceTitle = question.SourceTitle,
        SourceUrl = question.SourceUrl,
        Explanation = question.Explanation,
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
                IsCorrect = o.IsCorrect,
                SortOrder = o.SortOrder,
                ContentBlocks = o.ContentBlocks
                    .Where(b => !b.IsDeleted)
                    .OrderBy(b => b.SortOrder)
                    .Select(ToDto)
                    .ToList()
            })
            .ToList(),
        Explanations = question.Explanations
            .Where(e => !e.IsDeleted)
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
            .Select(ToDto)
            .ToList(),
        Stimuli = question.StimulusLinks
            .Where(l => !l.QuestionStimulus.IsDeleted)
            .OrderBy(l => l.SortOrder)
            .Select(l => ToDto(l.QuestionStimulus, l.SortOrder))
            .ToList(),
        Validation = ValidateQuestion(question, forPublish: question.QualityStatus == "approved")
    };

    private static QuestionContentBlockDto ToDto(QuestionContentBlock block) => new()
    {
        Id = block.Id,
        BlockType = block.BlockType,
        Text = block.Text,
        ContentJson = block.ContentJson,
        AssetId = block.AssetId,
        Asset = block.Asset is null || block.Asset.IsDeleted ? null : ToDto(block.Asset),
        SortOrder = block.SortOrder,
        AltText = block.AltText,
        Caption = block.Caption,
        LongDescription = block.LongDescription
    };

    private static QuestionOptionContentBlockDto ToDto(QuestionOptionContentBlock block) => new()
    {
        Id = block.Id,
        BlockType = block.BlockType,
        Text = block.Text,
        ContentJson = block.ContentJson,
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
        CreatedAt = asset.CreatedAt,
        UpdatedAt = asset.UpdatedAt
    };

    private static QuestionStimulusDto ToDto(QuestionStimulus stimulus, int sortOrder) => new()
    {
        Id = stimulus.Id,
        OwnershipState = stimulus.OwnerUserId is null ? "system" : "user",
        Title = stimulus.Title,
        StimulusType = stimulus.StimulusType,
        ContentText = stimulus.ContentText,
        ContentJson = stimulus.ContentJson,
        SourceRegistryItemId = stimulus.SourceRegistryItemId,
        CurriculumNodeId = stimulus.CurriculumNodeId,
        VerificationStatus = stimulus.VerificationStatus,
        LicenseStatus = stimulus.LicenseStatus,
        SortOrder = sortOrder,
        CreatedAt = stimulus.CreatedAt,
        UpdatedAt = stimulus.UpdatedAt
    };

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

    private sealed record ResolvedExamLinks(
        Guid ExamDefinitionId,
        Guid? ExamVariantId,
        Guid? ExamSectionId,
        Guid? ExamSubjectId,
        Guid? ExamTopicId,
        Guid? ExamOutcomeId,
        IReadOnlyList<QuestionOutcomeLinkDto> OutcomeLinks);
}

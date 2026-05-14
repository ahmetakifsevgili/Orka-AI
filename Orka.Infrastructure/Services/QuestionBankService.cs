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

    private IQueryable<QuestionItem> VisibleQuestions(Guid userId) =>
        _db.QuestionItems
            .AsNoTracking()
            .Include(q => q.Options)
            .Include(q => q.Explanations)
            .Include(q => q.Tags)
            .Include(q => q.OutcomeLinks)
            .Where(q => !q.IsDeleted && (q.OwnerUserId == null || q.OwnerUserId == userId));

    private IQueryable<QuestionItem> OwnedQuestionForMutation(Guid userId, Guid questionId) =>
        _db.QuestionItems
            .Include(q => q.Options)
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

    private static QuestionValidationResultDto ValidateQuestion(QuestionItem question, bool forPublish)
    {
        var result = new QuestionValidationResultDto();

        if (string.IsNullOrWhiteSpace(question.Stem))
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

            if (activeOptions.Any(o => string.IsNullOrWhiteSpace(o.OptionKey) || string.IsNullOrWhiteSpace(o.Text)))
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
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
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
                SortOrder = o.SortOrder
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
        Validation = ValidateQuestion(question, forPublish: question.QualityStatus == "approved")
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

    private sealed record ResolvedExamLinks(
        Guid ExamDefinitionId,
        Guid? ExamVariantId,
        Guid? ExamSectionId,
        Guid? ExamSubjectId,
        Guid? ExamTopicId,
        Guid? ExamOutcomeId,
        IReadOnlyList<QuestionOutcomeLinkDto> OutcomeLinks);
}

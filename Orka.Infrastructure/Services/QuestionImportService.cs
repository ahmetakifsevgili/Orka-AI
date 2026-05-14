using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed partial class QuestionImportService : IQuestionImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PreviewTtl = TimeSpan.FromHours(24);

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

    private static readonly HashSet<string> AllowedLicenseStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "unknown",
        "user_provided",
        "licensed",
        "open",
        "restricted"
    };

    private static readonly HashSet<string> SafeReviewLicenseStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "user_provided",
        "licensed",
        "open"
    };

    private readonly OrkaDbContext _db;
    private readonly IQuestionBankService _questionBank;

    public QuestionImportService(OrkaDbContext db, IQuestionBankService questionBank)
    {
        _db = db;
        _questionBank = questionBank;
    }

    public async Task<QuestionImportPreviewDto> PreviewImportAsync(
        Guid userId,
        QuestionImportRequestDto request,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var preview = new QuestionImportPreview
        {
            OwnerUserId = userId,
            Status = "pending",
            CreatedAt = now,
            ExpiresAt = now.Add(PreviewTtl)
        };

        var externalIds = request.Items
            .Select(i => CleanOptional(i.ExternalId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            var issues = new List<QuestionImportValidationIssueDto>();
            var externalId = CleanOptional(item.ExternalId);

            if (!string.IsNullOrWhiteSpace(externalId) && externalIds.Contains(externalId))
            {
                issues.Add(Error("duplicate_external_id", "Aynı externalId bu import içinde birden fazla kez kullanılmış."));
            }

            var normalizedQuestion = await NormalizeItemAsync(userId, item, issues, ct);
            Guid? duplicateQuestionId = null;
            if (normalizedQuestion is not null)
            {
                duplicateQuestionId = await FindDuplicateQuestionAsync(userId, normalizedQuestion, ct);
                if (duplicateQuestionId is not null)
                {
                    issues.Add(Error("duplicate_existing_question", "Bu soru aynı sınav/kazanım kapsamındaki mevcut soru bankasında zaten var."));
                }
            }

            var hasErrors = issues.Any(i => i.Severity == "error");
            var status = duplicateQuestionId is not null
                ? "duplicate"
                : hasErrors
                    ? "rejected"
                    : "accepted";

            preview.Items.Add(new QuestionImportPreviewItem
            {
                RowIndex = index,
                ExternalId = externalId,
                Status = status,
                IssuesJson = JsonSerializer.Serialize(issues, JsonOptions),
                NormalizedQuestionJson = status == "accepted" && normalizedQuestion is not null
                    ? JsonSerializer.Serialize(normalizedQuestion, JsonOptions)
                    : null,
                DuplicateQuestionId = duplicateQuestionId,
                CreatedAt = now
            });
        }

        RecalculateCounts(preview);
        _db.QuestionImportPreviews.Add(preview);
        await _db.SaveChangesAsync(ct);

        return ToDto(preview);
    }

    public async Task<QuestionImportResultDto> ApproveImportAsync(
        Guid userId,
        QuestionImportApprovalDto request,
        CancellationToken ct = default)
    {
        var preview = await _db.QuestionImportPreviews
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == request.ImportPreviewId
                                      && p.OwnerUserId == userId
                                      && !p.IsDeleted, ct);

        if (preview is null)
        {
            return InvalidResult(request.ImportPreviewId, "preview_not_found", "Import önizlemesi bulunamadı.");
        }

        if (preview.Status == "approved")
        {
            return ToResult(preview, "approved");
        }

        if (preview.Status != "pending")
        {
            return InvalidResult(preview.Id, "preview_not_pending", "Import önizlemesi onaylanabilir durumda değil.");
        }

        if (preview.ExpiresAt <= DateTime.UtcNow)
        {
            preview.Status = "expired";
            await _db.SaveChangesAsync(ct);
            return InvalidResult(preview.Id, "preview_expired", "Import önizlemesinin süresi dolmuş.");
        }

        foreach (var item in preview.Items.Where(i => !i.IsDeleted && i.Status == "accepted").OrderBy(i => i.RowIndex))
        {
            if (item.CreatedQuestionId is not null)
            {
                continue;
            }

            var normalized = DeserializeQuestion(item.NormalizedQuestionJson);
            if (normalized is null)
            {
                item.Status = "rejected";
                item.IssuesJson = JsonSerializer.Serialize(new[] { Error("normalized_payload_missing", "Normalize edilmiş soru verisi bulunamadı.") }, JsonOptions);
                continue;
            }

            try
            {
                var created = await _questionBank.CreateQuestionAsync(userId, normalized, ct);
                item.CreatedQuestionId = created.Id;

                if (SafeReviewLicenseStatuses.Contains(normalized.LicenseStatus))
                {
                    await _questionBank.SubmitForReviewAsync(userId, created.Id, ct);
                }
            }
            catch (ArgumentException ex)
            {
                item.Status = "rejected";
                item.IssuesJson = JsonSerializer.Serialize(new[] { Error("question_bank_validation_failed", ex.Message) }, JsonOptions);
            }
        }

        preview.Status = "approved";
        preview.ApprovedAt = DateTime.UtcNow;
        RecalculateCounts(preview);
        await _db.SaveChangesAsync(ct);

        return ToResult(preview, "approved");
    }

    public async Task<QuestionImportPreviewDto?> GetImportPreviewAsync(
        Guid userId,
        Guid importPreviewId,
        CancellationToken ct = default)
    {
        var preview = await _db.QuestionImportPreviews
            .AsNoTracking()
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == importPreviewId
                                      && p.OwnerUserId == userId
                                      && !p.IsDeleted, ct);

        return preview is null ? null : ToDto(preview);
    }

    private async Task<CreateQuestionDto?> NormalizeItemAsync(
        Guid userId,
        QuestionImportItemDto item,
        List<QuestionImportValidationIssueDto> issues,
        CancellationToken ct)
    {
        var questionType = NormalizeBounded(item.QuestionType, AllowedQuestionTypes, "multiple_choice");
        if (!AllowedQuestionTypes.Contains(CleanOptional(item.QuestionType) ?? string.Empty))
        {
            issues.Add(Error("unsupported_question_type", "Desteklenmeyen soru tipi."));
        }

        var source = item.Source;
        var licenseStatus = NormalizeBounded(source?.LicenseStatus ?? item.LicenseStatus, AllowedLicenseStatuses, "unknown");
        if (!AllowedLicenseStatuses.Contains(CleanOptional(source?.LicenseStatus ?? item.LicenseStatus) ?? string.Empty))
        {
            issues.Add(Warning("unsupported_license_normalized", "Lisans durumu bilinmiyor olarak işaretlendi."));
        }

        if (!SafeReviewLicenseStatuses.Contains(licenseStatus))
        {
            issues.Add(Warning("unsafe_license_imported_as_draft", "Kaynak/lisans güvenli değil; soru sadece taslak olarak içe aktarılır."));
        }

        var sourceUrl = SafeOptional(source?.SourceUrl ?? item.SourceUrl);
        if (!string.IsNullOrWhiteSpace(sourceUrl) && !Uri.TryCreate(sourceUrl, UriKind.Absolute, out _))
        {
            issues.Add(Error("source_url_invalid", "Kaynak URL geçerli değil."));
        }

        var links = await ResolveExamLinksAsync(userId, item, issues, ct);
        if (links is null)
        {
            return null;
        }

        var stem = Clean(item.Stem);
        if (string.IsNullOrWhiteSpace(stem))
        {
            issues.Add(Error("question_stem_required", "Soru kökü boş olamaz."));
        }

        var options = NormalizeOptions(item.Options, issues);
        if (questionType == "multiple_choice")
        {
            if (options.Count < 2)
            {
                issues.Add(Error("multiple_choice_minimum_two_options", "Çoktan seçmeli soru en az iki seçenek içermeli."));
            }

            if (options.Any(o => string.IsNullOrWhiteSpace(o.OptionKey) || string.IsNullOrWhiteSpace(o.Text)))
            {
                issues.Add(Error("multiple_choice_option_text_required", "Seçenek anahtarı ve metni boş olamaz."));
            }

            var correctCount = options.Count(o => o.IsCorrect);
            if (correctCount == 0)
            {
                issues.Add(Error("multiple_choice_correct_option_required", "Çoktan seçmeli soruda bir doğru seçenek olmalı."));
            }
            else if (correctCount > 1)
            {
                issues.Add(Error("multiple_choice_single_correct_option_required", "Şimdilik yalnızca tek doğru seçenek desteklenir."));
            }
        }

        var tags = item.Tags
            .Select(t => CleanOptional(t))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => new QuestionTagDto { Tag = t! })
            .ToList();

        return new CreateQuestionDto
        {
            ExamDefinitionId = links.ExamDefinitionId,
            ExamVariantId = links.ExamVariantId,
            ExamSectionId = links.ExamSectionId,
            ExamSubjectId = links.ExamSubjectId,
            ExamTopicId = links.ExamTopicId,
            ExamOutcomeId = links.ExamOutcomeId,
            QuestionType = questionType,
            Stem = stem,
            Difficulty = NormalizeBounded(item.Difficulty, AllowedDifficulties, "medium"),
            CognitiveSkill = Clean(item.CognitiveSkill, "conceptual"),
            LicenseStatus = licenseStatus,
            SourceOrigin = Clean(source?.SourceOrigin ?? item.SourceOrigin, "structured_json"),
            SourceTitle = SafeOptional(source?.SourceTitle ?? item.SourceTitle),
            SourceUrl = sourceUrl,
            Explanation = CleanOptional(item.Explanation),
            Options = options,
            Explanations = string.IsNullOrWhiteSpace(item.Explanation)
                ? []
                : [new QuestionExplanationDto { ExplanationText = Clean(item.Explanation), Visibility = "authoring", IsSafeForLearners = true }],
            Tags = tags,
            OutcomeLinks = links.ExamOutcomeId is null
                ? []
                : [new QuestionOutcomeLinkDto { ExamOutcomeId = links.ExamOutcomeId.Value, IsPrimary = true, LinkStrength = 1.0m }]
        };
    }

    private async Task<ResolvedImportLinks?> ResolveExamLinksAsync(
        Guid userId,
        QuestionImportItemDto item,
        List<QuestionImportValidationIssueDto> issues,
        CancellationToken ct)
    {
        ExamDefinition? definition = null;
        if (item.ExamDefinitionId is { } definitionId && definitionId != Guid.Empty)
        {
            definition = await _db.ExamDefinitions.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == definitionId
                                          && !d.IsDeleted
                                          && (d.OwnerUserId == null || d.OwnerUserId == userId), ct);
            if (definition is null)
            {
                issues.Add(Error("exam_definition_not_visible", "Sınav tanımı görünür değil."));
                return null;
            }
        }
        else
        {
            var examCode = NormalizeCode(item.ExamCode);
            if (string.IsNullOrWhiteSpace(examCode))
            {
                issues.Add(Error("exam_code_required", "Sınav kodu zorunlu."));
                return null;
            }

            var definitions = await _db.ExamDefinitions.AsNoTracking()
                .Where(d => d.Code == examCode
                            && !d.IsDeleted
                            && (d.OwnerUserId == null || d.OwnerUserId == userId))
                .ToListAsync(ct);
            if (definitions.Count != 1)
            {
                issues.Add(Error(definitions.Count == 0 ? "exam_definition_not_visible" : "exam_definition_ambiguous", "Sınav tanımı tekil ve görünür olmalı."));
                return null;
            }

            definition = definitions[0];
        }

        var variantId = await ResolveVariantAsync(definition.Id, item, issues, ct);
        var sectionId = await ResolveSectionAsync(definition.Id, variantId, item, issues, ct);
        var subjectId = await ResolveSubjectAsync(definition.Id, sectionId, item, issues, ct);
        var topicId = await ResolveTopicAsync(definition.Id, subjectId, item, issues, ct);
        var outcomeId = await ResolveOutcomeAsync(definition.Id, topicId, item, issues, ct);

        return issues.Any(i => i.Severity == "error" && i.Code.StartsWith("exam_", StringComparison.OrdinalIgnoreCase))
            ? null
            : new ResolvedImportLinks(definition.Id, variantId, sectionId, subjectId, topicId, outcomeId);
    }

    private async Task<Guid?> ResolveVariantAsync(Guid definitionId, QuestionImportItemDto item, List<QuestionImportValidationIssueDto> issues, CancellationToken ct)
    {
        if (item.ExamVariantId is { } id && id != Guid.Empty)
        {
            var ok = await _db.ExamVariants.AsNoTracking().AnyAsync(v => v.Id == id && v.ExamDefinitionId == definitionId && !v.IsDeleted, ct);
            if (!ok) issues.Add(Error("exam_variant_not_visible", "Sınav varyantı görünür değil."));
            return id;
        }

        var code = NormalizeCode(item.VariantCode);
        if (string.IsNullOrWhiteSpace(code)) return null;
        var matches = await _db.ExamVariants.AsNoTracking().Where(v => v.ExamDefinitionId == definitionId && v.Code == code && !v.IsDeleted).ToListAsync(ct);
        if (matches.Count != 1) issues.Add(Error(matches.Count == 0 ? "exam_variant_not_visible" : "exam_variant_ambiguous", "Sınav varyantı tekil ve görünür olmalı."));
        return matches.Count == 1 ? matches[0].Id : null;
    }

    private async Task<Guid?> ResolveSectionAsync(Guid definitionId, Guid? variantId, QuestionImportItemDto item, List<QuestionImportValidationIssueDto> issues, CancellationToken ct)
    {
        if (item.ExamSectionId is { } id && id != Guid.Empty)
        {
            var section = await _db.ExamSections.AsNoTracking().Include(s => s.ExamVariant).FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct);
            if (section is null || section.ExamVariant.ExamDefinitionId != definitionId || (variantId is not null && section.ExamVariantId != variantId))
            {
                issues.Add(Error("exam_section_not_visible", "Sınav bölümü görünür değil."));
            }
            return id;
        }

        var code = NormalizeCode(item.SectionCode);
        if (string.IsNullOrWhiteSpace(code)) return null;
        var query = _db.ExamSections.AsNoTracking().Include(s => s.ExamVariant)
            .Where(s => s.Code == code && !s.IsDeleted && s.ExamVariant.ExamDefinitionId == definitionId);
        if (variantId is not null) query = query.Where(s => s.ExamVariantId == variantId);
        var matches = await query.ToListAsync(ct);
        if (matches.Count != 1) issues.Add(Error(matches.Count == 0 ? "exam_section_not_visible" : "exam_section_ambiguous", "Sınav bölümü tekil ve görünür olmalı."));
        return matches.Count == 1 ? matches[0].Id : null;
    }

    private async Task<Guid?> ResolveSubjectAsync(Guid definitionId, Guid? sectionId, QuestionImportItemDto item, List<QuestionImportValidationIssueDto> issues, CancellationToken ct)
    {
        if (item.ExamSubjectId is { } id && id != Guid.Empty)
        {
            var subject = await _db.ExamSubjects.AsNoTracking()
                .Include(s => s.ExamSection).ThenInclude(s => s.ExamVariant)
                .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct);
            if (subject is null || subject.ExamSection.ExamVariant.ExamDefinitionId != definitionId || (sectionId is not null && subject.ExamSectionId != sectionId))
            {
                issues.Add(Error("exam_subject_not_visible", "Sınav dersi görünür değil."));
            }
            return id;
        }

        var code = NormalizeCode(item.SubjectCode);
        if (string.IsNullOrWhiteSpace(code)) return null;
        var query = _db.ExamSubjects.AsNoTracking()
            .Include(s => s.ExamSection).ThenInclude(s => s.ExamVariant)
            .Where(s => s.Code == code && !s.IsDeleted && s.ExamSection.ExamVariant.ExamDefinitionId == definitionId);
        if (sectionId is not null) query = query.Where(s => s.ExamSectionId == sectionId);
        var matches = await query.ToListAsync(ct);
        if (matches.Count != 1) issues.Add(Error(matches.Count == 0 ? "exam_subject_not_visible" : "exam_subject_ambiguous", "Sınav dersi tekil ve görünür olmalı."));
        return matches.Count == 1 ? matches[0].Id : null;
    }

    private async Task<Guid?> ResolveTopicAsync(Guid definitionId, Guid? subjectId, QuestionImportItemDto item, List<QuestionImportValidationIssueDto> issues, CancellationToken ct)
    {
        if (item.ExamTopicId is { } id && id != Guid.Empty)
        {
            var topic = await _db.ExamTopics.AsNoTracking()
                .Include(t => t.ExamSubject).ThenInclude(s => s.ExamSection).ThenInclude(s => s.ExamVariant)
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct);
            if (topic is null || topic.ExamSubject.ExamSection.ExamVariant.ExamDefinitionId != definitionId || (subjectId is not null && topic.ExamSubjectId != subjectId))
            {
                issues.Add(Error("exam_topic_not_visible", "Sınav konusu görünür değil."));
            }
            return id;
        }

        var code = NormalizeCode(item.TopicCode);
        if (string.IsNullOrWhiteSpace(code)) return null;
        var query = _db.ExamTopics.AsNoTracking()
            .Include(t => t.ExamSubject).ThenInclude(s => s.ExamSection).ThenInclude(s => s.ExamVariant)
            .Where(t => t.Code == code && !t.IsDeleted && t.ExamSubject.ExamSection.ExamVariant.ExamDefinitionId == definitionId);
        if (subjectId is not null) query = query.Where(t => t.ExamSubjectId == subjectId);
        var matches = await query.ToListAsync(ct);
        if (matches.Count != 1) issues.Add(Error(matches.Count == 0 ? "exam_topic_not_visible" : "exam_topic_ambiguous", "Sınav konusu tekil ve görünür olmalı."));
        return matches.Count == 1 ? matches[0].Id : null;
    }

    private async Task<Guid?> ResolveOutcomeAsync(Guid definitionId, Guid? topicId, QuestionImportItemDto item, List<QuestionImportValidationIssueDto> issues, CancellationToken ct)
    {
        if (item.ExamOutcomeId is { } id && id != Guid.Empty)
        {
            var outcome = await _db.ExamOutcomes.AsNoTracking()
                .Include(o => o.ExamTopic).ThenInclude(t => t.ExamSubject).ThenInclude(s => s.ExamSection).ThenInclude(s => s.ExamVariant)
                .FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted && !o.ExamTopic.IsDeleted, ct);
            if (outcome is null || outcome.ExamTopic.ExamSubject.ExamSection.ExamVariant.ExamDefinitionId != definitionId || (topicId is not null && outcome.ExamTopicId != topicId))
            {
                issues.Add(Error("exam_outcome_not_visible", "Sınav kazanımı görünür değil."));
            }
            return id;
        }

        var code = NormalizeCode(item.OutcomeCode);
        if (string.IsNullOrWhiteSpace(code)) return null;
        if (topicId is null)
        {
            issues.Add(Error("exam_outcome_requires_topic", "Kazanım koduyla eşleşme için konu referansı gerekir."));
            return null;
        }

        var matches = await _db.ExamOutcomes.AsNoTracking()
            .Where(o => o.Code == code && !o.IsDeleted && o.ExamTopicId == topicId)
            .ToListAsync(ct);
        if (matches.Count != 1) issues.Add(Error(matches.Count == 0 ? "exam_outcome_not_visible" : "exam_outcome_ambiguous", "Sınav kazanımı tekil ve görünür olmalı."));
        return matches.Count == 1 ? matches[0].Id : null;
    }

    private async Task<Guid?> FindDuplicateQuestionAsync(Guid userId, CreateQuestionDto question, CancellationToken ct)
    {
        var candidates = await _db.QuestionItems.AsNoTracking()
            .Where(q => !q.IsDeleted
                        && (q.OwnerUserId == null || q.OwnerUserId == userId)
                        && q.ExamDefinitionId == question.ExamDefinitionId
                        && q.ExamTopicId == question.ExamTopicId
                        && q.ExamOutcomeId == question.ExamOutcomeId)
            .Select(q => new { q.Id, q.Stem })
            .ToListAsync(ct);

        var normalizedStem = NormalizeStem(question.Stem);
        return candidates.FirstOrDefault(q => NormalizeStem(q.Stem) == normalizedStem)?.Id;
    }

    private static List<QuestionOptionDto> NormalizeOptions(IReadOnlyList<QuestionImportOptionDto> options, List<QuestionImportValidationIssueDto> issues)
    {
        var normalized = options
            .Select((option, index) => new QuestionOptionDto
            {
                OptionKey = NormalizeOptionKey(option.OptionKey, index),
                Text = Clean(option.Text),
                IsCorrect = option.IsCorrect,
                SortOrder = option.SortOrder == 0 ? index : option.SortOrder
            })
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.OptionKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.GroupBy(o => o.OptionKey, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            issues.Add(Error("duplicate_option_key", "Aynı seçenek anahtarı birden fazla kullanılmış."));
        }

        return normalized;
    }

    private static void RecalculateCounts(QuestionImportPreview preview)
    {
        preview.TotalCount = preview.Items.Count(i => !i.IsDeleted);
        preview.AcceptedCount = preview.Items.Count(i => !i.IsDeleted && i.Status == "accepted");
        preview.RejectedCount = preview.Items.Count(i => !i.IsDeleted && (i.Status == "rejected" || i.Status == "duplicate"));
        preview.WarningCount = preview.Items
            .Where(i => !i.IsDeleted)
            .SelectMany(i => DeserializeIssues(i.IssuesJson))
            .Count(i => i.Severity == "warning");
    }

    private static QuestionImportPreviewDto ToDto(QuestionImportPreview preview) => new()
    {
        Id = preview.Id,
        Status = preview.Status == "pending" && preview.ExpiresAt <= DateTime.UtcNow ? "expired" : preview.Status,
        TotalCount = preview.TotalCount,
        AcceptedCount = preview.AcceptedCount,
        RejectedCount = preview.RejectedCount,
        WarningCount = preview.WarningCount,
        CreatedAt = preview.CreatedAt,
        ExpiresAt = preview.ExpiresAt,
        Items = preview.Items
            .Where(i => !i.IsDeleted)
            .OrderBy(i => i.RowIndex)
            .Select(ToItemDto)
            .ToList()
    };

    private static QuestionImportPreviewItemDto ToItemDto(QuestionImportPreviewItem item) => new()
    {
        Id = item.Id,
        RowIndex = item.RowIndex,
        ExternalId = item.ExternalId,
        Status = item.Status,
        Issues = DeserializeIssues(item.IssuesJson),
        IsDuplicate = item.Status == "duplicate" || item.DuplicateQuestionId is not null,
        DuplicateQuestionId = item.DuplicateQuestionId,
        CreatedQuestionId = item.CreatedQuestionId,
        NormalizedQuestion = DeserializeQuestion(item.NormalizedQuestionJson)
    };

    private static QuestionImportResultDto ToResult(QuestionImportPreview preview, string status) => new()
    {
        ImportPreviewId = preview.Id,
        Status = status,
        CreatedQuestionIds = preview.Items
            .Where(i => !i.IsDeleted && i.CreatedQuestionId is not null)
            .OrderBy(i => i.RowIndex)
            .Select(i => i.CreatedQuestionId!.Value)
            .ToList(),
        CreatedCount = preview.Items.Count(i => !i.IsDeleted && i.CreatedQuestionId is not null),
        RejectedCount = preview.Items.Count(i => !i.IsDeleted && (i.Status == "rejected" || i.Status == "duplicate")),
        SkippedCount = preview.Items.Count(i => !i.IsDeleted && i.Status != "accepted")
    };

    private static QuestionImportResultDto InvalidResult(Guid previewId, string code, string message) => new()
    {
        ImportPreviewId = previewId,
        Status = "rejected",
        Issues = [Error(code, message)]
    };

    private static List<QuestionImportValidationIssueDto> DeserializeIssues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<QuestionImportValidationIssueDto>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [Error("issue_payload_invalid", "Import issue payload could not be read.")];
        }
    }

    private static CreateQuestionDto? DeserializeQuestion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CreateQuestionDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static QuestionImportValidationIssueDto Error(string code, string message) => new()
    {
        Code = code,
        Severity = "error",
        Message = message
    };

    private static QuestionImportValidationIssueDto Warning(string code, string message) => new()
    {
        Code = code,
        Severity = "warning",
        Message = message
    };

    private static string NormalizeBounded(string? value, HashSet<string> allowed, string fallback)
    {
        var clean = CleanOptional(value)?.ToLowerInvariant();
        return clean is not null && allowed.Contains(clean) ? clean : fallback;
    }

    private static string NormalizeCode(string? value)
    {
        var clean = CleanOptional(value);
        if (string.IsNullOrWhiteSpace(clean))
        {
            return string.Empty;
        }

        return CodeUnsafeCharacters().Replace(clean.Trim().ToUpperInvariant(), "_");
    }

    private static string NormalizeOptionKey(string? value, int index)
    {
        var clean = CleanOptional(value);
        if (string.IsNullOrWhiteSpace(clean))
        {
            return ((char)('A' + Math.Min(index, 25))).ToString();
        }

        return clean.Trim().ToUpperInvariant();
    }

    private static string NormalizeStem(string value) => Whitespace().Replace(Clean(value).ToLowerInvariant(), " ").Trim();

    private static string Clean(string? value, string fallback = "") => CleanOptional(value) ?? fallback;

    private static string CleanOptional(string? value)
    {
        var clean = value?.Trim();
        return string.IsNullOrWhiteSpace(clean) ? string.Empty : Whitespace().Replace(clean, " ");
    }

    private static string? SafeOptional(string? value)
    {
        var clean = CleanOptional(value);
        return string.IsNullOrWhiteSpace(clean) ? null : clean;
    }

    private sealed record ResolvedImportLinks(
        Guid ExamDefinitionId,
        Guid? ExamVariantId,
        Guid? ExamSectionId,
        Guid? ExamSubjectId,
        Guid? ExamTopicId,
        Guid? ExamOutcomeId);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[^A-Z0-9_]+")]
    private static partial Regex CodeUnsafeCharacters();
}

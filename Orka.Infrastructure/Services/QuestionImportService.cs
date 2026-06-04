using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

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

    public async Task<QuestionImportPreviewDto> PreviewPackageImportAsync(
        Guid userId,
        QuestionImportPackageDto request,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var preview = new QuestionImportPreview
        {
            OwnerUserId = userId,
            Status = "pending",
            ImportFormat = "json_v2",
            PackageTitle = SafeOptional(request.PackageTitle),
            PackageVersion = Clean(request.PackageVersion, "2.0"),
            CreatedAt = now,
            ExpiresAt = now.Add(PreviewTtl)
        };

        var packageIssues = new List<QuestionImportValidationIssueDto>();
        var normalizedAssets = NormalizePackageAssets(request.Assets, packageIssues);
        var normalizedStimuli = NormalizePackageStimuli(request.Stimuli, packageIssues);
        var assetIds = normalizedAssets.Select(a => a.ExternalAssetId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stimulusIds = normalizedStimuli.Select(s => s.ExternalStimulusId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var externalIds = request.Questions
            .Select(q => CleanOptional(q.ExternalId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var normalizedQuestions = new List<QuestionImportRichQuestionDto>();
        for (var index = 0; index < request.Questions.Count; index++)
        {
            var richQuestion = MergePackageDefaults(request, request.Questions[index]);
            var issues = new List<QuestionImportValidationIssueDto>(packageIssues);
            var externalId = CleanOptional(richQuestion.ExternalId);

            if (!string.IsNullOrWhiteSpace(externalId) && externalIds.Contains(externalId))
            {
                issues.Add(Error("duplicate_external_id", "Aynı externalId bu rich import içinde birden fazla kez kullanılmış."));
            }

            ValidateRichReferences(richQuestion, assetIds, stimulusIds, issues);
            var normalizedQuestion = await NormalizeRichQuestionAsync(userId, richQuestion, issues, ct);
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

            normalizedQuestions.Add(richQuestion);
            preview.Items.Add(new QuestionImportPreviewItem
            {
                RowIndex = index,
                ExternalId = externalId,
                Status = status,
                IssuesJson = JsonSerializer.Serialize(issues, JsonOptions),
                NormalizedQuestionJson = normalizedQuestion is not null
                    ? JsonSerializer.Serialize(normalizedQuestion, JsonOptions)
                    : null,
                DuplicateQuestionId = duplicateQuestionId,
                CreatedAt = now
            });
        }

        preview.NormalizedPackageJson = JsonSerializer.Serialize(
            new NormalizedRichImportPackage(normalizedAssets, normalizedStimuli, normalizedQuestions),
            JsonOptions);
        RecalculateCounts(preview);
        _db.QuestionImportPreviews.Add(preview);
        await _db.SaveChangesAsync(ct);

        return ToDto(preview);
    }

    public async Task<QuestionImportPreviewDto> PreviewAikenImportAsync(
        Guid userId,
        QuestionImportTextAdapterRequestDto request,
        CancellationToken ct = default)
    {
        var parsed = ParseAiken(request);
        return parsed.Items.Count == 0
            ? await CreateAdapterUnsupportedPreviewAsync(userId, "aiken", "aiken_partial_support", "Aiken içeriği güvenli çoktan seçmeli formata çevrilemedi.", ct)
            : await PreviewImportAsync(userId, parsed, ct);
    }

    public async Task<QuestionImportPreviewDto> PreviewGiftImportAsync(
        Guid userId,
        QuestionImportTextAdapterRequestDto request,
        CancellationToken ct = default)
    {
        var parsed = ParseGift(request);
        return parsed.Items.Count == 0
            ? await CreateAdapterUnsupportedPreviewAsync(userId, "gift", "gift_partial_support", "GIFT içeriği güvenli çoktan seçmeli formata çevrilemedi.", ct)
            : await PreviewImportAsync(userId, parsed, ct);
    }

    public Task<QuestionImportPreviewDto> PreviewQtiImportAsync(
        Guid userId,
        QuestionImportTextAdapterRequestDto request,
        CancellationToken ct = default) =>
        CreateAdapterUnsupportedPreviewAsync(userId, "qti3", "qti3_partial_support", "QTI 3 için Pack C yalnızca güvenli preview seam sağlar; tam uyumluluk henüz iddia edilmez.", ct);

    public Task<QuestionImportPreviewDto> PreviewMoodleImportAsync(
        Guid userId,
        QuestionImportTextAdapterRequestDto request,
        CancellationToken ct = default) =>
        CreateAdapterUnsupportedPreviewAsync(userId, "moodle_xml", "moodle_partial_support", "Moodle XML için Pack C yalnızca güvenli preview seam sağlar; tam uyumluluk henüz iddia edilmez.", ct);

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

        if (string.Equals(preview.ImportFormat, "json_v2", StringComparison.OrdinalIgnoreCase))
        {
            return await ApproveRichPackageImportAsync(userId, preview, ct);
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

    private async Task<QuestionImportResultDto> ApproveRichPackageImportAsync(
        Guid userId,
        QuestionImportPreview preview,
        CancellationToken ct)
    {
        var package = DeserializeRichPackage(preview.NormalizedPackageJson);
        if (package is null)
        {
            return InvalidResult(preview.Id, "normalized_package_missing", "Normalize edilmiş rich import paketi bulunamadı.");
        }

        var acceptedRows = preview.Items
            .Where(i => !i.IsDeleted && i.Status == "accepted")
            .Select(i => i.RowIndex)
            .ToHashSet();
        var acceptedQuestions = package.Questions
            .Select((question, index) => new { question, index })
            .Where(item => acceptedRows.Contains(item.index))
            .Select(item => item.question)
            .ToList();
        var referencedAssetIds = acceptedQuestions
            .SelectMany(q => q.ContentBlocks.Concat(q.Options.SelectMany(o => o.ContentBlocks)))
            .Select(b => CleanOptional(b.ExternalAssetId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referencedStimulusIds = acceptedQuestions
            .SelectMany(q => q.ExternalStimulusIds)
            .Select(CleanOptional)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assetIdMap = await CreateOrReuseAssetsAsync(
            userId,
            package.Assets.Where(a => referencedAssetIds.Contains(a.ExternalAssetId)).ToList(),
            ct);
        var stimulusIdMap = await CreateOrReuseStimuliAsync(
            userId,
            package.Stimuli.Where(s => referencedStimulusIds.Contains(s.ExternalStimulusId)).ToList(),
            ct);

        foreach (var item in preview.Items.Where(i => !i.IsDeleted && i.Status == "accepted").OrderBy(i => i.RowIndex))
        {
            if (item.CreatedQuestionId is not null)
            {
                continue;
            }

            var richQuestion = package.Questions.ElementAtOrDefault(item.RowIndex);
            if (richQuestion is null)
            {
                item.Status = "rejected";
                item.IssuesJson = JsonSerializer.Serialize(new[] { Error("normalized_rich_question_missing", "Normalize edilmiş rich soru verisi bulunamadı.") }, JsonOptions);
                continue;
            }

            var issues = DeserializeIssues(item.IssuesJson);
            var create = await NormalizeRichQuestionAsync(userId, richQuestion, issues, ct);
            if (create is null || issues.Any(i => i.Severity == "error"))
            {
                item.Status = "rejected";
                item.IssuesJson = JsonSerializer.Serialize(issues, JsonOptions);
                continue;
            }

            create.ContentBlocks = MapQuestionContentBlocks(richQuestion.ContentBlocks, assetIdMap);
            create.Stimuli = richQuestion.ExternalStimulusIds
                .Select(id => CleanOptional(id))
                .Where(id => !string.IsNullOrWhiteSpace(id) && stimulusIdMap.ContainsKey(id))
                .Select((id, index) => new QuestionStimulusLinkDto
                {
                    QuestionStimulusId = stimulusIdMap[id!],
                    SortOrder = index
                })
                .ToList();

            try
            {
                var created = await _questionBank.CreateQuestionAsync(userId, create, ct);
                foreach (var option in richQuestion.Options)
                {
                    var createdOption = created.Options.FirstOrDefault(o =>
                        string.Equals(o.OptionKey, NormalizeOptionKey(option.OptionKey, option.SortOrder), StringComparison.OrdinalIgnoreCase));
                    if (createdOption?.Id is null)
                    {
                        continue;
                    }

                    foreach (var block in option.ContentBlocks.OrderBy(b => b.SortOrder))
                    {
                        await _questionBank.AddOptionContentBlockAsync(
                            userId,
                            createdOption.Id.Value,
                            MapOptionContentBlock(block, assetIdMap),
                            ct);
                    }
                }

                item.CreatedQuestionId = created.Id;
                if (SafeReviewLicenseStatuses.Contains(create.LicenseStatus))
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

    private async Task<Dictionary<string, Guid>> CreateOrReuseAssetsAsync(
        Guid userId,
        IReadOnlyList<QuestionImportAssetDto> assets,
        CancellationToken ct)
    {
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            var externalId = Clean(asset.ExternalAssetId);
            if (string.IsNullOrWhiteSpace(externalId))
            {
                continue;
            }

            var hash = Clean(asset.Sha256Hash).ToLowerInvariant();
            var existing = await _db.QuestionAssets.AsNoTracking()
                .Where(a => !a.IsDeleted
                            && a.Sha256Hash == hash
                            && (a.OwnerUserId == null || a.OwnerUserId == userId))
                .OrderByDescending(a => a.OwnerUserId == userId)
                .FirstOrDefaultAsync(ct);
            if (existing is not null)
            {
                map[externalId] = existing.Id;
                continue;
            }

            var created = await _questionBank.CreateAssetAsync(userId, new CreateQuestionAssetDto
            {
                AssetType = asset.AssetType,
                StorageKey = asset.StorageKey,
                FileName = asset.FileName,
                MimeType = asset.MimeType,
                SizeBytes = asset.SizeBytes,
                Sha256Hash = hash,
                SourceRegistryItemId = asset.SourceRegistryItemId,
                SourceTitle = asset.SourceTitle,
                SourceUrl = asset.SourceUrl,
                LicenseStatus = asset.LicenseStatus,
                VerificationStatus = asset.VerificationStatus,
                AltText = asset.AltText,
                Caption = asset.Caption,
                LongDescription = asset.LongDescription
            }, ct);
            map[externalId] = created.Id;
        }

        return map;
    }

    private async Task<Dictionary<string, Guid>> CreateOrReuseStimuliAsync(
        Guid userId,
        IReadOnlyList<QuestionImportStimulusDto> stimuli,
        CancellationToken ct)
    {
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var stimulus in stimuli)
        {
            var externalId = Clean(stimulus.ExternalStimulusId);
            if (string.IsNullOrWhiteSpace(externalId))
            {
                continue;
            }

            var title = Clean(stimulus.Title);
            var type = NormalizeBounded(stimulus.StimulusType, AllowedStimulusTypes, "passage");
            var contentText = SafeOptional(stimulus.ContentText);
            var existing = await _db.QuestionStimuli.AsNoTracking()
                .Where(s => !s.IsDeleted
                            && (s.OwnerUserId == null || s.OwnerUserId == userId)
                            && s.StimulusType == type
                            && s.Title == title
                            && s.ContentText == contentText)
                .OrderByDescending(s => s.OwnerUserId == userId)
                .FirstOrDefaultAsync(ct);
            if (existing is not null)
            {
                map[externalId] = existing.Id;
                continue;
            }

            var created = await _questionBank.CreateStimulusAsync(userId, new CreateQuestionStimulusDto
            {
                Title = title,
                StimulusType = type,
                ContentText = contentText,
                ContentJson = SafeContentJson(stimulus.ContentJson),
                SourceRegistryItemId = stimulus.SourceRegistryItemId,
                CurriculumNodeId = stimulus.CurriculumNodeId,
                LicenseStatus = NormalizeBounded(stimulus.LicenseStatus, AllowedLicenseStatuses, "unknown"),
                VerificationStatus = Clean(stimulus.VerificationStatus, "unverified")
            }, ct);
            map[externalId] = created.Id;
        }

        return map;
    }

    private async Task<CreateQuestionDto?> NormalizeRichQuestionAsync(
        Guid userId,
        QuestionImportRichQuestionDto question,
        List<QuestionImportValidationIssueDto> issues,
        CancellationToken ct)
    {
        var item = new QuestionImportItemDto
        {
            ExternalId = question.ExternalId,
            ExamDefinitionId = question.ExamDefinitionId,
            ExamVariantId = question.ExamVariantId,
            ExamSectionId = question.ExamSectionId,
            ExamSubjectId = question.ExamSubjectId,
            ExamTopicId = question.ExamTopicId,
            ExamOutcomeId = question.ExamOutcomeId,
            ExamCode = question.ExamCode,
            VariantCode = question.VariantCode,
            SectionCode = question.SectionCode,
            SubjectCode = question.SubjectCode,
            TopicCode = question.TopicCode,
            OutcomeCode = question.OutcomeCode,
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
            QuestionBankSource = question.QuestionBankSource
        };
        var links = await ResolveExamLinksAsync(userId, item, issues, ct);
        if (links is null)
        {
            return null;
        }

        var questionType = NormalizeBounded(question.QuestionType, AllowedQuestionTypes, "multiple_choice");
        if (!AllowedQuestionTypes.Contains(CleanOptional(question.QuestionType) ?? string.Empty))
        {
            issues.Add(Error("unsupported_question_type", "Desteklenmeyen soru tipi."));
        }

        var licenseStatus = NormalizeBounded(question.LicenseStatus, AllowedLicenseStatuses, "unknown");
        if (!SafeReviewLicenseStatuses.Contains(licenseStatus))
        {
            issues.Add(Warning("unsafe_license_imported_as_draft", "Kaynak/lisans güvenli değil; soru sadece taslak olarak içe aktarılır."));
        }

        var sourceUrl = SafeOptional(question.SourceUrl);
        if (!string.IsNullOrWhiteSpace(sourceUrl) && !Uri.TryCreate(sourceUrl, UriKind.Absolute, out _))
        {
            issues.Add(Error("source_url_invalid", "Kaynak URL geçerli değil."));
        }

        var stem = CleanOptional(question.Stem) ?? string.Empty;
        var hasBlocks = question.ContentBlocks.Any(b => HasBlockContent(b));
        if (string.IsNullOrWhiteSpace(stem) && !hasBlocks)
        {
            issues.Add(Error("question_stem_or_content_required", "Rich soru için soru kökü veya content block gerekir."));
        }

        var options = NormalizeRichOptions(question.Options, issues);
        if (questionType == "multiple_choice")
        {
            if (options.Count < 2)
            {
                issues.Add(Error("multiple_choice_minimum_two_options", "Çoktan seçmeli soru en az iki seçenek içermeli."));
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

        ValidateAccessibility(question.ContentBlocks, question.Options, issues);

        var tags = question.Tags
            .Select(t => CleanOptional(t))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => new QuestionTagDto { Tag = t! })
            .ToList();

        if (question.ContentBlocks.Count > 0)
        {
            tags.Add(new QuestionTagDto { Tag = "rich_import" });
        }

        return new CreateQuestionDto
        {
            ExamDefinitionId = links.ExamDefinitionId,
            ExamVariantId = links.ExamVariantId,
            ExamSectionId = links.ExamSectionId,
            ExamSubjectId = links.ExamSubjectId,
            ExamTopicId = links.ExamTopicId,
            ExamOutcomeId = links.ExamOutcomeId,
            LearningTopicId = question.LearningTopicId,
            ConceptGraphSnapshotId = question.ConceptGraphSnapshotId,
            LearningConceptId = question.LearningConceptId,
            AssessmentItemId = question.AssessmentItemId,
            QuizRunId = question.QuizRunId,
            PlanRequestId = question.PlanRequestId,
            ConceptKey = SafeOptional(question.ConceptKey),
            ConceptLabel = SafeOptional(question.ConceptLabel),
            MisconceptionTarget = SafeOptional(question.MisconceptionTarget),
            EvidenceExpected = SafeOptional(question.EvidenceExpected),
            ScoringRuleJson = SafeAssessmentMetadataJson(question.ScoringRuleJson),
            CalibrationStatus = SafeOptional(question.CalibrationStatus),
            VisualReadinessStatus = SafeOptional(question.VisualReadinessStatus),
            QuestionBankSource = SafeOptional(question.QuestionBankSource),
            QuestionType = questionType,
            Stem = stem,
            Difficulty = NormalizeBounded(question.Difficulty, AllowedDifficulties, "medium"),
            CognitiveSkill = Clean(question.CognitiveSkill, "conceptual"),
            LicenseStatus = licenseStatus,
            SourceOrigin = Clean(question.SourceOrigin, "structured_json_v2"),
            SourceTitle = SafeOptional(question.SourceTitle),
            SourceUrl = sourceUrl,
            Explanation = CleanOptional(question.Explanation),
            Options = options,
            Explanations = question.Explanations.Count > 0
                ? question.Explanations
                : string.IsNullOrWhiteSpace(question.Explanation)
                    ? []
                    : [new QuestionExplanationDto { ExplanationText = Clean(question.Explanation), Visibility = "authoring", IsSafeForLearners = true }],
            Tags = tags,
            OutcomeLinks = question.OutcomeLinks.Count > 0
                ? question.OutcomeLinks
                : links.ExamOutcomeId is null
                    ? []
                    : [new QuestionOutcomeLinkDto { ExamOutcomeId = links.ExamOutcomeId.Value, IsPrimary = true, LinkStrength = 1.0m }],
            ContentBlocks = MapQuestionContentBlocks(question.ContentBlocks, new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase))
        };
    }

    private static List<QuestionOptionDto> NormalizeRichOptions(
        IReadOnlyList<QuestionImportRichOptionDto> options,
        List<QuestionImportValidationIssueDto> issues)
    {
        var normalized = options
            .Select((option, index) =>
            {
                var key = NormalizeOptionKey(option.OptionKey, index);
                var text = CleanOptional(option.Text);
                if (string.IsNullOrWhiteSpace(text) && !option.ContentBlocks.Any(HasBlockContent))
                {
                    issues.Add(Error("multiple_choice_option_text_or_content_required", "Rich seçenekte metin veya content block gerekir."));
                }

                return new QuestionOptionDto
                {
                    OptionKey = key,
                    Text = string.IsNullOrWhiteSpace(text) ? $"Rich option {key}" : text,
                    IsCorrect = option.IsCorrect,
                    SortOrder = option.SortOrder == 0 ? index : option.SortOrder,
                    Rationale = SafeOptional(option.Rationale),
                    MisconceptionKey = SafeOptional(option.MisconceptionKey),
                    DiagnosticSignalJson = SafeAssessmentMetadataJson(option.DiagnosticSignalJson)
                };
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

    private static List<QuestionImportAssetDto> NormalizePackageAssets(
        IReadOnlyList<QuestionImportAssetDto> assets,
        List<QuestionImportValidationIssueDto> issues)
    {
        var duplicateIds = assets
            .Select(a => CleanOptional(a.ExternalAssetId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var normalized = new List<QuestionImportAssetDto>();
        foreach (var asset in assets)
        {
            var externalId = Clean(asset.ExternalAssetId);
            if (string.IsNullOrWhiteSpace(externalId))
            {
                issues.Add(Error("asset_external_id_required", "Asset externalAssetId zorunlu."));
                continue;
            }

            if (duplicateIds.Contains(externalId))
            {
                issues.Add(Error("duplicate_external_asset_id", "Aynı externalAssetId birden fazla asset için kullanılmış."));
            }

            if (!AllowedAssetTypes.Contains(CleanOptional(asset.AssetType) ?? string.Empty))
            {
                issues.Add(Error("unsupported_asset_type", "Asset tipi desteklenmiyor."));
            }

            var storageKey = Clean(asset.StorageKey);
            if (string.IsNullOrWhiteSpace(storageKey))
            {
                storageKey = Clean(asset.RelativePath);
            }

            if (!IsSafePackageStorageKey(storageKey))
            {
                issues.Add(Error("unsafe_asset_storage_key", "Asset storageKey/relativePath güvenli paket yolu olmalı."));
            }

            var hash = Clean(asset.Sha256Hash).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(hash))
            {
                issues.Add(Error("asset_sha256_required", "Import asset için sha256Hash zorunlu."));
            }

            var sourceUrl = SafeOptional(asset.SourceUrl);
            if (!string.IsNullOrWhiteSpace(sourceUrl) && !Uri.TryCreate(sourceUrl, UriKind.Absolute, out _))
            {
                issues.Add(Error("asset_source_url_invalid", "Asset kaynak URL geçerli değil."));
            }

            normalized.Add(new QuestionImportAssetDto
            {
                ExternalAssetId = externalId,
                AssetType = NormalizeBounded(asset.AssetType, AllowedAssetTypes, "image"),
                StorageKey = storageKey,
                FileName = Clean(asset.FileName, externalId),
                MimeType = Clean(asset.MimeType, "application/octet-stream"),
                SizeBytes = Math.Max(asset.SizeBytes, 0),
                Sha256Hash = hash,
                SourceRegistryItemId = asset.SourceRegistryItemId,
                SourceTitle = SafeOptional(asset.SourceTitle),
                SourceUrl = sourceUrl,
                LicenseStatus = NormalizeBounded(asset.LicenseStatus, AllowedLicenseStatuses, "unknown"),
                VerificationStatus = Clean(asset.VerificationStatus, "unverified"),
                AltText = SafeOptional(asset.AltText),
                Caption = SafeOptional(asset.Caption),
                LongDescription = SafeOptional(asset.LongDescription)
            });
        }

        return normalized;
    }

    private static List<QuestionImportStimulusDto> NormalizePackageStimuli(
        IReadOnlyList<QuestionImportStimulusDto> stimuli,
        List<QuestionImportValidationIssueDto> issues)
    {
        var duplicateIds = stimuli
            .Select(s => CleanOptional(s.ExternalStimulusId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var normalized = new List<QuestionImportStimulusDto>();
        foreach (var stimulus in stimuli)
        {
            var externalId = Clean(stimulus.ExternalStimulusId);
            if (string.IsNullOrWhiteSpace(externalId))
            {
                issues.Add(Error("stimulus_external_id_required", "Stimulus externalStimulusId zorunlu."));
                continue;
            }

            if (duplicateIds.Contains(externalId))
            {
                issues.Add(Error("duplicate_external_stimulus_id", "Aynı externalStimulusId birden fazla stimulus için kullanılmış."));
            }

            var title = Clean(stimulus.Title);
            if (string.IsNullOrWhiteSpace(title))
            {
                issues.Add(Error("stimulus_title_required", "Stimulus title zorunlu."));
            }

            if (string.IsNullOrWhiteSpace(stimulus.ContentText) && string.IsNullOrWhiteSpace(stimulus.ContentJson))
            {
                issues.Add(Error("stimulus_content_required", "Stimulus contentText veya contentJson içermeli."));
            }

            normalized.Add(new QuestionImportStimulusDto
            {
                ExternalStimulusId = externalId,
                Title = title,
                StimulusType = NormalizeBounded(stimulus.StimulusType, AllowedStimulusTypes, "passage"),
                ContentText = SafeOptional(stimulus.ContentText),
                ContentJson = SafeContentJson(stimulus.ContentJson),
                SourceRegistryItemId = stimulus.SourceRegistryItemId,
                CurriculumNodeId = stimulus.CurriculumNodeId,
                LicenseStatus = NormalizeBounded(stimulus.LicenseStatus, AllowedLicenseStatuses, "unknown"),
                VerificationStatus = Clean(stimulus.VerificationStatus, "unverified")
            });
        }

        return normalized;
    }

    private static QuestionImportRichQuestionDto MergePackageDefaults(
        QuestionImportPackageDto package,
        QuestionImportRichQuestionDto question)
    {
        question.ExamDefinitionId ??= package.ExamDefinitionId;
        question.ExamVariantId ??= package.ExamVariantId;
        question.ExamSectionId ??= package.ExamSectionId;
        question.ExamSubjectId ??= package.ExamSubjectId;
        question.ExamTopicId ??= package.ExamTopicId;
        question.ExamOutcomeId ??= package.ExamOutcomeId;
        question.ExamCode ??= package.ExamCode;
        question.VariantCode ??= package.VariantCode;
        question.SectionCode ??= package.SectionCode;
        question.SubjectCode ??= package.SubjectCode;
        question.TopicCode ??= package.TopicCode;
        question.OutcomeCode ??= package.OutcomeCode;
        question.LearningTopicId ??= package.LearningTopicId;
        question.ConceptGraphSnapshotId ??= package.ConceptGraphSnapshotId;
        question.LearningConceptId ??= package.LearningConceptId;
        question.AssessmentItemId ??= package.AssessmentItemId;
        question.QuizRunId ??= package.QuizRunId;
        question.PlanRequestId ??= package.PlanRequestId;
        question.ConceptKey ??= package.ConceptKey;
        question.ConceptLabel ??= package.ConceptLabel;
        question.MisconceptionTarget ??= package.MisconceptionTarget;
        question.EvidenceExpected ??= package.EvidenceExpected;
        question.ScoringRuleJson ??= package.ScoringRuleJson;
        question.CalibrationStatus ??= package.CalibrationStatus;
        question.VisualReadinessStatus ??= package.VisualReadinessStatus;
        question.QuestionBankSource ??= package.QuestionBankSource;
        question.SourceOrigin ??= package.SourceOrigin;
        question.LicenseStatus ??= package.LicenseStatus;
        question.SourceTitle ??= package.SourceTitle;
        question.SourceUrl ??= package.SourceUrl;
        return question;
    }

    private static void ValidateRichReferences(
        QuestionImportRichQuestionDto question,
        HashSet<string> assetIds,
        HashSet<string> stimulusIds,
        List<QuestionImportValidationIssueDto> issues)
    {
        foreach (var block in question.ContentBlocks)
        {
            ValidateBlock("question", block, assetIds, AllowedQuestionBlockTypes, issues);
        }

        foreach (var option in question.Options)
        {
            foreach (var block in option.ContentBlocks)
            {
                ValidateBlock("option", block, assetIds, AllowedOptionBlockTypes, issues);
            }
        }

        foreach (var stimulusId in question.ExternalStimulusIds.Select(CleanOptional).Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            if (!stimulusIds.Contains(stimulusId!))
            {
                issues.Add(Error("missing_referenced_stimulus", "Soru, paket içinde bulunmayan stimulus referansı içeriyor."));
            }
        }
    }

    private static void ValidateBlock(
        string target,
        QuestionImportContentBlockDto block,
        HashSet<string> assetIds,
        HashSet<string> allowedBlockTypes,
        List<QuestionImportValidationIssueDto> issues)
    {
        var blockType = NormalizeBounded(block.BlockType, allowedBlockTypes, "text");
        if (!allowedBlockTypes.Contains(CleanOptional(block.BlockType) ?? string.Empty))
        {
            issues.Add(Error("unsupported_content_block_type", $"{target} content block tipi desteklenmiyor."));
        }

        var externalAssetId = CleanOptional(block.ExternalAssetId);
        if (!string.IsNullOrWhiteSpace(externalAssetId) && !assetIds.Contains(externalAssetId))
        {
            issues.Add(Error("missing_referenced_asset", "Content block, paket içinde bulunmayan asset referansı içeriyor."));
        }

        if (!HasBlockContent(block))
        {
            issues.Add(Error("content_block_empty", "Content block metin, JSON veya asset referansı içermeli."));
        }

        if ((blockType is "image" or "table" or "chart") && string.IsNullOrWhiteSpace(block.AltText) && string.IsNullOrWhiteSpace(block.Caption))
        {
            issues.Add(Warning("content_block_accessibility_missing", "Görsel/tablo/grafik block için altText veya caption önerilir; publish Pack B gate tarafından engellenebilir."));
        }

        if (blockType == "formula" && string.IsNullOrWhiteSpace(block.Text) && string.IsNullOrWhiteSpace(block.AltText))
        {
            issues.Add(Warning("formula_accessible_fallback_missing", "Formül block için metin fallback veya altText gerekir."));
        }
    }

    private static void ValidateAccessibility(
        IReadOnlyList<QuestionImportContentBlockDto> questionBlocks,
        IReadOnlyList<QuestionImportRichOptionDto> options,
        List<QuestionImportValidationIssueDto> issues)
    {
        foreach (var block in questionBlocks)
        {
            var type = Clean(block.BlockType).ToLowerInvariant();
            if ((type is "image" or "table" or "chart") && string.IsNullOrWhiteSpace(block.AltText) && string.IsNullOrWhiteSpace(block.Caption))
            {
                issues.Add(Warning("rich_question_accessibility_missing", "Soru görsel/tablo/grafik block'u altText veya caption içermeli."));
            }
        }

        foreach (var block in options.SelectMany(o => o.ContentBlocks))
        {
            var type = Clean(block.BlockType).ToLowerInvariant();
            if (type == "image" && string.IsNullOrWhiteSpace(block.AltText))
            {
                issues.Add(Warning("rich_option_image_alt_text_missing", "Seçenek görseli altText içermeli."));
            }
        }
    }

    private static List<CreateQuestionContentBlockDto> MapQuestionContentBlocks(
        IReadOnlyList<QuestionImportContentBlockDto> blocks,
        IReadOnlyDictionary<string, Guid> assetIdMap) =>
        blocks.OrderBy(b => b.SortOrder)
            .Select(b => new CreateQuestionContentBlockDto
            {
                BlockType = NormalizeBounded(b.BlockType, AllowedQuestionBlockTypes, "text"),
                Text = SafeOptional(b.Text),
                ContentJson = SafeContentJson(b.ContentJson),
                AssetId = ResolveExternalAssetId(b.ExternalAssetId, assetIdMap),
                SortOrder = b.SortOrder,
                AltText = SafeOptional(b.AltText),
                Caption = SafeOptional(b.Caption),
                LongDescription = SafeOptional(b.LongDescription)
            })
            .ToList();

    private static CreateQuestionOptionContentBlockDto MapOptionContentBlock(
        QuestionImportContentBlockDto block,
        IReadOnlyDictionary<string, Guid> assetIdMap) => new()
    {
        BlockType = NormalizeBounded(block.BlockType, AllowedOptionBlockTypes, "text"),
        Text = SafeOptional(block.Text),
        ContentJson = SafeContentJson(block.ContentJson),
        AssetId = ResolveExternalAssetId(block.ExternalAssetId, assetIdMap),
        SortOrder = block.SortOrder,
        AltText = SafeOptional(block.AltText),
        Caption = SafeOptional(block.Caption)
    };

    private static Guid? ResolveExternalAssetId(string? externalAssetId, IReadOnlyDictionary<string, Guid> assetIdMap)
    {
        var clean = CleanOptional(externalAssetId);
        return !string.IsNullOrWhiteSpace(clean) && assetIdMap.TryGetValue(clean, out var assetId)
            ? assetId
            : null;
    }

    private static bool HasBlockContent(QuestionImportContentBlockDto block) =>
        !string.IsNullOrWhiteSpace(block.Text)
        || !string.IsNullOrWhiteSpace(block.ContentJson)
        || !string.IsNullOrWhiteSpace(block.ExternalAssetId);

    private static bool IsSafePackageStorageKey(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)
            || storageKey.StartsWith("/", StringComparison.Ordinal)
            || storageKey.StartsWith("\\", StringComparison.Ordinal)
            || storageKey.Contains("..", StringComparison.Ordinal)
            || storageKey.Contains(':', StringComparison.Ordinal)
            || storageKey.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
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
            LearningTopicId = item.LearningTopicId,
            ConceptGraphSnapshotId = item.ConceptGraphSnapshotId,
            LearningConceptId = item.LearningConceptId,
            AssessmentItemId = item.AssessmentItemId,
            QuizRunId = item.QuizRunId,
            PlanRequestId = item.PlanRequestId,
            ConceptKey = SafeOptional(item.ConceptKey),
            ConceptLabel = SafeOptional(item.ConceptLabel),
            MisconceptionTarget = SafeOptional(item.MisconceptionTarget),
            EvidenceExpected = SafeOptional(item.EvidenceExpected),
            ScoringRuleJson = SafeAssessmentMetadataJson(item.ScoringRuleJson),
            CalibrationStatus = SafeOptional(item.CalibrationStatus),
            VisualReadinessStatus = SafeOptional(item.VisualReadinessStatus),
            QuestionBankSource = SafeOptional(item.QuestionBankSource),
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

    private async Task<QuestionImportPreviewDto> CreateAdapterUnsupportedPreviewAsync(
        Guid userId,
        string importFormat,
        string issueCode,
        string message,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var preview = new QuestionImportPreview
        {
            OwnerUserId = userId,
            Status = "pending",
            ImportFormat = importFormat,
            CreatedAt = now,
            ExpiresAt = now.Add(PreviewTtl),
            Items =
            [
                new QuestionImportPreviewItem
                {
                    RowIndex = 0,
                    Status = "rejected",
                    IssuesJson = JsonSerializer.Serialize(new[] { Error(issueCode, message) }, JsonOptions),
                    CreatedAt = now
                }
            ]
        };
        RecalculateCounts(preview);
        _db.QuestionImportPreviews.Add(preview);
        await _db.SaveChangesAsync(ct);
        return ToDto(preview);
    }

    private static QuestionImportRequestDto ParseAiken(QuestionImportTextAdapterRequestDto request)
    {
        var lines = request.Content.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count < 4)
        {
            return new QuestionImportRequestDto();
        }

        var answerLine = lines.FirstOrDefault(l => l.StartsWith("ANSWER:", StringComparison.OrdinalIgnoreCase));
        if (answerLine is null)
        {
            return new QuestionImportRequestDto();
        }

        var answer = NormalizeOptionKey(answerLine["ANSWER:".Length..], 0);
        var optionLines = lines
            .Where(l => Regex.IsMatch(l, "^[A-Z][).]\\s+"))
            .ToList();
        if (optionLines.Count < 2)
        {
            return new QuestionImportRequestDto();
        }

        var stem = lines.FirstOrDefault(l => !Regex.IsMatch(l, "^[A-Z][).]\\s+") && !l.StartsWith("ANSWER:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(stem))
        {
            return new QuestionImportRequestDto();
        }

        return new QuestionImportRequestDto
        {
            Items =
            [
                BuildAdapterItem(request, stem, optionLines.Select((line, index) =>
                {
                    var key = NormalizeOptionKey(line[..1], index);
                    return new QuestionImportOptionDto
                    {
                        OptionKey = key,
                        Text = Clean(line[2..]),
                        IsCorrect = string.Equals(key, answer, StringComparison.OrdinalIgnoreCase),
                        SortOrder = index
                    };
                }).ToList(), "aiken")
            ]
        };
    }

    private static QuestionImportRequestDto ParseGift(QuestionImportTextAdapterRequestDto request)
    {
        var content = request.Content.Trim();
        var open = content.IndexOf('{');
        var close = content.LastIndexOf('}');
        if (open <= 0 || close <= open)
        {
            return new QuestionImportRequestDto();
        }

        var stem = Clean(content[..open]);
        var body = content[(open + 1)..close];
        var parts = Regex.Split(body, @"(?=[=~])")
            .Select(p => p.Trim())
            .Where(p => p.Length > 1)
            .ToList();
        if (parts.Count < 2)
        {
            return new QuestionImportRequestDto();
        }

        var options = parts.Select((part, index) => new QuestionImportOptionDto
        {
            OptionKey = ((char)('A' + Math.Min(index, 25))).ToString(),
            Text = Clean(part[1..]),
            IsCorrect = part[0] == '=',
            SortOrder = index
        }).ToList();

        return new QuestionImportRequestDto
        {
            Items = [BuildAdapterItem(request, stem, options, "gift")]
        };
    }

    private static QuestionImportItemDto BuildAdapterItem(
        QuestionImportTextAdapterRequestDto request,
        string stem,
        List<QuestionImportOptionDto> options,
        string sourceOrigin) => new()
    {
        ExamDefinitionId = request.ExamDefinitionId,
        ExamVariantId = request.ExamVariantId,
        ExamSectionId = request.ExamSectionId,
        ExamSubjectId = request.ExamSubjectId,
        ExamTopicId = request.ExamTopicId,
        ExamOutcomeId = request.ExamOutcomeId,
        ExamCode = request.ExamCode,
        VariantCode = request.VariantCode,
        SectionCode = request.SectionCode,
        SubjectCode = request.SubjectCode,
        TopicCode = request.TopicCode,
        OutcomeCode = request.OutcomeCode,
        LearningTopicId = request.LearningTopicId,
        ConceptGraphSnapshotId = request.ConceptGraphSnapshotId,
        LearningConceptId = request.LearningConceptId,
        AssessmentItemId = request.AssessmentItemId,
        QuizRunId = request.QuizRunId,
        PlanRequestId = request.PlanRequestId,
        ConceptKey = request.ConceptKey,
        ConceptLabel = request.ConceptLabel,
        MisconceptionTarget = request.MisconceptionTarget,
        EvidenceExpected = request.EvidenceExpected,
        ScoringRuleJson = request.ScoringRuleJson,
        CalibrationStatus = request.CalibrationStatus,
        VisualReadinessStatus = request.VisualReadinessStatus,
        QuestionBankSource = request.QuestionBankSource,
        QuestionType = "multiple_choice",
        Stem = stem,
        Options = options,
        SourceOrigin = sourceOrigin,
        LicenseStatus = request.LicenseStatus,
        SourceTitle = request.SourceTitle,
        SourceUrl = request.SourceUrl,
        Tags = [sourceOrigin, "standards_preview_adapter"]
    };

    private static List<QuestionOptionDto> NormalizeOptions(IReadOnlyList<QuestionImportOptionDto> options, List<QuestionImportValidationIssueDto> issues)
    {
        var normalized = options
            .Select((option, index) => new QuestionOptionDto
            {
                OptionKey = NormalizeOptionKey(option.OptionKey, index),
                Text = Clean(option.Text),
                IsCorrect = option.IsCorrect,
                SortOrder = option.SortOrder == 0 ? index : option.SortOrder,
                Rationale = SafeOptional(option.Rationale),
                MisconceptionKey = SafeOptional(option.MisconceptionKey),
                DiagnosticSignalJson = SafeAssessmentMetadataJson(option.DiagnosticSignalJson)
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
        ImportFormat = preview.ImportFormat,
        PackageTitle = preview.PackageTitle,
        PackageVersion = preview.PackageVersion,
        TotalCount = preview.TotalCount,
        AcceptedCount = preview.AcceptedCount,
        RejectedCount = preview.RejectedCount,
        WarningCount = preview.WarningCount,
        CreatedAt = preview.CreatedAt,
        ExpiresAt = preview.ExpiresAt,
        Assets = DeserializeRichPackage(preview.NormalizedPackageJson)?.Assets ?? [],
        Stimuli = DeserializeRichPackage(preview.NormalizedPackageJson)?.Stimuli ?? [],
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

    private static NormalizedRichImportPackage? DeserializeRichPackage(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<NormalizedRichImportPackage>(json, JsonOptions);
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

    private static string? SafeContentJson(string? value) =>
        LearnerSafeContentJson.Sanitize(SafeOptional(value));

    private static string? SafeAssessmentMetadataJson(string? value) =>
        LearnerSafeContentJson.SanitizeAssessmentMetadata(SafeOptional(value));

    private sealed record ResolvedImportLinks(
        Guid ExamDefinitionId,
        Guid? ExamVariantId,
        Guid? ExamSectionId,
        Guid? ExamSubjectId,
        Guid? ExamTopicId,
        Guid? ExamOutcomeId);

    private sealed record NormalizedRichImportPackage(
        List<QuestionImportAssetDto> Assets,
        List<QuestionImportStimulusDto> Stimuli,
        List<QuestionImportRichQuestionDto> Questions);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[^A-Z0-9_]+")]
    private static partial Regex CodeUnsafeCharacters();
}

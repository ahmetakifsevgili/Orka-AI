using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class CurriculumSourceRegistryService : ICurriculumSourceRegistryService
{
    private static readonly HashSet<string> VerificationStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "unverified",
        "source_backed",
        "official_source_backed",
        "official_verified",
        "deprecated",
        "superseded"
    };

    private static readonly HashSet<string> SourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "osym_guide",
        "osym_question_sample",
        "meb_curriculum",
        "meb_sample_question",
        "meb_textbook_reference",
        "open_reference",
        "user_reference",
        "publisher_reference",
        "internal_outline"
    };

    private static readonly HashSet<string> OfficialSourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "osym_guide",
        "osym_question_sample",
        "meb_curriculum",
        "meb_sample_question",
        "meb_textbook_reference"
    };

    private static readonly HashSet<string> CurriculumLifecycleStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft",
        "active",
        "deprecated",
        "superseded",
        "archived"
    };

    private static readonly HashSet<string> CurriculumNodeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "domain",
        "section",
        "subject",
        "unit",
        "topic",
        "subtopic",
        "outcome_group",
        "outcome"
    };

    private static readonly HashSet<string> MappingTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "direct",
        "inferred",
        "equivalent",
        "prerequisite",
        "related"
    };

    private static readonly HashSet<string> MappingConfidenceStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "high",
        "medium",
        "low",
        "needs_review"
    };

    private static readonly HashSet<string> MappingReviewStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft",
        "needs_review",
        "approved",
        "rejected"
    };

    private static readonly HashSet<string> SafeLicenseStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "user_provided",
        "licensed",
        "open",
        "official_public_reference"
    };

    private readonly OrkaDbContext _db;

    public CurriculumSourceRegistryService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SourceRegistryItemDto>> GetSourcesAsync(Guid userId, CancellationToken ct = default)
    {
        var sources = await _db.SourceRegistryItems
            .AsNoTracking()
            .Include(s => s.LicenseReviews.Where(r => !r.IsDeleted))
            .Where(s => !s.IsDeleted && (s.OwnerUserId == null || s.OwnerUserId == userId))
            .OrderBy(s => s.SourceKey)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync(ct);

        return sources.Select(ToDto).ToList();
    }

    public async Task<SourceRegistryItemDto?> GetSourceAsync(Guid userId, Guid sourceId, CancellationToken ct = default)
    {
        var source = await _db.SourceRegistryItems
            .AsNoTracking()
            .Include(s => s.LicenseReviews.Where(r => !r.IsDeleted))
            .FirstOrDefaultAsync(s => s.Id == sourceId && !s.IsDeleted && (s.OwnerUserId == null || s.OwnerUserId == userId), ct);

        return source is null ? null : ToDto(source);
    }

    public async Task<SourceRegistryItemDto> RegisterSourceAsync(
        Guid userId,
        RegisterSourceRegistryItemDto request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SourceKey))
        {
            throw new ArgumentException("source_key_required");
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("source_title_required");
        }

        if (!IsValidOptionalUrl(request.SourceUrl))
        {
            throw new ArgumentException("source_url_invalid");
        }

        var sourceType = NormalizeSourceType(request.SourceType);
        var verificationStatus = NormalizeBounded(request.VerificationStatus, VerificationStatuses, "unverified");
        var canClaimOfficial = CanClaimOfficial(verificationStatus, request.SourceUrl, sourceType);
        if (verificationStatus == "official_verified" && !canClaimOfficial)
        {
            throw new ArgumentException("official_verified_requires_official_source_metadata");
        }

        var now = DateTime.UtcNow;
        var source = new SourceRegistryItem
        {
            OwnerUserId = userId,
            SourceKey = NormalizeCode(request.SourceKey),
            Title = Clean(request.Title),
            SourceUrl = CleanOptional(request.SourceUrl),
            SourceType = sourceType,
            Publisher = CleanOptional(request.Publisher, "unknown"),
            LicenseStatus = NormalizeSimple(request.LicenseStatus, "unknown"),
            VerificationStatus = verificationStatus,
            OfficialClaimAllowed = canClaimOfficial,
            SourceContentHash = CleanOptional(request.SourceContentHash),
            VerifiedAt = canClaimOfficial ? now : null,
            VerifiedBy = canClaimOfficial ? "source_registry" : null,
            Visibility = "user",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.SourceRegistryItems.Add(source);
        if (verificationStatus != "unverified")
        {
            AddVerificationRecord(source, verificationStatus, "registration_metadata", null, null, now);
        }

        await _db.SaveChangesAsync(ct);
        return (await GetSourceAsync(userId, source.Id, ct))!;
    }

    public async Task<SourceRegistryItemDto?> VerifySourceAsync(
        Guid userId,
        Guid sourceId,
        VerifySourceRegistryItemDto request,
        CancellationToken ct = default)
    {
        var source = await _db.SourceRegistryItems
            .Include(s => s.LicenseReviews.Where(r => !r.IsDeleted))
            .FirstOrDefaultAsync(s => s.Id == sourceId && !s.IsDeleted && (s.OwnerUserId == null || s.OwnerUserId == userId), ct);

        if (source is null)
        {
            return null;
        }

        var verificationStatus = NormalizeBounded(request.VerificationStatus, VerificationStatuses, "source_backed");
        var canClaimOfficial = CanClaimOfficial(verificationStatus, source.SourceUrl, source.SourceType);
        if (verificationStatus == "official_verified" && !canClaimOfficial)
        {
            throw new ArgumentException("official_verified_requires_official_source_metadata");
        }

        var now = DateTime.UtcNow;
        source.VerificationStatus = verificationStatus;
        source.OfficialClaimAllowed = canClaimOfficial;
        source.VerifiedAt = verificationStatus == "unverified" ? null : now;
        source.VerifiedBy = verificationStatus == "unverified" ? null : "content_review";
        source.UpdatedAt = now;

        var record = AddVerificationRecord(
            source,
            verificationStatus,
            NormalizeSimple(request.VerificationMethod, "manual_review"),
            CleanOptional(request.EvidenceLocator),
            CleanOptional(request.InternalNotes),
            now);

        _db.OfficialClaimPolicies.Add(new OfficialClaimPolicy
        {
            EntityType = "source_registry_item",
            EntityId = source.Id,
            ClaimType = "official_curriculum",
            IsAllowed = canClaimOfficial,
            Reason = canClaimOfficial
                ? "Verified official-source metadata is present."
                : "Official claims remain disabled until verified official-source metadata is present.",
            SourceVerificationRecord = record,
            CreatedAt = now
        });

        await _db.SaveChangesAsync(ct);
        return ToDto(source);
    }

    public async Task<ContentLicenseReviewDto?> ReviewSourceLicenseAsync(
        Guid userId,
        Guid sourceId,
        ReviewSourceLicenseDto request,
        CancellationToken ct = default)
    {
        var source = await _db.SourceRegistryItems
            .FirstOrDefaultAsync(s => s.Id == sourceId && !s.IsDeleted && (s.OwnerUserId == null || s.OwnerUserId == userId), ct);

        if (source is null)
        {
            return null;
        }

        var licenseStatus = NormalizeSimple(request.LicenseStatus, "unknown");
        var reviewStatus = NormalizeSimple(request.ReviewStatus, "pending");
        var publishAllowed = reviewStatus == "approved" && SafeLicenseStatuses.Contains(licenseStatus);

        source.LicenseStatus = licenseStatus;
        source.UpdatedAt = DateTime.UtcNow;

        var review = new ContentLicenseReview
        {
            SourceRegistryItemId = source.Id,
            ReviewedByUserId = userId,
            LicenseStatus = licenseStatus,
            ReviewStatus = reviewStatus,
            PublishAllowed = publishAllowed,
            DecisionReason = CleanOptional(request.DecisionReason, publishAllowed ? "License review allows publication." : "License review does not allow publication."),
            CreatedAt = DateTime.UtcNow
        };

        _db.ContentLicenseReviews.Add(review);
        await _db.SaveChangesAsync(ct);
        return ToDto(review);
    }

    public async Task<CurriculumVersionDto> CreateCurriculumVersionAsync(
        Guid userId,
        CreateCurriculumVersionDto request,
        CancellationToken ct = default)
    {
        if (request.ExamDefinitionId == Guid.Empty)
        {
            throw new ArgumentException("exam_definition_required");
        }

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("curriculum_code_and_name_required");
        }

        var exam = await _db.ExamDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.ExamDefinitionId && !e.IsDeleted && (e.OwnerUserId == null || e.OwnerUserId == userId), ct);

        if (exam is null)
        {
            throw new ArgumentException("exam_definition_not_visible");
        }

        SourceRegistryItem? source = null;
        if (request.SourceRegistryItemId is Guid sourceId)
        {
            source = await _db.SourceRegistryItems
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sourceId && !s.IsDeleted && (s.OwnerUserId == null || s.OwnerUserId == userId), ct);

            if (source is null)
            {
                throw new ArgumentException("source_not_visible");
            }
        }

        var status = NormalizeBounded(request.Status, CurriculumLifecycleStatuses, "draft");
        var verificationStatus = NormalizeBounded(request.VerificationStatus, VerificationStatuses, "source_backed");
        var canClaimOfficial = AllowsOfficialClaimByLifecycle(status)
                               && verificationStatus == "official_verified"
                               && source?.OfficialClaimAllowed == true;
        if (verificationStatus == "official_verified" && !canClaimOfficial)
        {
            throw new ArgumentException("official_curriculum_version_requires_verified_source");
        }

        var now = DateTime.UtcNow;
        var version = new CurriculumVersion
        {
            ExamDefinitionId = request.ExamDefinitionId,
            SourceRegistryItemId = source?.Id,
            OwnerUserId = userId,
            Code = NormalizeCode(request.Code),
            Name = Clean(request.Name),
            Description = CleanOptional(request.Description),
            VersionLabel = CleanOptional(request.VersionLabel, "v1"),
            Status = status,
            VerificationStatus = verificationStatus,
            OfficialClaimAllowed = canClaimOfficial,
            SourceSnapshotHash = source?.SourceContentHash,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveUntil = request.EffectiveUntil,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (status == "active")
        {
            var sourceScopeId = source?.Id;
            var activeVersions = await _db.CurriculumVersions
                .Where(v => !v.IsDeleted
                            && v.ExamDefinitionId == request.ExamDefinitionId
                            && v.OwnerUserId == userId
                            && v.SourceRegistryItemId == sourceScopeId
                            && v.Status == "active")
                .ToListAsync(ct);

            foreach (var active in activeVersions)
            {
                active.Status = "superseded";
                active.SupersededByCurriculumVersionId = version.Id;
                active.DeprecatedAt = now;
                active.DeprecatedReason = "Replaced by a newer active curriculum version in the same source scope.";
                active.OfficialClaimAllowed = false;
                active.UpdatedAt = now;
            }
        }

        _db.CurriculumVersions.Add(version);
        _db.OfficialClaimPolicies.Add(new OfficialClaimPolicy
        {
            EntityType = "curriculum_version",
            EntityId = version.Id,
            ClaimType = "official_curriculum",
            IsAllowed = canClaimOfficial,
            Reason = canClaimOfficial
                ? "Curriculum version is linked to verified official source metadata."
                : "Curriculum version has no verified official source claim.",
            CreatedAt = now
        });

        await _db.SaveChangesAsync(ct);
        return (await GetCurriculumVersionAsync(userId, version.Id, ct))!;
    }

    public async Task<CurriculumVersionDto?> GetCurriculumVersionAsync(
        Guid userId,
        Guid curriculumVersionId,
        CancellationToken ct = default)
    {
        var version = await _db.CurriculumVersions
            .AsNoTracking()
            .Include(v => v.Nodes.Where(n => !n.IsDeleted))
            .FirstOrDefaultAsync(v => v.Id == curriculumVersionId && !v.IsDeleted && (v.OwnerUserId == null || v.OwnerUserId == userId), ct);

        return version is null ? null : ToDto(version);
    }

    public async Task<IReadOnlyList<CurriculumVersionDto>> GetCurriculumVersionsForExamAsync(
        Guid userId,
        string examCode,
        CancellationToken ct = default)
    {
        var normalizedExamCode = NormalizeCode(examCode);
        var versions = await _db.CurriculumVersions
            .AsNoTracking()
            .Include(v => v.Nodes.Where(n => !n.IsDeleted))
            .Include(v => v.ExamDefinition)
            .Where(v => !v.IsDeleted
                        && v.ExamDefinition.Code == normalizedExamCode
                        && !v.ExamDefinition.IsDeleted
                        && (v.ExamDefinition.OwnerUserId == null || v.ExamDefinition.OwnerUserId == userId)
                        && (v.OwnerUserId == null || v.OwnerUserId == userId))
            .OrderByDescending(v => v.Status == "active")
            .ThenBy(v => v.Code)
            .ToListAsync(ct);

        return versions.Select(ToDto).ToList();
    }

    public async Task<CurriculumVersionDto?> DeprecateCurriculumVersionAsync(
        Guid userId,
        Guid curriculumVersionId,
        DeprecateCurriculumVersionDto request,
        CancellationToken ct = default)
    {
        var version = await _db.CurriculumVersions
            .FirstOrDefaultAsync(v => v.Id == curriculumVersionId && !v.IsDeleted && (v.OwnerUserId == null || v.OwnerUserId == userId), ct);

        if (version is null)
        {
            return null;
        }

        version.Status = "deprecated";
        version.OfficialClaimAllowed = false;
        version.DeprecatedAt = DateTime.UtcNow;
        version.DeprecatedReason = CleanOptional(request.DeprecatedReason, "Deprecated by curriculum review.");
        version.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await GetCurriculumVersionAsync(userId, version.Id, ct);
    }

    public async Task<CurriculumVersionDto?> SupersedeCurriculumVersionAsync(
        Guid userId,
        Guid curriculumVersionId,
        SupersedeCurriculumVersionDto request,
        CancellationToken ct = default)
    {
        if (request.ReplacementCurriculumVersionId == Guid.Empty)
        {
            throw new ArgumentException("replacement_curriculum_version_required");
        }

        var version = await _db.CurriculumVersions
            .FirstOrDefaultAsync(v => v.Id == curriculumVersionId && !v.IsDeleted && (v.OwnerUserId == null || v.OwnerUserId == userId), ct);

        if (version is null)
        {
            return null;
        }

        var replacement = await _db.CurriculumVersions
            .FirstOrDefaultAsync(v => v.Id == request.ReplacementCurriculumVersionId
                                      && !v.IsDeleted
                                      && v.ExamDefinitionId == version.ExamDefinitionId
                                      && (v.OwnerUserId == null || v.OwnerUserId == userId), ct);

        if (replacement is null)
        {
            throw new ArgumentException("replacement_curriculum_version_not_visible");
        }

        version.Status = "superseded";
        version.SupersededByCurriculumVersionId = replacement.Id;
        version.OfficialClaimAllowed = false;
        version.DeprecatedAt = DateTime.UtcNow;
        version.DeprecatedReason = CleanOptional(request.DeprecatedReason, "Superseded by replacement curriculum version.");
        version.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return await GetCurriculumVersionAsync(userId, version.Id, ct);
    }

    public async Task<CurriculumNodeDto?> AddCurriculumNodeAsync(
        Guid userId,
        Guid curriculumVersionId,
        CreateCurriculumNodeDto request,
        CancellationToken ct = default)
    {
        var version = await _db.CurriculumVersions
            .FirstOrDefaultAsync(v => v.Id == curriculumVersionId && !v.IsDeleted && (v.OwnerUserId == null || v.OwnerUserId == userId), ct);

        if (version is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("curriculum_node_code_and_title_required");
        }

        if (request.ParentCurriculumNodeId is Guid parentId)
        {
            var parentExists = await _db.CurriculumNodes
                .AnyAsync(n => n.Id == parentId && n.CurriculumVersionId == curriculumVersionId && !n.IsDeleted, ct);
            if (!parentExists)
            {
                throw new ArgumentException("parent_curriculum_node_not_visible");
            }
        }

        var normalizedCode = NormalizeCode(request.Code);
        var duplicateSibling = await _db.CurriculumNodes
            .AnyAsync(n => n.CurriculumVersionId == curriculumVersionId
                           && n.ParentCurriculumNodeId == request.ParentCurriculumNodeId
                           && n.Code == normalizedCode
                           && !n.IsDeleted, ct);
        if (duplicateSibling)
        {
            throw new ArgumentException("duplicate_curriculum_node_sibling_code");
        }

        var nodeType = NormalizeBounded(request.NodeType, CurriculumNodeTypes, "topic");
        var verificationStatus = NormalizeBounded(request.VerificationStatus, VerificationStatuses, version.VerificationStatus);
        var canClaimOfficial = verificationStatus == "official_verified"
                               && AllowsOfficialClaimByLifecycle(version.Status)
                               && version.OfficialClaimAllowed;
        if (verificationStatus == "official_verified" && !canClaimOfficial)
        {
            throw new ArgumentException("official_curriculum_node_requires_verified_source");
        }

        var node = new CurriculumNode
        {
            CurriculumVersionId = curriculumVersionId,
            ParentCurriculumNodeId = request.ParentCurriculumNodeId,
            NodeType = nodeType,
            Code = normalizedCode,
            Title = Clean(request.Title),
            Description = CleanOptional(request.Description),
            VerificationStatus = verificationStatus,
            OfficialClaimAllowed = canClaimOfficial,
            SourceAnchor = CleanOptional(request.SourceAnchor),
            SourceLocator = CleanOptional(request.SourceLocator),
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.CurriculumNodes.Add(node);
        await _db.SaveChangesAsync(ct);
        return ToDto(node);
    }

    public async Task<CurriculumOutcomeMappingDto?> MapOutcomeAsync(
        Guid userId,
        Guid curriculumVersionId,
        CreateCurriculumOutcomeMappingDto request,
        CancellationToken ct = default)
    {
        var version = await _db.CurriculumVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == curriculumVersionId && !v.IsDeleted && (v.OwnerUserId == null || v.OwnerUserId == userId), ct);

        if (version is null)
        {
            return null;
        }

        var node = await _db.CurriculumNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == request.CurriculumNodeId && n.CurriculumVersionId == curriculumVersionId && !n.IsDeleted, ct);

        if (node is null)
        {
            throw new ArgumentException("curriculum_node_not_visible");
        }

        var outcomeVisible = await IsExamOutcomeVisibleAsync(userId, request.ExamOutcomeId, ct);
        if (!outcomeVisible)
        {
            throw new ArgumentException("exam_outcome_not_visible");
        }

        if (!IsValidOptionalUrl(request.EvidenceUrl))
        {
            throw new ArgumentException("evidence_url_invalid");
        }

        SourceRegistryItem? source = null;
        if (request.SourceRegistryItemId is Guid sourceId)
        {
            source = await _db.SourceRegistryItems
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sourceId && !s.IsDeleted && (s.OwnerUserId == null || s.OwnerUserId == userId), ct);
            if (source is null)
            {
                throw new ArgumentException("source_not_visible");
            }
        }

        var verificationStatus = NormalizeBounded(request.VerificationStatus, VerificationStatuses, "source_backed");
        var mappingType = NormalizeBounded(request.MappingType, MappingTypes, "direct");
        var confidenceStatus = NormalizeBounded(request.ConfidenceStatus, MappingConfidenceStatuses, "medium");
        var reviewStatus = NormalizeBounded(request.ReviewStatus, MappingReviewStatuses, "draft");
        var canClaimOfficial = verificationStatus == "official_verified"
                               && AllowsOfficialClaimByLifecycle(version.Status)
                               && version.OfficialClaimAllowed
                               && node.OfficialClaimAllowed
                               && (source is null || source.OfficialClaimAllowed);

        if (verificationStatus == "official_verified" && !canClaimOfficial)
        {
            throw new ArgumentException("official_outcome_mapping_requires_verified_source");
        }

        var mapping = new CurriculumOutcomeMapping
        {
            CurriculumVersionId = curriculumVersionId,
            CurriculumNodeId = node.Id,
            ExamOutcomeId = request.ExamOutcomeId,
            SourceRegistryItemId = source?.Id ?? version.SourceRegistryItemId,
            MappingType = mappingType,
            ConfidenceStatus = confidenceStatus,
            ReviewStatus = reviewStatus,
            VerificationStatus = verificationStatus,
            OfficialClaimAllowed = canClaimOfficial,
            SourceLocator = CleanOptional(request.SourceLocator),
            PageNumber = request.PageNumber,
            SectionTitle = CleanOptional(request.SectionTitle),
            Clause = CleanOptional(request.Clause),
            AnchorText = CleanOptional(request.AnchorText),
            EvidenceUrl = CleanOptional(request.EvidenceUrl),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.CurriculumOutcomeMappings.Add(mapping);
        await _db.SaveChangesAsync(ct);
        return ToDto(mapping);
    }

    public async Task<CurriculumOutcomeSourceDto> GetOutcomeSourcesAsync(
        Guid userId,
        Guid examOutcomeId,
        CancellationToken ct = default)
    {
        if (!await IsExamOutcomeVisibleAsync(userId, examOutcomeId, ct))
        {
            return new CurriculumOutcomeSourceDto { ExamOutcomeId = examOutcomeId };
        }

        var mappings = await _db.CurriculumOutcomeMappings
            .AsNoTracking()
            .Include(m => m.CurriculumVersion)
            .Include(m => m.CurriculumNode)
            .Include(m => m.SourceRegistryItem)
            .Where(m => !m.IsDeleted
                        && m.ExamOutcomeId == examOutcomeId
                        && !m.CurriculumVersion.IsDeleted
                        && !m.CurriculumNode.IsDeleted
                        && (m.CurriculumVersion.OwnerUserId == null || m.CurriculumVersion.OwnerUserId == userId)
                        && (m.SourceRegistryItem == null || m.SourceRegistryItem.OwnerUserId == null || m.SourceRegistryItem.OwnerUserId == userId))
            .OrderByDescending(m => m.OfficialClaimAllowed)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return new CurriculumOutcomeSourceDto
        {
            ExamOutcomeId = examOutcomeId,
            Mappings = mappings.Select(ToDto).ToList()
        };
    }

    private async Task<bool> IsExamOutcomeVisibleAsync(Guid userId, Guid examOutcomeId, CancellationToken ct)
    {
        return await _db.ExamOutcomes
            .AsNoTracking()
            .Include(o => o.ExamTopic)
            .ThenInclude(t => t.ExamSubject)
            .ThenInclude(s => s.ExamSection)
            .ThenInclude(s => s.ExamVariant)
            .ThenInclude(v => v.ExamDefinition)
            .AnyAsync(o => o.Id == examOutcomeId
                           && !o.IsDeleted
                           && !o.ExamTopic.IsDeleted
                           && !o.ExamTopic.ExamSubject.IsDeleted
                           && !o.ExamTopic.ExamSubject.ExamSection.IsDeleted
                           && !o.ExamTopic.ExamSubject.ExamSection.ExamVariant.IsDeleted
                           && !o.ExamTopic.ExamSubject.ExamSection.ExamVariant.ExamDefinition.IsDeleted
                           && (o.ExamTopic.ExamSubject.ExamSection.ExamVariant.ExamDefinition.OwnerUserId == null
                               || o.ExamTopic.ExamSubject.ExamSection.ExamVariant.ExamDefinition.OwnerUserId == userId),
                ct);
    }

    private static SourceVerificationRecord AddVerificationRecord(
        SourceRegistryItem source,
        string verificationStatus,
        string verificationMethod,
        string? evidenceLocator,
        string? internalNotes,
        DateTime now)
    {
        var record = new SourceVerificationRecord
        {
            SourceRegistryItem = source,
            VerificationStatus = verificationStatus,
            VerificationMethod = verificationMethod,
            EvidenceLocator = evidenceLocator,
            InternalNotes = internalNotes,
            VerifiedBy = "content_review",
            VerifiedAt = now
        };

        source.VerificationRecords.Add(record);
        return record;
    }

    private static SourceRegistryItemDto ToDto(SourceRegistryItem source) => new()
    {
        Id = source.Id,
        OwnershipState = source.OwnerUserId is null ? "system" : "user",
        SourceKey = source.SourceKey,
        Title = source.Title,
        SourceUrl = source.SourceUrl,
        SourceType = source.SourceType,
        Publisher = source.Publisher,
        LicenseStatus = source.LicenseStatus,
        VerificationStatus = source.VerificationStatus,
        CanClaimOfficial = source.OfficialClaimAllowed,
        UserSafeVerificationLabel = VerificationLabel(source.VerificationStatus, source.OfficialClaimAllowed),
        SourceContentHash = source.SourceContentHash,
        VerifiedAt = source.VerifiedAt,
        Visibility = source.Visibility,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        LicenseReviews = source.LicenseReviews
            .Where(r => !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .Select(ToDto)
            .ToList()
    };

    private static ContentLicenseReviewDto ToDto(ContentLicenseReview review) => new()
    {
        Id = review.Id,
        LicenseStatus = review.LicenseStatus,
        ReviewStatus = review.ReviewStatus,
        PublishAllowed = review.PublishAllowed,
        DecisionReason = review.DecisionReason,
        CreatedAt = review.CreatedAt
    };

    private static CurriculumVersionDto ToDto(CurriculumVersion version)
    {
        var nodes = version.Nodes
            .Where(n => !n.IsDeleted)
            .OrderBy(n => n.SortOrder)
            .ThenBy(n => n.Code)
            .ToList();

        return new CurriculumVersionDto
        {
            Id = version.Id,
            ExamDefinitionId = version.ExamDefinitionId,
            SourceRegistryItemId = version.SourceRegistryItemId,
            OwnershipState = version.OwnerUserId is null ? "system" : "user",
            Code = version.Code,
            Name = version.Name,
            Description = version.Description,
            VersionLabel = version.VersionLabel,
            Status = version.Status,
            VerificationStatus = version.VerificationStatus,
            CanClaimOfficial = version.OfficialClaimAllowed,
            UserSafeVerificationLabel = VerificationLabel(version.VerificationStatus, version.OfficialClaimAllowed),
            SourceSnapshotHash = version.SourceSnapshotHash,
            SupersededByCurriculumVersionId = version.SupersededByCurriculumVersionId,
            DeprecatedAt = version.DeprecatedAt,
            DeprecatedReason = version.DeprecatedReason,
            ArchivedAt = version.ArchivedAt,
            EffectiveFrom = version.EffectiveFrom,
            EffectiveUntil = version.EffectiveUntil,
            CreatedAt = version.CreatedAt,
            UpdatedAt = version.UpdatedAt,
            Nodes = BuildNodeTree(nodes, null)
        };
    }

    private static List<CurriculumNodeDto> BuildNodeTree(IReadOnlyList<CurriculumNode> nodes, Guid? parentId) =>
        nodes
            .Where(n => n.ParentCurriculumNodeId == parentId)
            .Select(n =>
            {
                var dto = ToDto(n);
                dto.Children = BuildNodeTree(nodes, n.Id);
                return dto;
            })
            .ToList();

    private static CurriculumNodeDto ToDto(CurriculumNode node) => new()
    {
        Id = node.Id,
        CurriculumVersionId = node.CurriculumVersionId,
        ParentCurriculumNodeId = node.ParentCurriculumNodeId,
        NodeType = node.NodeType,
        Code = node.Code,
        Title = node.Title,
        Description = node.Description,
        VerificationStatus = node.VerificationStatus,
        CanClaimOfficial = node.OfficialClaimAllowed,
        SourceAnchor = node.SourceAnchor,
        SourceLocator = node.SourceLocator,
        SortOrder = node.SortOrder
    };

    private static CurriculumOutcomeMappingDto ToDto(CurriculumOutcomeMapping mapping) => new()
    {
        Id = mapping.Id,
        CurriculumVersionId = mapping.CurriculumVersionId,
        CurriculumNodeId = mapping.CurriculumNodeId,
        ExamOutcomeId = mapping.ExamOutcomeId,
        SourceRegistryItemId = mapping.SourceRegistryItemId,
        MappingType = mapping.MappingType,
        ConfidenceStatus = mapping.ConfidenceStatus,
        ReviewStatus = mapping.ReviewStatus,
        VerificationStatus = mapping.VerificationStatus,
        CanClaimOfficial = mapping.OfficialClaimAllowed,
        SourceLocator = mapping.SourceLocator,
        PageNumber = mapping.PageNumber,
        SectionTitle = mapping.SectionTitle,
        Clause = mapping.Clause,
        AnchorText = mapping.AnchorText,
        EvidenceUrl = mapping.EvidenceUrl,
        UserSafeVerificationLabel = VerificationLabel(mapping.VerificationStatus, mapping.OfficialClaimAllowed),
        CreatedAt = mapping.CreatedAt
    };

    private static bool CanClaimOfficial(string verificationStatus, string? sourceUrl, string sourceType)
    {
        if (!string.Equals(verificationStatus, "official_verified", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!OfficialSourceTypes.Contains(sourceType))
        {
            return false;
        }

        return Uri.TryCreate(sourceUrl?.Trim(), UriKind.Absolute, out var uri)
               && IsOfficialExamSourceHost(uri.Host);
    }

    private static bool IsOfficialExamSourceHost(string host)
    {
        var normalized = host.Trim().ToLowerInvariant();
        return normalized == "osym.gov.tr"
               || normalized.EndsWith(".osym.gov.tr", StringComparison.Ordinal)
               || normalized == "meb.gov.tr"
               || normalized.EndsWith(".meb.gov.tr", StringComparison.Ordinal);
    }

    private static string VerificationLabel(string status, bool canClaimOfficial)
    {
        if (canClaimOfficial)
        {
            return "Doğrulanmış resmi kaynak metadata'sı mevcut.";
        }

        return status == "source_backed"
            ? "Kaynak destekli ancak resmi müfredat iddiası değildir."
            : "Resmi müfredat iddiası değildir; doğrulanmış kaynak eklendiğinde resmi kaynak etiketi gösterilir.";
    }

    private static bool AllowsOfficialClaimByLifecycle(string status)
    {
        return string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSourceType(string? value)
    {
        var normalized = NormalizeSimple(value, "user_reference");
        if (normalized == "curriculum")
        {
            return "user_reference";
        }

        return SourceTypes.Contains(normalized) ? normalized : "user_reference";
    }

    private static string NormalizeBounded(string? value, HashSet<string> allowed, string fallback)
    {
        var normalized = NormalizeSimple(value, fallback);
        return allowed.Contains(normalized) ? normalized : fallback;
    }

    private static string NormalizeSimple(string? value, string fallback)
    {
        var cleaned = CleanOptional(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return fallback;
        }

        return cleaned.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static string NormalizeCode(string value)
    {
        return new string((value ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray())
            .Trim('_');
    }

    private static string Clean(string value) => value.Trim();

    private static string CleanOptional(string? value, string fallback = "")
    {
        var cleaned = value?.Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static bool IsValidOptionalUrl(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
               || (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));
    }
}

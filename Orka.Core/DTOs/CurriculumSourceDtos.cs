namespace Orka.Core.DTOs;

public sealed class SourceRegistryItemDto
{
    public Guid Id { get; set; }
    public string OwnershipState { get; set; } = "user";
    public string SourceKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string SourceType { get; set; } = "curriculum";
    public string Publisher { get; set; } = string.Empty;
    public string LicenseStatus { get; set; } = "unknown";
    public string VerificationStatus { get; set; } = "unverified";
    public bool CanClaimOfficial { get; set; }
    public string UserSafeVerificationLabel { get; set; } = string.Empty;
    public string? SourceContentHash { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string Visibility { get; set; } = "user";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ContentLicenseReviewDto> LicenseReviews { get; set; } = [];
}

public sealed class RegisterSourceRegistryItemDto
{
    public string SourceKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string SourceType { get; set; } = "curriculum";
    public string Publisher { get; set; } = string.Empty;
    public string LicenseStatus { get; set; } = "unknown";
    public string VerificationStatus { get; set; } = "unverified";
    public string? SourceContentHash { get; set; }
}

public sealed class VerifySourceRegistryItemDto
{
    public string VerificationStatus { get; set; } = "source_backed";
    public string VerificationMethod { get; set; } = "manual_review";
    public string? EvidenceLocator { get; set; }
    public string? InternalNotes { get; set; }
}

public sealed class ReviewSourceLicenseDto
{
    public string LicenseStatus { get; set; } = "unknown";
    public string ReviewStatus { get; set; } = "pending";
    public string DecisionReason { get; set; } = string.Empty;
}

public sealed class ContentLicenseReviewDto
{
    public Guid Id { get; set; }
    public string LicenseStatus { get; set; } = "unknown";
    public string ReviewStatus { get; set; } = "pending";
    public bool PublishAllowed { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class CurriculumVersionDto
{
    public Guid Id { get; set; }
    public Guid ExamDefinitionId { get; set; }
    public Guid? SourceRegistryItemId { get; set; }
    public string OwnershipState { get; set; } = "user";
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string VersionLabel { get; set; } = string.Empty;
    public string Status { get; set; } = "draft";
    public string VerificationStatus { get; set; } = "unverified";
    public bool CanClaimOfficial { get; set; }
    public string UserSafeVerificationLabel { get; set; } = string.Empty;
    public string? SourceSnapshotHash { get; set; }
    public Guid? SupersededByCurriculumVersionId { get; set; }
    public DateTime? DeprecatedAt { get; set; }
    public string? DeprecatedReason { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveUntil { get; set; }
    public List<CurriculumNodeDto> Nodes { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CreateCurriculumVersionDto
{
    public Guid ExamDefinitionId { get; set; }
    public Guid? SourceRegistryItemId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string VersionLabel { get; set; } = string.Empty;
    public string Status { get; set; } = "draft";
    public string VerificationStatus { get; set; } = "source_backed";
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveUntil { get; set; }
}

public sealed class DeprecateCurriculumVersionDto
{
    public string DeprecatedReason { get; set; } = string.Empty;
}

public sealed class SupersedeCurriculumVersionDto
{
    public Guid ReplacementCurriculumVersionId { get; set; }
    public string DeprecatedReason { get; set; } = string.Empty;
}

public sealed class CurriculumNodeDto
{
    public Guid Id { get; set; }
    public Guid CurriculumVersionId { get; set; }
    public Guid? ParentCurriculumNodeId { get; set; }
    public string NodeType { get; set; } = "topic";
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = "unverified";
    public bool CanClaimOfficial { get; set; }
    public string? SourceAnchor { get; set; }
    public string? SourceLocator { get; set; }
    public int SortOrder { get; set; }
    public List<CurriculumNodeDto> Children { get; set; } = [];
}

public sealed class CreateCurriculumNodeDto
{
    public Guid? ParentCurriculumNodeId { get; set; }
    public string NodeType { get; set; } = "topic";
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = "source_backed";
    public string? SourceAnchor { get; set; }
    public string? SourceLocator { get; set; }
    public int SortOrder { get; set; }
}

public sealed class CurriculumOutcomeMappingDto
{
    public Guid Id { get; set; }
    public Guid CurriculumVersionId { get; set; }
    public Guid CurriculumNodeId { get; set; }
    public Guid ExamOutcomeId { get; set; }
    public Guid? SourceRegistryItemId { get; set; }
    public string MappingType { get; set; } = "direct";
    public string ConfidenceStatus { get; set; } = "medium";
    public string ReviewStatus { get; set; } = "draft";
    public string VerificationStatus { get; set; } = "source_backed";
    public bool CanClaimOfficial { get; set; }
    public string? SourceLocator { get; set; }
    public int? PageNumber { get; set; }
    public string? SectionTitle { get; set; }
    public string? Clause { get; set; }
    public string? AnchorText { get; set; }
    public string? EvidenceUrl { get; set; }
    public string UserSafeVerificationLabel { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class CreateCurriculumOutcomeMappingDto
{
    public Guid CurriculumNodeId { get; set; }
    public Guid ExamOutcomeId { get; set; }
    public Guid? SourceRegistryItemId { get; set; }
    public string MappingType { get; set; } = "direct";
    public string ConfidenceStatus { get; set; } = "medium";
    public string ReviewStatus { get; set; } = "draft";
    public string VerificationStatus { get; set; } = "source_backed";
    public string? SourceLocator { get; set; }
    public int? PageNumber { get; set; }
    public string? SectionTitle { get; set; }
    public string? Clause { get; set; }
    public string? AnchorText { get; set; }
    public string? EvidenceUrl { get; set; }
}

public sealed class CurriculumOutcomeSourceDto
{
    public Guid ExamOutcomeId { get; set; }
    public List<CurriculumOutcomeMappingDto> Mappings { get; set; } = [];
}

namespace Orka.Core.Entities;

public sealed class SourceRegistryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OwnerUserId { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string SourceType { get; set; } = "curriculum";
    public string Publisher { get; set; } = string.Empty;
    public string LicenseStatus { get; set; } = "unknown";
    public string VerificationStatus { get; set; } = "unverified";
    public bool OfficialClaimAllowed { get; set; }
    public string? SourceContentHash { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public string Visibility { get; set; } = "user";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public User? OwnerUser { get; set; }
    public ICollection<CurriculumVersion> CurriculumVersions { get; set; } = [];
    public ICollection<SourceVerificationRecord> VerificationRecords { get; set; } = [];
    public ICollection<ContentLicenseReview> LicenseReviews { get; set; } = [];
}

public sealed class CurriculumVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamDefinitionId { get; set; }
    public Guid? SourceRegistryItemId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string VersionLabel { get; set; } = string.Empty;
    public string Status { get; set; } = "draft";
    public string VerificationStatus { get; set; } = "unverified";
    public bool OfficialClaimAllowed { get; set; }
    public string? SourceSnapshotHash { get; set; }
    public Guid? SupersededByCurriculumVersionId { get; set; }
    public DateTime? DeprecatedAt { get; set; }
    public string? DeprecatedReason { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveUntil { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public ExamDefinition ExamDefinition { get; set; } = null!;
    public SourceRegistryItem? SourceRegistryItem { get; set; }
    public User? OwnerUser { get; set; }
    public CurriculumVersion? SupersededByCurriculumVersion { get; set; }
    public ICollection<CurriculumVersion> SupersededVersions { get; set; } = [];
    public ICollection<CurriculumNode> Nodes { get; set; } = [];
    public ICollection<CurriculumOutcomeMapping> OutcomeMappings { get; set; } = [];
}

public sealed class CurriculumNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CurriculumVersionId { get; set; }
    public Guid? ParentCurriculumNodeId { get; set; }
    public string NodeType { get; set; } = "topic";
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = "unverified";
    public bool OfficialClaimAllowed { get; set; }
    public string? SourceAnchor { get; set; }
    public string? SourceLocator { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public CurriculumVersion CurriculumVersion { get; set; } = null!;
    public CurriculumNode? ParentCurriculumNode { get; set; }
    public ICollection<CurriculumNode> Children { get; set; } = [];
    public ICollection<CurriculumOutcomeMapping> OutcomeMappings { get; set; } = [];
}

public sealed class CurriculumOutcomeMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CurriculumVersionId { get; set; }
    public Guid CurriculumNodeId { get; set; }
    public Guid ExamOutcomeId { get; set; }
    public Guid? SourceRegistryItemId { get; set; }
    public string MappingType { get; set; } = "direct";
    public string ConfidenceStatus { get; set; } = "medium";
    public string ReviewStatus { get; set; } = "draft";
    public string VerificationStatus { get; set; } = "source_backed";
    public bool OfficialClaimAllowed { get; set; }
    public string? SourceLocator { get; set; }
    public int? PageNumber { get; set; }
    public string? SectionTitle { get; set; }
    public string? Clause { get; set; }
    public string? AnchorText { get; set; }
    public string? EvidenceUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public CurriculumVersion CurriculumVersion { get; set; } = null!;
    public CurriculumNode CurriculumNode { get; set; } = null!;
    public ExamOutcome ExamOutcome { get; set; } = null!;
    public SourceRegistryItem? SourceRegistryItem { get; set; }
}

public sealed class SourceVerificationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceRegistryItemId { get; set; }
    public string VerificationStatus { get; set; } = "unverified";
    public string VerificationMethod { get; set; } = "manual_review";
    public string? EvidenceLocator { get; set; }
    public string? InternalNotes { get; set; }
    public string VerifiedBy { get; set; } = "content_review";
    public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public SourceRegistryItem SourceRegistryItem { get; set; } = null!;
}

public sealed class OfficialClaimPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string ClaimType { get; set; } = "official_curriculum";
    public bool IsAllowed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? SourceVerificationRecordId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public SourceVerificationRecord? SourceVerificationRecord { get; set; }
}

public sealed class ContentLicenseReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceRegistryItemId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string LicenseStatus { get; set; } = "unknown";
    public string ReviewStatus { get; set; } = "pending";
    public bool PublishAllowed { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public SourceRegistryItem SourceRegistryItem { get; set; } = null!;
    public User? ReviewedByUser { get; set; }
}

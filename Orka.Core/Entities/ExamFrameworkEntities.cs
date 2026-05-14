namespace Orka.Core.Entities;

public sealed class ExamDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OwnerUserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExamFamily { get; set; } = "general";
    public string Visibility { get; set; } = "system";
    public string VerificationStatus { get; set; } = "unverified";
    public bool OfficialClaimAllowed { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public User? OwnerUser { get; set; }
    public ICollection<ExamVariant> Variants { get; set; } = [];
    public ICollection<ExamContentPack> ContentPacks { get; set; } = [];
}

public sealed class ExamVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamDefinitionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public ExamDefinition ExamDefinition { get; set; } = null!;
    public ICollection<ExamSection> Sections { get; set; } = [];
    public ICollection<ExamScoringRule> ScoringRules { get; set; } = [];
    public ICollection<ExamTimeRule> TimeRules { get; set; } = [];
}

public sealed class ExamSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamVariantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public ExamVariant ExamVariant { get; set; } = null!;
    public ICollection<ExamSubject> Subjects { get; set; } = [];
    public ICollection<ExamScoringRule> ScoringRules { get; set; } = [];
    public ICollection<ExamTimeRule> TimeRules { get; set; } = [];
}

public sealed class ExamSubject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamSectionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public ExamSection ExamSection { get; set; } = null!;
    public ICollection<ExamTopic> Topics { get; set; } = [];
}

public sealed class ExamTopic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamSubjectId { get; set; }
    public Guid? ParentExamTopicId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public ExamSubject ExamSubject { get; set; } = null!;
    public ExamTopic? ParentExamTopic { get; set; }
    public ICollection<ExamTopic> Children { get; set; } = [];
    public ICollection<ExamOutcome> Outcomes { get; set; } = [];
}

public sealed class ExamOutcome
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamTopicId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public ExamTopic ExamTopic { get; set; } = null!;
}

public sealed class ExamScoringRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public string RuleType { get; set; } = "metadata";
    public string Label { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public ExamVariant? ExamVariant { get; set; }
    public ExamSection? ExamSection { get; set; }
}

public sealed class ExamTimeRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public string RuleType { get; set; } = "metadata";
    public string Label { get; set; } = string.Empty;
    public int? DurationMinutes { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public ExamVariant? ExamVariant { get; set; }
    public ExamSection? ExamSection { get; set; }
}

public sealed class ExamContentPack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamDefinitionId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? ImportedByUserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Visibility { get; set; } = "user";
    public string SourceOrigin { get; set; } = "manual";
    public string LicenseStatus { get; set; } = "unknown";
    public string VerificationStatus { get; set; } = "unverified";
    public bool OfficialClaimAllowed { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string Status { get; set; } = "draft";
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public ExamDefinition ExamDefinition { get; set; } = null!;
    public User? OwnerUser { get; set; }
    public User? ImportedByUser { get; set; }
}

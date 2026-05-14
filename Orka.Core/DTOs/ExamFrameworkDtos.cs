namespace Orka.Core.DTOs;

public sealed class ExamDefinitionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExamFamily { get; set; } = string.Empty;
    public string Visibility { get; set; } = "system";
    public string VerificationStatus { get; set; } = "unverified";
    public bool CanClaimOfficial { get; set; }
    public string UserSafeVerificationLabel { get; set; } = string.Empty;
    public ExamSourceVerificationDto SourceVerification { get; set; } = new();
    public List<ExamVariantDto> Variants { get; set; } = [];
    public List<ExamContentPackDto> ContentPacks { get; set; } = [];
}

public sealed class ExamVariantDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<ExamSectionDto> Sections { get; set; } = [];
}

public sealed class ExamSectionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<ExamSubjectDto> Subjects { get; set; } = [];
}

public sealed class ExamSubjectDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<ExamTopicDto> Topics { get; set; } = [];
}

public sealed class ExamTopicDto
{
    public Guid Id { get; set; }
    public Guid? ParentExamTopicId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<ExamOutcomeDto> Outcomes { get; set; } = [];
    public List<ExamTopicDto> Children { get; set; } = [];
}

public sealed class ExamOutcomeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class ExamContentPackDto
{
    public Guid Id { get; set; }
    public Guid ExamDefinitionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Visibility { get; set; } = "user";
    public string SourceOrigin { get; set; } = "manual";
    public string LicenseStatus { get; set; } = "unknown";
    public string VerificationStatus { get; set; } = "unverified";
    public bool CanClaimOfficial { get; set; }
    public string UserSafeVerificationLabel { get; set; } = string.Empty;
    public ExamSourceVerificationDto SourceVerification { get; set; } = new();
    public string Status { get; set; } = "draft";
    public DateTime? PublishedAt { get; set; }
}

public sealed class ExamSourceVerificationDto
{
    public string VerificationStatus { get; set; } = "unverified";
    public bool CanClaimOfficial { get; set; }
    public string UserSafeVerificationLabel { get; set; } = "Resmi müfredat iddiası değildir; doğrulanmış kaynak eklendiğinde resmi kaynak etiketi gösterilir.";
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

public sealed class ExamTreeImportDto
{
    public string ExamCode { get; set; } = string.Empty;
    public string ExamName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExamFamily { get; set; } = "general";
    public string VerificationStatus { get; set; } = "unverified";
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string? ContentPackCode { get; set; }
    public string? ContentPackName { get; set; }
    public string SourceOrigin { get; set; } = "manual";
    public string LicenseStatus { get; set; } = "unknown";
    public List<ExamVariantImportDto> Variants { get; set; } = [];
}

public sealed class ExamVariantImportDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<ExamSectionImportDto> Sections { get; set; } = [];
}

public sealed class ExamSectionImportDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<ExamSubjectImportDto> Subjects { get; set; } = [];
}

public sealed class ExamSubjectImportDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<ExamTopicImportDto> Topics { get; set; } = [];
}

public sealed class ExamTopicImportDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<ExamOutcomeImportDto> Outcomes { get; set; } = [];
    public List<ExamTopicImportDto> Children { get; set; } = [];
}

public sealed class ExamOutcomeImportDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class ExamFrameworkService : IExamFrameworkService
{
    private const string Unverified = "unverified";
    private const string SourceBacked = "source_backed";
    private const string OfficialVerified = "official_verified";
    private const string SystemVisibility = "system";
    private const string UserVisibility = "user";
    private const string UnverifiedLabel = "Resmi müfredat iddiası değildir; doğrulanmış kaynak eklendiğinde resmi kaynak etiketi gösterilir.";

    private readonly OrkaDbContext _db;

    public ExamFrameworkService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ExamDefinitionDto>> GetDefinitionsAsync(Guid userId, CancellationToken ct = default)
    {
        var definitions = await _db.ExamDefinitions
            .AsNoTracking()
            .Where(d => !d.IsDeleted && (d.OwnerUserId == null || d.OwnerUserId == userId))
            .OrderBy(d => d.Code)
            .ThenBy(d => d.OwnerUserId == null ? 0 : 1)
            .ThenBy(d => d.CreatedAt)
            .ToListAsync(ct);

        var results = new List<ExamDefinitionDto>(definitions.Count);
        foreach (var definition in definitions)
        {
            var dto = await BuildDefinitionDtoAsync(definition.Id, null, userId, ct);
            if (dto is not null)
            {
                results.Add(dto);
            }
        }

        return results;
    }

    public async Task<ExamDefinitionDto?> GetTreeAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var normalizedExamCode = NormalizeCode(examCode);
        var normalizedVariantCode = string.IsNullOrWhiteSpace(variantCode) ? null : NormalizeCode(variantCode);

        var definition = await _db.ExamDefinitions
            .AsNoTracking()
            .Where(d => !d.IsDeleted
                        && d.Code == normalizedExamCode
                        && (d.OwnerUserId == null || d.OwnerUserId == userId))
            .OrderByDescending(d => d.OwnerUserId == userId)
            .ThenBy(d => d.OwnerUserId == null ? 0 : 1)
            .ThenBy(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (definition is null)
        {
            return null;
        }

        return await BuildDefinitionDtoAsync(definition.Id, normalizedVariantCode, userId, ct);
    }

    public async Task<ExamDefinitionDto> ImportTreeAsync(
        Guid userId,
        ExamTreeImportDto request,
        CancellationToken ct = default)
    {
        ValidateImportRequest(request);

        var now = DateTime.UtcNow;
        var verificationStatus = NormalizeVerificationStatus(request.VerificationStatus);
        var canClaimOfficial = CanClaimOfficial(verificationStatus, request.SourceTitle, request.SourceUrl);
        if (verificationStatus == OfficialVerified && !canClaimOfficial)
        {
            throw new ArgumentException("official_verified requires safe source metadata.");
        }

        var definition = new ExamDefinition
        {
            OwnerUserId = userId,
            Code = NormalizeCode(request.ExamCode),
            Name = CleanLabel(request.ExamName, request.ExamCode),
            Description = CleanOptional(request.Description),
            ExamFamily = CleanOptional(request.ExamFamily, "general"),
            Visibility = UserVisibility,
            VerificationStatus = verificationStatus,
            OfficialClaimAllowed = canClaimOfficial,
            SourceTitle = SafeSourceText(request.SourceTitle),
            SourceUrl = SafeSourceText(request.SourceUrl),
            VerifiedAt = canClaimOfficial ? now : null,
            VerifiedBy = canClaimOfficial ? "import_source_metadata" : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var variantImport in request.Variants.OrderBy(v => v.SortOrder).ThenBy(v => NormalizeCode(v.Code)))
        {
            var variant = new ExamVariant
            {
                Code = NormalizeCode(variantImport.Code),
                Name = CleanLabel(variantImport.Name, variantImport.Code),
                Description = CleanOptional(variantImport.Description),
                SortOrder = variantImport.SortOrder,
                CreatedAt = now,
                UpdatedAt = now
            };

            foreach (var sectionImport in variantImport.Sections.OrderBy(s => s.SortOrder).ThenBy(s => NormalizeCode(s.Code)))
            {
                var section = new ExamSection
                {
                    Code = NormalizeCode(sectionImport.Code),
                    Name = CleanLabel(sectionImport.Name, sectionImport.Code),
                    Description = CleanOptional(sectionImport.Description),
                    SortOrder = sectionImport.SortOrder,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                foreach (var subjectImport in sectionImport.Subjects.OrderBy(s => s.SortOrder).ThenBy(s => NormalizeCode(s.Code)))
                {
                    var subject = new ExamSubject
                    {
                        Code = NormalizeCode(subjectImport.Code),
                        Name = CleanLabel(subjectImport.Name, subjectImport.Code),
                        Description = CleanOptional(subjectImport.Description),
                        SortOrder = subjectImport.SortOrder,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    foreach (var topicImport in subjectImport.Topics.OrderBy(t => t.SortOrder).ThenBy(t => NormalizeCode(t.Code)))
                    {
                        var topic = BuildTopic(topicImport, now);
                        AssignSubject(topic, subject);
                        subject.Topics.Add(topic);
                    }

                    section.Subjects.Add(subject);
                }

                variant.Sections.Add(section);
            }

            definition.Variants.Add(variant);
        }

        definition.ContentPacks.Add(new ExamContentPack
        {
            OwnerUserId = userId,
            ImportedByUserId = userId,
            Code = NormalizeCode(string.IsNullOrWhiteSpace(request.ContentPackCode) ? $"{request.ExamCode}_PACK" : request.ContentPackCode),
            Name = CleanLabel(request.ContentPackName, $"{definition.Name} içerik paketi"),
            Description = "Kullanıcı tarafından içe aktarılan taslak sınav ağacı.",
            Visibility = UserVisibility,
            SourceOrigin = CleanOptional(request.SourceOrigin, "manual"),
            LicenseStatus = CleanOptional(request.LicenseStatus, "unknown"),
            VerificationStatus = verificationStatus,
            OfficialClaimAllowed = canClaimOfficial,
            SourceTitle = SafeSourceText(request.SourceTitle),
            SourceUrl = SafeSourceText(request.SourceUrl),
            Status = "draft",
            CreatedAt = now,
            UpdatedAt = now
        });

        _db.ExamDefinitions.Add(definition);
        await _db.SaveChangesAsync(ct);

        return (await BuildDefinitionDtoAsync(definition.Id, null, userId, ct))!;
    }

    public async Task<ExamDefinitionDto> CreateSystemSkeletonAsync(CancellationToken ct = default)
    {
        var existing = await _db.ExamDefinitions
            .AsNoTracking()
            .Where(d => !d.IsDeleted && d.OwnerUserId == null && d.Code == "KPSS")
            .OrderBy(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return (await BuildDefinitionDtoAsync(existing.Id, null, Guid.Empty, ct))!;
        }

        var now = DateTime.UtcNow;
        var definition = new ExamDefinition
        {
            Code = "KPSS",
            Name = "KPSS hazırlık iskeleti",
            Description = UnverifiedLabel,
            ExamFamily = "exam",
            Visibility = SystemVisibility,
            VerificationStatus = Unverified,
            OfficialClaimAllowed = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        definition.Variants.Add(BuildKpssVariant("KPSS_LISANS", "KPSS Lisans", 0, now));
        definition.Variants.Add(BuildKpssVariant("KPSS_ONLISANS", "KPSS Önlisans", 1, now));
        definition.ContentPacks.Add(new ExamContentPack
        {
            Code = "KPSS_UNVERIFIED_SKELETON",
            Name = "KPSS hazırlık iskeleti",
            Description = UnverifiedLabel,
            Visibility = SystemVisibility,
            SourceOrigin = "architecture_skeleton",
            LicenseStatus = "unknown",
            VerificationStatus = Unverified,
            OfficialClaimAllowed = false,
            Status = "published",
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        _db.ExamDefinitions.Add(definition);
        await _db.SaveChangesAsync(ct);

        return (await BuildDefinitionDtoAsync(definition.Id, null, Guid.Empty, ct))!;
    }

    public async Task<ExamDefinitionDto> CreateSystemSkeletonAsync(string examCode, CancellationToken ct = default)
    {
        var normalizedExamCode = NormalizeCode(examCode);
        if (normalizedExamCode == "KPSS")
        {
            return await CreateSystemSkeletonAsync(ct);
        }

        var existing = await _db.ExamDefinitions
            .AsNoTracking()
            .Where(d => !d.IsDeleted && d.OwnerUserId == null && d.Code == normalizedExamCode)
            .OrderBy(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return (await BuildDefinitionDtoAsync(existing.Id, null, Guid.Empty, ct))!;
        }

        var now = DateTime.UtcNow;
        var definition = normalizedExamCode switch
        {
            "YKS" => BuildYksDefinition(now),
            "LGS" => BuildLgsDefinition(now),
            "YDS" => BuildYdsDefinition(now),
            _ => throw new ArgumentException("Unsupported central exam skeleton.")
        };

        _db.ExamDefinitions.Add(definition);
        await _db.SaveChangesAsync(ct);

        return (await BuildDefinitionDtoAsync(definition.Id, null, Guid.Empty, ct))!;
    }

    private async Task<ExamDefinitionDto?> BuildDefinitionDtoAsync(
        Guid definitionId,
        string? variantCode,
        Guid userId,
        CancellationToken ct)
    {
        var definition = await _db.ExamDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == definitionId && !d.IsDeleted, ct);

        if (definition is null || (definition.OwnerUserId is not null && definition.OwnerUserId != userId))
        {
            return null;
        }

        var variants = await _db.ExamVariants
            .AsNoTracking()
            .Where(v => v.ExamDefinitionId == definition.Id
                        && !v.IsDeleted
                        && (variantCode == null || v.Code == variantCode))
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.Code)
            .ToListAsync(ct);

        var variantIds = variants.Select(v => v.Id).ToArray();
        var sections = await _db.ExamSections
            .AsNoTracking()
            .Where(s => variantIds.Contains(s.ExamVariantId) && !s.IsDeleted)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Code)
            .ToListAsync(ct);

        var sectionIds = sections.Select(s => s.Id).ToArray();
        var subjects = await _db.ExamSubjects
            .AsNoTracking()
            .Where(s => sectionIds.Contains(s.ExamSectionId) && !s.IsDeleted)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Code)
            .ToListAsync(ct);

        var subjectIds = subjects.Select(s => s.Id).ToArray();
        var topics = await _db.ExamTopics
            .AsNoTracking()
            .Where(t => subjectIds.Contains(t.ExamSubjectId) && !t.IsDeleted)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Code)
            .ToListAsync(ct);

        var topicIds = topics.Select(t => t.Id).ToArray();
        var outcomes = await _db.ExamOutcomes
            .AsNoTracking()
            .Where(o => topicIds.Contains(o.ExamTopicId) && !o.IsDeleted)
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Code)
            .ToListAsync(ct);

        var contentPacks = await _db.ExamContentPacks
            .AsNoTracking()
            .Where(p => p.ExamDefinitionId == definition.Id
                        && !p.IsDeleted
                        && (p.OwnerUserId == null || p.OwnerUserId == userId))
            .OrderBy(p => p.Visibility)
            .ThenBy(p => p.Code)
            .ToListAsync(ct);

        var topicDtos = topics.ToDictionary(
            t => t.Id,
            t => new ExamTopicDto
            {
                Id = t.Id,
                ParentExamTopicId = t.ParentExamTopicId,
                Code = t.Code,
                Name = t.Name,
                Description = t.Description,
                SortOrder = t.SortOrder,
                Outcomes = outcomes
                    .Where(o => o.ExamTopicId == t.Id)
                    .Select(o => new ExamOutcomeDto
                    {
                        Id = o.Id,
                        Code = o.Code,
                        Name = o.Name,
                        Description = o.Description,
                        SortOrder = o.SortOrder
                    })
                    .ToList()
            });

        foreach (var topic in topics.Where(t => t.ParentExamTopicId is not null))
        {
            if (topic.ParentExamTopicId is { } parentId && topicDtos.TryGetValue(parentId, out var parent))
            {
                parent.Children.Add(topicDtos[topic.Id]);
            }
        }

        return new ExamDefinitionDto
        {
            Id = definition.Id,
            Code = definition.Code,
            Name = definition.Name,
            Description = definition.Description,
            ExamFamily = definition.ExamFamily,
            Visibility = definition.Visibility,
            VerificationStatus = NormalizeVerificationStatus(definition.VerificationStatus),
            CanClaimOfficial = CanClaimOfficial(definition.VerificationStatus, definition.SourceTitle, definition.SourceUrl) && definition.OfficialClaimAllowed,
            UserSafeVerificationLabel = BuildVerificationLabel(definition.VerificationStatus, definition.OfficialClaimAllowed, definition.SourceTitle, definition.SourceUrl),
            SourceVerification = BuildVerificationDto(definition.VerificationStatus, definition.OfficialClaimAllowed, definition.SourceTitle, definition.SourceUrl, definition.VerifiedAt),
            Variants = variants
                .Select(v => new ExamVariantDto
                {
                    Id = v.Id,
                    Code = v.Code,
                    Name = v.Name,
                    Description = v.Description,
                    SortOrder = v.SortOrder,
                    Sections = sections
                        .Where(s => s.ExamVariantId == v.Id)
                        .Select(s => new ExamSectionDto
                        {
                            Id = s.Id,
                            Code = s.Code,
                            Name = s.Name,
                            Description = s.Description,
                            SortOrder = s.SortOrder,
                            Subjects = subjects
                                .Where(subject => subject.ExamSectionId == s.Id)
                                .Select(subject => new ExamSubjectDto
                                {
                                    Id = subject.Id,
                                    Code = subject.Code,
                                    Name = subject.Name,
                                    Description = subject.Description,
                                    SortOrder = subject.SortOrder,
                                    Topics = topics
                                        .Where(t => t.ExamSubjectId == subject.Id && t.ParentExamTopicId is null)
                                        .Select(t => topicDtos[t.Id])
                                        .ToList()
                                })
                                .ToList()
                        })
                        .ToList()
                })
                .ToList(),
            ContentPacks = contentPacks.Select(BuildContentPackDto).ToList()
        };
    }

    private static ExamTopic BuildTopic(ExamTopicImportDto import, DateTime now)
    {
        var topic = new ExamTopic
        {
            Code = NormalizeCode(import.Code),
            Name = CleanLabel(import.Name, import.Code),
            Description = CleanOptional(import.Description),
            SortOrder = import.SortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var outcomeImport in import.Outcomes.OrderBy(o => o.SortOrder).ThenBy(o => NormalizeCode(o.Code)))
        {
            topic.Outcomes.Add(new ExamOutcome
            {
                Code = NormalizeCode(outcomeImport.Code),
                Name = CleanLabel(outcomeImport.Name, outcomeImport.Code),
                Description = CleanOptional(outcomeImport.Description),
                SortOrder = outcomeImport.SortOrder,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        foreach (var childImport in import.Children.OrderBy(t => t.SortOrder).ThenBy(t => NormalizeCode(t.Code)))
        {
            topic.Children.Add(BuildTopic(childImport, now));
        }

        return topic;
    }

    private static void AssignSubject(ExamTopic topic, ExamSubject subject)
    {
        topic.ExamSubject = subject;
        foreach (var child in topic.Children)
        {
            AssignSubject(child, subject);
        }
    }

    private static ExamDefinition BuildYksDefinition(DateTime now)
    {
        var definition = BuildSystemDefinition("YKS", "YKS hazirlik iskeleti", now);
        definition.Variants.Add(BuildCentralExamVariant(
            "TYT",
            "TYT",
            "TEMEL_YETERLILIK",
            "Temel Yeterlilik",
            now,
            [
                ("TURKCE", "Turkce", "OKUMA_ANLAMA", "Okuma ve anlama", "Okuma-anlama hazirlik sinyali"),
                ("MATEMATIK", "Matematik", "TEMEL_MATEMATIK", "Temel matematik", "Temel matematik hazirlik sinyali"),
                ("SOSYAL_BILIMLER", "Sosyal Bilimler", "SOSYAL_OKURYAZARLIK", "Sosyal okuryazarlik", "Sosyal bilimler hazirlik sinyali"),
                ("FEN_BILIMLERI", "Fen Bilimleri", "FEN_OKURYAZARLIGI", "Fen okuryazarligi", "Fen bilimleri hazirlik sinyali")
            ]));
        definition.Variants.Add(BuildCentralExamVariant(
            "AYT",
            "AYT",
            "ALAN_YETERLILIK",
            "Alan Yeterlilik",
            now,
            [
                ("TURKCE", "Turkce", "EDEBIYAT_OKUMA", "Edebiyat ve okuma", "Alan okuma hazirlik sinyali"),
                ("MATEMATIK", "Matematik", "ALAN_MATEMATIK", "Alan matematik", "Alan matematik hazirlik sinyali"),
                ("SOSYAL_BILIMLER", "Sosyal Bilimler", "ALAN_SOSYAL", "Alan sosyal bilimler", "Alan sosyal hazirlik sinyali"),
                ("FEN_BILIMLERI", "Fen Bilimleri", "ALAN_FEN", "Alan fen bilimleri", "Alan fen hazirlik sinyali")
            ],
            sortOrder: 1));
        return definition;
    }

    private static ExamDefinition BuildLgsDefinition(DateTime now)
    {
        var definition = BuildSystemDefinition("LGS", "LGS hazirlik iskeleti", now);
        definition.Variants.Add(BuildCentralExamVariant(
            "LGS_8",
            "LGS 8. sinif",
            "MERKEZI_SINAV",
            "Merkezi Sinav",
            now,
            [
                ("TURKCE", "Turkce", "OKUMA_ANLAMA", "Okuma ve anlama", "Turkce hazirlik sinyali"),
                ("MATEMATIK", "Matematik", "SAYISAL_MANTIK", "Sayisal mantik", "Matematik hazirlik sinyali"),
                ("FEN_BILIMLERI", "Fen Bilimleri", "FEN_OKURYAZARLIGI", "Fen okuryazarligi", "Fen hazirlik sinyali"),
                ("INKILAP_TARIHI", "T.C. Inkilap Tarihi", "INKILAP_TEKRAR", "Inkilap tarihi tekrar", "Inkilap tarihi hazirlik sinyali"),
                ("INGILIZCE", "Ingilizce", "INGILIZCE_ANLAMA", "Ingilizce anlama", "Ingilizce hazirlik sinyali"),
                ("DIN_KULTURU", "Din Kulturu", "DIN_KULTURU_TEKRAR", "Din kulturu tekrar", "Din kulturu hazirlik sinyali")
            ]));
        return definition;
    }

    private static ExamDefinition BuildYdsDefinition(DateTime now)
    {
        var definition = BuildSystemDefinition("YDS", "YDS hazirlik iskeleti", now);
        var variant = new ExamVariant
        {
            Code = "YDS_INGILIZCE",
            Name = "YDS Ingilizce",
            Description = UnverifiedLabel,
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        var section = new ExamSection
        {
            Code = "DIL_YETERLILIGI",
            Name = "Dil Yeterliligi",
            Description = "Temsili hazirlik bolumu; resmi sinav kapsami iddiasi degildir.",
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        var subject = new ExamSubject
        {
            Code = "INGILIZCE",
            Name = "Ingilizce",
            Description = "Temsili baslik; resmi sinav kapsami iddiasi degildir.",
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        subject.Topics.Add(BuildSkeletonTopic("VOCABULARY", "Vocabulary", "Kelime hazirlik sinyali", 0, now));
        subject.Topics.Add(BuildSkeletonTopic("GRAMMAR", "Grammar", "Dil bilgisi hazirlik sinyali", 1, now));
        subject.Topics.Add(BuildSkeletonTopic("READING", "Reading", "Okuma hazirlik sinyali", 2, now));
        subject.Topics.Add(BuildSkeletonTopic("CLOZE", "Cloze", "Cloze hazirlik sinyali", 3, now));
        section.Subjects.Add(subject);
        variant.Sections.Add(section);
        definition.Variants.Add(variant);
        return definition;
    }

    private static ExamDefinition BuildSystemDefinition(string code, string name, DateTime now)
    {
        var definition = new ExamDefinition
        {
            Code = code,
            Name = name,
            Description = UnverifiedLabel,
            ExamFamily = "exam",
            Visibility = SystemVisibility,
            VerificationStatus = Unverified,
            OfficialClaimAllowed = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        definition.ContentPacks.Add(new ExamContentPack
        {
            Code = $"{code}_UNVERIFIED_SKELETON",
            Name = name,
            Description = UnverifiedLabel,
            Visibility = SystemVisibility,
            SourceOrigin = "architecture_skeleton",
            LicenseStatus = "unknown",
            VerificationStatus = Unverified,
            OfficialClaimAllowed = false,
            Status = "published",
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        return definition;
    }

    private static ExamVariant BuildCentralExamVariant(
        string code,
        string name,
        string sectionCode,
        string sectionName,
        DateTime now,
        IReadOnlyList<(string Code, string Name, string TopicCode, string TopicName, string OutcomeName)> subjects,
        int sortOrder = 0)
    {
        var variant = new ExamVariant
        {
            Code = code,
            Name = name,
            Description = UnverifiedLabel,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };
        var section = new ExamSection
        {
            Code = sectionCode,
            Name = sectionName,
            Description = "Temsili hazirlik bolumu; resmi sinav kapsami iddiasi degildir.",
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        foreach (var subject in subjects.Select((value, index) => new { value, index }))
        {
            section.Subjects.Add(BuildSkeletonSubject(
                subject.value.Code,
                subject.value.Name,
                subject.value.TopicCode,
                subject.value.TopicName,
                subject.value.OutcomeName,
                subject.index,
                now));
        }

        variant.Sections.Add(section);
        return variant;
    }

    private static ExamTopic BuildSkeletonTopic(string code, string name, string outcomeName, int sortOrder, DateTime now) => new()
    {
        Code = code,
        Name = name,
        Description = "Hazirlik iskeleti icin temsili konu.",
        SortOrder = sortOrder,
        CreatedAt = now,
        UpdatedAt = now,
        Outcomes =
        [
            new ExamOutcome
            {
                Code = $"{code}_OUTCOME",
                Name = outcomeName,
                Description = "Temsili kazanim; resmi kapsam iddiasi degildir.",
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            }
        ]
    };

    private static ExamVariant BuildKpssVariant(string code, string name, int sortOrder, DateTime now)
    {
        var variant = new ExamVariant
        {
            Code = code,
            Name = name,
            Description = UnverifiedLabel,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        var generalAbility = new ExamSection
        {
            Code = "GENEL_YETENEK",
            Name = "Genel Yetenek",
            Description = "Temsilî hazırlık bölümü; resmi müfredat iddiası değildir.",
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        generalAbility.Subjects.Add(BuildSkeletonSubject("TURKCE", "Türkçe", "PARAGRAF", "Paragraf ve anlam", "Ana fikir ve çıkarım pratiği", 0, now));
        generalAbility.Subjects.Add(BuildSkeletonSubject("MATEMATIK", "Matematik", "TEMEL_KAVRAMLAR", "Temel kavramlar", "Temel işlem ve problem çözme pratiği", 1, now));

        var generalCulture = new ExamSection
        {
            Code = "GENEL_KULTUR",
            Name = "Genel Kültür",
            Description = "Temsilî hazırlık bölümü; resmi müfredat iddiası değildir.",
            SortOrder = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        generalCulture.Subjects.Add(BuildSkeletonSubject("TARIH", "Tarih", "GENEL_TARIH_TEKRAR", "Genel tarih tekrarı", "Kronoloji ve kavram tekrarı", 0, now));
        generalCulture.Subjects.Add(BuildSkeletonSubject("COGRAFYA", "Coğrafya", "TURKIYE_COGRAFYASI_TEKRAR", "Türkiye coğrafyası tekrarı", "Harita ve bölge bilgisi tekrarı", 1, now));
        generalCulture.Subjects.Add(BuildSkeletonSubject("VATANDASLIK", "Vatandaşlık", "TEMEL_VATANDASLIK", "Temel vatandaşlık", "Temel yurttaşlık kavramları", 2, now));

        variant.Sections.Add(generalAbility);
        variant.Sections.Add(generalCulture);
        return variant;
    }

    private static ExamSubject BuildSkeletonSubject(
        string code,
        string name,
        string topicCode,
        string topicName,
        string outcomeName,
        int sortOrder,
        DateTime now)
    {
        var subject = new ExamSubject
        {
            Code = code,
            Name = name,
            Description = "Temsilî başlık; resmi müfredat iddiası değildir.",
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };
        subject.Topics.Add(new ExamTopic
        {
            Code = topicCode,
            Name = topicName,
            Description = "Hazırlık iskeleti için temsilî konu.",
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now,
            Outcomes =
            [
                new ExamOutcome
                {
                    Code = $"{topicCode}_OUTCOME",
                    Name = outcomeName,
                    Description = "Temsilî kazanım; resmi kapsam iddiası değildir.",
                    SortOrder = 0,
                    CreatedAt = now,
                    UpdatedAt = now
                }
            ]
        });
        return subject;
    }

    private static ExamContentPackDto BuildContentPackDto(ExamContentPack pack) => new()
    {
        Id = pack.Id,
        ExamDefinitionId = pack.ExamDefinitionId,
        Code = pack.Code,
        Name = pack.Name,
        Description = pack.Description,
        Visibility = pack.Visibility,
        SourceOrigin = pack.SourceOrigin,
        LicenseStatus = pack.LicenseStatus,
        VerificationStatus = NormalizeVerificationStatus(pack.VerificationStatus),
        CanClaimOfficial = CanClaimOfficial(pack.VerificationStatus, pack.SourceTitle, pack.SourceUrl) && pack.OfficialClaimAllowed,
        UserSafeVerificationLabel = BuildVerificationLabel(pack.VerificationStatus, pack.OfficialClaimAllowed, pack.SourceTitle, pack.SourceUrl),
        SourceVerification = BuildVerificationDto(pack.VerificationStatus, pack.OfficialClaimAllowed, pack.SourceTitle, pack.SourceUrl, null),
        Status = pack.Status,
        PublishedAt = pack.PublishedAt
    };

    private static ExamSourceVerificationDto BuildVerificationDto(
        string verificationStatus,
        bool officialClaimAllowed,
        string? sourceTitle,
        string? sourceUrl,
        DateTime? verifiedAt)
    {
        var normalized = NormalizeVerificationStatus(verificationStatus);
        var canClaimOfficial = CanClaimOfficial(normalized, sourceTitle, sourceUrl) && officialClaimAllowed;
        return new ExamSourceVerificationDto
        {
            VerificationStatus = normalized,
            CanClaimOfficial = canClaimOfficial,
            UserSafeVerificationLabel = BuildVerificationLabel(normalized, officialClaimAllowed, sourceTitle, sourceUrl),
            SourceTitle = SafeSourceText(sourceTitle),
            SourceUrl = SafeSourceText(sourceUrl),
            VerifiedAt = canClaimOfficial ? verifiedAt : null
        };
    }

    private static string BuildVerificationLabel(
        string verificationStatus,
        bool officialClaimAllowed,
        string? sourceTitle,
        string? sourceUrl)
    {
        var normalized = NormalizeVerificationStatus(verificationStatus);
        if (normalized == OfficialVerified && officialClaimAllowed && CanClaimOfficial(normalized, sourceTitle, sourceUrl))
        {
            return "Doğrulanmış resmi kaynakla eşleşiyor.";
        }

        if (normalized == SourceBacked && HasSafeSourceMetadata(sourceTitle, sourceUrl))
        {
            return "Kaynakla desteklenmiş hazırlık içeriği.";
        }

        return UnverifiedLabel;
    }

    private static bool CanClaimOfficial(string verificationStatus, string? sourceTitle, string? sourceUrl) =>
        NormalizeVerificationStatus(verificationStatus) == OfficialVerified && HasSafeSourceMetadata(sourceTitle, sourceUrl);

    private static bool HasSafeSourceMetadata(string? sourceTitle, string? sourceUrl) =>
        !string.IsNullOrWhiteSpace(sourceTitle)
        && Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string NormalizeVerificationStatus(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is SourceBacked or OfficialVerified ? normalized : Unverified;
    }

    private static string NormalizeCode(string? value)
    {
        var normalized = new string((value ?? string.Empty)
            .Trim()
            .ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray());
        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        return normalized.Trim('_');
    }

    private static string CleanLabel(string? value, string fallback)
    {
        var label = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(label) ? fallback.Trim() : label;
    }

    private static string CleanOptional(string? value, string fallback = "") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? SafeSourceText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateImportRequest(ExamTreeImportDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ExamCode))
        {
            throw new ArgumentException("Exam code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ExamName))
        {
            throw new ArgumentException("Exam name is required.");
        }

        if (request.Variants.Count == 0)
        {
            throw new ArgumentException("At least one exam variant is required.");
        }

        EnsureUniqueCodes(request.Variants.Select(v => v.Code), "variant");
        foreach (var variant in request.Variants)
        {
            EnsureRequiredLabel(variant.Code, variant.Name, "variant");
            if (variant.Sections.Count == 0)
            {
                throw new ArgumentException("Each variant needs at least one section.");
            }

            EnsureUniqueCodes(variant.Sections.Select(s => s.Code), "section");
            foreach (var section in variant.Sections)
            {
                EnsureRequiredLabel(section.Code, section.Name, "section");
                if (section.Subjects.Count == 0)
                {
                    throw new ArgumentException("Each section needs at least one subject.");
                }

                EnsureUniqueCodes(section.Subjects.Select(s => s.Code), "subject");
                foreach (var subject in section.Subjects)
                {
                    EnsureRequiredLabel(subject.Code, subject.Name, "subject");
                    if (subject.Topics.Count == 0)
                    {
                        throw new ArgumentException("Each subject needs at least one topic.");
                    }

                    EnsureTopicImports(subject.Topics);
                }
            }
        }
    }

    private static void EnsureTopicImports(IReadOnlyList<ExamTopicImportDto> topics)
    {
        EnsureUniqueCodes(topics.Select(t => t.Code), "topic");
        foreach (var topic in topics)
        {
            EnsureRequiredLabel(topic.Code, topic.Name, "topic");
            EnsureUniqueCodes(topic.Outcomes.Select(o => o.Code), "outcome");
            foreach (var outcome in topic.Outcomes)
            {
                if (string.IsNullOrWhiteSpace(outcome.Code) || string.IsNullOrWhiteSpace(outcome.Name))
                {
                    throw new ArgumentException("Outcome code and label are required.");
                }
            }

            if (topic.Children.Count > 0)
            {
                EnsureTopicImports(topic.Children);
            }
        }
    }

    private static void EnsureRequiredLabel(string code, string label, string entity)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException($"{entity} code and label are required.");
        }
    }

    private static void EnsureUniqueCodes(IEnumerable<string> codes, string entity)
    {
        var normalized = codes.Select(NormalizeCode).Where(code => !string.IsNullOrWhiteSpace(code)).ToArray();
        if (normalized.Length != normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new ArgumentException($"Duplicate sibling {entity} codes are not allowed.");
        }
    }
}

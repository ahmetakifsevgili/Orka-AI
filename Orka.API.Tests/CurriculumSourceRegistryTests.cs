using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class CurriculumSourceRegistryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task SourceRegistry_BlocksOfficialClaimWithoutOfficialSourceMetadata()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "curriculum-unsafe-official");

        var source = await RegisterSourceAsync(user, new RegisterSourceRegistryItemDto
        {
            SourceKey = "unsafe-kpss",
            Title = "Unsafe KPSS source",
            SourceUrl = "https://example.com/kpss",
            SourceType = "user_reference",
            LicenseStatus = "unknown"
        });

        Assert.False(source.CanClaimOfficial);

        var verify = await user.Client.PostAsJsonAsync($"/api/curriculum/sources/{source.Id}/verify", new VerifySourceRegistryItemDto
        {
            VerificationStatus = "official_verified",
            VerificationMethod = "manual_review"
        });

        Assert.Equal(HttpStatusCode.BadRequest, verify.StatusCode);
        var body = await verify.Content.ReadAsStringAsync();
        Assert.Contains("official_verified_requires_official_source_metadata", body);
    }

    [Fact]
    public async Task OfficialSourceVerification_AllowsClaimAndDoesNotExposeInternalNotes()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "curriculum-official-source");

        var source = await RegisterSourceAsync(user, new RegisterSourceRegistryItemDto
        {
            SourceKey = "kpss-osym-scope",
            Title = "OSYM KPSS test kapsamı",
            SourceUrl = "https://www.osym.gov.tr/Eklenti/2147%2Cbolum31pdf.pdf?0=",
            SourceType = "osym_guide",
            LicenseStatus = "official_public_reference",
            SourceContentHash = "scope-v1"
        });

        var verify = await user.Client.PostAsJsonAsync($"/api/curriculum/sources/{source.Id}/verify", new VerifySourceRegistryItemDto
        {
            VerificationStatus = "official_verified",
            VerificationMethod = "official_source_url_review",
            EvidenceLocator = "KPSS test kapsamları",
            InternalNotes = "secret reviewer note"
        });

        verify.EnsureSuccessStatusCode();
        var body = await verify.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<SourceRegistryItemDto>(body, JsonOptions);

        Assert.True(dto!.CanClaimOfficial);
        Assert.Equal("official_verified", dto.VerificationStatus);
        Assert.Contains("Doğrulanmış resmi kaynak", dto.UserSafeVerificationLabel);
        Assert.DoesNotContain("InternalNotes", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret reviewer note", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OwnerUserId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CurriculumVersion_MapsOutcomeToSourceAndReturnsSourceBackedOutcomeEvidence()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "curriculum-map");
        var outcomeId = await GetKpssParagrafOutcomeIdAsync(factory);
        var examDefinitionId = await GetKpssExamDefinitionIdAsync(factory);
        var source = await RegisterOfficialSourceAsync(user);

        var version = await CreateVersionAsync(user, new CreateCurriculumVersionDto
        {
            ExamDefinitionId = examDefinitionId,
            SourceRegistryItemId = source.Id,
            Code = "KPSS_SOURCE_BACKED_V1",
            Name = "KPSS source-backed curriculum proof",
            VersionLabel = "2026-proof",
            VerificationStatus = "official_verified",
            Status = "active"
        });

        Assert.True(version.CanClaimOfficial);
        Assert.Equal("scope-v1", version.SourceSnapshotHash);

        var nodeResponse = await user.Client.PostAsJsonAsync($"/api/curriculum/versions/{version.Id}/nodes", new CreateCurriculumNodeDto
        {
            NodeType = "topic",
            Code = "PARAGRAF",
            Title = "Paragraf",
            VerificationStatus = "official_verified",
            SourceAnchor = "Türkçe / Okuma-anlama",
            SortOrder = 1
        });
        nodeResponse.EnsureSuccessStatusCode();
        var node = await nodeResponse.Content.ReadFromJsonAsync<CurriculumNodeDto>();
        Assert.True(node!.CanClaimOfficial);

        var mapResponse = await user.Client.PostAsJsonAsync($"/api/curriculum/versions/{version.Id}/outcome-mappings", new CreateCurriculumOutcomeMappingDto
        {
            CurriculumNodeId = node.Id,
            ExamOutcomeId = outcomeId,
            SourceRegistryItemId = source.Id,
            VerificationStatus = "official_verified",
            ConfidenceStatus = "high",
            ReviewStatus = "approved",
            SourceLocator = "KPSS Türkçe test kapsamı"
        });
        mapResponse.EnsureSuccessStatusCode();

        var sources = await user.Client.GetFromJsonAsync<CurriculumOutcomeSourceDto>($"/api/curriculum/outcomes/{outcomeId}/sources");
        var mapping = Assert.Single(sources!.Mappings);
        Assert.True(mapping.CanClaimOfficial);
        Assert.Equal(outcomeId, mapping.ExamOutcomeId);
        Assert.Equal("official_verified", mapping.VerificationStatus);
        Assert.Contains("Doğrulanmış resmi kaynak", mapping.UserSafeVerificationLabel);
    }

    [Fact]
    public async Task UserOwnedSourcesVersionsAndMappings_AreNotVisibleAcrossUsers()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "curriculum-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "curriculum-other");
        var outcomeId = await GetKpssParagrafOutcomeIdAsync(factory);
        var examDefinitionId = await GetKpssExamDefinitionIdAsync(factory);
        var source = await RegisterOfficialSourceAsync(owner);
        var version = await CreateVersionAsync(owner, new CreateCurriculumVersionDto
        {
            ExamDefinitionId = examDefinitionId,
            SourceRegistryItemId = source.Id,
            Code = "PRIVATE_CURRICULUM",
            Name = "Private curriculum",
            VerificationStatus = "source_backed"
        });

        var node = await (await owner.Client.PostAsJsonAsync($"/api/curriculum/versions/{version.Id}/nodes", new CreateCurriculumNodeDto
        {
            Code = "PRIVATE_NODE",
            Title = "Private node",
            VerificationStatus = "source_backed"
        })).Content.ReadFromJsonAsync<CurriculumNodeDto>();

        var map = await owner.Client.PostAsJsonAsync($"/api/curriculum/versions/{version.Id}/outcome-mappings", new CreateCurriculumOutcomeMappingDto
        {
            CurriculumNodeId = node!.Id,
            ExamOutcomeId = outcomeId,
            SourceRegistryItemId = source.Id
        });
        map.EnsureSuccessStatusCode();

        var otherSource = await other.Client.GetAsync($"/api/curriculum/sources/{source.Id}");
        Assert.Equal(HttpStatusCode.NotFound, otherSource.StatusCode);

        var otherVersion = await other.Client.GetAsync($"/api/curriculum/versions/{version.Id}");
        Assert.Equal(HttpStatusCode.NotFound, otherVersion.StatusCode);

        var otherOutcomeSources = await other.Client.GetFromJsonAsync<CurriculumOutcomeSourceDto>($"/api/curriculum/outcomes/{outcomeId}/sources");
        Assert.Empty(otherOutcomeSources!.Mappings);
    }

    [Fact]
    public async Task LicenseReview_BlocksRestrictedSourcesFromPublishAllowedState()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "curriculum-license");
        var source = await RegisterSourceAsync(user, new RegisterSourceRegistryItemDto
        {
            SourceKey = "restricted-source",
            Title = "Restricted source",
            SourceUrl = "https://example.com/restricted",
            LicenseStatus = "restricted"
        });

        var restricted = await user.Client.PostAsJsonAsync($"/api/curriculum/sources/{source.Id}/license-review", new ReviewSourceLicenseDto
        {
            LicenseStatus = "restricted",
            ReviewStatus = "approved",
            DecisionReason = "Reference only; no publication rights."
        });
        restricted.EnsureSuccessStatusCode();
        var restrictedDto = await restricted.Content.ReadFromJsonAsync<ContentLicenseReviewDto>();
        Assert.False(restrictedDto!.PublishAllowed);

        var open = await user.Client.PostAsJsonAsync($"/api/curriculum/sources/{source.Id}/license-review", new ReviewSourceLicenseDto
        {
            LicenseStatus = "open",
            ReviewStatus = "approved",
            DecisionReason = "Open content can be published after review."
        });
        open.EnsureSuccessStatusCode();
        var openDto = await open.Content.ReadFromJsonAsync<ContentLicenseReviewDto>();
        Assert.True(openDto!.PublishAllowed);
    }

    [Fact]
    public async Task SourceTaxonomy_NormalizesOfficialTypesAndFallsBackUnknownTypes()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "curriculum-source-taxonomy");

        var official = await RegisterSourceAsync(user, new RegisterSourceRegistryItemDto
        {
            SourceKey = "osym-normalized",
            Title = "OSYM guide",
            SourceUrl = "https://www.osym.gov.tr/example",
            SourceType = "OSYM Guide"
        });
        Assert.Equal("osym_guide", official.SourceType);

        var unknown = await RegisterSourceAsync(user, new RegisterSourceRegistryItemDto
        {
            SourceKey = "unknown-source-type",
            Title = "Unknown source type",
            SourceType = "surprise_bucket"
        });
        Assert.Equal("user_reference", unknown.SourceType);

        var nonOfficial = await RegisterSourceAsync(user, new RegisterSourceRegistryItemDto
        {
            SourceKey = "open-osym-url",
            Title = "Open reference with official URL",
            SourceUrl = "https://www.osym.gov.tr/example",
            SourceType = "open_reference"
        });

        var verify = await user.Client.PostAsJsonAsync($"/api/curriculum/sources/{nonOfficial.Id}/verify", new VerifySourceRegistryItemDto
        {
            VerificationStatus = "official_verified"
        });
        Assert.Equal(HttpStatusCode.BadRequest, verify.StatusCode);
    }

    [Fact]
    public async Task VerificationLevels_OfficialSourceBackedAndDeprecatedDoNotAllowOfficialClaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "curriculum-verification-levels");
        var source = await RegisterSourceAsync(user, new RegisterSourceRegistryItemDto
        {
            SourceKey = "osym-source-backed",
            Title = "OSYM source backed",
            SourceUrl = "https://www.osym.gov.tr/example",
            SourceType = "osym_guide"
        });

        var sourceBacked = await user.Client.PostAsJsonAsync($"/api/curriculum/sources/{source.Id}/verify", new VerifySourceRegistryItemDto
        {
            VerificationStatus = "official_source_backed"
        });
        sourceBacked.EnsureSuccessStatusCode();
        var backedDto = await sourceBacked.Content.ReadFromJsonAsync<SourceRegistryItemDto>();
        Assert.False(backedDto!.CanClaimOfficial);
        Assert.Equal("official_source_backed", backedDto.VerificationStatus);

        var deprecated = await user.Client.PostAsJsonAsync($"/api/curriculum/sources/{source.Id}/verify", new VerifySourceRegistryItemDto
        {
            VerificationStatus = "deprecated"
        });
        deprecated.EnsureSuccessStatusCode();
        var deprecatedDto = await deprecated.Content.ReadFromJsonAsync<SourceRegistryItemDto>();
        Assert.False(deprecatedDto!.CanClaimOfficial);
    }

    [Fact]
    public async Task CurriculumLifecycle_SupersedesActiveVersionAndDeprecatesOfficialClaims()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "curriculum-lifecycle");
        var examDefinitionId = await GetKpssExamDefinitionIdAsync(factory);
        var source = await RegisterOfficialSourceAsync(user);

        var first = await CreateVersionAsync(user, new CreateCurriculumVersionDto
        {
            ExamDefinitionId = examDefinitionId,
            SourceRegistryItemId = source.Id,
            Code = "KPSS_ACTIVE_ONE",
            Name = "KPSS active one",
            VerificationStatus = "official_verified",
            Status = "active"
        });
        Assert.True(first.CanClaimOfficial);

        var second = await CreateVersionAsync(user, new CreateCurriculumVersionDto
        {
            ExamDefinitionId = examDefinitionId,
            SourceRegistryItemId = source.Id,
            Code = "KPSS_ACTIVE_TWO",
            Name = "KPSS active two",
            VerificationStatus = "official_verified",
            Status = "active"
        });

        var firstAfter = await user.Client.GetFromJsonAsync<CurriculumVersionDto>($"/api/curriculum/versions/{first.Id}");
        Assert.Equal("superseded", firstAfter!.Status);
        Assert.Equal(second.Id, firstAfter.SupersededByCurriculumVersionId);
        Assert.False(firstAfter.CanClaimOfficial);

        var versions = await user.Client.GetFromJsonAsync<List<CurriculumVersionDto>>("/api/curriculum/exams/KPSS/versions");
        Assert.Contains(versions!, v => v.Id == second.Id && v.Status == "active");

        var deprecate = await user.Client.PostAsJsonAsync($"/api/curriculum/versions/{second.Id}/deprecate", new DeprecateCurriculumVersionDto
        {
            DeprecatedReason = "Replaced by source review."
        });
        deprecate.EnsureSuccessStatusCode();
        var deprecated = await deprecate.Content.ReadFromJsonAsync<CurriculumVersionDto>();
        Assert.Equal("deprecated", deprecated!.Status);
        Assert.False(deprecated.CanClaimOfficial);
        Assert.Equal("Replaced by source review.", deprecated.DeprecatedReason);
    }

    [Fact]
    public async Task CurriculumNodeHierarchy_RejectsDuplicateSiblingsAndExcludesSoftDeletedNodes()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "curriculum-node-hardening");
        var version = await CreateSourceBackedKpssVersionAsync(factory, user, "KPSS_NODE_HARDENING");

        var root = await AddNodeAsync(user, version.Id, new CreateCurriculumNodeDto
        {
            NodeType = "subject",
            Code = "TURKCE",
            Title = "Turkce",
            SourceLocator = "section:turkce"
        });
        Assert.Equal("subject", root.NodeType);
        Assert.Equal("section:turkce", root.SourceLocator);

        var child = await AddNodeAsync(user, version.Id, new CreateCurriculumNodeDto
        {
            ParentCurriculumNodeId = root.Id,
            NodeType = "topic",
            Code = "PARAGRAF",
            Title = "Paragraf"
        });

        var duplicate = await user.Client.PostAsJsonAsync($"/api/curriculum/versions/{version.Id}/nodes", new CreateCurriculumNodeDto
        {
            ParentCurriculumNodeId = root.Id,
            Code = "PARAGRAF",
            Title = "Duplicate"
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var empty = await user.Client.PostAsJsonAsync($"/api/curriculum/versions/{version.Id}/nodes", new CreateCurriculumNodeDto
        {
            Code = "",
            Title = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var node = await db.CurriculumNodes.FirstAsync(n => n.Id == child.Id);
            node.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var refreshed = await user.Client.GetFromJsonAsync<CurriculumVersionDto>($"/api/curriculum/versions/{version.Id}");
        var refreshedRoot = Assert.Single(refreshed!.Nodes);
        Assert.Empty(refreshedRoot.Children);
    }

    [Fact]
    public async Task OutcomeMappingConfidence_ExposesSafeLocatorFieldsAndMarksNonFinalMappings()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "curriculum-mapping-hardening");
        var outcomeId = await GetKpssParagrafOutcomeIdAsync(factory);
        var source = await RegisterSourceAsync(user, new RegisterSourceRegistryItemDto
        {
            SourceKey = "mapping-source",
            Title = "Mapping source",
            SourceUrl = "https://example.com/source",
            SourceType = "open_reference",
            VerificationStatus = "source_backed"
        });
        var version = await CreateSourceBackedKpssVersionAsync(factory, user, "KPSS_MAPPING_HARDENING", source.Id);
        var node = await AddNodeAsync(user, version.Id, new CreateCurriculumNodeDto
        {
            Code = "PARAGRAF",
            Title = "Paragraf"
        });

        var response = await user.Client.PostAsJsonAsync($"/api/curriculum/versions/{version.Id}/outcome-mappings", new CreateCurriculumOutcomeMappingDto
        {
            CurriculumNodeId = node.Id,
            ExamOutcomeId = outcomeId,
            SourceRegistryItemId = source.Id,
            MappingType = "inferred",
            ConfidenceStatus = "low",
            ReviewStatus = "needs_review",
            SourceLocator = "chapter:reading",
            PageNumber = 12,
            SectionTitle = "Reading",
            Clause = "sample clause",
            AnchorText = "paragraph skill",
            EvidenceUrl = "https://example.com/source#reading"
        });
        response.EnsureSuccessStatusCode();
        var mapping = await response.Content.ReadFromJsonAsync<CurriculumOutcomeMappingDto>();

        Assert.Equal("inferred", mapping!.MappingType);
        Assert.Equal("low", mapping.ConfidenceStatus);
        Assert.Equal("needs_review", mapping.ReviewStatus);
        Assert.False(mapping.CanClaimOfficial);
        Assert.Equal(12, mapping.PageNumber);
        Assert.Equal("Reading", mapping.SectionTitle);
        Assert.Equal("https://example.com/source#reading", mapping.EvidenceUrl);
    }

    [Fact]
    public async Task KpssAndLgsProofMappings_AreRepresentableWithoutCompleteCurriculumClaims()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "curriculum-proof-mapping");
        var kpssOutcomeId = await GetKpssParagrafOutcomeIdAsync(factory);
        var kpssVersion = await CreateSourceBackedKpssVersionAsync(factory, user, "KPSS_PROOF_PATH");

        var kpssSubject = await AddNodeAsync(user, kpssVersion.Id, new CreateCurriculumNodeDto { NodeType = "subject", Code = "TURKCE", Title = "Turkce" });
        var kpssTopic = await AddNodeAsync(user, kpssVersion.Id, new CreateCurriculumNodeDto { ParentCurriculumNodeId = kpssSubject.Id, NodeType = "topic", Code = "PARAGRAF", Title = "Paragraf" });
        var kpssMap = await user.Client.PostAsJsonAsync($"/api/curriculum/versions/{kpssVersion.Id}/outcome-mappings", new CreateCurriculumOutcomeMappingDto
        {
            CurriculumNodeId = kpssTopic.Id,
            ExamOutcomeId = kpssOutcomeId,
            MappingType = "direct",
            ConfidenceStatus = "high",
            ReviewStatus = "approved",
            VerificationStatus = "source_backed"
        });
        kpssMap.EnsureSuccessStatusCode();

        var lgsOutcomeId = await GetFirstExamOutcomeIdAsync(factory, "LGS");
        var lgsExamDefinitionId = await GetExamDefinitionIdAsync(factory, "LGS");
        var lgsSource = await RegisterSourceAsync(user, new RegisterSourceRegistryItemDto
        {
            SourceKey = "lgs-meb-proof",
            Title = "MEB LGS proof source",
            SourceUrl = "https://www.meb.gov.tr/example",
            SourceType = "meb_curriculum",
            VerificationStatus = "official_source_backed"
        });
        var lgsVersion = await CreateVersionAsync(user, new CreateCurriculumVersionDto
        {
            ExamDefinitionId = lgsExamDefinitionId,
            SourceRegistryItemId = lgsSource.Id,
            Code = "LGS_PROOF_PATH",
            Name = "LGS proof path",
            VerificationStatus = "source_backed",
            Status = "active"
        });
        Assert.False(lgsVersion.CanClaimOfficial);

        var lgsNode = await AddNodeAsync(user, lgsVersion.Id, new CreateCurriculumNodeDto { NodeType = "subject", Code = "TURKCE", Title = "Turkce" });
        var lgsMap = await user.Client.PostAsJsonAsync($"/api/curriculum/versions/{lgsVersion.Id}/outcome-mappings", new CreateCurriculumOutcomeMappingDto
        {
            CurriculumNodeId = lgsNode.Id,
            ExamOutcomeId = lgsOutcomeId,
            SourceRegistryItemId = lgsSource.Id,
            MappingType = "direct",
            ConfidenceStatus = "high",
            ReviewStatus = "approved",
            VerificationStatus = "source_backed"
        });
        lgsMap.EnsureSuccessStatusCode();
        var body = await lgsMap.Content.ReadAsStringAsync();
        Assert.DoesNotContain("complete curriculum", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success guarantee", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<SourceRegistryItemDto> RegisterOfficialSourceAsync(CoordinationTestUser user)
    {
        var source = await RegisterSourceAsync(user, new RegisterSourceRegistryItemDto
        {
            SourceKey = $"kpss-osym-{Guid.NewGuid():N}",
            Title = "OSYM KPSS test kapsamı",
            SourceUrl = "https://www.osym.gov.tr/Eklenti/2147%2Cbolum31pdf.pdf?0=",
            SourceType = "osym_guide",
            LicenseStatus = "official_public_reference",
            SourceContentHash = "scope-v1"
        });

        var verify = await user.Client.PostAsJsonAsync($"/api/curriculum/sources/{source.Id}/verify", new VerifySourceRegistryItemDto
        {
            VerificationStatus = "official_verified",
            VerificationMethod = "official_source_url_review"
        });
        verify.EnsureSuccessStatusCode();
        return (await verify.Content.ReadFromJsonAsync<SourceRegistryItemDto>())!;
    }

    private static async Task<CurriculumVersionDto> CreateSourceBackedKpssVersionAsync(
        ApiSmokeFactory factory,
        CoordinationTestUser user,
        string code,
        Guid? sourceId = null)
    {
        var examDefinitionId = await GetKpssExamDefinitionIdAsync(factory);
        return await CreateVersionAsync(user, new CreateCurriculumVersionDto
        {
            ExamDefinitionId = examDefinitionId,
            SourceRegistryItemId = sourceId,
            Code = code,
            Name = code,
            VerificationStatus = "source_backed",
            Status = "active"
        });
    }

    private static async Task<CurriculumNodeDto> AddNodeAsync(
        CoordinationTestUser user,
        Guid versionId,
        CreateCurriculumNodeDto request)
    {
        var response = await user.Client.PostAsJsonAsync($"/api/curriculum/versions/{versionId}/nodes", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CurriculumNodeDto>())!;
    }

    private static async Task<SourceRegistryItemDto> RegisterSourceAsync(
        CoordinationTestUser user,
        RegisterSourceRegistryItemDto request)
    {
        var response = await user.Client.PostAsJsonAsync("/api/curriculum/sources", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SourceRegistryItemDto>())!;
    }

    private static async Task<CurriculumVersionDto> CreateVersionAsync(
        CoordinationTestUser user,
        CreateCurriculumVersionDto request)
    {
        var response = await user.Client.PostAsJsonAsync("/api/curriculum/versions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CurriculumVersionDto>())!;
    }

    private static async Task<Guid> GetKpssExamDefinitionIdAsync(ApiSmokeFactory factory)
    {
        return await GetExamDefinitionIdAsync(factory, "KPSS");
    }

    private static async Task<Guid> GetKpssParagrafOutcomeIdAsync(ApiSmokeFactory factory)
    {
        await EnsureKpssSkeletonAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        return await db.ExamOutcomes
            .Where(o => o.Code == "PARAGRAF_OUTCOME" && !o.IsDeleted)
            .Select(o => o.Id)
            .FirstAsync();
    }

    private static async Task EnsureKpssSkeletonAsync(ApiSmokeFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExamFrameworkService>();
        await service.CreateSystemSkeletonAsync();
    }

    private static async Task<Guid> GetExamDefinitionIdAsync(ApiSmokeFactory factory, string examCode)
    {
        await EnsureSkeletonAsync(factory, examCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        return await db.ExamDefinitions
            .Where(e => e.Code == examCode && e.OwnerUserId == null && !e.IsDeleted)
            .Select(e => e.Id)
            .FirstAsync();
    }

    private static async Task<Guid> GetFirstExamOutcomeIdAsync(ApiSmokeFactory factory, string examCode)
    {
        await EnsureSkeletonAsync(factory, examCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        return await db.ExamOutcomes
            .Where(o => !o.IsDeleted
                        && o.ExamTopic.ExamSubject.ExamSection.ExamVariant.ExamDefinition.Code == examCode)
            .Select(o => o.Id)
            .FirstAsync();
    }

    private static async Task EnsureSkeletonAsync(ApiSmokeFactory factory, string examCode)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExamFrameworkService>();
        await service.CreateSystemSkeletonAsync(examCode);
    }
}

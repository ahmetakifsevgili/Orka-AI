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

public sealed class ExamFrameworkTests
{
    [Fact]
    public async Task SystemExamDefinition_CanBeCreatedAndReadByAuthenticatedUsers()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-system-a");
        var userB = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-system-b");
        await CreateKpssSkeletonAsync(factory);

        var responseA = await userA.Client.GetAsync("/api/exams");
        responseA.EnsureSuccessStatusCode();
        var definitionsA = await responseA.Content.ReadFromJsonAsync<List<ExamDefinitionDto>>();

        var responseB = await userB.Client.GetAsync("/api/exams/KPSS");
        responseB.EnsureSuccessStatusCode();
        var treeB = await responseB.Content.ReadFromJsonAsync<ExamDefinitionDto>();

        Assert.Contains(definitionsA!, definition => definition.Code == "KPSS" && definition.Visibility == "system");
        Assert.Equal("KPSS hazırlık iskeleti", treeB!.Name);
        Assert.False(treeB.CanClaimOfficial);
        Assert.Equal("unverified", treeB.VerificationStatus);
        Assert.Contains("Resmi müfredat iddiası değildir", treeB.UserSafeVerificationLabel);
    }

    [Fact]
    public async Task UserOwnedExamTree_ImportsAndPreservesVariantSubjectTopicOutcomeTree()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-import");

        var import = BuildImport("ORKA_EXAM_TREE");
        var create = await user.Client.PostAsJsonAsync("/api/exams/import-tree", import);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<ExamDefinitionDto>();
        Assert.NotNull(created);
        Assert.Equal("user", created!.Visibility);
        Assert.Single(created.ContentPacks);
        Assert.Equal("draft", created.ContentPacks[0].Status);

        var response = await user.Client.GetAsync("/api/exams/ORKA_EXAM_TREE/variants/VARIANT_A");
        response.EnsureSuccessStatusCode();
        var tree = await response.Content.ReadFromJsonAsync<ExamDefinitionDto>();

        var variant = Assert.Single(tree!.Variants);
        var section = Assert.Single(variant.Sections);
        var subject = Assert.Single(section.Subjects);
        var rootTopic = Assert.Single(subject.Topics);
        var childTopic = Assert.Single(rootTopic.Children);

        Assert.Equal("ROOT_TOPIC", rootTopic.Code);
        Assert.Equal("CHILD_TOPIC", childTopic.Code);
        Assert.Equal("OUTCOME_A", Assert.Single(rootTopic.Outcomes).Code);
        Assert.Equal("OUTCOME_B", Assert.Single(childTopic.Outcomes).Code);
    }

    [Fact]
    public async Task UserOwnedImport_IsNotVisibleAcrossUsers()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-other");

        var create = await owner.Client.PostAsJsonAsync("/api/exams/import-tree", BuildImport("PRIVATE_EXAM_A"));
        create.EnsureSuccessStatusCode();

        var otherTree = await other.Client.GetAsync("/api/exams/PRIVATE_EXAM_A");
        Assert.Equal(HttpStatusCode.NotFound, otherTree.StatusCode);

        var otherList = await other.Client.GetFromJsonAsync<List<ExamDefinitionDto>>("/api/exams");
        Assert.DoesNotContain(otherList!, definition => definition.Code == "PRIVATE_EXAM_A");
    }

    [Fact]
    public async Task SoftDeletedTreeNodes_AreExcludedFromPublicTree()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-soft-delete");
        var create = await user.Client.PostAsJsonAsync("/api/exams/import-tree", BuildImport("SOFT_DELETE_EXAM"));
        create.EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var topic = await db.ExamTopics.SingleAsync(t => t.Code == "CHILD_TOPIC");
            topic.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var tree = await user.Client.GetFromJsonAsync<ExamDefinitionDto>("/api/exams/SOFT_DELETE_EXAM");
        var rootTopic = tree!.Variants[0].Sections[0].Subjects[0].Topics.Single();

        Assert.Empty(rootTopic.Children);
        Assert.DoesNotContain("CHILD_TOPIC", JsonSerializer.Serialize(tree));
    }

    [Fact]
    public async Task ImportRejectsMalformedTree()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-malformed");

        var missingCode = BuildImport("");
        var duplicateVariant = BuildImport("DUP_VARIANT");
        duplicateVariant.Variants.Add(new ExamVariantImportDto
        {
            Code = "VARIANT_A",
            Name = "Duplicate",
            Sections =
            [
                new ExamSectionImportDto
                {
                    Code = "S2",
                    Name = "Section",
                    Subjects =
                    [
                        new ExamSubjectImportDto
                        {
                            Code = "SUB2",
                            Name = "Subject",
                            Topics = [new ExamTopicImportDto { Code = "T2", Name = "Topic" }]
                        }
                    ]
                }
            ]
        });
        var emptySubjectLabel = BuildImport("EMPTY_LABEL");
        emptySubjectLabel.Variants[0].Sections[0].Subjects[0].Name = "";

        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsJsonAsync("/api/exams/import-tree", missingCode)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsJsonAsync("/api/exams/import-tree", duplicateVariant)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsJsonAsync("/api/exams/import-tree", emptySubjectLabel)).StatusCode);
    }

    [Fact]
    public async Task SourceVerification_BlocksUnsafeOfficialClaimsAndKeepsPublicDtoSafe()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-verify");

        var unverified = await user.Client.PostAsJsonAsync("/api/exams/import-tree", BuildImport("UNVERIFIED_EXAM"));
        unverified.EnsureSuccessStatusCode();
        var unverifiedDto = await unverified.Content.ReadFromJsonAsync<ExamDefinitionDto>();
        Assert.False(unverifiedDto!.CanClaimOfficial);
        Assert.Equal("unverified", unverifiedDto.SourceVerification.VerificationStatus);

        var unsafeOfficial = BuildImport("UNSAFE_OFFICIAL");
        unsafeOfficial.VerificationStatus = "official_verified";
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsJsonAsync("/api/exams/import-tree", unsafeOfficial)).StatusCode);

        var safeOfficial = BuildImport("SAFE_OFFICIAL");
        safeOfficial.VerificationStatus = "official_verified";
        safeOfficial.SourceTitle = "Official source";
        safeOfficial.SourceUrl = "https://example.gov.tr/source";
        var safeOfficialResponse = await user.Client.PostAsJsonAsync("/api/exams/import-tree", safeOfficial);
        safeOfficialResponse.EnsureSuccessStatusCode();
        var body = await safeOfficialResponse.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<ExamDefinitionDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.True(dto!.CanClaimOfficial);
        Assert.Contains("Doğrulanmış resmi kaynak", dto.UserSafeVerificationLabel);
        Assert.DoesNotContain("VerifiedBy", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OwnerUserId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ImportedByUserId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debug", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KpssSkeleton_ReturnsSafeUnverifiedLabelsAndNoOfficialCurriculumClaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-kpss");
        await CreateKpssSkeletonAsync(factory);

        var response = await user.Client.GetAsync("/api/exams/KPSS");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<ExamDefinitionDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(dto);
        Assert.Equal("KPSS hazırlık iskeleti", dto!.Name);
        Assert.False(dto.CanClaimOfficial);
        Assert.Equal("unverified", dto.VerificationStatus);
        Assert.Contains("KPSS_LISANS", body);
        Assert.Contains("KPSS_ONLISANS", body);
        Assert.Contains("GENEL_YETENEK", body);
        Assert.Contains("GENEL_KULTUR", body);
        Assert.Contains("Resmi müfredat iddiası değildir", body);
        Assert.DoesNotContain("official curriculum complete", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resmi KPSS müfredatı tamamlandı", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CreateKpssSkeletonAsync(ApiSmokeFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExamFrameworkService>();
        await service.CreateSystemSkeletonAsync();
    }

    private static ExamTreeImportDto BuildImport(string examCode) => new()
    {
        ExamCode = examCode,
        ExamName = $"{examCode} hazırlık paketi",
        ExamFamily = "exam",
        Variants =
        [
            new ExamVariantImportDto
            {
                Code = "VARIANT_A",
                Name = "Variant A",
                Sections =
                [
                    new ExamSectionImportDto
                    {
                        Code = "SECTION_A",
                        Name = "Section A",
                        Subjects =
                        [
                            new ExamSubjectImportDto
                            {
                                Code = "SUBJECT_A",
                                Name = "Subject A",
                                Topics =
                                [
                                    new ExamTopicImportDto
                                    {
                                        Code = "ROOT_TOPIC",
                                        Name = "Root topic",
                                        Outcomes =
                                        [
                                            new ExamOutcomeImportDto
                                            {
                                                Code = "OUTCOME_A",
                                                Name = "Outcome A"
                                            }
                                        ],
                                        Children =
                                        [
                                            new ExamTopicImportDto
                                            {
                                                Code = "CHILD_TOPIC",
                                                Name = "Child topic",
                                                Outcomes =
                                                [
                                                    new ExamOutcomeImportDto
                                                    {
                                                        Code = "OUTCOME_B",
                                                        Name = "Outcome B"
                                                    }
                                                ]
                                            }
                                        ]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        ]
    };
}

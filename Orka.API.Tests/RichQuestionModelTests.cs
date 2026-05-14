using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class RichQuestionModelTests
{
    [Fact]
    public async Task TextOnlyQuestionCreation_RemainsBackwardCompatible()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-text-backcompat");
        var ids = await ImportExamTreeAsync(user);

        var response = await user.Client.PostAsJsonAsync("/api/questions", BuildQuestion(ids));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var question = await response.Content.ReadFromJsonAsync<QuestionItemDto>();

        Assert.Equal("Which option best matches the outcome?", question!.Stem);
        Assert.Empty(question.ContentBlocks);
        Assert.Empty(question.Stimuli);
        Assert.Equal(2, question.Options.Count);
    }

    [Fact]
    public async Task RichQuestionContentBlocksOptionsAndSharedStimulus_AreReturnedInSortOrder()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-blocks");
        var ids = await ImportExamTreeAsync(user);
        var q1 = await CreateQuestionAsync(user, ids);
        var q2 = await CreateQuestionAsync(user, ids);

        var asset = await CreateAssetAsync(user, new CreateQuestionAssetDto
        {
            AssetType = "image",
            StorageKey = "question-assets/chart-1.png",
            FileName = "chart-1.png",
            MimeType = "image/png",
            SizeBytes = 1234,
            Sha256Hash = "ABC123",
            LicenseStatus = "open",
            AltText = "Bar chart showing paragraph answer distribution."
        });

        var stimulusResponse = await user.Client.PostAsJsonAsync("/api/questions/stimuli", new CreateQuestionStimulusDto
        {
            Title = "Shared paragraph",
            StimulusType = "passage",
            ContentText = "A shared reading passage."
        });
        stimulusResponse.EnsureSuccessStatusCode();
        var stimulus = await stimulusResponse.Content.ReadFromJsonAsync<QuestionStimulusDto>();

        await user.Client.PostAsJsonAsync($"/api/questions/{q1.Id}/stimuli", new QuestionStimulusLinkDto { QuestionStimulusId = stimulus!.Id, SortOrder = 0 });
        await user.Client.PostAsJsonAsync($"/api/questions/{q2.Id}/stimuli", new QuestionStimulusLinkDto { QuestionStimulusId = stimulus.Id, SortOrder = 0 });

        var table = await user.Client.PostAsJsonAsync($"/api/questions/{q1.Id}/content-blocks", new CreateQuestionContentBlockDto
        {
            BlockType = "table",
            ContentJson = "{\"columns\":[\"A\"],\"rows\":[[1]]}",
            Caption = "Small table",
            SortOrder = 1
        });
        table.EnsureSuccessStatusCode();

        var image = await user.Client.PostAsJsonAsync($"/api/questions/{q1.Id}/content-blocks", new CreateQuestionContentBlockDto
        {
            BlockType = "image",
            AssetId = asset.Id,
            AltText = "Chart for question",
            SortOrder = 0
        });
        image.EnsureSuccessStatusCode();

        var formula = await user.Client.PostAsJsonAsync($"/api/questions/{q1.Id}/content-blocks", new CreateQuestionContentBlockDto
        {
            BlockType = "formula",
            Text = "x + 1 = 2",
            SortOrder = 2
        });
        formula.EnsureSuccessStatusCode();

        var optionId = q1.Options.First(o => o.OptionKey == "B").Id!.Value;
        var optionBlock = await user.Client.PostAsJsonAsync($"/api/questions/options/{optionId}/content-blocks", new CreateQuestionOptionContentBlockDto
        {
            BlockType = "formula",
            Text = "x = 3",
            SortOrder = 0
        });
        optionBlock.EnsureSuccessStatusCode();

        var loaded = await user.Client.GetFromJsonAsync<QuestionItemDto>($"/api/questions/{q1.Id}");
        Assert.Equal(["image", "table", "formula"], loaded!.ContentBlocks.Select(b => b.BlockType).ToArray());
        Assert.Single(loaded.Stimuli);
        Assert.Equal(stimulus.Id, loaded.Stimuli[0].Id);
        Assert.Single(loaded.Options.First(o => o.OptionKey == "B").ContentBlocks);
        Assert.Equal("image", loaded.ContentBlocks[0].Asset!.AssetType);

        var second = await user.Client.GetFromJsonAsync<QuestionItemDto>($"/api/questions/{q2.Id}");
        Assert.Equal(stimulus.Id, Assert.Single(second!.Stimuli).Id);
    }

    [Fact]
    public async Task AssetOwnershipAndStorageSafety_AreEnforced()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-asset-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-asset-other");
        var ids = await ImportExamTreeAsync(other);
        var question = await CreateQuestionAsync(other, ids);
        var ownerAsset = await CreateAssetAsync(owner, new CreateQuestionAssetDto
        {
            AssetType = "image",
            StorageKey = "question-assets/private.png",
            FileName = "private.png",
            MimeType = "image/png",
            SizeBytes = 10,
            Sha256Hash = "ownerhash",
            LicenseStatus = "open",
            AltText = "Private image"
        });

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/question-assets/{ownerAsset.Id}")).StatusCode);

        var attachOtherAsset = await other.Client.PostAsJsonAsync($"/api/questions/{question.Id}/content-blocks", new CreateQuestionContentBlockDto
        {
            BlockType = "image",
            AssetId = ownerAsset.Id,
            AltText = "Should not attach"
        });
        Assert.Equal(HttpStatusCode.BadRequest, attachOtherAsset.StatusCode);

        var unsafeStorage = await other.Client.PostAsJsonAsync("/api/question-assets", new CreateQuestionAssetDto
        {
            AssetType = "image",
            StorageKey = @"C:\secret\image.png",
            FileName = "image.png",
            Sha256Hash = "unsafe"
        });
        Assert.Equal(HttpStatusCode.BadRequest, unsafeStorage.StatusCode);

        var systemAssetId = await CreateSystemAssetAsync(factory);
        var systemRead = await other.Client.GetAsync($"/api/question-assets/{systemAssetId}");
        systemRead.EnsureSuccessStatusCode();
        var raw = await systemRead.Content.ReadAsStringAsync();
        Assert.DoesNotContain("OwnerUserId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccessibilityAndAssetLicense_GatePublishButAllowDraftWarnings()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-accessibility");
        var ids = await ImportExamTreeAsync(user);
        var question = await CreateQuestionAsync(user, ids);
        var asset = await CreateAssetAsync(user, new CreateQuestionAssetDto
        {
            AssetType = "image",
            StorageKey = "question-assets/no-alt.png",
            FileName = "no-alt.png",
            MimeType = "image/png",
            SizeBytes = 10,
            Sha256Hash = "noalt",
            LicenseStatus = "open"
        });

        var draft = await user.Client.PostAsJsonAsync($"/api/questions/{question.Id}/content-blocks", new CreateQuestionContentBlockDto
        {
            BlockType = "image",
            AssetId = asset.Id,
            SortOrder = 0
        });
        draft.EnsureSuccessStatusCode();
        var draftDto = await draft.Content.ReadFromJsonAsync<QuestionItemDto>();
        Assert.Contains("image_asset_requires_alt_text", draftDto!.Validation.Warnings);

        var approved = await user.Client.PutAsJsonAsync($"/api/questions/{question.Id}", new UpdateQuestionDto
        {
            QualityStatus = "approved",
            LicenseStatus = "open"
        });
        approved.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsync($"/api/questions/{question.Id}/publish", null)).StatusCode);

        var validQuestion = await CreateQuestionAsync(user, ids);
        var validAsset = await CreateAssetAsync(user, new CreateQuestionAssetDto
        {
            AssetType = "image",
            StorageKey = "question-assets/with-alt.png",
            FileName = "with-alt.png",
            MimeType = "image/png",
            SizeBytes = 10,
            Sha256Hash = "withalt",
            LicenseStatus = "open",
            AltText = "Diagram with labeled paragraph structure"
        });
        var validBlock = await user.Client.PostAsJsonAsync($"/api/questions/{validQuestion.Id}/content-blocks", new CreateQuestionContentBlockDto
        {
            BlockType = "image",
            AssetId = validAsset.Id,
            SortOrder = 0
        });
        validBlock.EnsureSuccessStatusCode();
        var validApproved = await user.Client.PutAsJsonAsync($"/api/questions/{validQuestion.Id}", new UpdateQuestionDto
        {
            QualityStatus = "approved",
            LicenseStatus = "open"
        });
        validApproved.EnsureSuccessStatusCode();
        var publish = await user.Client.PostAsync($"/api/questions/{validQuestion.Id}/publish", null);
        publish.EnsureSuccessStatusCode();

        var restrictedQuestion = await CreateQuestionAsync(user, ids);
        var restrictedAsset = await CreateAssetAsync(user, new CreateQuestionAssetDto
        {
            AssetType = "image",
            StorageKey = "question-assets/restricted.png",
            FileName = "restricted.png",
            MimeType = "image/png",
            SizeBytes = 10,
            Sha256Hash = "restricted",
            LicenseStatus = "restricted",
            AltText = "Restricted image"
        });
        await user.Client.PostAsJsonAsync($"/api/questions/{restrictedQuestion.Id}/content-blocks", new CreateQuestionContentBlockDto
        {
            BlockType = "image",
            AssetId = restrictedAsset.Id
        });
        await user.Client.PutAsJsonAsync($"/api/questions/{restrictedQuestion.Id}", new UpdateQuestionDto
        {
            QualityStatus = "approved",
            LicenseStatus = "open"
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsync($"/api/questions/{restrictedQuestion.Id}/publish", null)).StatusCode);
    }

    [Fact]
    public async Task AssetSourceMetadata_IsPreservedWithoutOfficialClaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-source-metadata");
        var source = await RegisterSourceAsync(user);

        var asset = await CreateAssetAsync(user, new CreateQuestionAssetDto
        {
            AssetType = "image",
            StorageKey = "question-assets/source-backed.png",
            FileName = "source-backed.png",
            MimeType = "image/png",
            SizeBytes = 10,
            Sha256Hash = "sourcebacked",
            SourceRegistryItemId = source.Id,
            SourceTitle = "Source image",
            SourceUrl = "https://example.org/source-image",
            LicenseStatus = "open",
            VerificationStatus = "source_backed",
            AltText = "Source-backed image"
        });

        Assert.Equal(source.Id, asset.SourceRegistryItemId);
        Assert.Equal("source_backed", asset.VerificationStatus);
        Assert.Equal("open", asset.LicenseStatus);
    }

    private static async Task<QuestionAssetDto> CreateAssetAsync(CoordinationTestUser user, CreateQuestionAssetDto request)
    {
        var response = await user.Client.PostAsJsonAsync("/api/question-assets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<QuestionAssetDto>())!;
    }

    private static async Task<QuestionItemDto> CreateQuestionAsync(CoordinationTestUser user, QuestionTreeIds ids)
    {
        var response = await user.Client.PostAsJsonAsync("/api/questions", BuildQuestion(ids));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuestionItemDto>())!;
    }

    private static async Task<SourceRegistryItemDto> RegisterSourceAsync(CoordinationTestUser user)
    {
        var response = await user.Client.PostAsJsonAsync("/api/curriculum/sources", new RegisterSourceRegistryItemDto
        {
            SourceKey = $"rich-source-{Guid.NewGuid():N}",
            Title = "Rich source",
            SourceUrl = "https://example.org/rich-source",
            SourceType = "open_reference",
            VerificationStatus = "source_backed"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SourceRegistryItemDto>())!;
    }

    private static async Task<Guid> CreateSystemAssetAsync(ApiSmokeFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var asset = new QuestionAsset
        {
            AssetType = "image",
            StorageKey = "question-assets/system.png",
            FileName = "system.png",
            MimeType = "image/png",
            SizeBytes = 42,
            Sha256Hash = "systemhash",
            LicenseStatus = "open",
            VerificationStatus = "source_backed",
            AltText = "System image"
        };
        db.QuestionAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    private static async Task<QuestionTreeIds> ImportExamTreeAsync(CoordinationTestUser user)
    {
        var import = new ExamTreeImportDto
        {
            ExamCode = $"RICH_{Guid.NewGuid():N}",
            ExamName = "Rich exam tree",
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
                                            Code = "TOPIC_A",
                                            Name = "Topic A",
                                            Outcomes =
                                            [
                                                new ExamOutcomeImportDto
                                                {
                                                    Code = "OUTCOME_A",
                                                    Name = "Outcome A"
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
        var response = await user.Client.PostAsJsonAsync("/api/exams/import-tree", import);
        response.EnsureSuccessStatusCode();
        var tree = await response.Content.ReadFromJsonAsync<ExamDefinitionDto>();
        var variant = tree!.Variants[0];
        var section = variant.Sections[0];
        var subject = section.Subjects[0];
        var topic = subject.Topics[0];
        var outcome = topic.Outcomes[0];
        return new QuestionTreeIds(tree.Id, variant.Id, section.Id, subject.Id, topic.Id, outcome.Id);
    }

    private static CreateQuestionDto BuildQuestion(QuestionTreeIds ids) => new()
    {
        ExamDefinitionId = ids.DefinitionId,
        ExamVariantId = ids.VariantId,
        ExamSectionId = ids.SectionId,
        ExamSubjectId = ids.SubjectId,
        ExamTopicId = ids.TopicId,
        ExamOutcomeId = ids.OutcomeId,
        QuestionType = "multiple_choice",
        Stem = "Which option best matches the outcome?",
        Difficulty = "medium",
        CognitiveSkill = "conceptual",
        LicenseStatus = "unknown",
        SourceOrigin = "user_provided",
        Explanation = "A is the intended answer.",
        Options =
        [
            new QuestionOptionDto { OptionKey = "A", Text = "Correct answer", IsCorrect = true, SortOrder = 0 },
            new QuestionOptionDto { OptionKey = "B", Text = "Distractor", IsCorrect = false, SortOrder = 1 }
        ],
        Tags = [new QuestionTagDto { Tag = "rich" }]
    };

    private sealed record QuestionTreeIds(Guid DefinitionId, Guid VariantId, Guid SectionId, Guid SubjectId, Guid TopicId, Guid OutcomeId);
}

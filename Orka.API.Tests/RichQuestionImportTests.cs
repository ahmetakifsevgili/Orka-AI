using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class RichQuestionImportTests
{
    [Fact]
    public async Task JsonV2PreviewAcceptsRichPackageAndDoesNotCreateContent()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-import-preview");
        var ids = await ImportExamTreeAsync(user);
        var package = ValidRichPackage(ids);

        var before = await CountRichContentAsync(factory);
        var preview = await PreviewPackageAsync(user, package);
        var after = await CountRichContentAsync(factory);

        Assert.Equal("json_v2", preview.ImportFormat);
        Assert.Equal("Rich KPSS package", preview.PackageTitle);
        Assert.Equal(1, preview.AcceptedCount);
        Assert.Equal(0, preview.RejectedCount);
        Assert.Single(preview.Assets);
        Assert.Single(preview.Stimuli);
        Assert.Equal("accepted", preview.Items[0].Status);
        Assert.Equal(before, after);
        Assert.DoesNotContain("raw", await user.Client.GetStringAsync($"/api/question-imports/{preview.Id}"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApprovalCreatesDraftReviewQuestionWithAssetsStimuliAndRichBlocks()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-import-approve");
        var ids = await ImportExamTreeAsync(user);
        var preview = await PreviewPackageAsync(user, ValidRichPackage(ids));

        var approve = await user.Client.PostAsJsonAsync("/api/question-imports/approve", new QuestionImportApprovalDto { ImportPreviewId = preview.Id });
        approve.EnsureSuccessStatusCode();
        var result = await approve.Content.ReadFromJsonAsync<QuestionImportResultDto>();

        Assert.Equal(1, result!.CreatedCount);
        var question = await user.Client.GetFromJsonAsync<QuestionItemDto>($"/api/questions/{result.CreatedQuestionIds[0]}");
        Assert.NotNull(question);
        Assert.Equal("needs_review", question!.QualityStatus);
        Assert.NotEqual("published", question.QualityStatus);
        Assert.Single(question.Stimuli);
        Assert.Equal(3, question.ContentBlocks.Count);
        Assert.Contains(question.ContentBlocks, b => b.BlockType == "image" && b.Asset is not null);
        Assert.Contains(question.Options, o => o.ContentBlocks.Any(b => b.BlockType == "image" && b.Asset is not null));
    }

    [Fact]
    public async Task JsonV2ValidationRejectsUnsafePackageReferences()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-import-invalid");
        var ids = await ImportExamTreeAsync(user);
        var package = ValidRichPackage(ids);
        package.Assets =
        [
            package.Assets[0],
            Mutate(package.Assets[0], a =>
            {
                a.ExternalAssetId = "IMG_1";
                a.StorageKey = "C:\\unsafe\\image.png";
                a.Sha256Hash = "";
            })
        ];
        package.Questions[0].ContentBlocks.Add(new QuestionImportContentBlockDto
        {
            BlockType = "image",
            ExternalAssetId = "MISSING",
            SortOrder = 10,
            AltText = "Missing image"
        });

        var preview = await PreviewPackageAsync(user, package);
        var codes = preview.Items.SelectMany(i => i.Issues).Select(i => i.Code).ToList();

        Assert.Equal(1, preview.RejectedCount);
        Assert.Contains("duplicate_external_asset_id", codes);
        Assert.Contains("unsafe_asset_storage_key", codes);
        Assert.Contains("asset_sha256_required", codes);
        Assert.Contains("missing_referenced_asset", codes);
    }

    [Fact]
    public async Task MissingAccessibilityWarnsAndPublishRemainsBlocked()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-import-accessibility");
        var ids = await ImportExamTreeAsync(user);
        var package = ValidRichPackage(ids);
        package.Assets[0].AltText = null;
        package.Assets[0].Caption = null;
        package.Questions[0].ContentBlocks[0].AltText = null;
        package.Questions[0].ContentBlocks[0].Caption = null;
        var preview = await PreviewPackageAsync(user, package);

        Assert.Equal(1, preview.AcceptedCount);
        Assert.Contains(preview.Items[0].Issues, i => i.Code.Contains("accessibility", StringComparison.OrdinalIgnoreCase));

        var approve = await user.Client.PostAsJsonAsync("/api/question-imports/approve", new QuestionImportApprovalDto { ImportPreviewId = preview.Id });
        approve.EnsureSuccessStatusCode();
        var result = await approve.Content.ReadFromJsonAsync<QuestionImportResultDto>();
        var publish = await user.Client.PostAsync($"/api/questions/{result!.CreatedQuestionIds[0]}/publish", null);

        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
    }

    [Fact]
    public async Task ApprovalIsCallerScopedAndIdempotent()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-import-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-import-other");
        var ids = await ImportExamTreeAsync(owner);
        var preview = await PreviewPackageAsync(owner, ValidRichPackage(ids));

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/question-imports/{preview.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.Client.PostAsJsonAsync("/api/question-imports/approve", new QuestionImportApprovalDto { ImportPreviewId = preview.Id })).StatusCode);

        var first = await owner.Client.PostAsJsonAsync("/api/question-imports/approve", new QuestionImportApprovalDto { ImportPreviewId = preview.Id });
        first.EnsureSuccessStatusCode();
        var firstResult = await first.Content.ReadFromJsonAsync<QuestionImportResultDto>();
        var second = await owner.Client.PostAsJsonAsync("/api/question-imports/approve", new QuestionImportApprovalDto { ImportPreviewId = preview.Id });
        second.EnsureSuccessStatusCode();
        var secondResult = await second.Content.ReadFromJsonAsync<QuestionImportResultDto>();

        Assert.Equal(firstResult!.CreatedQuestionIds, secondResult!.CreatedQuestionIds);
    }

    [Fact]
    public async Task StandardsPreviewAdaptersAreSafeAndDoNotAutoPublish()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rich-import-adapters");
        var ids = await ImportExamTreeAsync(user);
        var request = AdapterRequest(ids);
        request.Content = """
        Paragrafın ana fikri nedir?
        A. Ana fikir
        B. Yardımcı düşünce
        ANSWER: A
        """;

        var aiken = await user.Client.PostAsJsonAsync("/api/question-imports/preview-aiken", request);
        aiken.EnsureSuccessStatusCode();
        var aikenPreview = await aiken.Content.ReadFromJsonAsync<QuestionImportPreviewDto>();
        Assert.Equal(1, aikenPreview!.AcceptedCount);

        var qti = await user.Client.PostAsJsonAsync("/api/question-imports/preview-qti", request);
        qti.EnsureSuccessStatusCode();
        var qtiPreview = await qti.Content.ReadFromJsonAsync<QuestionImportPreviewDto>();
        Assert.Equal("qti3", qtiPreview!.ImportFormat);
        Assert.Equal(1, qtiPreview.RejectedCount);
        Assert.Contains(qtiPreview.Items[0].Issues, i => i.Code == "qti3_partial_support");

        var questions = await user.Client.GetFromJsonAsync<List<QuestionItemDto>>($"/api/questions?examOutcomeId={ids.OutcomeId}");
        Assert.Empty(questions!);
    }

    private static async Task<QuestionImportPreviewDto> PreviewPackageAsync(CoordinationTestUser user, QuestionImportPackageDto package)
    {
        var response = await user.Client.PostAsJsonAsync("/api/question-imports/preview-package", package);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuestionImportPreviewDto>())!;
    }

    private static QuestionImportPackageDto ValidRichPackage(QuestionTreeIds ids) => new()
    {
        PackageVersion = "2.0",
        PackageTitle = "Rich KPSS package",
        SourceOrigin = "structured_json_v2",
        LicenseStatus = "open",
        SourceTitle = "Safe source",
        SourceUrl = "https://example.org/source",
        ExamDefinitionId = ids.DefinitionId,
        ExamVariantId = ids.VariantId,
        ExamSectionId = ids.SectionId,
        ExamSubjectId = ids.SubjectId,
        ExamTopicId = ids.TopicId,
        ExamOutcomeId = ids.OutcomeId,
        Assets =
        [
            new QuestionImportAssetDto
            {
                ExternalAssetId = "IMG_1",
                AssetType = "image",
                StorageKey = "assets/image-1.png",
                FileName = "image-1.png",
                MimeType = "image/png",
                SizeBytes = 1200,
                Sha256Hash = "abc123richimport",
                LicenseStatus = "open",
                VerificationStatus = "source_backed",
                SourceTitle = "Safe image source",
                SourceUrl = "https://example.org/image",
                AltText = "Paragrafı temsil eden görsel",
                Caption = "Paragraf görseli"
            }
        ],
        Stimuli =
        [
            new QuestionImportStimulusDto
            {
                ExternalStimulusId = "PASSAGE_1",
                Title = "Paragraf stimulus",
                StimulusType = "passage",
                ContentText = "Kısa bir paragraf stimulus metni.",
                LicenseStatus = "open",
                VerificationStatus = "source_backed"
            }
        ],
        Questions =
        [
            new QuestionImportRichQuestionDto
            {
                ExternalId = "RICH_Q_1",
                QuestionType = "multiple_choice",
                Stem = "Paragrafın ana fikri nedir?",
                Difficulty = "medium",
                CognitiveSkill = "analysis",
                Explanation = "Ana fikir paragrafın genel yargısıdır.",
                ExternalStimulusIds = ["PASSAGE_1"],
                Tags = ["rich_package"],
                ContentBlocks =
                [
                    new QuestionImportContentBlockDto { BlockType = "image", ExternalAssetId = "IMG_1", SortOrder = 0, AltText = "Paragraf görseli" },
                    new QuestionImportContentBlockDto { BlockType = "table", ContentJson = "{\"headers\":[\"A\"],\"rows\":[[\"B\"]]}", SortOrder = 1, Caption = "Mini tablo" },
                    new QuestionImportContentBlockDto { BlockType = "formula", Text = "x + y = z", SortOrder = 2 }
                ],
                Options =
                [
                    new QuestionImportRichOptionDto
                    {
                        OptionKey = "A",
                        Text = "Ana fikir",
                        IsCorrect = true,
                        SortOrder = 0,
                        ContentBlocks = [new QuestionImportContentBlockDto { BlockType = "image", ExternalAssetId = "IMG_1", SortOrder = 0, AltText = "A seçeneği görseli" }]
                    },
                    new QuestionImportRichOptionDto { OptionKey = "B", Text = "Örnek olay", IsCorrect = false, SortOrder = 1 }
                ]
            }
        ]
    };

    private static QuestionImportTextAdapterRequestDto AdapterRequest(QuestionTreeIds ids) => new()
    {
        ExamDefinitionId = ids.DefinitionId,
        ExamVariantId = ids.VariantId,
        ExamSectionId = ids.SectionId,
        ExamSubjectId = ids.SubjectId,
        ExamTopicId = ids.TopicId,
        ExamOutcomeId = ids.OutcomeId,
        LicenseStatus = "open",
        SourceOrigin = "adapter_test",
        SourceTitle = "Adapter source",
        SourceUrl = "https://example.org/adapter"
    };

    private static T Mutate<T>(T value, Action<T> action)
    {
        action(value);
        return value;
    }

    private static async Task<(int Questions, int Assets, int Stimuli)> CountRichContentAsync(ApiSmokeFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        return (
            await db.QuestionItems.CountAsync(),
            await db.QuestionAssets.CountAsync(),
            await db.QuestionStimuli.CountAsync());
    }

    private static async Task<QuestionTreeIds> ImportExamTreeAsync(CoordinationTestUser user)
    {
        var import = new ExamTreeImportDto
        {
            ExamCode = $"RICH_IMPORT_{Guid.NewGuid():N}",
            ExamName = "Rich import tree",
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
                                    Code = "TURKCE",
                                    Name = "Türkçe",
                                    Topics =
                                    [
                                        new ExamTopicImportDto
                                        {
                                            Code = "PARAGRAF",
                                            Name = "Paragraf",
                                            Outcomes =
                                            [
                                                new ExamOutcomeImportDto
                                                {
                                                    Code = "ANA_FIKIR",
                                                    Name = "Ana fikir"
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

    private sealed record QuestionTreeIds(
        Guid DefinitionId,
        Guid VariantId,
        Guid SectionId,
        Guid SubjectId,
        Guid TopicId,
        Guid OutcomeId);
}

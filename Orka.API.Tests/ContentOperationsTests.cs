using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class ContentOperationsTests
{
    [Fact]
    public async Task SubmitReviewAdvanceApprovalAndPublish_CreateAuditTrailAndVersions()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "content-ops-happy");
        var ids = await ImportExamTreeAsync(user);
        var question = await CreateQuestionAsync(user, ids);

        var readinessBefore = await user.Client.GetFromJsonAsync<QuestionPublishReadinessDto>($"/api/content-ops/questions/{question.Id}/publish-readiness");
        Assert.False(readinessBefore!.IsReadyToPublish);
        Assert.Contains(readinessBefore.BlockingIssues, i => i.Code == "workflow_required");

        var submit = await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/submit-review", new SubmitQuestionReviewDto { SafeNote = "Ready for editorial review" });
        submit.EnsureSuccessStatusCode();
        var workflow = await submit.Content.ReadFromJsonAsync<QuestionReviewWorkflowDto>();
        Assert.Equal("editorial_review", workflow!.CurrentStage);
        Assert.Contains(workflow.Events, e => e.EventType == "submitted");

        await AdvanceAsync(user, question.Id, "pedagogy_review");
        await AdvanceAsync(user, question.Id, "accessibility_review");
        await AdvanceAsync(user, question.Id, "source_review");
        var approved = await AdvanceAsync(user, question.Id, "approved");
        Assert.Equal("approved", approved.CurrentStage);
        Assert.Equal("approved", approved.Status);

        var ready = await user.Client.GetFromJsonAsync<QuestionPublishReadinessDto>($"/api/content-ops/questions/{question.Id}/publish-readiness");
        Assert.True(ready!.IsReadyToPublish);

        var publish = await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/publish", new PublishQuestionContentDto { SafeNote = "Publish after review" });
        publish.EnsureSuccessStatusCode();
        var published = await publish.Content.ReadFromJsonAsync<QuestionItemDto>();
        Assert.Equal("published", published!.QualityStatus);

        var finalWorkflow = await user.Client.GetFromJsonAsync<QuestionReviewWorkflowDto>($"/api/content-ops/questions/{question.Id}/workflow");
        Assert.Equal("published", finalWorkflow!.CurrentStage);
        Assert.Contains(finalWorkflow.Events, e => e.EventType == "published");

        var versions = await user.Client.GetFromJsonAsync<List<QuestionContentVersionDto>>($"/api/content-ops/questions/{question.Id}/versions");
        Assert.True(versions!.Count >= 3);
        Assert.Equal(versions.Select(v => v.VersionNumber), versions.Select(v => v.VersionNumber).OrderBy(v => v));
        var rawVersions = await user.Client.GetStringAsync($"/api/content-ops/questions/{question.Id}/versions");
        Assert.DoesNotContain("snapshotJson", rawVersions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debug", rawVersions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidTransitionRejectAndRetireRules_AreEnforced()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "content-ops-transitions");
        var ids = await ImportExamTreeAsync(user);
        var question = await CreateQuestionAsync(user, ids);
        await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/submit-review", new SubmitQuestionReviewDto());

        var invalid = await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/advance-stage", new AdvanceQuestionReviewStageDto { ToStage = "approved" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var rejectWithoutReason = await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/reject", new RejectQuestionReviewDto());
        Assert.Equal(HttpStatusCode.BadRequest, rejectWithoutReason.StatusCode);

        var reject = await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/reject", new RejectQuestionReviewDto { Reason = "Needs source cleanup" });
        reject.EnsureSuccessStatusCode();
        var rejected = await reject.Content.ReadFromJsonAsync<QuestionReviewWorkflowDto>();
        Assert.Equal("rejected", rejected!.CurrentStage);

        var published = await CreateAndPublishThroughContentOpsAsync(user, ids);
        var retire = await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{published.Id}/retire", new RetireQuestionDto { Reason = "Superseded by better item" });
        retire.EnsureSuccessStatusCode();

        var list = await user.Client.GetFromJsonAsync<List<QuestionItemDto>>($"/api/questions?qualityStatus=published&examOutcomeId={ids.OutcomeId}");
        Assert.DoesNotContain(list!, q => q.Id == published.Id);
    }

    [Fact]
    public async Task ReadinessReportsBlockingAndWarningIssuesWithoutInternalMetadata()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "content-ops-readiness");
        var ids = await ImportExamTreeAsync(user);
        var question = await CreateQuestionAsync(user, ids, licenseStatus: "restricted", sourceTitle: null, explanation: "");
        await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/submit-review", new SubmitQuestionReviewDto());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var option = await db.QuestionOptions.FirstAsync(o => o.QuestionItemId == question.Id && o.IsCorrect);
            option.IsCorrect = false;
            await db.SaveChangesAsync();
        }

        var readiness = await user.Client.GetFromJsonAsync<QuestionPublishReadinessDto>($"/api/content-ops/questions/{question.Id}/publish-readiness");
        Assert.False(readiness!.IsReadyToPublish);
        Assert.Contains(readiness.BlockingIssues, i => i.Code == "multiple_choice_single_correct_option_required");
        Assert.Contains(readiness.BlockingIssues, i => i.Code == "safe_license_required");
        Assert.Contains(readiness.WarningIssues, i => i.Code == "source_title_missing");
        Assert.Contains(readiness.WarningIssues, i => i.Code == "explanation_missing");

        var raw = await user.Client.GetStringAsync($"/api/content-ops/questions/{question.Id}/publish-readiness");
        Assert.DoesNotContain("OwnerUserId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debug", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawImport", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccessibilityAndLicenseIssues_BlockContentOpsApprovalAndPublish()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "content-ops-accessibility");
        var ids = await ImportExamTreeAsync(user);
        var question = await CreateQuestionAsync(user, ids);
        var asset = await CreateAssetAsync(user, licenseStatus: "restricted", altText: null);

        var block = await user.Client.PostAsJsonAsync($"/api/questions/{question.Id}/content-blocks", new CreateQuestionContentBlockDto
        {
            BlockType = "image",
            AssetId = asset.Id,
            SortOrder = 0
        });
        block.EnsureSuccessStatusCode();

        await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/submit-review", new SubmitQuestionReviewDto());
        await AdvanceAsync(user, question.Id, "pedagogy_review");
        await AdvanceAsync(user, question.Id, "accessibility_review");
        await AdvanceAsync(user, question.Id, "source_review");
        var approve = await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/advance-stage", new AdvanceQuestionReviewStageDto { ToStage = "approved" });
        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);

        var readiness = await user.Client.GetFromJsonAsync<QuestionPublishReadinessDto>($"/api/content-ops/questions/{question.Id}/publish-readiness");
        Assert.Contains(readiness!.BlockingIssues, i => i.Code == "image_requires_alt_text_or_caption");
        Assert.Contains(readiness.BlockingIssues, i => i.Code == "image_asset_requires_alt_text");
        Assert.Contains(readiness.BlockingIssues, i => i.Code == "asset_requires_safe_license_status");
    }

    [Fact]
    public async Task OwnershipAndSystemMutationRules_AreEnforced()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "content-ops-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "content-ops-other");
        var ids = await ImportExamTreeAsync(owner);
        var question = await CreateQuestionAsync(owner, ids);
        await owner.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/submit-review", new SubmitQuestionReviewDto());

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/content-ops/questions/{question.Id}/workflow")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/publish", new PublishQuestionContentDto())).StatusCode);

        var systemQuestionId = await CreateSystemQuestionAsync(factory, ids);
        var systemSubmit = await owner.Client.PostAsJsonAsync($"/api/content-ops/questions/{systemQuestionId}/submit-review", new SubmitQuestionReviewDto());
        Assert.Equal(HttpStatusCode.BadRequest, systemSubmit.StatusCode);
    }

    [Fact]
    public async Task ImportedGeneratedContentKeepsReviewWarningsAndDoesNotAutoPublish()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "content-ops-generated");
        var ids = await ImportExamTreeAsync(user);
        var question = await CreateQuestionAsync(user, ids, sourceOrigin: "generated_draft");
        await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/submit-review", new SubmitQuestionReviewDto());

        var readiness = await user.Client.GetFromJsonAsync<QuestionPublishReadinessDto>($"/api/content-ops/questions/{question.Id}/publish-readiness");
        Assert.Contains(readiness!.WarningIssues, i => i.Code == "imported_or_generated_content_requires_review");

        var loaded = await user.Client.GetFromJsonAsync<QuestionItemDto>($"/api/questions/{question.Id}");
        Assert.NotEqual("published", loaded!.QualityStatus);
    }

    private static async Task<QuestionItemDto> CreateAndPublishThroughContentOpsAsync(CoordinationTestUser user, QuestionTreeIds ids)
    {
        var question = await CreateQuestionAsync(user, ids);
        await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/submit-review", new SubmitQuestionReviewDto());
        await AdvanceAsync(user, question.Id, "pedagogy_review");
        await AdvanceAsync(user, question.Id, "accessibility_review");
        await AdvanceAsync(user, question.Id, "source_review");
        await AdvanceAsync(user, question.Id, "approved");
        var publish = await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{question.Id}/publish", new PublishQuestionContentDto());
        publish.EnsureSuccessStatusCode();
        return (await publish.Content.ReadFromJsonAsync<QuestionItemDto>())!;
    }

    private static async Task<QuestionReviewWorkflowDto> AdvanceAsync(CoordinationTestUser user, Guid questionId, string toStage)
    {
        var response = await user.Client.PostAsJsonAsync($"/api/content-ops/questions/{questionId}/advance-stage", new AdvanceQuestionReviewStageDto { ToStage = toStage });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuestionReviewWorkflowDto>())!;
    }

    private static async Task<QuestionAssetDto> CreateAssetAsync(CoordinationTestUser user, string licenseStatus, string? altText)
    {
        var response = await user.Client.PostAsJsonAsync("/api/question-assets", new CreateQuestionAssetDto
        {
            AssetType = "image",
            StorageKey = $"question-assets/{Guid.NewGuid():N}.png",
            FileName = "asset.png",
            MimeType = "image/png",
            SizeBytes = 12,
            Sha256Hash = Guid.NewGuid().ToString("N"),
            LicenseStatus = licenseStatus,
            AltText = altText
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuestionAssetDto>())!;
    }

    private static async Task<QuestionItemDto> CreateQuestionAsync(
        CoordinationTestUser user,
        QuestionTreeIds ids,
        string licenseStatus = "open",
        string? sourceTitle = "Safe source",
        string sourceOrigin = "user_provided",
        string explanation = "A is the intended answer.")
    {
        var response = await user.Client.PostAsJsonAsync("/api/questions", new CreateQuestionDto
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
            LicenseStatus = licenseStatus,
            SourceOrigin = sourceOrigin,
            SourceTitle = sourceTitle,
            SourceUrl = "https://example.org/source",
            Explanation = explanation,
            Options =
            [
                new QuestionOptionDto { OptionKey = "A", Text = "Correct answer", IsCorrect = true, SortOrder = 0 },
                new QuestionOptionDto { OptionKey = "B", Text = "Distractor", IsCorrect = false, SortOrder = 1 }
            ],
            Tags = [new QuestionTagDto { Tag = sourceOrigin }]
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuestionItemDto>())!;
    }

    private static async Task<Guid> CreateSystemQuestionAsync(ApiSmokeFactory factory, QuestionTreeIds ids)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var question = new QuestionItem
        {
            ExamDefinitionId = ids.DefinitionId,
            ExamVariantId = ids.VariantId,
            ExamSectionId = ids.SectionId,
            ExamSubjectId = ids.SubjectId,
            ExamTopicId = ids.TopicId,
            ExamOutcomeId = ids.OutcomeId,
            QuestionType = "multiple_choice",
            Stem = "System global question",
            Difficulty = "medium",
            CognitiveSkill = "conceptual",
            QualityStatus = "approved",
            LicenseStatus = "open",
            SourceOrigin = "system_seed",
            Explanation = "System explanation",
            Options =
            [
                new QuestionOption { OptionKey = "A", Text = "Correct", IsCorrect = true, SortOrder = 0 },
                new QuestionOption { OptionKey = "B", Text = "Wrong", IsCorrect = false, SortOrder = 1 }
            ]
        };
        db.QuestionItems.Add(question);
        await db.SaveChangesAsync();
        return question.Id;
    }

    private static async Task<QuestionTreeIds> ImportExamTreeAsync(CoordinationTestUser user)
    {
        var import = new ExamTreeImportDto
        {
            ExamCode = $"CONTENT_OPS_{Guid.NewGuid():N}",
            ExamName = "Content ops exam tree",
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

    private sealed record QuestionTreeIds(Guid DefinitionId, Guid VariantId, Guid SectionId, Guid SubjectId, Guid TopicId, Guid OutcomeId);
}

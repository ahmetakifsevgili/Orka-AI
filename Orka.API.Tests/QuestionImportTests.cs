using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class QuestionImportTests
{
    [Fact]
    public async Task PreviewAcceptsValidStructuredQuestionAndDoesNotCreateQuestions()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qimport-preview-valid");
        var ids = await ImportExamTreeAsync(user);

        var preview = await PreviewAsync(user, BuildImport(ids));

        Assert.Equal("pending", preview.Status);
        Assert.Equal(1, preview.TotalCount);
        Assert.Equal(1, preview.AcceptedCount);
        Assert.Equal(0, preview.RejectedCount);
        Assert.Equal("accepted", preview.Items[0].Status);
        Assert.Equal(ids.OutcomeId, preview.Items[0].NormalizedQuestion!.ExamOutcomeId);

        var questions = await user.Client.GetFromJsonAsync<List<QuestionItemDto>>($"/api/questions?examOutcomeId={ids.OutcomeId}");
        Assert.Empty(questions!);
    }

    [Fact]
    public async Task PreviewRejectsMalformedStructuredItems()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qimport-malformed");
        var ids = await ImportExamTreeAsync(user);
        var missingCode = ValidItem(ids);
        missingCode.ExamDefinitionId = null;
        missingCode.ExamCode = "";
        var emptyStem = ValidItem(ids);
        emptyStem.Stem = "";
        var tooFew = ValidItem(ids);
        tooFew.Options = [new QuestionImportOptionDto { OptionKey = "A", Text = "Only", IsCorrect = true }];
        var noCorrect = ValidItem(ids);
        noCorrect.Options =
        [
            new QuestionImportOptionDto { OptionKey = "A", Text = "A", IsCorrect = false },
            new QuestionImportOptionDto { OptionKey = "B", Text = "B", IsCorrect = false }
        ];
        var multipleCorrect = ValidItem(ids);
        multipleCorrect.Options =
        [
            new QuestionImportOptionDto { OptionKey = "A", Text = "A", IsCorrect = true },
            new QuestionImportOptionDto { OptionKey = "B", Text = "B", IsCorrect = true }
        ];
        var duplicateOption = ValidItem(ids);
        duplicateOption.Options =
        [
            new QuestionImportOptionDto { OptionKey = "A", Text = "A", IsCorrect = true },
            new QuestionImportOptionDto { OptionKey = "A", Text = "B", IsCorrect = false }
        ];

        var request = new QuestionImportRequestDto
        {
            Items = [missingCode, emptyStem, tooFew, noCorrect, multipleCorrect, duplicateOption]
        };

        var preview = await PreviewAsync(user, request);
        var codes = preview.Items.SelectMany(i => i.Issues).Select(i => i.Code).ToList();

        Assert.Equal(6, preview.RejectedCount);
        Assert.Contains("exam_code_required", codes);
        Assert.Contains("question_stem_required", codes);
        Assert.Contains("multiple_choice_minimum_two_options", codes);
        Assert.Contains("multiple_choice_correct_option_required", codes);
        Assert.Contains("multiple_choice_single_correct_option_required", codes);
        Assert.Contains("duplicate_option_key", codes);
    }

    [Fact]
    public async Task PreviewRejectsInvisibleTreeDuplicateExternalIdsAndExistingDuplicates()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qimport-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qimport-other");
        var privateIds = await ImportExamTreeAsync(owner);
        var visibleIds = await ImportExamTreeAsync(other);

        var existing = await other.Client.PostAsJsonAsync("/api/questions", BuildQuestion(visibleIds, "Duplicate stem"));
        existing.EnsureSuccessStatusCode();

        var request = new QuestionImportRequestDto
        {
            Items =
            [
                Mutate(ValidItem(privateIds), i => i.ExternalId = "DUP"),
                Mutate(ValidItem(visibleIds), i =>
                {
                    i.ExternalId = "DUP";
                    i.Stem = "Duplicate   stem";
                })
            ]
        };

        var preview = await PreviewAsync(other, request);
        var codes = preview.Items.SelectMany(i => i.Issues).Select(i => i.Code).ToList();

        Assert.Equal(2, preview.RejectedCount);
        Assert.Contains("duplicate_external_id", codes);
        Assert.Contains("exam_definition_not_visible", codes);
        Assert.Contains("duplicate_existing_question", codes);
        Assert.Contains(preview.Items, i => i.Status == "duplicate");
    }

    [Fact]
    public async Task ApprovalCreatesCallerOwnedDraftOrNeedsReviewQuestionsAndNeverPublishes()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qimport-approve");
        var ids = await ImportExamTreeAsync(user);
        var request = new QuestionImportRequestDto
        {
            Items =
            [
                Mutate(ValidItem(ids), i =>
                {
                    i.ExternalId = "SAFE";
                    i.LicenseStatus = "open";
                    i.SourceUrl = "https://example.org/source";
                }),
                Mutate(ValidItem(ids), i =>
                {
                    i.ExternalId = "UNSAFE";
                    i.Stem = "Unsafe license stem";
                    i.LicenseStatus = "restricted";
                })
            ]
        };
        var preview = await PreviewAsync(user, request);

        var approve = await user.Client.PostAsJsonAsync("/api/question-imports/approve", new QuestionImportApprovalDto { ImportPreviewId = preview.Id });
        approve.EnsureSuccessStatusCode();
        var result = await approve.Content.ReadFromJsonAsync<QuestionImportResultDto>();

        Assert.Equal(2, result!.CreatedCount);
        var questions = await user.Client.GetFromJsonAsync<List<QuestionItemDto>>($"/api/questions?examOutcomeId={ids.OutcomeId}");
        Assert.Equal(2, questions!.Count);
        Assert.Contains(questions, q => q.QualityStatus == "needs_review");
        Assert.Contains(questions, q => q.QualityStatus == "draft");
        Assert.DoesNotContain(questions, q => q.QualityStatus == "published");

        var unsafeQuestion = questions.Single(q => q.LicenseStatus == "restricted");
        var publish = await user.Client.PostAsync($"/api/questions/{unsafeQuestion.Id}/publish", null);
        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
    }

    [Fact]
    public async Task ApprovalIsCallerScopedExpiringAndIdempotent()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qimport-scope-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qimport-scope-other");
        var ids = await ImportExamTreeAsync(owner);
        var preview = await PreviewAsync(owner, BuildImport(ids));

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/question-imports/{preview.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.Client.PostAsJsonAsync("/api/question-imports/approve", new QuestionImportApprovalDto { ImportPreviewId = preview.Id })).StatusCode);

        var first = await owner.Client.PostAsJsonAsync("/api/question-imports/approve", new QuestionImportApprovalDto { ImportPreviewId = preview.Id });
        first.EnsureSuccessStatusCode();
        var firstResult = await first.Content.ReadFromJsonAsync<QuestionImportResultDto>();
        var second = await owner.Client.PostAsJsonAsync("/api/question-imports/approve", new QuestionImportApprovalDto { ImportPreviewId = preview.Id });
        second.EnsureSuccessStatusCode();
        var secondResult = await second.Content.ReadFromJsonAsync<QuestionImportResultDto>();
        Assert.Equal(firstResult!.CreatedQuestionIds, secondResult!.CreatedQuestionIds);

        var expiredPreview = await PreviewAsync(owner, BuildImport(ids, "Expired stem"));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var row = await db.QuestionImportPreviews.SingleAsync(p => p.Id == expiredPreview.Id);
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var expiredApproval = await owner.Client.PostAsJsonAsync("/api/question-imports/approve", new QuestionImportApprovalDto { ImportPreviewId = expiredPreview.Id });
        Assert.Equal(HttpStatusCode.BadRequest, expiredApproval.StatusCode);
        var body = await expiredApproval.Content.ReadAsStringAsync();
        Assert.Contains("preview_expired", body);
    }

    [Fact]
    public async Task PublicDtoDoesNotExposeRawPayloadOrDebugMetadata()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qimport-public-dto");
        var ids = await ImportExamTreeAsync(user);
        var preview = await PreviewAsync(user, BuildImport(ids));

        var raw = await user.Client.GetStringAsync($"/api/question-imports/{preview.Id}");

        Assert.DoesNotContain("OwnerUserId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawImport", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debug", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reviewNotes", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PDF", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NotebookLM", raw, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<QuestionImportPreviewDto> PreviewAsync(CoordinationTestUser user, QuestionImportRequestDto request)
    {
        var response = await user.Client.PostAsJsonAsync("/api/question-imports/preview", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuestionImportPreviewDto>())!;
    }

    private static QuestionImportRequestDto BuildImport(QuestionTreeIds ids, string stem = "Imported valid stem") => new()
    {
        Items = [Mutate(ValidItem(ids), i => i.Stem = stem)]
    };

    private static QuestionImportItemDto Mutate(QuestionImportItemDto item, Action<QuestionImportItemDto> action)
    {
        action(item);
        return item;
    }

    private static QuestionImportItemDto ValidItem(QuestionTreeIds ids) => new()
    {
        ExternalId = Guid.NewGuid().ToString("N"),
        ExamDefinitionId = ids.DefinitionId,
        ExamVariantId = ids.VariantId,
        ExamSectionId = ids.SectionId,
        ExamSubjectId = ids.SubjectId,
        ExamTopicId = ids.TopicId,
        ExamOutcomeId = ids.OutcomeId,
        QuestionType = "multiple_choice",
        Stem = "Imported valid stem",
        Difficulty = "medium",
        CognitiveSkill = "conceptual",
        LicenseStatus = "open",
        SourceOrigin = "structured_json",
        SourceTitle = "Structured source",
        SourceUrl = "https://example.org/source",
        Explanation = "Safe explanation",
        Tags = ["imported"],
        Options =
        [
            new QuestionImportOptionDto { OptionKey = "A", Text = "Correct", IsCorrect = true, SortOrder = 0 },
            new QuestionImportOptionDto { OptionKey = "B", Text = "Wrong", IsCorrect = false, SortOrder = 1 }
        ]
    };

    private static CreateQuestionDto BuildQuestion(QuestionTreeIds ids, string stem) => new()
    {
        ExamDefinitionId = ids.DefinitionId,
        ExamVariantId = ids.VariantId,
        ExamSectionId = ids.SectionId,
        ExamSubjectId = ids.SubjectId,
        ExamTopicId = ids.TopicId,
        ExamOutcomeId = ids.OutcomeId,
        QuestionType = "multiple_choice",
        Stem = stem,
        LicenseStatus = "open",
        SourceOrigin = "manual",
        Options =
        [
            new QuestionOptionDto { OptionKey = "A", Text = "Correct", IsCorrect = true, SortOrder = 0 },
            new QuestionOptionDto { OptionKey = "B", Text = "Wrong", IsCorrect = false, SortOrder = 1 }
        ],
        OutcomeLinks = [new QuestionOutcomeLinkDto { ExamOutcomeId = ids.OutcomeId, IsPrimary = true, LinkStrength = 1.0m }]
    };

    private static async Task<QuestionTreeIds> ImportExamTreeAsync(CoordinationTestUser user)
    {
        var import = BuildExamImport($"QIMPORT_{Guid.NewGuid():N}");
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

    private static ExamTreeImportDto BuildExamImport(string code) => new()
    {
        ExamCode = code,
        ExamName = $"{code} import tree",
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

    private sealed record QuestionTreeIds(
        Guid DefinitionId,
        Guid VariantId,
        Guid SectionId,
        Guid SubjectId,
        Guid TopicId,
        Guid OutcomeId);
}

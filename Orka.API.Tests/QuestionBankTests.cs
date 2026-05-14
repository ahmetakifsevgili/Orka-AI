using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class QuestionBankTests
{
    [Fact]
    public async Task UserCanCreateDraftMultipleChoiceQuestionLinkedToExamOutcome()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-create");
        var ids = await ImportExamTreeAsync(user);

        var response = await user.Client.PostAsJsonAsync("/api/questions", BuildQuestion(ids));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var question = await response.Content.ReadFromJsonAsync<QuestionItemDto>();
        Assert.NotNull(question);
        Assert.Equal("user", question!.OwnershipState);
        Assert.Equal("draft", question.QualityStatus);
        Assert.Equal(ids.OutcomeId, question.ExamOutcomeId);
        Assert.Single(question.OutcomeLinks);
        Assert.Equal(2, question.Options.Count);
        Assert.Single(question.Options.Where(o => o.IsCorrect));
    }

    [Fact]
    public async Task SystemGlobalQuestionIsVisibleToAuthenticatedUsers()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-system-a");
        var userB = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-system-b");
        var ids = await CreateSystemQuestionAsync(factory);

        var response = await userB.Client.GetAsync($"/api/questions/{ids.QuestionId}");
        response.EnsureSuccessStatusCode();

        var question = await response.Content.ReadFromJsonAsync<QuestionItemDto>();
        Assert.Equal("system", question!.OwnershipState);
        Assert.Equal(ids.OutcomeId, question.ExamOutcomeId);

        var list = await userA.Client.GetFromJsonAsync<List<QuestionItemDto>>($"/api/questions?examOutcomeId={ids.OutcomeId}");
        Assert.Contains(list!, q => q.Id == ids.QuestionId);
    }

    [Fact]
    public async Task UserCannotReadAnotherUsersPrivateQuestion()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-other");
        var ids = await ImportExamTreeAsync(owner);

        var created = await owner.Client.PostAsJsonAsync("/api/questions", BuildQuestion(ids));
        created.EnsureSuccessStatusCode();
        var question = await created.Content.ReadFromJsonAsync<QuestionItemDto>();

        var otherRead = await other.Client.GetAsync($"/api/questions/{question!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, otherRead.StatusCode);

        var otherList = await other.Client.GetFromJsonAsync<List<QuestionItemDto>>("/api/questions");
        Assert.DoesNotContain(otherList!, q => q.Id == question.Id);
    }

    [Fact]
    public async Task QuestionListFiltersByExamTreeLinks()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-filter");
        var ids = await ImportExamTreeAsync(user);
        var created = await user.Client.PostAsJsonAsync("/api/questions", BuildQuestion(ids));
        created.EnsureSuccessStatusCode();
        var question = await created.Content.ReadFromJsonAsync<QuestionItemDto>();

        Assert.Contains(await ListAsync(user, $"examDefinitionId={ids.DefinitionId}"), q => q.Id == question!.Id);
        Assert.Contains(await ListAsync(user, $"examVariantId={ids.VariantId}"), q => q.Id == question!.Id);
        Assert.Contains(await ListAsync(user, $"examSubjectId={ids.SubjectId}"), q => q.Id == question!.Id);
        Assert.Contains(await ListAsync(user, $"examTopicId={ids.TopicId}"), q => q.Id == question!.Id);
        Assert.Contains(await ListAsync(user, $"examOutcomeId={ids.OutcomeId}"), q => q.Id == question!.Id);
        Assert.DoesNotContain(await ListAsync(user, $"examOutcomeId={Guid.NewGuid()}"), q => q.Id == question!.Id);
    }

    [Fact]
    public async Task InvalidMultipleChoiceQuestionsAreRejected()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-invalid");
        var ids = await ImportExamTreeAsync(user);

        var noCorrect = BuildQuestion(ids);
        noCorrect.Options.ForEach(o => o.IsCorrect = false);
        var multipleCorrect = BuildQuestion(ids);
        multipleCorrect.Options.ForEach(o => o.IsCorrect = true);
        var tooFew = BuildQuestion(ids);
        tooFew.Options = [tooFew.Options[0]];
        var emptyStem = BuildQuestion(ids);
        emptyStem.Stem = "";

        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsJsonAsync("/api/questions", noCorrect)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsJsonAsync("/api/questions", multipleCorrect)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsJsonAsync("/api/questions", tooFew)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsJsonAsync("/api/questions", emptyStem)).StatusCode);
    }

    [Fact]
    public async Task PublishEnforcesQualityLicenseSourceAndValidationRules()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-publish");
        var ids = await ImportExamTreeAsync(user);
        var created = await user.Client.PostAsJsonAsync("/api/questions", BuildQuestion(ids));
        created.EnsureSuccessStatusCode();
        var question = await created.Content.ReadFromJsonAsync<QuestionItemDto>();

        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsync($"/api/questions/{question!.Id}/publish", null)).StatusCode);

        var review = await user.Client.PostAsync($"/api/questions/{question.Id}/submit-review", null);
        review.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsync($"/api/questions/{question.Id}/publish", null)).StatusCode);

        var rejected = await user.Client.PutAsJsonAsync($"/api/questions/{question.Id}", new UpdateQuestionDto { QualityStatus = "rejected" });
        rejected.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsync($"/api/questions/{question.Id}/publish", null)).StatusCode);

        var approvedUnsafe = await user.Client.PutAsJsonAsync($"/api/questions/{question.Id}", new UpdateQuestionDto { QualityStatus = "approved", LicenseStatus = "unknown" });
        approvedUnsafe.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsync($"/api/questions/{question.Id}/publish", null)).StatusCode);

        var approvedMalformedSource = await user.Client.PutAsJsonAsync($"/api/questions/{question.Id}", new UpdateQuestionDto
        {
            QualityStatus = "approved",
            LicenseStatus = "open",
            SourceUrl = "not a url"
        });
        approvedMalformedSource.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsync($"/api/questions/{question.Id}/publish", null)).StatusCode);

        var approvedSafe = await user.Client.PutAsJsonAsync($"/api/questions/{question.Id}", new UpdateQuestionDto
        {
            QualityStatus = "approved",
            LicenseStatus = "open",
            SourceUrl = "https://example.org/question-source"
        });
        approvedSafe.EnsureSuccessStatusCode();
        var publish = await user.Client.PostAsync($"/api/questions/{question.Id}/publish", null);
        publish.EnsureSuccessStatusCode();
        var published = await publish.Content.ReadFromJsonAsync<QuestionItemDto>();
        Assert.Equal("published", published!.QualityStatus);
    }

    [Fact]
    public async Task SoftDeletedQuestionsAreNotReturned()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-delete");
        var ids = await ImportExamTreeAsync(user);
        var created = await user.Client.PostAsJsonAsync("/api/questions", BuildQuestion(ids));
        created.EnsureSuccessStatusCode();
        var question = await created.Content.ReadFromJsonAsync<QuestionItemDto>();

        var delete = await user.Client.DeleteAsync($"/api/questions/{question!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/questions/{question.Id}")).StatusCode);
        Assert.DoesNotContain(await ListAsync(user, $"examDefinitionId={ids.DefinitionId}"), q => q.Id == question.Id);
    }

    [Fact]
    public async Task LinkedExamTreeMustBeVisibleToCaller()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-link-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-link-other");
        var privateIds = await ImportExamTreeAsync(owner);

        var response = await other.Client.PostAsJsonAsync("/api/questions", BuildQuestion(privateIds));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PublicDtoDoesNotExposeRawInternalOwnershipOrDebugMetadata()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-public-dto");
        var ids = await ImportExamTreeAsync(user);
        var created = await user.Client.PostAsJsonAsync("/api/questions", BuildQuestion(ids));
        created.EnsureSuccessStatusCode();
        var question = await created.Content.ReadFromJsonAsync<QuestionItemDto>();

        var raw = await user.Client.GetStringAsync($"/api/questions/{question!.Id}");
        Assert.Contains("ownershipState", raw);
        Assert.DoesNotContain("OwnerUserId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawImport", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reviewNotes", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debug", raw, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<QuestionItemDto>> ListAsync(CoordinationTestUser user, string query)
    {
        return await user.Client.GetFromJsonAsync<List<QuestionItemDto>>($"/api/questions?{query}") ?? [];
    }

    private static async Task<QuestionTreeIds> ImportExamTreeAsync(CoordinationTestUser user)
    {
        var import = BuildExamImport($"QBANK_{Guid.NewGuid():N}");
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

    private static async Task<SystemQuestionIds> CreateSystemQuestionAsync(ApiSmokeFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var examService = scope.ServiceProvider.GetRequiredService<IExamFrameworkService>();
        var tree = await examService.CreateSystemSkeletonAsync();
        var variant = tree.Variants[0];
        var section = variant.Sections[0];
        var subject = section.Subjects[0];
        var topic = subject.Topics[0];
        var outcome = topic.Outcomes[0];

        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var question = new QuestionItem
        {
            ExamDefinitionId = tree.Id,
            ExamVariantId = variant.Id,
            ExamSectionId = section.Id,
            ExamSubjectId = subject.Id,
            ExamTopicId = topic.Id,
            ExamOutcomeId = outcome.Id,
            QuestionType = "multiple_choice",
            Stem = "System sample stem",
            Difficulty = "easy",
            CognitiveSkill = "conceptual",
            QualityStatus = "published",
            LicenseStatus = "open",
            SourceOrigin = "system_seed",
            Explanation = "System sample explanation",
            Options =
            [
                new QuestionOption { OptionKey = "A", Text = "Correct", IsCorrect = true, SortOrder = 0 },
                new QuestionOption { OptionKey = "B", Text = "Wrong", IsCorrect = false, SortOrder = 1 }
            ],
            OutcomeLinks =
            [
                new QuestionOutcomeLink { ExamOutcomeId = outcome.Id, IsPrimary = true, LinkStrength = 1.0m }
            ]
        };

        db.QuestionItems.Add(question);
        await db.SaveChangesAsync();
        return new SystemQuestionIds(question.Id, outcome.Id);
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
        Tags = [new QuestionTagDto { Tag = "core" }]
    };

    private static ExamTreeImportDto BuildExamImport(string code) => new()
    {
        ExamCode = code,
        ExamName = $"{code} exam tree",
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

    private sealed record QuestionTreeIds(Guid DefinitionId, Guid VariantId, Guid SectionId, Guid SubjectId, Guid TopicId, Guid OutcomeId);
    private sealed record SystemQuestionIds(Guid QuestionId, Guid OutcomeId);
}

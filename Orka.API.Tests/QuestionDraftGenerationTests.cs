using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Orka.Core.DTOs;
using Xunit;

namespace Orka.API.Tests;

public sealed class QuestionDraftGenerationTests
{
    private const string SourceFact = "Paragrafta ana fikir metnin savundugu temel dusuncedir";

    [Fact]
    public async Task PreviewCreatesSourceGroundedDraftCandidatesAndDoesNotCreateQuestions()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qdraft-preview");
        var ids = await ImportExamTreeAsync(user);

        var preview = await PreviewAsync(user, ValidRequest(ids, desiredCount: 2, sourceText: TwoSourceFacts()));

        Assert.Equal("pending", preview.Status);
        Assert.Equal(2, preview.GeneratedCount);
        Assert.Equal(2, preview.AcceptedDraftCount);
        Assert.All(preview.Items, item =>
        {
            Assert.Equal("accepted", item.Status);
            Assert.NotNull(item.Candidate);
            Assert.Contains("generated_draft", item.Candidate!.Tags);
            Assert.Contains("source_grounded", item.Candidate.Tags);
            Assert.Contains(item.Issues, i => i.Code == "generated_draft_requires_review");
            Assert.Contains(item.Issues, i => i.Code == "deterministic_stub_generator");
            Assert.Contains(item.Issues, i => i.Code == "review_distractors_before_publish");
        });

        var questions = await user.Client.GetFromJsonAsync<List<QuestionItemDto>>($"/api/questions?examOutcomeId={ids.OutcomeId}");
        Assert.Empty(questions!);
    }

    [Fact]
    public async Task PreviewRejectsRequestLevelSafetyProblems()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qdraft-invalid");
        var ids = await ImportExamTreeAsync(user);

        await AssertRejectedAsync(user, Mutate(ValidRequest(ids), r => r.Context = new QuestionDraftGenerationContextDto()), "exam_context_required");
        await AssertRejectedAsync(user, Mutate(ValidRequest(ids), r => r.Source.SourceText = ""), "source_context_required");
        await AssertRejectedAsync(user, Mutate(ValidRequest(ids), r => r.Source.SourceUrl = "not a url"), "source_url_invalid");
        await AssertRejectedAsync(user, Mutate(ValidRequest(ids), r => r.QuestionType = "essay"), "unsupported_generation_question_type");
        await AssertRejectedAsync(user, Mutate(ValidRequest(ids), r => r.DesiredCount = 6), "desired_count_exceeds_cap");
    }

    [Fact]
    public async Task PreviewRejectsInvisibleExamContextThroughImportValidation()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qdraft-private-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qdraft-private-other");
        var privateIds = await ImportExamTreeAsync(owner);

        var response = await other.Client.PostAsJsonAsync("/api/question-drafts/preview", ValidRequest(privateIds));
        response.EnsureSuccessStatusCode();
        var preview = await response.Content.ReadFromJsonAsync<QuestionDraftPreviewDto>();

        Assert.Equal(0, preview!.AcceptedDraftCount);
        Assert.Contains(preview.Items.SelectMany(i => i.Issues), i => i.Code == "exam_definition_not_visible");
    }

    [Fact]
    public async Task OfficialClaimsAreBlockedWithoutVerifiedSourceMetadata()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qdraft-official-claim");
        var ids = await ImportExamTreeAsync(user);
        var request = Mutate(ValidRequest(ids), r => r.Source.SourceText = "OSYM resmi mufredat kapsami tamamlanmistir.");

        await AssertRejectedAsync(user, request, "official_claim_requires_verified_source");
    }

    [Fact]
    public async Task DuplicateExistingQuestionIsDetectedThroughImportPreview()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qdraft-duplicate");
        var ids = await ImportExamTreeAsync(user);
        var existing = await user.Client.PostAsJsonAsync("/api/questions", BuildQuestion(ids, ExpectedStem(SourceFact)));
        existing.EnsureSuccessStatusCode();

        var preview = await PreviewAsync(user, ValidRequest(ids, sourceText: SourceFact));

        Assert.Equal(0, preview.AcceptedDraftCount);
        Assert.Contains(preview.Items, i => i.Status == "duplicate");
        Assert.Contains(preview.Items.SelectMany(i => i.Issues), i => i.Code == "duplicate_existing_question");
    }

    [Fact]
    public async Task ApprovalCreatesCallerOwnedReviewDraftsAndNeverPublishes()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qdraft-approve");
        var ids = await ImportExamTreeAsync(user);
        var preview = await PreviewAsync(user, ValidRequest(ids, desiredCount: 2, sourceText: TwoSourceFacts(), licenseStatus: "open"));

        var approval = await user.Client.PostAsJsonAsync("/api/question-drafts/approve", new QuestionDraftApprovalDto { DraftPreviewId = preview.Id });
        approval.EnsureSuccessStatusCode();
        var result = await approval.Content.ReadFromJsonAsync<QuestionDraftApprovalResultDto>();

        Assert.Equal(2, result!.CreatedCount);
        var questions = await user.Client.GetFromJsonAsync<List<QuestionItemDto>>($"/api/questions?examOutcomeId={ids.OutcomeId}");
        Assert.Equal(2, questions!.Count);
        Assert.All(questions, q =>
        {
            Assert.Equal("user", q.OwnershipState);
            Assert.NotEqual("published", q.QualityStatus);
            Assert.Equal("needs_review", q.QualityStatus);
            Assert.Equal("source_grounded_draft", q.SourceOrigin);
            Assert.Equal("Draft source", q.SourceTitle);
            Assert.Contains(q.Tags, t => t.Tag == "generated_draft");
            Assert.Contains(q.Tags, t => t.Tag == "source_grounded");
        });

        var secondApproval = await user.Client.PostAsJsonAsync("/api/question-drafts/approve", new QuestionDraftApprovalDto { DraftPreviewId = preview.Id });
        secondApproval.EnsureSuccessStatusCode();
        var secondResult = await secondApproval.Content.ReadFromJsonAsync<QuestionDraftApprovalResultDto>();
        Assert.Equal(result.CreatedQuestionIds, secondResult!.CreatedQuestionIds);
    }

    [Fact]
    public async Task UnknownLicenseApprovalCreatesDraftThatPublishValidationStillBlocks()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qdraft-unsafe-license");
        var ids = await ImportExamTreeAsync(user);
        var preview = await PreviewAsync(user, ValidRequest(ids, licenseStatus: "unknown"));

        var approval = await user.Client.PostAsJsonAsync("/api/question-drafts/approve", new QuestionDraftApprovalDto { DraftPreviewId = preview.Id });
        approval.EnsureSuccessStatusCode();
        var result = await approval.Content.ReadFromJsonAsync<QuestionDraftApprovalResultDto>();
        var question = await user.Client.GetFromJsonAsync<QuestionItemDto>($"/api/questions/{result!.CreatedQuestionIds[0]}");

        Assert.Equal("draft", question!.QualityStatus);
        var publish = await user.Client.PostAsync($"/api/questions/{question.Id}/publish", null);
        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
    }

    [Fact]
    public async Task PreviewAndApprovalAreCallerScopedAndPublicDtoIsSafe()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qdraft-scope-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "qdraft-scope-other");
        var ids = await ImportExamTreeAsync(owner);
        var preview = await PreviewAsync(owner, ValidRequest(ids));

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/question-drafts/{preview.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.Client.PostAsJsonAsync("/api/question-drafts/approve", new QuestionDraftApprovalDto { DraftPreviewId = preview.Id })).StatusCode);

        var raw = await owner.Client.GetStringAsync($"/api/question-drafts/{preview.Id}");
        Assert.DoesNotContain("OwnerUserId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceText", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("model", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debug", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reviewNotes", raw, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertRejectedAsync(CoordinationTestUser user, QuestionDraftGenerationRequestDto request, string expectedCode)
    {
        var response = await user.Client.PostAsJsonAsync("/api/question-drafts/preview", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<QuestionDraftPreviewDto>();
        Assert.Contains(preview!.Issues, i => i.Code == expectedCode);
    }

    private static async Task<QuestionDraftPreviewDto> PreviewAsync(CoordinationTestUser user, QuestionDraftGenerationRequestDto request)
    {
        var response = await user.Client.PostAsJsonAsync("/api/question-drafts/preview", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuestionDraftPreviewDto>())!;
    }

    private static QuestionDraftGenerationRequestDto Mutate(QuestionDraftGenerationRequestDto request, Action<QuestionDraftGenerationRequestDto> action)
    {
        action(request);
        return request;
    }

    private static QuestionDraftGenerationRequestDto ValidRequest(
        QuestionTreeIds ids,
        int desiredCount = 1,
        string sourceText = SourceFact,
        string licenseStatus = "open") => new()
    {
        Context = new QuestionDraftGenerationContextDto
        {
            ExamDefinitionId = ids.DefinitionId,
            ExamVariantId = ids.VariantId,
            ExamSectionId = ids.SectionId,
            ExamSubjectId = ids.SubjectId,
            ExamTopicId = ids.TopicId,
            ExamOutcomeId = ids.OutcomeId
        },
        Source = new QuestionDraftGenerationSourceDto
        {
            SourceTitle = "Draft source",
            SourceUrl = "https://example.org/draft-source",
            SourceOrigin = "source_grounded_draft",
            LicenseStatus = licenseStatus,
            SourceText = sourceText
        },
        QuestionType = "multiple_choice",
        DesiredCount = desiredCount,
        Difficulty = "medium",
        CognitiveSkill = "reading_comprehension"
    };

    private static string ExpectedStem(string sourceStatement) =>
        $"Kaynaga gore asagidaki ifade hangi kaynak bilgisine dayanir? {sourceStatement}";

    private static string TwoSourceFacts() =>
        $"{SourceFact}. Cikarim sorulari metindeki ipuclarina dayanir";

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
        var import = BuildExamImport($"QDRAFT_{Guid.NewGuid():N}");
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
        ExamName = $"{code} draft tree",
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

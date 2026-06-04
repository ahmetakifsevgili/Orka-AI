using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;
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
    public async Task QuestionBankSurfacesDiagnosticAssessmentItemsByLearningConceptWithoutAnswerKeys()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-diagnostic-bank");
        var itemIds = await SeedDiagnosticAssessmentItemAsync(factory, user.UserId);

        var list = await user.Client.GetFromJsonAsync<List<QuestionItemDto>>(
            $"/api/questions?learningTopicId={itemIds.TopicId}&conceptKey=query-plan-cardinality&difficulty=Medium&questionType=MultipleChoice&qualityStatus=diagnostic_ready&take=5");

        var question = Assert.Single(list!);
        Assert.Equal(itemIds.AssessmentItemId, question.Id);
        Assert.Equal("diagnostic_assessment_item", question.QuestionBankSource);
        Assert.Equal(itemIds.AssessmentItemId, question.AssessmentItemId);
        Assert.Equal(itemIds.TopicId, question.LearningTopicId);
        Assert.Equal(itemIds.ConceptGraphSnapshotId, question.ConceptGraphSnapshotId);
        Assert.Equal(itemIds.LearningConceptId, question.LearningConceptId);
        Assert.Equal("query-plan-cardinality", question.ConceptKey);
        Assert.Equal("multiple_choice", question.QuestionType);
        Assert.Equal("medium", question.Difficulty);
        Assert.Equal("review_ready", question.CalibrationStatus);
        Assert.Equal(3, question.Options.Count);
        Assert.All(question.Options, option => Assert.False(option.IsCorrect));
        Assert.Empty(question.Explanations);
        Assert.Empty(question.Explanation);

        var rawList = await user.Client.GetStringAsync(
            $"/api/questions?learningTopicId={itemIds.TopicId}&conceptKey=query-plan-cardinality&qualityStatus=diagnostic_ready");
        Assert.DoesNotContain("correctAnswer", rawList, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", rawList, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isCorrect\":true", rawList, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnosticSignalIfChosen", rawList, StringComparison.OrdinalIgnoreCase);

        var detail = await user.Client.GetFromJsonAsync<QuestionItemDto>($"/api/questions/{itemIds.AssessmentItemId}");
        Assert.NotNull(detail);
        Assert.Equal("diagnostic_assessment_item", detail!.QuestionBankSource);
        Assert.Equal("query-plan-cardinality", detail.ConceptKey);
        Assert.All(detail.Options, option => Assert.False(option.IsCorrect));
    }

    [Fact]
    public async Task QuestionPracticeUsesMaterializedDiagnosticQuestionsAndRecordsLearningAttempt()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-practice-diagnostic");
        var itemIds = await SeedMaterializedDiagnosticQuestionAsync(factory, user.UserId);

        var startResponse = await user.Client.PostAsJsonAsync("/api/question-practice/start", new QuestionPracticeStartRequestDto
        {
            TopicId = itemIds.TopicId,
            ConceptKeys = ["query-plan-cardinality"],
            Count = 3
        });
        startResponse.EnsureSuccessStatusCode();

        var session = await startResponse.Content.ReadFromJsonAsync<QuestionPracticeSessionDto>();
        Assert.NotNull(session);
        Assert.Equal("ready", session!.Status);
        var question = Assert.Single(session.Questions);
        Assert.Equal(itemIds.QuestionItemId, question.QuestionItemId);
        Assert.Equal(itemIds.AssessmentItemId, question.AssessmentItemId);
        Assert.Equal("diagnostic_assessment_item", question.QuestionBankSource);
        Assert.All(question.Options, option =>
        {
            Assert.False(option.IsCorrect);
            Assert.Null(option.Rationale);
            Assert.Null(option.DiagnosticSignalJson);
        });

        var rawStart = await startResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("correctAnswer", rawStart, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnosticSignalIfChosen", rawStart, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isCorrect\":true", rawStart, StringComparison.OrdinalIgnoreCase);

        var submitResponse = await user.Client.PostAsJsonAsync("/api/question-practice/submit", new QuestionPracticeSubmitRequestDto
        {
            PracticeSetId = session.PracticeSetId,
            TopicId = itemIds.TopicId,
            Answers =
            [
                new QuestionPracticeAnswerDto
                {
                    QuestionItemId = itemIds.QuestionItemId,
                    SelectedOptionKey = "B",
                    ConfidenceSelfRating = 0.4m
                }
            ]
        });
        submitResponse.EnsureSuccessStatusCode();

        var result = await submitResponse.Content.ReadFromJsonAsync<QuestionPracticeSubmitResponseDto>();
        Assert.NotNull(result);
        Assert.Equal(1, result!.TotalQuestions);
        Assert.Equal(1, result.AnsweredCount);
        Assert.Equal(0, result.CorrectCount);
        Assert.Equal(1, result.WrongCount);
        Assert.Equal(itemIds.AssessmentItemId, result.Results[0].AssessmentItemId);
        Assert.Equal("query-plan-cardinality", result.Results[0].ConceptKey);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var attempt = await db.QuizAttempts.SingleAsync(a => a.UserId == user.UserId && a.AssessmentItemId == itemIds.AssessmentItemId);
        Assert.False(attempt.IsCorrect);
        Assert.Equal(itemIds.TopicId, attempt.TopicId);
        Assert.Equal("B", attempt.UserAnswer);
    }

    [Fact]
    public async Task QuestionPracticeDoesNotSurfacePublishedQuestionsWithoutProfessionalKgBinding()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-practice-unbound");
        await CreateSystemQuestionAsync(factory);

        var startResponse = await user.Client.PostAsJsonAsync("/api/question-practice/start", new QuestionPracticeStartRequestDto
        {
            Count = 5,
            Mode = "weak_concept_drill"
        });

        startResponse.EnsureSuccessStatusCode();
        var session = await startResponse.Content.ReadFromJsonAsync<QuestionPracticeSessionDto>();
        Assert.NotNull(session);
        Assert.Equal("empty", session!.Status);
        Assert.Equal(0, session.TotalQuestions);
        Assert.Empty(session.Questions);
    }

    [Fact]
    public async Task QuestionQualityAnalyticsUsesQuestionBankQuizAttempts()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-bank-analytics");
        var itemIds = await SeedMaterializedDiagnosticQuestionAsync(factory, user.UserId);

        var startResponse = await user.Client.PostAsJsonAsync("/api/question-practice/start", new QuestionPracticeStartRequestDto
        {
            TopicId = itemIds.TopicId,
            ConceptKeys = ["query-plan-cardinality"],
            Count = 3
        });
        startResponse.EnsureSuccessStatusCode();

        var session = await startResponse.Content.ReadFromJsonAsync<QuestionPracticeSessionDto>();
        Assert.NotNull(session);

        var submitResponse = await user.Client.PostAsJsonAsync("/api/question-practice/submit", new QuestionPracticeSubmitRequestDto
        {
            PracticeSetId = session!.PracticeSetId,
            TopicId = itemIds.TopicId,
            Answers =
            [
                new QuestionPracticeAnswerDto
                {
                    QuestionItemId = itemIds.QuestionItemId,
                    SelectedOptionKey = "B"
                }
            ]
        });
        submitResponse.EnsureSuccessStatusCode();

        var recalculate = await user.Client.PostAsync($"/api/question-quality/questions/{itemIds.QuestionItemId}/recalculate", null);
        recalculate.EnsureSuccessStatusCode();

        var analytics = await recalculate.Content.ReadFromJsonAsync<RecalculateQuestionAnalyticsResultDto>();
        Assert.NotNull(analytics);
        Assert.True(analytics!.Recalculated);
        Assert.NotNull(analytics.Analytics);
        Assert.Equal(1, analytics.Analytics!.AttemptCount);
        Assert.Equal(1, analytics.Analytics.AnsweredCount);
        Assert.Equal(0, analytics.Analytics.CorrectCount);
        Assert.Equal(1, analytics.Analytics.WrongCount);
        Assert.Contains(analytics.Analytics.Options, option => option.OptionKey == "B" && option.SelectionCount == 1);
    }

    [Fact]
    public async Task QuestionPracticeIncludesParentDiagnosticQuestionsWhenStartedFromLessonTopic()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-practice-topic-scope");
        var itemIds = await SeedMaterializedDiagnosticQuestionAsync(factory, user.UserId);

        var now = DateTime.UtcNow;
        var lessonTopicId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            db.Topics.Add(new Topic
            {
                Id = lessonTopicId,
                UserId = user.UserId,
                ParentTopicId = itemIds.TopicId,
                Title = "Cardinality estimate lesson",
                PlanIntent = "Lesson",
                CreatedAt = now,
                LastAccessedAt = now
            });
            await db.SaveChangesAsync();
        }

        var startResponse = await user.Client.PostAsJsonAsync("/api/question-practice/start", new QuestionPracticeStartRequestDto
        {
            TopicId = lessonTopicId,
            ConceptKeys = ["query-plan-cardinality"],
            Count = 3
        });
        startResponse.EnsureSuccessStatusCode();

        var session = await startResponse.Content.ReadFromJsonAsync<QuestionPracticeSessionDto>();
        Assert.NotNull(session);
        Assert.Equal("ready", session!.Status);
        var question = Assert.Single(session.Questions);
        Assert.Equal(itemIds.QuestionItemId, question.QuestionItemId);
        Assert.Equal(itemIds.TopicId, question.TopicId);

        var submitResponse = await user.Client.PostAsJsonAsync("/api/question-practice/submit", new QuestionPracticeSubmitRequestDto
        {
            PracticeSetId = session.PracticeSetId,
            TopicId = lessonTopicId,
            Answers =
            [
                new QuestionPracticeAnswerDto
                {
                    QuestionItemId = itemIds.QuestionItemId,
                    SelectedOptionKey = "B"
                }
            ]
        });
        submitResponse.EnsureSuccessStatusCode();

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var attempt = await verifyDb.QuizAttempts.SingleAsync(a => a.UserId == user.UserId && a.AssessmentItemId == itemIds.AssessmentItemId);
        Assert.False(attempt.IsCorrect);
        Assert.Equal(itemIds.TopicId, attempt.TopicId);
    }

    [Fact]
    public async Task QuestionPracticeSupportsTypedKgAndAssessmentFilters()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-practice-typed-filters");
        var itemIds = await SeedMaterializedDiagnosticQuestionAsync(factory, user.UserId);

        var byAssessment = await user.Client.PostAsJsonAsync("/api/question-practice/start", new QuestionPracticeStartRequestDto
        {
            TopicId = itemIds.TopicId,
            AssessmentItemIds = [itemIds.AssessmentItemId],
            QuestionBankSource = "diagnostic_assessment_item",
            Count = 3
        });
        byAssessment.EnsureSuccessStatusCode();

        var assessmentSession = await byAssessment.Content.ReadFromJsonAsync<QuestionPracticeSessionDto>();
        Assert.NotNull(assessmentSession);
        var assessmentQuestion = Assert.Single(assessmentSession!.Questions);
        Assert.Equal(itemIds.AssessmentItemId, assessmentQuestion.AssessmentItemId);
        Assert.Equal(itemIds.ConceptGraphSnapshotId, assessmentQuestion.ConceptGraphSnapshotId);
        Assert.Equal(itemIds.LearningConceptId, assessmentQuestion.LearningConceptId);

        var byLearningConcept = await user.Client.PostAsJsonAsync("/api/question-practice/start", new QuestionPracticeStartRequestDto
        {
            TopicId = itemIds.TopicId,
            ConceptGraphSnapshotId = itemIds.ConceptGraphSnapshotId,
            LearningConceptIds = [itemIds.LearningConceptId],
            Count = 3
        });
        byLearningConcept.EnsureSuccessStatusCode();

        var conceptSession = await byLearningConcept.Content.ReadFromJsonAsync<QuestionPracticeSessionDto>();
        Assert.NotNull(conceptSession);
        var conceptQuestion = Assert.Single(conceptSession!.Questions);
        Assert.Equal(itemIds.QuestionItemId, conceptQuestion.QuestionItemId);
        Assert.Equal(itemIds.LearningConceptId, conceptQuestion.LearningConceptId);
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
    public async Task DirectPublishBlocksProfessionalDiagnosticQuestionsWithoutKgContract()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "question-publish-professional-contract");
        var ids = await ImportExamTreeAsync(user);
        var request = BuildQuestion(ids);
        request.LicenseStatus = "open";

        var created = await user.Client.PostAsJsonAsync("/api/questions", request);
        created.EnsureSuccessStatusCode();
        var question = await created.Content.ReadFromJsonAsync<QuestionItemDto>();

        var approved = await user.Client.PutAsJsonAsync($"/api/questions/{question!.Id}", new UpdateQuestionDto
        {
            QualityStatus = "diagnostic_ready",
            LicenseStatus = "open",
            SourceUrl = "https://example.org/question-source"
        });
        approved.EnsureSuccessStatusCode();

        var publish = await user.Client.PostAsync($"/api/questions/{question.Id}/publish", null);
        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
        var body = await publish.Content.ReadAsStringAsync();
        Assert.Contains("assessment_item_binding_required", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("concept_graph_binding_required", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("distractor_rationale_required", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("distractor_diagnostic_signal_required", body, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void ContentJsonWritePathStripsLearnerAnswerKeysRecursively()
    {
        var malicious = """
        {
          "title": "safe table",
          "rows": [["A", "B"]],
          "answerKey": "B",
          "children": [
            { "label": "visible", "correctAnswer": "secret", "rubric": "hidden" }
          ],
          "data": { "caption": "kept", "solution": "hidden", "values": [1, 2] }
        }
        """;

        var stored = LearnerSafeContentJson.Sanitize(malicious);

        Assert.NotNull(stored);
        Assert.Contains("safe table", stored!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("visible", stored!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", stored!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", stored!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rubric", stored!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("solution", stored!, StringComparison.OrdinalIgnoreCase);
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

    private static async Task<DiagnosticQuestionIds> SeedDiagnosticAssessmentItemAsync(ApiSmokeFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        var topic = new Topic
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Query plan diagnostics",
            CreatedAt = now,
            LastAccessedAt = now
        };
        var snapshot = new ConceptGraphSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topic.Id,
            ApprovedResearchIntent = "diagnose query-plan cardinality skills",
            TopicTitle = "Query plan diagnostics",
            Domain = "software_engineering",
            SourceConfidence = "model_assisted",
            GraphJson = "{}",
            CreatedAt = now
        };
        var concept = new LearningConcept
        {
            Id = Guid.NewGuid(),
            ConceptGraphSnapshotId = snapshot.Id,
            StableKey = "query-plan-cardinality",
            Label = "Query plan cardinality",
            Description = "Recognize cardinality estimate problems in query plans.",
            DifficultyBand = "core",
            Order = 1,
            CreatedAt = now
        };
        var assessmentItemId = Guid.NewGuid();
        var assessmentItem = new AssessmentItem
        {
            Id = assessmentItemId,
            UserId = userId,
            TopicId = topic.Id,
            QuizRunId = Guid.NewGuid(),
            PlanRequestId = Guid.NewGuid(),
            ConceptGraphSnapshotId = snapshot.Id,
            LearningConceptId = concept.Id,
            AssessmentItemKey = "query-plan-cardinality:item-1",
            ConceptKey = concept.StableKey,
            ConceptLabel = concept.Label,
            QuestionType = "diagnostic_multiple_choice",
            CognitiveSkill = "analysis",
            Difficulty = "core",
            MisconceptionTarget = "index_selectivity_equals_speed",
            EvidenceExpected = "Learner distinguishes index existence from selectivity/cardinality usefulness.",
            GeneratedQuestionJson = $$"""
            {
              "assessmentItemId": "{{assessmentItemId}}",
              "question": "A query uses an index but the plan still scans many rows. Which explanation best fits a cardinality-estimation issue?",
              "options": [
                { "id": "A", "text": "The optimizer estimated too few rows, so it chose an access path that became expensive.", "isCorrect": true, "diagnosticSignalIfChosen": "cardinality_reasoning_present" },
                { "id": "B", "text": "Any index automatically makes every predicate selective.", "isCorrect": false, "diagnosticSignalIfChosen": "index_selectivity_equals_speed" },
                { "id": "C", "text": "The query must be rewritten only by adding another index hint.", "isCorrect": false, "diagnosticSignalIfChosen": "hint_overuse" }
              ],
              "correctAnswer": "A",
              "explanation": "The correct reasoning depends on selectivity and row-estimate quality, not index presence alone."
            }
            """,
            Order = 1,
            CreatedAt = now
        };
        var stat = new AssessmentItemStat
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topic.Id,
            ConceptGraphSnapshotId = snapshot.Id,
            AssessmentItemId = assessmentItemId,
            ConceptKey = concept.StableKey,
            CalibrationStatus = "review_ready",
            QualityStatus = "usable",
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Topics.Add(topic);
        db.ConceptGraphSnapshots.Add(snapshot);
        db.LearningConcepts.Add(concept);
        db.AssessmentItems.Add(assessmentItem);
        db.AssessmentItemStats.Add(stat);
        await db.SaveChangesAsync();

        return new DiagnosticQuestionIds(topic.Id, snapshot.Id, concept.Id, assessmentItemId);
    }

    private static async Task<MaterializedDiagnosticQuestionIds> SeedMaterializedDiagnosticQuestionAsync(ApiSmokeFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var examService = scope.ServiceProvider.GetRequiredService<IExamFrameworkService>();
        var exam = await examService.CreateSystemSkeletonAsync();

        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        var topic = new Topic
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Query plan diagnostics",
            CreatedAt = now,
            LastAccessedAt = now
        };
        var snapshot = new ConceptGraphSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topic.Id,
            ApprovedResearchIntent = "diagnose query-plan cardinality skills",
            TopicTitle = "Query plan diagnostics",
            Domain = "software_engineering",
            SourceConfidence = "model_assisted",
            GraphJson = "{}",
            CreatedAt = now
        };
        var concept = new LearningConcept
        {
            Id = Guid.NewGuid(),
            ConceptGraphSnapshotId = snapshot.Id,
            StableKey = "query-plan-cardinality",
            Label = "Query plan cardinality",
            Description = "Recognize cardinality estimate problems in query plans.",
            DifficultyBand = "core",
            Order = 1,
            CreatedAt = now
        };
        var assessmentItemId = Guid.NewGuid();
        var generatedQuestionJson = $$"""
        {
          "assessmentItemId": "{{assessmentItemId}}",
          "question": "A query uses an index but the plan still scans many rows. Which explanation best fits a cardinality-estimation issue?",
          "options": [
            { "id": "A", "text": "The optimizer estimated too few rows, so it chose an access path that became expensive.", "isCorrect": true, "rationale": "This checks selectivity and row-estimate reasoning.", "diagnosticSignalIfChosen": "cardinality_reasoning_present" },
            { "id": "B", "text": "Any index automatically makes every predicate selective.", "isCorrect": false, "rationale": "This exposes index-presence overgeneralization.", "diagnosticSignalIfChosen": "index_selectivity_equals_speed" },
            { "id": "C", "text": "The query must be rewritten only by adding another index hint.", "isCorrect": false, "rationale": "This exposes hint-overuse reasoning.", "diagnosticSignalIfChosen": "hint_overuse" }
          ],
          "correctAnswer": "A",
          "explanation": "The correct reasoning depends on selectivity and row-estimate quality, not index presence alone."
        }
        """;
        var assessmentItem = new AssessmentItem
        {
            Id = assessmentItemId,
            UserId = userId,
            TopicId = topic.Id,
            ConceptGraphSnapshotId = snapshot.Id,
            LearningConceptId = concept.Id,
            AssessmentItemKey = "query-plan-cardinality:item-1",
            ConceptKey = concept.StableKey,
            ConceptLabel = concept.Label,
            QuestionType = "diagnostic_multiple_choice",
            CognitiveSkill = "analysis",
            Difficulty = "core",
            MisconceptionTarget = "index_selectivity_equals_speed",
            EvidenceExpected = "Learner distinguishes index existence from selectivity/cardinality usefulness.",
            GeneratedQuestionJson = generatedQuestionJson,
            ScoringRuleJson = """{"basis":"assessment_item"}""",
            Order = 1,
            CreatedAt = now
        };
        var question = new QuestionItem
        {
            Id = assessmentItemId,
            OwnerUserId = userId,
            ExamDefinitionId = exam.Id,
            LearningTopicId = topic.Id,
            ConceptGraphSnapshotId = snapshot.Id,
            LearningConceptId = concept.Id,
            AssessmentItemId = assessmentItemId,
            QuestionBankSource = "diagnostic_assessment_item",
            ConceptKey = concept.StableKey,
            ConceptLabel = concept.Label,
            MisconceptionTarget = assessmentItem.MisconceptionTarget,
            EvidenceExpected = assessmentItem.EvidenceExpected,
            ScoringRuleJson = assessmentItem.ScoringRuleJson,
            CalibrationStatus = "review_ready",
            VisualReadinessStatus = "not_required",
            QuestionType = "multiple_choice",
            Stem = "A query uses an index but the plan still scans many rows. Which explanation best fits a cardinality-estimation issue?",
            Difficulty = "medium",
            CognitiveSkill = "analysis",
            QualityStatus = "diagnostic_ready",
            LicenseStatus = "user_provided",
            SourceOrigin = "diagnostic_engine",
            SourceTitle = "Orka diagnostic assessment item",
            Explanation = "The correct reasoning depends on selectivity and row-estimate quality, not index presence alone.",
            CreatedAt = now,
            UpdatedAt = now,
            Options =
            [
                new QuestionOption { OptionKey = "A", Text = "The optimizer estimated too few rows, so it chose an access path that became expensive.", IsCorrect = true, Rationale = "This checks selectivity and row-estimate reasoning.", DiagnosticSignalJson = """{"signal":"cardinality_reasoning_present"}""", SortOrder = 0 },
                new QuestionOption { OptionKey = "B", Text = "Any index automatically makes every predicate selective.", IsCorrect = false, Rationale = "This exposes index-presence overgeneralization.", MisconceptionKey = "index_selectivity_equals_speed", DiagnosticSignalJson = """{"signal":"index_selectivity_equals_speed"}""", SortOrder = 1 },
                new QuestionOption { OptionKey = "C", Text = "The query must be rewritten only by adding another index hint.", IsCorrect = false, Rationale = "This exposes hint-overuse reasoning.", MisconceptionKey = "hint_overuse", DiagnosticSignalJson = """{"signal":"hint_overuse"}""", SortOrder = 2 }
            ]
        };

        db.Topics.Add(topic);
        db.ConceptGraphSnapshots.Add(snapshot);
        db.LearningConcepts.Add(concept);
        db.AssessmentItems.Add(assessmentItem);
        db.QuestionItems.Add(question);
        await db.SaveChangesAsync();

        return new MaterializedDiagnosticQuestionIds(topic.Id, snapshot.Id, concept.Id, assessmentItemId, question.Id);
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
    private sealed record DiagnosticQuestionIds(Guid TopicId, Guid ConceptGraphSnapshotId, Guid LearningConceptId, Guid AssessmentItemId);
    private sealed record MaterializedDiagnosticQuestionIds(Guid TopicId, Guid ConceptGraphSnapshotId, Guid LearningConceptId, Guid AssessmentItemId, Guid QuestionItemId);
}

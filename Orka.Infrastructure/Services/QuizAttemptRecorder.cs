using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class QuizAttemptRecorder : IQuizAttemptRecorder
{
    private readonly OrkaDbContext _db;
    private readonly ILearningSignalService _learningSignals;
    private readonly ISkillMasteryService _skillMastery;
    private readonly IReviewSrsService _reviews;
    private readonly IXpEventService _xpEvents;
    private readonly IMistakeClassifierService _mistakeClassifier;

    public QuizAttemptRecorder(
        OrkaDbContext db,
        ILearningSignalService learningSignals,
        ISkillMasteryService skillMastery,
        IReviewSrsService reviews,
        IXpEventService xpEvents,
        IMistakeClassifierService mistakeClassifier)
    {
        _db = db;
        _learningSignals = learningSignals;
        _skillMastery = skillMastery;
        _reviews = reviews;
        _xpEvents = xpEvents;
        _mistakeClassifier = mistakeClassifier;
    }

    public async Task<QuizAttemptRecordResult> RecordAsync(
        Guid userId,
        RecordQuizAttemptRequest request,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var reviewIdentity = ReviewIdentitySelector.Select(
            request.ConceptTag,
            request.SkillTag,
            request.LearningObjective,
            request.TopicPath);
        var sourceRefsJson = MergeMetadata(request.SourceRefsJson, request, reviewIdentity);
        var alreadyAwarded = request.IsCorrect && !string.IsNullOrWhiteSpace(request.QuestionHash)
            ? await _db.QuizAttempts.AnyAsync(a =>
                a.UserId == userId &&
                a.TopicId == request.TopicId &&
                a.QuestionHash == request.QuestionHash &&
                a.IsCorrect,
                ct)
            : false;

        QuizRun? quizRun = null;
        if (request.QuizRunId.HasValue)
        {
            quizRun = await _db.QuizRuns
                .FirstOrDefaultAsync(q => q.Id == request.QuizRunId.Value && q.UserId == userId, ct);
        }

        var validQuizRunId = quizRun?.Id;

        var attempt = new QuizAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            QuizRunId = validQuizRunId,
            QuestionId = Clean(request.QuestionId),
            SessionId = request.SessionId,
            TopicId = request.TopicId,
            Question = Clean(request.Question) ?? string.Empty,
            UserAnswer = Clean(request.SelectedOptionId) ?? string.Empty,
            IsCorrect = request.IsCorrect,
            Explanation = Clean(request.Explanation) ?? string.Empty,
            SkillTag = reviewIdentity,
            TopicPath = Clean(request.TopicPath) ?? Clean(request.LearningObjective) ?? reviewIdentity,
            Difficulty = Clean(request.Difficulty),
            CognitiveType = Clean(request.QuestionType) ?? Clean(request.CognitiveType),
            QuestionHash = Clean(request.QuestionHash),
            SourceRefsJson = sourceRefsJson,
            CreatedAt = now
        };

        _db.QuizAttempts.Add(attempt);

        if (quizRun != null && validQuizRunId.HasValue)
        {
            await UpdateQuizRunAsync(quizRun, userId, validQuizRunId.Value, request.IsCorrect, now, ct);
        }

        await _db.SaveChangesAsync(ct);

        await _learningSignals.RecordQuizAnsweredAsync(attempt, ct);

        MistakeClassificationResult? mistake = null;
        ReviewItem? reviewItem = null;
        if (!request.IsCorrect && request.TopicId.HasValue)
        {
            mistake = await _mistakeClassifier.ClassifyAndRecordAsync(
                userId,
                request.TopicId,
                request.SessionId,
                new MistakeClassificationRequest(
                    request.Question,
                    request.Explanation,
                    request.SelectedOptionId,
                    request.Explanation,
                    request.TopicId,
                    request.SkillTag,
                    request.ConceptTag,
                    SourceOrWikiContext: request.SourceRefsJson),
                ct);

            reviewItem = await _reviews.EnsureReviewItemAsync(
                userId,
                request.TopicId,
                request.ConceptTag,
                request.SkillTag,
                request.LearningObjective,
                mistake?.Category ?? request.MistakeCategory,
                request.TopicPath,
                "quiz",
                attempt.Id,
                attempt.Id,
                learningSignalId: null,
                flashcardId: null,
                remediationPlanId: null,
                ct);
            await _db.SaveChangesAsync(ct);
        }

        SkillMastery? mastery = null;
        if (request.TopicId.HasValue)
        {
            await _skillMastery.RecordMasteryAsync(
                userId,
                request.TopicId.Value,
                reviewIdentity,
                request.IsCorrect ? 100 : 0);

            mastery = await _db.SkillMasteries
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.TopicId == request.TopicId.Value && m.SubTopicTitle == reviewIdentity)
                .OrderByDescending(m => m.MasteredAt)
                .FirstOrDefaultAsync(ct);
        }

        var xp = await AwardQuizXpAsync(userId, request.IsCorrect, alreadyAwarded, attempt.Id, request.QuestionHash, ct);
        var review = reviewItem != null
            ? ToLegacyReviewDto(reviewItem, mastery?.Id)
            : await BuildReviewDtoAsync(userId, request.TopicId, reviewIdentity, mastery?.Id, ct);
        return new QuizAttemptRecordResult(attempt, xp, review, mistake == null ? null : new MistakeTaxonomyResult(
            ToLegacyMistakeCategory(mistake.Category),
            mistake.CategoryLabel,
            mistake.Reason,
            mistake.SuggestedReviewPressure,
            null,
            mistake.SuggestedReviewPressure >= 3,
            mistake.SuggestFlashcard));
    }

    private async Task UpdateQuizRunAsync(
        QuizRun quizRun,
        Guid userId,
        Guid quizRunId,
        bool isCorrect,
        DateTime now,
        CancellationToken ct)
    {
        if (isCorrect)
        {
            quizRun.CorrectCount += 1;
        }

        var existingAttemptCount = await _db.QuizAttempts
            .CountAsync(a => a.UserId == userId && a.QuizRunId == quizRunId, ct);
        var answeredCount = existingAttemptCount + 1;

        if (quizRun.TotalQuestions > 0 && answeredCount >= quizRun.TotalQuestions)
        {
            quizRun.Status = "completed";
            quizRun.CompletedAt ??= now;
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MistakeCategory ToLegacyMistakeCategory(string category) => category switch
    {
        "Conceptual" => MistakeCategory.Conceptual,
        "Procedural" => MistakeCategory.Procedural,
        "Careless" => MistakeCategory.Careless,
        "MisreadQuestion" => MistakeCategory.MisreadQuestion,
        "Vocabulary" => MistakeCategory.Reading,
        "FormulaMisuse" => MistakeCategory.Application,
        "CodeSyntax" or "CodeRuntime" or "CodeLogic" => MistakeCategory.Application,
        _ => MistakeCategory.Unknown
    };

    private async Task<XpAwardResult?> AwardQuizXpAsync(
        Guid userId,
        bool isCorrect,
        bool alreadyAwarded,
        Guid attemptId,
        string? questionHash,
        CancellationToken ct)
    {
        if (!isCorrect || alreadyAwarded) return new XpAwardResult(false, 0, await GetTotalXpAsync(userId, ct), 0, []);

        var key = string.IsNullOrWhiteSpace(questionHash)
            ? $"quiz:{attemptId:N}"
            : $"quiz:{questionHash.Trim()}";
        var result = await _xpEvents.AwardAsync(userId, key, "quiz_correct", 20, "QuizAttempt", attemptId, ct);
        await _db.SaveChangesAsync(ct);
        return result;
    }

    private static ReviewItemDto ToLegacyReviewDto(ReviewItem item, Guid? masteryId) =>
        new(
            item.Id,
            masteryId,
            item.ConceptTag ?? item.SkillTag ?? item.LearningObjective ?? item.ReviewKey,
            item.DueAt,
            item.IntervalDays,
            item.EaseFactor,
            item.RepetitionCount,
            item.LapseCount);

    private async Task<int> GetTotalXpAsync(Guid userId, CancellationToken ct) =>
        await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.TotalXP)
            .FirstOrDefaultAsync(ct);

    private async Task<ReviewItemDto?> BuildReviewDtoAsync(
        Guid userId,
        Guid? topicId,
        string reviewIdentity,
        Guid? masteryId,
        CancellationToken ct)
    {
        if (!topicId.HasValue) return null;

        var recommendation = await _db.StudyRecommendations
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.TopicId == topicId.Value && r.SkillTag == reviewIdentity && !r.IsDone)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (recommendation == null) return null;

        return new ReviewItemDto(
            recommendation.Id,
            masteryId,
            recommendation.SkillTag ?? reviewIdentity,
            recommendation.CreatedAt,
            IntervalDays: 0,
            EaseFactor: 2.5,
            RepetitionCount: 0,
            LastReviewQuality: 0);
    }

    private static string? MergeMetadata(string? sourceRefsJson, RecordQuizAttemptRequest request, string reviewIdentity)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["reviewIdentity"] = reviewIdentity,
            ["conceptTag"] = Clean(request.ConceptTag),
            ["skillTag"] = Clean(request.SkillTag),
            ["learningObjective"] = Clean(request.LearningObjective),
            ["questionType"] = Clean(request.QuestionType),
            ["mistakeCategory"] = Clean(request.MistakeCategory)
        };

        if (!string.IsNullOrWhiteSpace(sourceRefsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(sourceRefsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in doc.RootElement.EnumerateObject())
                    {
                        if (!metadata.ContainsKey(property.Name))
                            metadata[property.Name] = property.Value.ToString();
                    }
                }
            }
            catch (JsonException)
            {
                metadata["rawSourceRefs"] = sourceRefsJson;
            }
        }

        var compact = metadata
            .Where(kvp => kvp.Value is not null && !string.IsNullOrWhiteSpace(kvp.Value.ToString()))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return compact.Count == 0 ? null : JsonSerializer.Serialize(compact);
    }
}

using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class QuizAttemptRecorder : IQuizAttemptRecorder
{
    private readonly OrkaDbContext _db;
    private readonly ILearningSignalService _learningSignals;
    private readonly ISkillMasteryService _skillMastery;

    public QuizAttemptRecorder(
        OrkaDbContext db,
        ILearningSignalService learningSignals,
        ISkillMasteryService skillMastery)
    {
        _db = db;
        _learningSignals = learningSignals;
        _skillMastery = skillMastery;
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

        var attempt = new QuizAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            QuizRunId = request.QuizRunId,
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

        if (request.QuizRunId.HasValue)
        {
            var quizRun = await _db.QuizRuns
                .FirstOrDefaultAsync(q => q.Id == request.QuizRunId.Value && q.UserId == userId, ct);

            if (quizRun != null)
            {
                await UpdateQuizRunAsync(quizRun, userId, request.QuizRunId.Value, request.IsCorrect, now, ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        await _learningSignals.RecordQuizAnsweredAsync(attempt, ct);

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

        var xp = await AwardQuizXpAsync(userId, request.IsCorrect, alreadyAwarded, ct);
        var review = await BuildReviewDtoAsync(userId, request.TopicId, reviewIdentity, mastery?.Id, ct);
        return new QuizAttemptRecordResult(attempt, xp, review, null);
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

    private async Task<XpAwardResult?> AwardQuizXpAsync(
        Guid userId,
        bool isCorrect,
        bool alreadyAwarded,
        CancellationToken ct)
    {
        if (!isCorrect || alreadyAwarded) return new XpAwardResult(false, 0, await GetTotalXpAsync(userId, ct), 0, []);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return null;

        var today = DateTime.UtcNow.Date;
        if (user.LastActiveDate?.Date == today.AddDays(-1))
        {
            user.CurrentStreak += 1;
        }
        else if (user.LastActiveDate?.Date != today)
        {
            user.CurrentStreak = 1;
        }

        user.LastActiveDate = DateTime.UtcNow;
        user.TotalXP += 20;
        await _db.SaveChangesAsync(ct);

        return new XpAwardResult(true, 20, user.TotalXP, user.CurrentStreak, []);
    }

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

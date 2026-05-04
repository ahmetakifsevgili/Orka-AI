using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class QuizAttemptRecorder : IQuizAttemptRecorder
{
    private readonly OrkaDbContext _db;

    public QuizAttemptRecorder(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<QuizAttemptRecordResult> RecordAsync(
        Guid userId,
        RecordQuizAttemptRequest request,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
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
            SkillTag = Clean(request.SkillTag),
            TopicPath = Clean(request.TopicPath),
            Difficulty = Clean(request.Difficulty),
            CognitiveType = Clean(request.CognitiveType),
            QuestionHash = Clean(request.QuestionHash),
            SourceRefsJson = Clean(request.SourceRefsJson),
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
        return new QuizAttemptRecordResult(attempt, null, null, null);
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
}

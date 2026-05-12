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
    private readonly ILearningEventNormalizer _learningEvents;
    private readonly IAssessmentQualityService? _assessmentQuality;
    private readonly IKnowledgeTracingService? _knowledgeTracing;

    public QuizAttemptRecorder(
        OrkaDbContext db,
        ILearningSignalService learningSignals,
        ISkillMasteryService skillMastery,
        IReviewSrsService reviews,
        IXpEventService xpEvents,
        IMistakeClassifierService mistakeClassifier,
        ILearningEventNormalizer learningEvents,
        IAssessmentQualityService? assessmentQuality = null,
        IKnowledgeTracingService? knowledgeTracing = null)
    {
        _db = db;
        _learningSignals = learningSignals;
        _skillMastery = skillMastery;
        _reviews = reviews;
        _xpEvents = xpEvents;
        _mistakeClassifier = mistakeClassifier;
        _learningEvents = learningEvents;
        _assessmentQuality = assessmentQuality;
        _knowledgeTracing = knowledgeTracing;
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
            AssessmentItemId = request.AssessmentItemId,
            TopicPath = Clean(request.TopicPath) ?? Clean(request.LearningObjective) ?? reviewIdentity,
            Difficulty = Clean(request.Difficulty),
            CognitiveType = Clean(request.QuestionType) ?? Clean(request.CognitiveType),
            QuestionHash = Clean(request.QuestionHash),
            SourceRefsJson = sourceRefsJson,
            ResponseTimeMs = request.ResponseTimeMs.HasValue ? Math.Max(0, request.ResponseTimeMs.Value) : null,
            WasSkipped = request.WasSkipped == true || string.Equals(request.SelectedOptionId, "skip", StringComparison.OrdinalIgnoreCase),
            ConfidenceSelfRating = request.ConfidenceSelfRating.HasValue
                ? Math.Clamp(request.ConfidenceSelfRating.Value, 0m, 1m)
                : null,
            CreatedAt = now
        };

        _db.QuizAttempts.Add(attempt);

        if (quizRun != null && validQuizRunId.HasValue)
        {
            await UpdateQuizRunAsync(quizRun, userId, validQuizRunId.Value, request.IsCorrect, now, ct);
        }

        await _db.SaveChangesAsync(ct);

        var itemStat = _assessmentQuality == null ? null : await _assessmentQuality.UpdateItemStatsAsync(attempt, ct);
        var tracingState = _knowledgeTracing == null ? null : await _knowledgeTracing.UpdateFromAttemptAsync(attempt, ct);
        if (tracingState != null && request.TopicId.HasValue)
        {
            await UpsertConceptMasteryFromTracingAsync(userId, request.TopicId.Value, attempt, tracingState, ct);
        }
        if (itemStat != null || tracingState != null)
        {
            attempt.SourceRefsJson = MergeLearningStateMetadata(attempt.SourceRefsJson, itemStat, tracingState);
            await _db.SaveChangesAsync(ct);
        }

        await _learningEvents.RecordQuizAttemptEventAsync(attempt, ct);
        await _learningSignals.RecordQuizAnsweredAsync(attempt, ct);

        MistakeClassificationResult? mistake = null;
        MisconceptionIntelligenceResult? misconceptionIntelligence = null;
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
            misconceptionIntelligence = MisconceptionIntelligenceEvaluator.FromQuizAttempt(
                attempt,
                mistake,
                tracingState);
            attempt.SourceRefsJson = MergeMisconceptionMetadata(attempt.SourceRefsJson, misconceptionIntelligence);
            await UpdateConceptMasteryMisconceptionEvidenceAsync(userId, request.TopicId.Value, misconceptionIntelligence, ct);
            await EnrichQuizLearningSignalsAsync(attempt, misconceptionIntelligence, ct);
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
            mistake.SuggestFlashcard),
            misconceptionIntelligence?.MisconceptionSignal,
            misconceptionIntelligence?.LearningSignalConfidence,
            misconceptionIntelligence?.RemediationSeed);
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

    private async Task UpsertConceptMasteryFromTracingAsync(
        Guid userId,
        Guid topicId,
        QuizAttempt attempt,
        KnowledgeTracingStateDto tracingState,
        CancellationToken ct)
    {
        var conceptKey = string.IsNullOrWhiteSpace(tracingState.ConceptKey)
            ? attempt.SkillTag
            : tracingState.ConceptKey;
        if (string.IsNullOrWhiteSpace(conceptKey))
        {
            return;
        }

        var mastery = await _db.ConceptMasteries
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TopicId == topicId && m.ConceptKey == conceptKey, ct);
        if (mastery == null)
        {
            mastery = new ConceptMastery
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                ConceptKey = conceptKey,
                CreatedAt = DateTime.UtcNow
            };
            _db.ConceptMasteries.Add(mastery);
        }

        mastery.Label = string.IsNullOrWhiteSpace(tracingState.Label)
            ? conceptKey
            : tracingState.Label;
        mastery.MasteryScore = Math.Round(tracingState.MasteryProbability * 100m, 2);
        mastery.Confidence = tracingState.Confidence;
        mastery.RemediationNeed = tracingState.RemediationNeed;
        mastery.PracticeReadiness = tracingState.PracticeReadiness;
        mastery.Attempts = tracingState.EvidenceCount;
        mastery.Correct = tracingState.CorrectCount;
        mastery.LastEvidenceAt = tracingState.LastEvidenceAt?.UtcDateTime ?? attempt.CreatedAt;
        mastery.UpdatedAt = DateTime.UtcNow;
    }

    private async Task UpdateConceptMasteryMisconceptionEvidenceAsync(
        Guid userId,
        Guid topicId,
        MisconceptionIntelligenceResult intelligence,
        CancellationToken ct)
    {
        var conceptKey = intelligence.MisconceptionSignal.ConceptKey;
        if (string.IsNullOrWhiteSpace(conceptKey))
        {
            return;
        }

        var mastery = await _db.ConceptMasteries
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TopicId == topicId && m.ConceptKey == conceptKey, ct);
        if (mastery == null)
        {
            return;
        }

        var evidence = new List<object>();
        if (!string.IsNullOrWhiteSpace(mastery.MisconceptionEvidenceJson))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<List<JsonElement>>(mastery.MisconceptionEvidenceJson);
                if (existing != null)
                {
                    evidence.AddRange(existing.Take(8).Select(item => JsonSerializer.Deserialize<object>(item.GetRawText())!));
                }
            }
            catch
            {
                evidence.Clear();
            }
        }

        evidence.Insert(0, new
        {
            intelligence.MisconceptionSignal.Category,
            intelligence.MisconceptionSignal.UserSafeLabel,
            intelligence.MisconceptionSignal.Confidence,
            intelligence.MisconceptionSignal.ConfidenceStatus,
            intelligence.RemediationSeed.FirstAction,
            CreatedAt = DateTime.UtcNow
        });
        mastery.MisconceptionEvidenceJson = JsonSerializer.Serialize(evidence.Take(10));
        mastery.UpdatedAt = DateTime.UtcNow;
    }

    private async Task EnrichQuizLearningSignalsAsync(
        QuizAttempt attempt,
        MisconceptionIntelligenceResult intelligence,
        CancellationToken ct)
    {
        var signals = await _db.LearningSignals
            .Where(s => s.UserId == attempt.UserId && s.QuizAttemptId == attempt.Id)
            .ToListAsync(ct);

        foreach (var signal in signals)
        {
            signal.PayloadJson = MergeMisconceptionMetadata(signal.PayloadJson, intelligence);
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
            ["assessmentItemId"] = request.AssessmentItemId?.ToString("D"),
            ["conceptKey"] = Clean(request.ConceptKey),
            ["conceptTag"] = Clean(request.ConceptTag),
            ["skillTag"] = Clean(request.SkillTag),
            ["cognitiveSkill"] = Clean(request.CognitiveSkill),
            ["misconceptionTarget"] = Clean(request.MisconceptionTarget),
            ["evidenceExpected"] = Clean(request.EvidenceExpected),
            ["scoringRule"] = Clean(request.ScoringRule),
            ["learningOutcomeIds"] = Clean(request.LearningOutcomeIdsJson),
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

    private static string? MergeLearningStateMetadata(
        string? sourceRefsJson,
        AssessmentItemStatDto? itemStat,
        KnowledgeTracingStateDto? tracingState)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(sourceRefsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(sourceRefsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in doc.RootElement.EnumerateObject())
                    {
                        metadata[property.Name] = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.ToString();
                    }
                }
            }
            catch
            {
                metadata["rawSourceRefs"] = sourceRefsJson;
            }
        }

        if (itemStat != null)
        {
            metadata["itemQualityStatus"] = itemStat.QualityStatus;
            metadata["assessmentItemAttempts"] = itemStat.Attempts;
            metadata["assessmentItemCorrectRate"] = itemStat.CorrectRate;
            metadata["assessmentItemDiscriminationProxy"] = itemStat.DiscriminationProxy;
        }

        if (tracingState != null)
        {
            metadata["knowledgeTracingStateId"] = tracingState.Id.ToString("D");
            metadata["masteryProbability"] = tracingState.MasteryProbability;
            metadata["masteryConfidence"] = tracingState.Confidence;
            metadata["remediationNeed"] = tracingState.RemediationNeed;
            metadata["practiceReadiness"] = tracingState.PracticeReadiness;
        }

        return metadata.Count == 0 ? null : JsonSerializer.Serialize(metadata);
    }

    private static string? MergeMisconceptionMetadata(
        string? json,
        MisconceptionIntelligenceResult intelligence)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in doc.RootElement.EnumerateObject())
                    {
                        metadata[property.Name] = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : JsonSerializer.Deserialize<object>(property.Value.GetRawText());
                    }
                }
            }
            catch
            {
                metadata["rawSourceRefs"] = json;
            }
        }

        metadata["misconceptionSignal"] = intelligence.MisconceptionSignal;
        metadata["learningSignalConfidence"] = intelligence.LearningSignalConfidence;
        metadata["remediationSeed"] = intelligence.RemediationSeed;
        return JsonSerializer.Serialize(metadata);
    }
}

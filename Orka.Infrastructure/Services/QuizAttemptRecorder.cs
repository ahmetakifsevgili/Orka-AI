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
    private readonly IRedisMemoryService? _redis;
    private readonly IAssessmentQualityService? _assessmentQuality;
    private readonly IKnowledgeTracingService? _knowledgeTracing;
    private readonly IActiveLessonSnapshotService? _learningSnapshots;
    private readonly IWikiService? _wikiService;
    private readonly IWikiLearningTraceWriter? _wikiTraceWriter;

    public QuizAttemptRecorder(
        OrkaDbContext db,
        ILearningSignalService learningSignals,
        ISkillMasteryService skillMastery,
        IReviewSrsService reviews,
        IXpEventService xpEvents,
        IMistakeClassifierService mistakeClassifier,
        ILearningEventNormalizer learningEvents,
        IAssessmentQualityService? assessmentQuality = null,
        IKnowledgeTracingService? knowledgeTracing = null,
        IActiveLessonSnapshotService? learningSnapshots = null,
        IWikiService? wikiService = null,
        IWikiLearningTraceWriter? wikiTraceWriter = null,
        IRedisMemoryService? redis = null)
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
        _learningSnapshots = learningSnapshots;
        _wikiService = wikiService;
        _wikiTraceWriter = wikiTraceWriter;
        _redis = redis;
    }

    public async Task<QuizAttemptRecordResult> RecordAsync(
        Guid userId,
        RecordQuizAttemptRequest request,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var verification = await ResolveAttemptVerificationAsync(userId, request, ct);
        var clientSourceEvidenceBundleId = request.SourceEvidenceBundleId;
        var verifiedSourceEvidenceBundleId = await ResolveVerifiedSourceEvidenceBundleIdAsync(userId, request, now, ct);
        request.SourceEvidenceBundleId = verifiedSourceEvidenceBundleId;
        var isVerified = verification.IsVerified;
        var isCorrect = isVerified && verification.IsCorrect;
        var safeExplanation = verification.SafeExplanation;
        var reviewIdentity = ReviewIdentitySelector.Select(
            request.ConceptTag,
            request.SkillTag,
            request.LearningObjective,
            request.TopicPath);
        var questionHash = BuildQuestionHash(request);
        var sourceRefsJson = MergeMetadata(
            request.SourceRefsJson,
            request,
            reviewIdentity,
            verification,
            clientSourceEvidenceBundleId.HasValue
                ? verifiedSourceEvidenceBundleId.HasValue ? "server_verified" : "client_rejected"
                : null);
        var alreadyAwarded = isCorrect && !string.IsNullOrWhiteSpace(questionHash)
            ? await _db.QuizAttempts.AnyAsync(a =>
                a.UserId == userId &&
                a.TopicId == request.TopicId &&
                a.QuestionHash == questionHash &&
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
        var assessmentItemId = verification.AssessmentItemId ?? request.AssessmentItemId;

        var existingAttempt = await FindExistingAttemptAsync(userId, validQuizRunId, assessmentItemId, request.TopicId, questionHash, ct);
        if (existingAttempt != null)
        {
            var replayImpact = BuildLearningImpact(request, existingAttempt, null, null, verification);
            return new QuizAttemptRecordResult(existingAttempt, null, null, null, LearningImpact: replayImpact);
        }

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
            IsCorrect = isCorrect,
            Explanation = safeExplanation ?? string.Empty,
            SkillTag = reviewIdentity,
            AssessmentItemId = assessmentItemId,
            TopicPath = Clean(request.TopicPath) ?? Clean(request.LearningObjective) ?? reviewIdentity,
            Difficulty = Clean(request.Difficulty),
            CognitiveType = Clean(request.QuestionType) ?? Clean(request.CognitiveType),
            QuestionHash = questionHash,
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
            await UpdateQuizRunAsync(quizRun, userId, validQuizRunId.Value, isCorrect, now, ct);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsLikelyDuplicateAttempt(ex))
        {
            _db.Entry(attempt).State = EntityState.Detached;
            if (quizRun != null)
            {
                _db.Entry(quizRun).State = EntityState.Detached;
            }

            var replayAttempt = await FindExistingAttemptAsync(userId, validQuizRunId, assessmentItemId, request.TopicId, questionHash, ct);
            if (replayAttempt != null)
            {
                var replayImpact = BuildLearningImpact(request, replayAttempt, null, null, verification);
                return new QuizAttemptRecordResult(replayAttempt, null, null, null, LearningImpact: replayImpact);
            }

            throw;
        }

        if (_redis != null && !string.IsNullOrWhiteSpace(attempt.QuestionHash) && attempt.TopicId.HasValue)
        {
            try
            {
                await _redis.RememberQuestionHashesAsync(userId, attempt.TopicId.Value, new[] { attempt.QuestionHash });
            }
            catch (Exception)
            {
                // fail-safe
            }
        }

        var itemStat = isVerified && _assessmentQuality != null ? await _assessmentQuality.UpdateItemStatsAsync(attempt, ct) : null;
        var tracingState = isVerified && _knowledgeTracing != null ? await _knowledgeTracing.UpdateFromAttemptAsync(attempt, ct) : null;
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
        if (isVerified)
        {
            await _learningSignals.RecordQuizAnsweredAsync(attempt, ct);
        }

        MistakeClassificationResult? mistake = null;
        MisconceptionIntelligenceResult? misconceptionIntelligence = null;
        ReviewItem? reviewItem = null;
        if (isVerified && !isCorrect && request.TopicId.HasValue)
        {
            mistake = await _mistakeClassifier.ClassifyAndRecordAsync(
                userId,
                request.TopicId,
                request.SessionId,
                new MistakeClassificationRequest(
                    request.Question,
                    safeExplanation,
                    request.SelectedOptionId,
                    safeExplanation,
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
        if (isVerified && request.TopicId.HasValue)
        {
            await _skillMastery.RecordMasteryAsync(
                userId,
                request.TopicId.Value,
                reviewIdentity,
                isCorrect ? 100 : 0);

            mastery = await _db.SkillMasteries
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.TopicId == request.TopicId.Value && m.SubTopicTitle == reviewIdentity)
                .OrderByDescending(m => m.MasteredAt)
                .FirstOrDefaultAsync(ct);
        }

        var xp = await AwardQuizXpAsync(userId, isCorrect, alreadyAwarded, attempt.Id, request.QuestionHash, ct);
        await RefreshSnapshotsAfterAttemptAsync(userId, request, ct);
        var review = !isVerified ? null : reviewItem != null
            ? ToLegacyReviewDto(reviewItem, mastery?.Id)
            : await BuildReviewDtoAsync(userId, request.TopicId, reviewIdentity, mastery?.Id, ct);
        var learningImpact = BuildLearningImpact(request, attempt, tracingState, misconceptionIntelligence, verification);
        await AppendQuizImpactToWikiAsync(userId, request, attempt, learningImpact, ct);
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
            misconceptionIntelligence?.RemediationSeed,
            learningImpact);
    }

    private async Task<QuizAttempt?> FindExistingAttemptAsync(
        Guid userId,
        Guid? quizRunId,
        Guid? assessmentItemId,
        Guid? topicId,
        string? questionHash,
        CancellationToken ct)
    {
        if (quizRunId.HasValue && assessmentItemId.HasValue)
        {
            var byRunAndItem = await _db.QuizAttempts
                .AsNoTracking()
                .OrderBy(a => a.CreatedAt)
                .FirstOrDefaultAsync(a =>
                    a.UserId == userId &&
                    a.QuizRunId == quizRunId.Value &&
                    a.AssessmentItemId == assessmentItemId.Value,
                    ct);
            if (byRunAndItem != null)
            {
                return byRunAndItem;
            }
        }

        if (!string.IsNullOrWhiteSpace(questionHash))
        {
            return await _db.QuizAttempts
                .AsNoTracking()
                .OrderBy(a => a.CreatedAt)
                .FirstOrDefaultAsync(a =>
                    a.UserId == userId &&
                    a.TopicId == topicId &&
                    a.QuestionHash == questionHash,
                    ct);
        }

        return null;
    }

    private static string? BuildQuestionHash(RecordQuizAttemptRequest request)
    {
        var questionHash = Clean(request.QuestionHash);
        if (!string.IsNullOrWhiteSpace(questionHash) || string.IsNullOrWhiteSpace(request.Question))
        {
            return questionHash;
        }

        return RedisMemoryService.ComputeQuestionHash(
            request.Question,
            request.SkillTag,
            request.TopicPath,
            request.ConceptTag ?? request.ConceptKey ?? request.LearningObjective,
            request.Difficulty);
    }

    private static bool IsLikelyDuplicateAttempt(DbUpdateException ex)
    {
        if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
        {
            if (sqlEx.Number == 2601 || sqlEx.Number == 2627)
            {
                return true;
            }
        }

        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("IX_QuizAttempts_UserId_QuizRunId_AssessmentItemId", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("IX_QuizAttempts_UserId_TopicId_QuestionHash", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unique", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<AttemptVerification> ResolveAttemptVerificationAsync(
        Guid userId,
        RecordQuizAttemptRequest request,
        CancellationToken ct)
    {
        AssessmentItem? item = null;
        if (request.AssessmentItemId.HasValue)
        {
            item = await _db.AssessmentItems
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == request.AssessmentItemId.Value && i.UserId == userId, ct);
        }

        var serverAuthoredDiagnostic = request.QuizRunId.HasValue &&
            await IsServerAuthoredDiagnosticRunAsync(userId, request.QuizRunId.Value, ct);
        if (serverAuthoredDiagnostic &&
            (item == null ||
             item.QuizRunId != request.QuizRunId ||
             item.TopicId != request.TopicId))
        {
            return AttemptVerification.Unverified("assessment_item_scope_mismatch", request.IsCorrect, request.AssessmentItemId);
        }

        if (item == null &&
            !request.AssessmentItemId.HasValue &&
            serverAuthoredDiagnostic)
        {
            return AttemptVerification.Unverified("assessment_item_required_for_diagnostic", request.IsCorrect);
        }

        if (item == null && request.QuizRunId.HasValue && !serverAuthoredDiagnostic)
        {
            var candidates = await _db.AssessmentItems
                .AsNoTracking()
                .Where(i => i.UserId == userId && i.QuizRunId == request.QuizRunId.Value)
                .OrderBy(i => i.Order)
                .Take(80)
                .ToListAsync(ct);

            item = candidates.FirstOrDefault(candidate =>
                QuestionMatches(candidate.GeneratedQuestionJson, request.QuestionId, request.Question)) ??
                candidates.FirstOrDefault();
        }

        if (item == null)
        {
            return AttemptVerification.Unverified("missing_server_answer_key", request.IsCorrect);
        }

        var selected = CleanSelectedOption(request.SelectedOptionId);
        var result = TryEvaluateGeneratedQuestion(item, selected);
        if (result != null)
        {
            return result with
            {
                AssessmentItemId = item.Id,
                ReasonCode = "server_assessment_item"
            };
        }

        return AttemptVerification.Unverified("unresolvable_server_answer_key", request.IsCorrect, item.Id);
    }

    private async Task<bool> IsServerAuthoredDiagnosticRunAsync(Guid userId, Guid quizRunId, CancellationToken ct)
    {
        var quizRun = await _db.QuizRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == quizRunId && q.UserId == userId, ct);
        if (quizRun == null)
        {
            return false;
        }

        return quizRun.QuizType.Equals("baseline", StringComparison.OrdinalIgnoreCase) ||
               (quizRun.MetadataJson?.Contains("planRequestId", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private async Task RefreshSnapshotsAfterAttemptAsync(Guid userId, RecordQuizAttemptRequest request, CancellationToken ct)
    {
        if (_learningSnapshots == null)
        {
            return;
        }

        try
        {
            await _learningSnapshots.MarkActiveLessonSnapshotStaleAsync(
                userId,
                request.TopicId,
                request.SessionId,
                "quiz_attempt_recorded",
                ct);

            await _learningSnapshots.BuildOrRefreshStudentContextSnapshotAsync(
                userId,
                new StudentContextSnapshotRequestDto
                {
                    TopicId = request.TopicId,
                    SessionId = request.SessionId
                },
                ct);
        }
        catch
        {
            // Snapshot convergence must never block durable quiz/memory recording.
        }
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

    private static AttemptVerification? TryEvaluateGeneratedQuestion(AssessmentItem item, string? selectedOption)
    {
        if (string.IsNullOrWhiteSpace(item.GeneratedQuestionJson) || string.IsNullOrWhiteSpace(selectedOption))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(item.GeneratedQuestionJson);
            var root = doc.RootElement;
            var explanation = Clean(GetString(root, "explanation")) ?? Clean(GetString(root, "rationale"));
            var correctHint = FirstNonEmpty(
                GetString(root, "correctAnswer"),
                GetString(root, "correct_answer"),
                GetString(root, "answer"),
                GetString(root, "correctOption"),
                GetString(root, "correctOptionId"),
                GetString(root, "correct_option_id"));

            if (root.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
            {
                foreach (var (option, index) in options.EnumerateArray().Select((value, index) => (value, index)))
                {
                    var optionId = OptionId(option, index);
                    var optionText = OptionText(option);
                    var optionLetter = ((char)('A' + Math.Min(index, 25))).ToString();
                    var isCorrect = OptionIsCorrect(option) || MatchesSelected(correctHint, optionId, optionText, optionLetter);
                    if (MatchesSelected(selectedOption, optionId, optionText, optionLetter))
                    {
                        return new AttemptVerification(true, isCorrect, "server_assessment_item", "strong", explanation, item.Id, null);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(correctHint))
            {
                return new AttemptVerification(
                    true,
                    MatchesSelected(selectedOption, correctHint, correctHint, correctHint),
                    "server_assessment_item",
                    "strong",
                    explanation,
                    item.Id,
                    null);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool QuestionMatches(string? generatedQuestionJson, string? questionId, string? question)
    {
        if (string.IsNullOrWhiteSpace(generatedQuestionJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(generatedQuestionJson);
            var root = doc.RootElement;
            var generatedId = FirstNonEmpty(GetString(root, "questionId"), GetString(root, "question_id"), GetString(root, "id"));
            if (!string.IsNullOrWhiteSpace(questionId) &&
                !string.IsNullOrWhiteSpace(generatedId) &&
                string.Equals(questionId.Trim(), generatedId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var generatedQuestion = GetString(root, "question");
            return !string.IsNullOrWhiteSpace(question) &&
                   !string.IsNullOrWhiteSpace(generatedQuestion) &&
                   string.Equals(NormalizeAnswerText(question), NormalizeAnswerText(generatedQuestion), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.ToString(),
            _ => null
        };
    }

    private static string OptionId(JsonElement option, int index)
    {
        if (option.ValueKind == JsonValueKind.Object)
        {
            return FirstNonEmpty(
                GetString(option, "id"),
                GetString(option, "key"),
                GetString(option, "label")) ?? ((char)('A' + Math.Min(index, 25))).ToString();
        }

        return ((char)('A' + Math.Min(index, 25))).ToString();
    }

    private static string OptionText(JsonElement option)
    {
        if (option.ValueKind == JsonValueKind.String)
        {
            return option.GetString() ?? string.Empty;
        }

        if (option.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return FirstNonEmpty(
            GetString(option, "text"),
            GetString(option, "value"),
            GetString(option, "label")) ?? string.Empty;
    }

    private static bool OptionIsCorrect(JsonElement option)
    {
        if (option.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var propertyName in new[] { "isCorrect", "is_correct", "correct" })
        {
            if (!option.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return property.GetBoolean();
            }

            if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return false;
    }

    private static string? CleanSelectedOption(string? selectedOptionId)
    {
        var value = Clean(selectedOptionId);
        if (value == null)
        {
            return null;
        }

        return value.Replace("）", ")", StringComparison.Ordinal).Trim();
    }

    private static bool MatchesSelected(string? selected, string? optionId, string? optionText, string? optionLetter)
    {
        var selectedNormalized = NormalizeAnswerText(selected);
        if (string.IsNullOrWhiteSpace(selectedNormalized))
        {
            return false;
        }

        var candidates = new[]
        {
            optionId,
            optionText,
            optionLetter,
            string.IsNullOrWhiteSpace(optionLetter) || string.IsNullOrWhiteSpace(optionText) ? null : $"{optionLetter}) {optionText}",
            string.IsNullOrWhiteSpace(optionId) || string.IsNullOrWhiteSpace(optionText) ? null : $"{optionId}) {optionText}"
        };

        return candidates
            .Select(NormalizeAnswerText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(candidate => selectedNormalized == candidate || selectedNormalized.StartsWith(candidate + " ", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeAnswerText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^[a-z0-9][\)\].:\-]\s*", "");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim();
    }

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

    private static string? MergeMetadata(
        string? sourceRefsJson,
        RecordQuizAttemptRequest request,
        string reviewIdentity,
        AttemptVerification verification,
        string? sourceEvidenceBundleStatus)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["reviewIdentity"] = reviewIdentity,
            ["assessmentItemId"] = (verification.AssessmentItemId ?? request.AssessmentItemId)?.ToString("D"),
            ["correctnessVerified"] = verification.IsVerified,
            ["correctnessStatus"] = verification.ConfidenceStatus,
            ["correctnessSource"] = verification.ReasonCode,
            ["clientCorrectnessIgnored"] = request.IsCorrect.HasValue && !verification.IsVerified,
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
            ["mistakeCategory"] = Clean(request.MistakeCategory),
            ["assessmentMode"] = NormalizeAssessmentMode(request.AssessmentMode, request.QuestionType, request.CognitiveSkill),
            ["sourceEvidenceBundleId"] = request.SourceEvidenceBundleId?.ToString("D"),
            ["sourceEvidenceBundleStatus"] = sourceEvidenceBundleStatus,
            ["wikiNotebookSectionKey"] = Clean(request.WikiNotebookSectionKey),
            ["sourceReadiness"] = sourceEvidenceBundleStatus == "server_verified"
                ? "source_grounded"
                : sourceEvidenceBundleStatus == "client_rejected"
                    ? "evidence_insufficient"
                    : ExtractClientSafeSourceReadiness(request.SourceRefsJson)
        };

        if (!string.IsNullOrWhiteSpace(sourceRefsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(sourceRefsJson);
                metadata["sourceRefsParseStatus"] = doc.RootElement.ValueKind == JsonValueKind.Object
                    ? "client_metadata_allowlisted"
                    : "client_metadata_ignored";
            }
            catch (JsonException)
            {
                metadata["sourceRefsParseStatus"] = "invalid_json_ignored";
            }
        }

        var compact = metadata
            .Where(kvp => kvp.Value is not null && !string.IsNullOrWhiteSpace(kvp.Value.ToString()))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return compact.Count == 0 ? null : JsonSerializer.Serialize(compact);
    }

    private async Task<Guid?> ResolveVerifiedSourceEvidenceBundleIdAsync(
        Guid userId,
        RecordQuizAttemptRequest request,
        DateTime now,
        CancellationToken ct)
    {
        if (!request.SourceEvidenceBundleId.HasValue)
        {
            return null;
        }

        var bundle = await _db.SourceEvidenceBundles
            .AsNoTracking()
            .Where(b => b.Id == request.SourceEvidenceBundleId.Value &&
                        b.UserId == userId &&
                        !b.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (bundle is null || (bundle.ExpiresAt.HasValue && bundle.ExpiresAt.Value <= now))
        {
            return null;
        }

        if (request.TopicId.HasValue && bundle.TopicId != request.TopicId.Value)
        {
            return null;
        }

        if (request.SessionId.HasValue &&
            bundle.SessionId.HasValue &&
            bundle.SessionId.Value != request.SessionId.Value)
        {
            return null;
        }

        return IsUsableSourceEvidenceBundle(bundle) ? bundle.Id : null;
    }

    private static bool IsUsableSourceEvidenceBundle(SourceEvidenceBundle bundle)
    {
        var status = (Clean(bundle.EvidenceStatus) ?? string.Empty).ToLowerInvariant();
        var readyStatus = status is "source_grounded";
        return readyStatus &&
               bundle.DeletedEvidenceCount == 0 &&
               bundle.StaleEvidenceCount == 0 &&
               (bundle.ReadySourceCount > 0 || bundle.ChunkCount > 0 || bundle.CitationCoverage > 0);
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
                metadata["sourceRefsParseStatus"] = "invalid_json_ignored";
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
                metadata["sourceRefsParseStatus"] = "invalid_json_ignored";
            }
        }

        metadata["misconceptionSignal"] = intelligence.MisconceptionSignal;
        metadata["learningSignalConfidence"] = intelligence.LearningSignalConfidence;
        metadata["remediationSeed"] = intelligence.RemediationSeed;
        return JsonSerializer.Serialize(metadata);
    }

    private static QuizResultLearningImpactDto BuildLearningImpact(
        RecordQuizAttemptRequest request,
        QuizAttempt attempt,
        KnowledgeTracingStateDto? tracingState,
        MisconceptionIntelligenceResult? misconception,
        AttemptVerification verification)
    {
        var conceptKey = FirstNonEmpty(
            tracingState?.ConceptKey,
            request.ConceptKey,
            request.ConceptTag,
            request.SkillTag,
            attempt.SkillTag) ?? string.Empty;
        var assessmentMode = NormalizeAssessmentMode(request.AssessmentMode, request.QuestionType, request.CognitiveSkill);
        var skipped = request.WasSkipped == true || string.Equals(request.SelectedOptionId, "skip", StringComparison.OrdinalIgnoreCase);
        var result = skipped ? "blank" : !verification.IsVerified ? "unverified" : attempt.IsCorrect ? "correct" : "wrong";
        var remediationNeed = skipped ? "medium" : !verification.IsVerified || attempt.IsCorrect ? "none" :
            misconception?.RemediationSeed.FirstAction == "prerequisite_review" ? "high" :
            misconception?.LearningSignalConfidence.Status == "usable" ? "medium" : "low";
        var nextTutorMove = skipped ? "prerequisite_scaffold" : !verification.IsVerified ? "server_assessment_needed" : attempt.IsCorrect ? "confirm_and_extend" :
            misconception?.RemediationSeed.FirstAction switch
            {
                "wiki_review" => "source_review_then_explain",
                "practice_quiz" => "guided_practice",
                "prerequisite_review" => "prerequisite_scaffold",
                _ => "misconception_repair"
            };
        var nextPlanAction = skipped ? "insert_prerequisite_review" : !verification.IsVerified ? "keep_as_practice_observation" : attempt.IsCorrect ? "advance_or_retrieval_practice" :
            assessmentMode == "misconception_probe" ? "insert_remediation_step" : "stay_on_current_step";
        decimal? masteryDelta = tracingState == null ? null :
            attempt.IsCorrect ? Math.Round(0.06m * tracingState.Confidence, 4) : Math.Round(-0.04m * Math.Max(0.30m, tracingState.Confidence), 4);

        var sourceReadiness = ExtractSourceReadiness(attempt.SourceRefsJson) ?? "unknown";
        var evidenceBasis = BuildImpactEvidenceBasis(request, tracingState, misconception, verification);
        var impact = new QuizResultLearningImpactDto
        {
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            QuizRunId = request.QuizRunId,
            QuizAttemptId = attempt.Id,
            AssessmentItemId = request.AssessmentItemId,
            AssessmentMode = assessmentMode,
            TargetConceptKey = conceptKey,
            Result = result,
            MisconceptionSignal = skipped ? null : misconception?.MisconceptionSignal,
            MisconceptionConfidence = skipped || !verification.IsVerified ? "observed_only" : misconception?.LearningSignalConfidence.Status ?? "none",
            RemediationNeed = remediationNeed,
            MasteryDelta = masteryDelta,
            MasteryProbability = tracingState?.MasteryProbability,
            NextTutorMove = nextTutorMove,
            NextPlanAction = nextPlanAction,
            WikiReviewHint = string.IsNullOrWhiteSpace(request.WikiNotebookSectionKey)
                ? (verification.IsVerified && !attempt.IsCorrect ? "wiki_review_if_available" : null)
                : request.WikiNotebookSectionKey,
            SourceReadiness = sourceReadiness,
            EvidenceBasis = evidenceBasis
        };

        impact.RemediationLesson = BuildRemediationLesson(request, impact, misconception, verification, evidenceBasis);
        return impact;
    }

    private static RemediationLessonDto? BuildRemediationLesson(
        RecordQuizAttemptRequest request,
        QuizResultLearningImpactDto impact,
        MisconceptionIntelligenceResult? misconception,
        AttemptVerification verification,
        IReadOnlyList<string> evidenceBasis)
    {
        if (impact.Result is not ("wrong" or "blank" or "partial"))
        {
            return null;
        }

        var skipped = impact.Result == "blank" ||
                      request.WasSkipped == true ||
                      string.Equals(request.SelectedOptionId, "skip", StringComparison.OrdinalIgnoreCase);
        var hasUsableMisconception = !skipped &&
                                     misconception?.MisconceptionSignal != null &&
                                     misconception.LearningSignalConfidence.Status is "usable" or "observed_only";
        var sourceGap = impact.SourceReadiness is "insufficient" or "degraded" or "stale" or "deleted" or "evidence_insufficient";
        var lowMastery = impact.MasteryProbability.HasValue && impact.MasteryProbability.Value < 0.45m;

        var triggerType = skipped ? (request.WasSkipped == true ? "skipped_answer" : "blank_answer") :
            sourceGap ? "source_evidence_gap" :
            hasUsableMisconception ? "misconception_signal" :
            "wrong_answer";
        var repairType = skipped ? "prerequisite_repair" :
            sourceGap ? "source_evidence_review" :
            hasUsableMisconception ? "misconception_repair" :
            lowMastery ? "weak_concept_repair" :
            "guided_reteach";
        var confidence = hasUsableMisconception && misconception?.LearningSignalConfidence.Status == "usable" ? "medium" :
            skipped || lowMastery ? "medium" :
            "low";
        var concept = FirstNonEmpty(impact.TargetConceptKey, request.ConceptKey, request.ConceptTag, request.SkillTag) ?? "aktif kavram";
        var gap = repairType switch
        {
            "misconception_repair" => FirstNonEmpty(
                impact.MisconceptionSignal?.UserSafeLabel,
                impact.MisconceptionSignal?.SafeHint,
                impact.MisconceptionSignal?.Category) ?? "Takilma sinyali kesin tani degil.",
            "prerequisite_repair" => "Cevap bos/skipped oldugu icin eksik onkosul veya guven araligi kontrol edilecek.",
            "source_evidence_review" => "Kaynak kaniti sinirli oldugu icin kaynak iddiasi kurulmadan once kanit kontrolu gerekiyor.",
            "weak_concept_repair" => "Mastery sinyali dusuk; kavram kisa tekrar ve ornek istiyor.",
            _ => "Yanlis cevap kesin yanilgi tani degil; guvenli tekrar gerektiriyor."
        };

        var warnings = new List<string>();
        if (skipped) warnings.Add("blank_answer_not_misconception");
        if (!verification.IsVerified) warnings.Add("correctness_observed_only");
        if (sourceGap) warnings.Add("source_evidence_limited");
        if (!hasUsableMisconception && !skipped) warnings.Add("misconception_not_confirmed");

        return new RemediationLessonDto
        {
            TopicId = request.TopicId,
            ConceptKey = string.IsNullOrWhiteSpace(impact.TargetConceptKey) ? null : impact.TargetConceptKey,
            Trigger = new RemediationTriggerDto
            {
                TriggerType = triggerType,
                UserSafeLabel = triggerType switch
                {
                    "blank_answer" => "Bos cevap telafisi",
                    "skipped_answer" => "Atlanan cevap telafisi",
                    "misconception_signal" => "Takilma sinyali telafisi",
                    "source_evidence_gap" => "Kaynak kaniti kontrolu",
                    _ => "Yanlis cevap telafisi"
                },
                EvidenceStatus = skipped ? "observed_only" : impact.MisconceptionConfidence
            },
            RepairType = repairType,
            Confidence = confidence,
            Basis = evidenceBasis
                .Append(skipped ? "blank_attempt" : "quiz_attempt")
                .Append(repairType)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToArray(),
            LessonShape = BuildRemediationRepairLoop(concept, repairType, gap, lowMastery),
            Checkpoint = new RemediationCheckpointDto
            {
                CheckpointType = repairType == "source_evidence_review" ? "evidence_check" : "micro_check",
                UserSafePrompt = repairType == "source_evidence_review"
                    ? "Bu iddia hangi kaynak kanitiyla destekleniyor, yoksa model destekli mi?"
                    : $"{concept} icin bir mini ornegi cevap anahtari olmadan dene.",
                AvoidsPreSubmitReveal = true,
                Required = true
            },
            Outcome = new RemediationOutcomeDto
            {
                ExpectedSignal = skipped ? "prerequisite_review" : "needs_review",
                MasteryPolicy = "do_not_overstate_mastery",
                NextTutorAction = impact.NextTutorMove,
                NotebookAction = "repair_pack_available"
            },
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(),
            SourceBasis = repairType == "source_evidence_review" ? "evidence_insufficient" : "tutor_generated",
            StudentVisibleSummary = BuildRemediationSummary(repairType, skipped)
        };
    }

    private static RemediationRepairLoopDto BuildRemediationRepairLoop(string concept, string repairType, string gap, bool lowMastery)
    {
        var steps = new List<RemediationStepDto>
        {
            new() { StepType = "goal", UserSafeLabel = "Telafi hedefini kur", Required = true },
            new() { StepType = "short_reteach", UserSafeLabel = "Kisa tekrar", Required = true },
            new() { StepType = "worked_example", UserSafeLabel = "Cozumlu mini ornek", Required = lowMastery || repairType != "source_evidence_review" },
            new() { StepType = "guided_practice", UserSafeLabel = "Tek adimlik pratik", Required = true },
            new() { StepType = "checkpoint", UserSafeLabel = "Cevap anahtarsiz kontrol", Required = true }
        };

        return new RemediationRepairLoopDto
        {
            Goal = repairType switch
            {
                "misconception_repair" => $"{concept} icin takilma sinyalini kesin tani gibi sunmadan duzelt.",
                "prerequisite_repair" => $"{concept} icin eksik onkosulu once kisa telafi et.",
                "source_evidence_review" => $"{concept} icin kaynak kaniti sinirini netlestir.",
                "weak_concept_repair" => $"{concept} icin zayif kavrami mikro dersle toparla.",
                _ => $"{concept} icin yanlis cevabi guvenli tekrar dersine cevir."
            },
            MisconceptionOrGap = gap,
            ShortReteach = repairType == "source_evidence_review"
                ? "Kaynakta desteklenen kisim ile Tutor yorumunu ayir."
                : "Tek kavramsal ayrimi iki kisa adimda yeniden kur.",
            WorkedExample = repairType == "prerequisite_repair"
                ? "Once en kucuk onkosul ornegini cozumlu goster."
                : "Ayni kavrami kisa bir cozumlu ornekle uygula.",
            GuidedPractice = "Ogrenciye tek kucuk adimi kendisi yaptir.",
            Checkpoint = "Cevap anahtari vermeden mikro kontrol sor.",
            NextAction = repairType == "source_evidence_review" ? "review_source_then_continue" : "guided_repair_then_check",
            Steps = steps.Take(5).ToArray()
        };
    }

    private static string BuildRemediationSummary(string repairType, bool skipped) =>
        repairType switch
        {
            "misconception_repair" => "Tutor yanlis cevabi kesin tani yapmadan yanilgi ayrimini onaracak.",
            "prerequisite_repair" when skipped => "Tutor bos/atlanmis cevabi onkosul ve guven telafisine cevirecek.",
            "prerequisite_repair" => "Tutor once eksik onkosulu kisa telafiyle toparlayacak.",
            "source_evidence_review" => "Tutor kaynak kaniti sinirliyken iddiayi kaynakli gibi sunmadan kontrol edecek.",
            "weak_concept_repair" => "Tutor zayif kavram icin mikro ders, ornek ve kontrol uygulayacak.",
            _ => "Tutor yanlis cevabi kisa tekrar, ornek ve kontrolle toparlayacak."
        };

    private static IReadOnlyList<string> BuildImpactEvidenceBasis(
        RecordQuizAttemptRequest request,
        KnowledgeTracingStateDto? tracingState,
        MisconceptionIntelligenceResult? misconception,
        AttemptVerification verification)
    {
        var basis = new List<string> { "quiz_attempt" };
        if ((verification.AssessmentItemId ?? request.AssessmentItemId).HasValue) basis.Add("assessment_item");
        basis.Add(verification.IsVerified ? "server_verified_correctness" : "observed_only_unverified_correctness");
        if (request.WasSkipped == true || string.Equals(request.SelectedOptionId, "skip", StringComparison.OrdinalIgnoreCase))
            basis.Add("blank_answer_prerequisite_review");
        if (!string.IsNullOrWhiteSpace(request.AssessmentMode)) basis.Add("assessment_mode");
        if (tracingState != null) basis.Add("knowledge_tracing");
        if (misconception != null) basis.Add("misconception_signal");
        if (request.SourceEvidenceBundleId.HasValue) basis.Add("source_evidence_bundle");
        if (!string.IsNullOrWhiteSpace(request.WikiNotebookSectionKey)) basis.Add("wiki_notebook");
        return basis.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task AppendQuizImpactToWikiAsync(
        Guid userId,
        RecordQuizAttemptRequest request,
        QuizAttempt attempt,
        QuizResultLearningImpactDto impact,
        CancellationToken ct)
    {
        if ((_wikiService == null && _wikiTraceWriter == null) || !request.TopicId.HasValue) return;

        try
        {
            var conceptKey = FirstNonEmpty(impact.TargetConceptKey, request.ConceptKey, request.ConceptTag, request.SkillTag, attempt.SkillTag);
            var sourceBasis = request.SourceEvidenceBundleId.HasValue
                ? "source_grounded"
                : !string.IsNullOrWhiteSpace(request.WikiNotebookSectionKey) ? "wiki_backed" : "model_assisted";
            var content = BuildWikiQuizImpactContent(request, impact);

            if (_wikiTraceWriter != null)
            {
                var resultBlock = await _wikiTraceWriter.RecordQuizResultAsync(new WikiLearningTraceRequestDto
                {
                    UserId = userId,
                    TopicId = request.TopicId,
                    SessionId = request.SessionId,
                    ConceptKey = conceptKey,
                    QuizAttemptId = attempt.Id,
                    SourceEvidenceBundleId = request.SourceEvidenceBundleId,
                    TraceType = impact.Result == "correct" ? "quiz_review" : "quiz_result",
                    Title = "Quiz sonucu",
                    SafeContent = content,
                    SourceBasis = sourceBasis,
                    MisconceptionKey = impact.MisconceptionSignal?.Category,
                    CreatedBy = "quiz_attempt",
                    Visibility = "normal"
                }, ct);

                WikiBlockDto? repairBlock = null;
                if (impact.Result is "wrong" or "blank" or "partial")
                {
                    repairBlock = await _wikiTraceWriter.RecordRepairNoteAsync(new WikiLearningTraceRequestDto
                    {
                        UserId = userId,
                        TopicId = request.TopicId,
                        SessionId = request.SessionId,
                        ConceptKey = conceptKey,
                        QuizAttemptId = attempt.Id,
                        SourceEvidenceBundleId = request.SourceEvidenceBundleId,
                        TraceType = impact.MisconceptionSignal == null ? "repair_note" : "misconception_note",
                        Title = impact.Result is "blank" ? "Bos cevap telafisi" : "Yanlis cevap telafisi",
                        SafeContent = content,
                        SourceBasis = impact.MisconceptionSignal == null ? "assessment_verified" : "assessment_signal",
                        MisconceptionKey = impact.MisconceptionSignal?.Category,
                        CreatedBy = "quiz_attempt",
                        Visibility = "highlighted"
                    }, ct);
                }

                var blockId = repairBlock?.Id ?? resultBlock?.Id;
                if (blockId.HasValue)
                {
                    attempt.SourceRefsJson = MergeWikiBlockMetadata(attempt.SourceRefsJson, blockId.Value);
                    await _db.SaveChangesAsync(ct);
                }

                return;
            }

            if (_wikiService == null) return;

            var pageQuery = _db.WikiPages
                .AsNoTracking()
                .Where(p => p.UserId == userId && p.TopicId == request.TopicId.Value && !p.IsDeleted);
            var page = !string.IsNullOrWhiteSpace(conceptKey)
                ? await pageQuery
                    .OrderByDescending(p => p.ConceptKey == conceptKey)
                    .ThenBy(p => p.OrderIndex)
                    .FirstOrDefaultAsync(ct)
                : await pageQuery.OrderBy(p => p.OrderIndex).FirstOrDefaultAsync(ct);
            if (page == null) return;

            var blockType = impact.Result is "wrong" or "blank" or "partial" ? "repair_note" : "quiz_review";
            var block = await _wikiService.AddWikiBlockAsync(page.Id, userId, new CreateWikiBlockRequestDto
            {
                BlockType = blockType,
                Title = impact.Result is "wrong" or "blank" ? "Quiz sonrasi pekistirme" : "Quiz sonucu",
                Content = content,
                Source = "quiz_attempt",
                SourceBasis = sourceBasis,
                ConceptKey = conceptKey,
                MisconceptionKey = impact.MisconceptionSignal?.Category,
                QuizAttemptId = attempt.Id,
                SourceEvidenceBundleId = request.SourceEvidenceBundleId,
                Visibility = impact.Result is "wrong" or "blank" ? "highlighted" : "normal"
            });

            if (block != null)
            {
                attempt.SourceRefsJson = MergeWikiBlockMetadata(attempt.SourceRefsJson, block.Id);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch
        {
            // Wiki capture is useful learning context, but quiz recording must remain authoritative even if Wiki is unavailable.
        }
    }

    private static string BuildWikiQuizImpactContent(RecordQuizAttemptRequest request, QuizResultLearningImpactDto impact)
    {
        var lines = new List<string>
        {
            $"Quiz modu: {impact.AssessmentMode}",
            $"Sonuc: {impact.Result}",
            $"Kavram: {FirstNonEmpty(impact.TargetConceptKey, request.ConceptKey, request.ConceptTag, request.SkillTag) ?? "belirsiz"}",
            $"Remediation ihtiyaci: {impact.RemediationNeed}",
            $"Tutor sonraki hamlesi: {impact.NextTutorMove}",
            $"Plan aksiyonu: {impact.NextPlanAction}"
        };
        if (impact.MisconceptionSignal != null)
        {
            lines.Add($"Takilma sinyali: {FirstNonEmpty(impact.MisconceptionSignal.UserSafeLabel, impact.MisconceptionSignal.SafeHint, impact.MisconceptionSignal.Category) ?? "dusuk guvenli sinyal"}");
            lines.Add($"Sinyal guveni: {impact.MisconceptionConfidence}");
        }
        if (impact.RemediationLesson != null)
        {
            lines.Add($"Telafi tipi: {impact.RemediationLesson.RepairType}");
            lines.Add($"Telafi ozeti: {impact.RemediationLesson.StudentVisibleSummary}");
            lines.Add($"Telafi checkpoint: {impact.RemediationLesson.Checkpoint.UserSafePrompt}");
            lines.Add($"Telafi sonraki aksiyon: {impact.RemediationLesson.Outcome.NextTutorAction}");
        }
        if (!string.IsNullOrWhiteSpace(impact.WikiReviewHint)) lines.Add($"Wiki tekrar ipucu: {impact.WikiReviewHint}");
        if (!string.IsNullOrWhiteSpace(impact.SourceReadiness) && impact.SourceReadiness != "unknown") lines.Add($"Kaynak hazirligi: {impact.SourceReadiness}");
        return string.Join("\n", lines);
    }

    private static string MergeWikiBlockMetadata(string? json, Guid blockId)
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
                metadata["sourceRefsParseStatus"] = "invalid_json_ignored";
            }
        }

        metadata["wikiBlockId"] = blockId.ToString("D");
        metadata["wikiCaptureStatus"] = "captured";
        return JsonSerializer.Serialize(metadata);
    }

    private static string NormalizeAssessmentMode(params string?[] values)
    {
        var value = FirstNonEmpty(values)?.Trim().ToLowerInvariant() ?? "diagnostic_check";
        return value switch
        {
            "micro_quiz" or "micro" => "micro_quiz",
            "misconception_probe" or "misconception" => "misconception_probe",
            "retrieval_practice" or "retrieval" => "retrieval_practice",
            "readiness_check" or "readiness" => "readiness_check",
            "review_check" or "review" => "review_check",
            "adaptive" => "retrieval_practice",
            _ => "diagnostic_check"
        };
    }

    private static string? ExtractSourceReadiness(string? sourceRefsJson)
    {
        if (string.IsNullOrWhiteSpace(sourceRefsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(sourceRefsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (doc.RootElement.TryGetProperty("sourceReadiness", out var readiness) && readiness.ValueKind == JsonValueKind.String)
                return readiness.GetString();
            if (doc.RootElement.TryGetProperty("evidenceStatus", out var status) && status.ValueKind == JsonValueKind.String)
                return status.GetString();
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractClientSafeSourceReadiness(string? sourceRefsJson)
    {
        var readiness = ExtractSourceReadiness(sourceRefsJson);
        return readiness?.Trim().ToLowerInvariant() switch
        {
            "source_limited" or "limited" => "source_limited",
            "no_sources" or "none" => "no_sources",
            "unknown" => "unknown",
            _ => null
        };
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record AttemptVerification(
        bool IsVerified,
        bool IsCorrect,
        string ReasonCode,
        string ConfidenceStatus,
        string? SafeExplanation,
        Guid? AssessmentItemId,
        bool? ClientClaimedCorrect)
    {
        public static AttemptVerification Unverified(string reasonCode, bool? clientClaimedCorrect, Guid? assessmentItemId = null) =>
            new(false, false, reasonCode, "observed_only", null, assessmentItemId, clientClaimedCorrect);
    }
}

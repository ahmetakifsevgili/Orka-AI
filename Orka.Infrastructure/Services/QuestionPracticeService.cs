using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class QuestionPracticeService : IQuestionPracticeService
{
    private static readonly HashSet<string> PracticeReadyStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "published",
        "diagnostic_ready"
    };

    private static readonly HashSet<string> PracticeReadyVisualStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "not_required",
        "ready",
        "validated"
    };

    private readonly OrkaDbContext _db;
    private readonly IQuizAttemptRecorder _quizAttemptRecorder;

    public QuestionPracticeService(OrkaDbContext db, IQuizAttemptRecorder quizAttemptRecorder)
    {
        _db = db;
        _quizAttemptRecorder = quizAttemptRecorder;
    }

    public async Task<QuestionPracticeSessionDto> StartAsync(
        Guid userId,
        QuestionPracticeStartRequestDto request,
        CancellationToken ct = default)
    {
        var practiceSetId = Guid.NewGuid();
        var count = Math.Clamp(request.Count <= 0 ? 8 : request.Count, 1, 25);
        var conceptKeys = NormalizeConceptKeys(request.ConceptKeys);
        var learningConceptIds = NormalizeIds(request.LearningConceptIds);
        var assessmentItemIds = NormalizeIds(request.AssessmentItemIds);
        var questionBankSource = NormalizeOptional(request.QuestionBankSource);
        var mode = NormalizeMode(request.Mode);

        var query = PracticeVisibleQuestions(userId);

        if (request.TopicId is { } topicId)
        {
            var topicScopeIds = await BuildTopicScopeIdsAsync(userId, topicId, ct);
            query = query.Where(q => q.LearningTopicId.HasValue && topicScopeIds.Contains(q.LearningTopicId.Value));
        }

        if (assessmentItemIds.Count > 0)
        {
            query = query.Where(q => q.AssessmentItemId.HasValue && assessmentItemIds.Contains(q.AssessmentItemId.Value));
        }

        if (request.ConceptGraphSnapshotId is { } snapshotId)
        {
            query = query.Where(q => q.ConceptGraphSnapshotId == snapshotId);
        }

        if (learningConceptIds.Count > 0)
        {
            query = query.Where(q => q.LearningConceptId.HasValue && learningConceptIds.Contains(q.LearningConceptId.Value));
        }

        if (request.PlanRequestId is { } planRequestId)
        {
            query = query.Where(q => q.PlanRequestId == planRequestId);
        }

        if (request.QuizRunId is { } quizRunId)
        {
            query = query.Where(q => q.QuizRunId == quizRunId);
        }

        if (!string.IsNullOrWhiteSpace(questionBankSource))
        {
            query = query.Where(q => q.QuestionBankSource == questionBankSource);
        }

        if (conceptKeys.Count > 0)
        {
            query = query.Where(q => q.ConceptKey != null && conceptKeys.Contains(q.ConceptKey));
        }

        var questions = await query
            .OrderByDescending(q => q.QuestionBankSource == "diagnostic_assessment_item")
            .ThenByDescending(q => q.UpdatedAt)
            .ThenBy(q => q.Id)
            .Take(count)
            .ToListAsync(ct);

        if (questions.Count == 0)
        {
            return new QuestionPracticeSessionDto
            {
                PracticeSetId = practiceSetId,
                Status = "empty",
                EmptyState = "No practice-ready questions matched this topic/concept scope.",
                TopicId = request.TopicId,
                SessionId = request.SessionId,
                Mode = mode,
                ConceptKeys = conceptKeys,
                TotalQuestions = 0
            };
        }

        return new QuestionPracticeSessionDto
        {
            PracticeSetId = practiceSetId,
            Status = "ready",
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            Mode = mode,
            ConceptKeys = conceptKeys,
            TotalQuestions = questions.Count,
            Questions = questions.Select(ToPracticeQuestionDto).ToList()
        };
    }

    public async Task<QuestionPracticeSubmitResponseDto> SubmitAsync(
        Guid userId,
        QuestionPracticeSubmitRequestDto request,
        CancellationToken ct = default)
    {
        if (request.Answers.Count == 0)
        {
            throw new ArgumentException("At least one practice answer is required.");
        }

        var practiceSetId = request.PracticeSetId.GetValueOrDefault(Guid.NewGuid());
        var answers = request.Answers
            .Where(a => a.QuestionItemId != Guid.Empty)
            .GroupBy(a => a.QuestionItemId)
            .Select(g => g.Last())
            .ToList();

        if (answers.Count == 0)
        {
            throw new ArgumentException("At least one valid practice question id is required.");
        }

        var requestedIds = answers.Select(a => a.QuestionItemId).ToHashSet();
        var questions = await PracticeVisibleQuestions(userId)
            .Where(q => requestedIds.Contains(q.Id))
            .ToListAsync(ct);

        if (questions.Count != requestedIds.Count)
        {
            throw new ArgumentException("Submitted practice question is not visible or practice-ready.");
        }

        if (request.TopicId is { } topicId)
        {
            var topicScopeIds = await BuildTopicScopeIdsAsync(userId, topicId, ct);
            if (questions.Any(q => !q.LearningTopicId.HasValue || !topicScopeIds.Contains(q.LearningTopicId.Value)))
            {
                throw new ArgumentException("Submitted practice question is outside the requested topic scope.");
            }
        }

        var questionsById = questions.ToDictionary(q => q.Id);
        var results = new List<QuestionPracticeResultDto>();
        var impacts = new List<QuizResultLearningImpactDto>();

        foreach (var answer in answers)
        {
            var question = questionsById[answer.QuestionItemId];
            var selected = NormalizeOptionKey(answer.WasSkipped ? "skip" : answer.SelectedOptionKey);
            var isBlank = answer.WasSkipped || string.IsNullOrWhiteSpace(selected) || selected == "skip";
            if (!isBlank && !question.Options.Any(o => NormalizeOptionKey(o.OptionKey) == selected))
            {
                throw new ArgumentException("Selected option is not valid for this practice question.");
            }

            var correctOption = question.Options.FirstOrDefault(o => o.IsCorrect);
            var isCorrect = !isBlank &&
                            correctOption != null &&
                            string.Equals(NormalizeOptionKey(correctOption.OptionKey), selected, StringComparison.OrdinalIgnoreCase);

            var recorderResult = await _quizAttemptRecorder.RecordAsync(
                userId,
                BuildRecordRequest(question, request, answer, selected, isBlank, isCorrect),
                ct);

            if (recorderResult.LearningImpact != null)
            {
                impacts.Add(recorderResult.LearningImpact);
            }

            results.Add(new QuestionPracticeResultDto
            {
                QuestionItemId = question.Id,
                AssessmentItemId = question.AssessmentItemId,
                ConceptKey = question.ConceptKey,
                SelectedOptionKey = selected ?? string.Empty,
                IsBlank = isBlank,
                IsCorrect = recorderResult.Attempt.IsCorrect,
                Explanation = SafeExplanation(question),
                LearningImpact = recorderResult.LearningImpact
            });
        }

        return new QuestionPracticeSubmitResponseDto
        {
            PracticeSetId = practiceSetId,
            Status = "submitted",
            TotalQuestions = results.Count,
            AnsweredCount = results.Count(r => !r.IsBlank),
            CorrectCount = results.Count(r => r.IsCorrect),
            WrongCount = results.Count(r => !r.IsCorrect && !r.IsBlank),
            BlankCount = results.Count(r => r.IsBlank),
            Results = results,
            LearningImpacts = impacts
        };
    }

    private IQueryable<QuestionItem> PracticeVisibleQuestions(Guid userId) =>
        _db.QuestionItems
            .AsNoTracking()
            .Include(q => q.Options)
            .ThenInclude(o => o.ContentBlocks.Where(b => !b.IsDeleted))
            .ThenInclude(b => b.Asset)
            .Include(q => q.ContentBlocks.Where(b => !b.IsDeleted))
            .ThenInclude(b => b.Asset)
            .Include(q => q.StimulusLinks)
            .ThenInclude(l => l.QuestionStimulus)
            .Where(q => !q.IsDeleted
                        && (q.OwnerUserId == null || q.OwnerUserId == userId)
                        && PracticeReadyStatuses.Contains(q.QualityStatus)
                        && q.QuestionType == "multiple_choice"
                        && q.AssessmentItemId.HasValue
                        && q.ConceptGraphSnapshotId.HasValue
                        && q.LearningConceptId.HasValue
                        && q.ConceptKey != null
                        && PracticeReadyVisualStatuses.Contains(q.VisualReadinessStatus));

    private async Task<HashSet<Guid>> BuildTopicScopeIdsAsync(Guid userId, Guid topicId, CancellationToken ct)
    {
        var rows = await _db.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new TopicScopeRow(t.Id, t.ParentTopicId))
            .ToListAsync(ct);

        var scope = new HashSet<Guid> { topicId };
        var byId = rows.ToDictionary(t => t.Id);
        if (!byId.TryGetValue(topicId, out var selected))
        {
            return scope;
        }

        var current = selected;
        while (current.ParentTopicId is { } parentId && byId.TryGetValue(parentId, out var parent))
        {
            if (!scope.Add(parent.Id))
            {
                break;
            }

            current = parent;
        }

        var childrenByParent = rows
            .Where(t => t.ParentTopicId.HasValue)
            .GroupBy(t => t.ParentTopicId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Id).ToList());

        var queue = new Queue<Guid>();
        queue.Enqueue(topicId);
        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();
            if (!childrenByParent.TryGetValue(parentId, out var childIds))
            {
                continue;
            }

            foreach (var childId in childIds)
            {
                if (scope.Add(childId))
                {
                    queue.Enqueue(childId);
                }
            }
        }

        return scope;
    }

    private static RecordQuizAttemptRequest BuildRecordRequest(
        QuestionItem question,
        QuestionPracticeSubmitRequestDto request,
        QuestionPracticeAnswerDto answer,
        string? selected,
        bool isBlank,
        bool isCorrect)
    {
        var topicId = question.LearningTopicId ?? request.TopicId;
        var concept = FirstNonEmpty(question.ConceptKey, question.ConceptLabel, question.CognitiveSkill);

        return new RecordQuizAttemptRequest
        {
            QuizRunId = question.QuizRunId,
            TopicId = topicId,
            SessionId = request.SessionId,
            QuestionId = question.AssessmentItemId?.ToString("D") ?? question.Id.ToString("D"),
            Question = question.Stem,
            SelectedOptionId = isBlank ? "skip" : selected,
            IsCorrect = question.AssessmentItemId.HasValue ? null : isCorrect,
            Explanation = SafeExplanation(question),
            SkillTag = concept,
            AssessmentItemId = question.AssessmentItemId,
            ConceptKey = question.ConceptKey,
            ConceptTag = concept,
            CognitiveSkill = question.CognitiveSkill,
            MisconceptionTarget = question.MisconceptionTarget,
            EvidenceExpected = question.EvidenceExpected,
            ScoringRule = question.ScoringRuleJson,
            LearningObjective = question.ConceptLabel,
            QuestionType = question.QuestionType,
            AssessmentMode = NormalizeMode(request.Mode),
            Difficulty = question.Difficulty,
            ResponseTimeMs = answer.ResponseTimeMs.HasValue ? Math.Max(0, answer.ResponseTimeMs.Value) : null,
            WasSkipped = isBlank,
            ConfidenceSelfRating = answer.ConfidenceSelfRating.HasValue
                ? Math.Clamp(answer.ConfidenceSelfRating.Value, 0m, 1m)
                : null
        };
    }

    private static QuestionPracticeQuestionDto ToPracticeQuestionDto(QuestionItem question) => new()
    {
        QuestionItemId = question.Id,
        AssessmentItemId = question.AssessmentItemId,
        ConceptGraphSnapshotId = question.ConceptGraphSnapshotId,
        LearningConceptId = question.LearningConceptId,
        QuizRunId = question.QuizRunId,
        PlanRequestId = question.PlanRequestId,
        TopicId = question.LearningTopicId,
        ConceptKey = question.ConceptKey,
        ConceptLabel = question.ConceptLabel,
        QuestionBankSource = question.QuestionBankSource,
        QuestionType = question.QuestionType,
        Stem = question.Stem,
        Difficulty = question.Difficulty,
        CognitiveSkill = question.CognitiveSkill,
        MisconceptionTarget = question.MisconceptionTarget,
        EvidenceExpected = question.EvidenceExpected,
        VisualReadinessStatus = question.VisualReadinessStatus,
        Options = question.Options
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.OptionKey)
            .Select(ToPracticeOptionDto)
            .ToList(),
        ContentBlocks = question.ContentBlocks
            .Where(b => !b.IsDeleted)
            .OrderBy(b => b.SortOrder)
            .Select(ToPracticeContentBlockDto)
            .ToList(),
        Stimuli = question.StimulusLinks
            .Where(l => !l.QuestionStimulus.IsDeleted)
            .OrderBy(l => l.SortOrder)
            .Select(l => ToPracticeStimulusDto(l.QuestionStimulus, l.SortOrder))
            .ToList()
    };

    private static QuestionOptionDto ToPracticeOptionDto(QuestionOption option) => new()
    {
        Id = option.Id,
        OptionKey = option.OptionKey,
        Text = option.Text,
        IsCorrect = false,
        SortOrder = option.SortOrder,
        ContentBlocks = option.ContentBlocks
            .Where(b => !b.IsDeleted)
            .OrderBy(b => b.SortOrder)
            .Select(ToPracticeOptionContentBlockDto)
            .ToList()
    };

    private static QuestionContentBlockDto ToPracticeContentBlockDto(QuestionContentBlock block) => new()
    {
        Id = block.Id,
        BlockType = block.BlockType,
        Text = block.Text,
        ContentJson = LearnerSafeContentJson.Sanitize(block.ContentJson),
        AssetId = block.AssetId,
        Asset = block.Asset is null || block.Asset.IsDeleted ? null : ToPracticeAssetDto(block.Asset),
        SortOrder = block.SortOrder,
        AltText = block.AltText,
        Caption = block.Caption,
        LongDescription = block.LongDescription
    };

    private static QuestionOptionContentBlockDto ToPracticeOptionContentBlockDto(QuestionOptionContentBlock block) => new()
    {
        Id = block.Id,
        BlockType = block.BlockType,
        Text = block.Text,
        ContentJson = LearnerSafeContentJson.Sanitize(block.ContentJson),
        AssetId = block.AssetId,
        Asset = block.Asset is null || block.Asset.IsDeleted ? null : ToPracticeAssetDto(block.Asset),
        SortOrder = block.SortOrder,
        AltText = block.AltText,
        Caption = block.Caption
    };

    private static QuestionAssetDto ToPracticeAssetDto(QuestionAsset asset) => new()
    {
        Id = asset.Id,
        OwnershipState = asset.OwnerUserId is null ? "system" : "user",
        AssetType = asset.AssetType,
        StorageKey = asset.StorageKey,
        FileName = asset.FileName,
        MimeType = asset.MimeType,
        SizeBytes = asset.SizeBytes,
        Sha256Hash = asset.Sha256Hash,
        SourceRegistryItemId = asset.SourceRegistryItemId,
        SourceTitle = asset.SourceTitle,
        SourceUrl = asset.SourceUrl,
        LicenseStatus = asset.LicenseStatus,
        VerificationStatus = asset.VerificationStatus,
        AltText = asset.AltText,
        Caption = asset.Caption,
        LongDescription = asset.LongDescription,
        GenerationProvider = asset.GenerationProvider,
        GenerationModel = asset.GenerationModel,
        RenderStrategy = asset.RenderStrategy,
        GenerationPromptHash = asset.GenerationPromptHash,
        ValidationReportJson = LearnerSafeContentJson.Sanitize(asset.ValidationReportJson),
        VisualReadinessStatus = asset.VisualReadinessStatus,
        CreatedAt = asset.CreatedAt,
        UpdatedAt = asset.UpdatedAt
    };

    private static QuestionStimulusDto ToPracticeStimulusDto(QuestionStimulus stimulus, int sortOrder) => new()
    {
        Id = stimulus.Id,
        OwnershipState = stimulus.OwnerUserId is null ? "system" : "user",
        Title = stimulus.Title,
        StimulusType = stimulus.StimulusType,
        ContentText = stimulus.ContentText,
        ContentJson = LearnerSafeContentJson.Sanitize(stimulus.ContentJson),
        SourceRegistryItemId = stimulus.SourceRegistryItemId,
        CurriculumNodeId = stimulus.CurriculumNodeId,
        VerificationStatus = stimulus.VerificationStatus,
        LicenseStatus = stimulus.LicenseStatus,
        SortOrder = sortOrder,
        CreatedAt = stimulus.CreatedAt,
        UpdatedAt = stimulus.UpdatedAt
    };

    private static List<string> NormalizeConceptKeys(IEnumerable<string>? conceptKeys) =>
        conceptKeys?
            .Select(key => key?.Trim())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .Select(key => key!)
            .ToList() ?? [];

    private static HashSet<Guid> NormalizeIds(IEnumerable<Guid>? ids) =>
        ids?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(50)
            .ToHashSet() ?? [];

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeMode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "weak_concept_drill" : value.Trim().ToLowerInvariant();

    private static string? NormalizeOptionKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string SafeExplanation(QuestionItem question) =>
        string.IsNullOrWhiteSpace(question.Explanation) ? string.Empty : question.Explanation.Trim();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record TopicScopeRow(Guid Id, Guid? ParentTopicId);
}

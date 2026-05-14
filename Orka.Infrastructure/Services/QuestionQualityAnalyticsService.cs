using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class QuestionQualityAnalyticsService : IQuestionQualityAnalyticsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IExamFrameworkService _examFramework;

    public QuestionQualityAnalyticsService(OrkaDbContext db, IExamFrameworkService examFramework)
    {
        _db = db;
        _examFramework = examFramework;
    }

    public async Task<RecalculateQuestionAnalyticsResultDto?> RecalculateQuestionAnalyticsAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default)
    {
        var question = await VisibleQuestions(userId)
            .Include(q => q.Options)
            .FirstOrDefaultAsync(q => q.Id == questionId, ct);
        if (question is null)
        {
            return null;
        }

        var practiceAnswers = await _db.CentralExamPracticeAnswers
            .AsNoTracking()
            .Include(a => a.PracticeAttempt)
            .Where(a => a.QuestionItemId == question.Id
                        && a.PracticeAttempt.Status == "submitted"
                        && !a.PracticeAttempt.IsDeleted)
            .Select(a => new AnswerFact(a.SelectedOptionKey, a.CorrectOptionKey, a.IsCorrect, a.IsBlank))
            .ToListAsync(ct);

        var denemeAnswers = await _db.CentralExamDenemeAnswers
            .AsNoTracking()
            .Include(a => a.DenemeAttempt)
            .Where(a => a.QuestionItemId == question.Id
                        && a.DenemeAttempt.Status == "submitted"
                        && !a.DenemeAttempt.IsDeleted)
            .Select(a => new AnswerFact(a.SelectedOptionKey, a.CorrectOptionKey, a.IsCorrect, a.IsBlank))
            .ToListAsync(ct);

        var facts = practiceAnswers.Concat(denemeAnswers).ToList();
        var now = DateTime.UtcNow;
        var attemptCount = facts.Count;
        var answered = facts.Count(a => !a.IsBlank);
        var correct = facts.Count(a => a.IsCorrect);
        var wrong = facts.Count(a => !a.IsCorrect && !a.IsBlank);
        var blank = facts.Count(a => a.IsBlank);
        var correctnessRate = answered == 0 ? 0 : Math.Round(correct / (decimal)answered, 4);
        var blankRate = attemptCount == 0 ? 0 : Math.Round(blank / (decimal)attemptCount, 4);
        var sampleSize = SampleSizeStatus(attemptCount);
        var difficulty = DifficultyEstimate(answered, correctnessRate);
        var optionDtos = BuildOptionSnapshots(question, facts, attemptCount, sampleSize, now);
        var qualitySignal = QualitySignal(attemptCount, correctnessRate, blankRate, optionDtos);

        var existing = await _db.QuestionItemAnalyticsSnapshots
            .Include(s => s.OptionSnapshots)
            .FirstOrDefaultAsync(s => s.QuestionItemId == question.Id && !s.IsDeleted, ct);
        if (existing is null)
        {
            existing = new QuestionItemAnalyticsSnapshot
            {
                Id = Guid.NewGuid(),
                QuestionItemId = question.Id,
                CreatedAt = now
            };
            _db.QuestionItemAnalyticsSnapshots.Add(existing);
        }

        existing.ExamDefinitionId = question.ExamDefinitionId;
        existing.ExamVariantId = question.ExamVariantId;
        existing.ExamSectionId = question.ExamSectionId;
        existing.ExamSubjectId = question.ExamSubjectId;
        existing.ExamTopicId = question.ExamTopicId;
        existing.ExamOutcomeId = question.ExamOutcomeId;
        existing.AttemptCount = attemptCount;
        existing.AnsweredCount = answered;
        existing.CorrectCount = correct;
        existing.WrongCount = wrong;
        existing.BlankCount = blank;
        existing.CorrectnessRate = correctnessRate;
        existing.BlankRate = blankRate;
        existing.DifficultyEstimate = difficulty;
        existing.DiscriminationStatus = DiscriminationStatus(sampleSize, optionDtos);
        existing.QualitySignal = qualitySignal;
        existing.SampleSizeStatus = sampleSize;
        existing.LastCalculatedAt = now;
        existing.UpdatedAt = now;

        foreach (var old in existing.OptionSnapshots)
        {
            old.IsDeleted = true;
            old.UpdatedAt = now;
        }

        existing.OptionSnapshots = optionDtos.Select(o => new QuestionOptionAnalyticsSnapshot
        {
            Id = Guid.NewGuid(),
            QuestionItemId = question.Id,
            OptionKey = o.OptionKey,
            SelectionCount = o.SelectionCount,
            CorrectSelectionCount = o.CorrectSelectionCount,
            WrongSelectionCount = o.WrongSelectionCount,
            SelectionRate = o.SelectionRate,
            IsCorrectOption = o.IsCorrectOption,
            DistractorSignal = o.DistractorSignal,
            LastCalculatedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        await ReplaceReviewSignalsAsync(question.Id, attemptCount, correctnessRate, blankRate, optionDtos, now, ct);
        await _db.SaveChangesAsync(ct);

        var dto = await GetQuestionAnalyticsAsync(userId, question.Id, ct);
        return new RecalculateQuestionAnalyticsResultDto
        {
            QuestionItemId = question.Id,
            Recalculated = true,
            Analytics = dto
        };
    }

    public async Task<RecalculateExamAnalyticsResultDto> RecalculateCentralExamAnalyticsAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var questions = await VisibleExamQuestions(userId, examCode, variantCode)
            .Select(q => q.Id)
            .ToListAsync(ct);

        var count = 0;
        foreach (var questionId in questions)
        {
            if (await RecalculateQuestionAnalyticsAsync(userId, questionId, ct) is not null)
            {
                count++;
            }
        }

        return new RecalculateExamAnalyticsResultDto
        {
            ExamCode = NormalizeCode(examCode),
            VariantCode = NormalizeCodeOrNull(variantCode),
            RecalculatedQuestionCount = count,
            CalculatedAt = DateTime.UtcNow
        };
    }

    public async Task<QuestionItemAnalyticsDto?> GetQuestionAnalyticsAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default)
    {
        if (!await VisibleQuestions(userId).AnyAsync(q => q.Id == questionId, ct))
        {
            return null;
        }

        var snapshot = await _db.QuestionItemAnalyticsSnapshots
            .AsNoTracking()
            .Include(s => s.OptionSnapshots.Where(o => !o.IsDeleted))
            .FirstOrDefaultAsync(s => s.QuestionItemId == questionId && !s.IsDeleted, ct);
        if (snapshot is null)
        {
            return null;
        }

        var signals = await GetQuestionQualitySignalsAsync(userId, questionId, ct);
        return ToDto(snapshot, signals);
    }

    public async Task<IReadOnlyList<QuestionQualityReviewSignalDto>> GetQuestionQualitySignalsAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default)
    {
        if (!await VisibleQuestions(userId).AnyAsync(q => q.Id == questionId, ct))
        {
            return [];
        }

        return await _db.QuestionQualityReviewSignals
            .AsNoTracking()
            .Where(s => s.QuestionItemId == questionId && !s.IsDeleted && s.ResolvedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new QuestionQualityReviewSignalDto
            {
                Id = s.Id,
                QuestionItemId = s.QuestionItemId,
                SignalType = s.SignalType,
                Severity = s.Severity,
                Message = s.Message,
                CreatedAt = s.CreatedAt,
                ResolvedAt = s.ResolvedAt
            })
            .ToListAsync(ct);
    }

    public async Task<CentralExamQualityOverviewDto?> GetCentralExamQualityOverviewAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var definition = await FindExamDefinitionAsync(userId, examCode, ct);
        if (definition is null)
        {
            return null;
        }

        var coverage = await BuildCoverageAsync(userId, definition, variantCode, ct);
        var questionIds = await VisibleExamQuestions(userId, examCode, variantCode).Select(q => q.Id).ToListAsync(ct);
        var published = await VisibleExamQuestions(userId, examCode, variantCode).CountAsync(q => q.QualityStatus == "published", ct);
        var snapshotCount = await _db.QuestionItemAnalyticsSnapshots.CountAsync(s => questionIds.Contains(s.QuestionItemId) && !s.IsDeleted, ct);
        var signalCount = await _db.QuestionQualityReviewSignals.CountAsync(s => questionIds.Contains(s.QuestionItemId) && !s.IsDeleted && s.ResolvedAt == null && s.Severity != "info", ct);

        return new CentralExamQualityOverviewDto
        {
            ExamCode = definition.Code,
            VariantCode = NormalizeCodeOrNull(variantCode),
            VisibleQuestionCount = questionIds.Count,
            PublishedQuestionCount = published,
            AnalyticsSnapshotCount = snapshotCount,
            NeedsReviewSignalCount = signalCount,
            LowCoverageTopicCount = coverage.Count(c => c.CoverageStatus is "no_content" or "low_content"),
            Topics = coverage
        };
    }

    public async Task<CentralExamBlueprintCoverageDto?> GetBlueprintCoverageAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var definition = await FindExamDefinitionAsync(userId, examCode, ct);
        if (definition is null)
        {
            return null;
        }

        var topics = await BuildCoverageAsync(userId, definition, variantCode, ct);
        return new CentralExamBlueprintCoverageDto
        {
            ExamCode = definition.Code,
            VariantCode = NormalizeCodeOrNull(variantCode),
            TopicCount = topics.Count,
            NoContentCount = topics.Count(t => t.CoverageStatus == "no_content"),
            LowContentCount = topics.Count(t => t.CoverageStatus == "low_content"),
            UsableCount = topics.Count(t => t.CoverageStatus == "usable"),
            StrongCount = topics.Count(t => t.CoverageStatus == "strong"),
            Topics = topics
        };
    }

    private async Task<List<CentralExamQualityTopicCoverageDto>> BuildCoverageAsync(
        Guid userId,
        ExamDefinition definition,
        string? variantCode,
        CancellationToken ct)
    {
        var variant = NormalizeCodeOrNull(variantCode);
        var topics = await _db.ExamTopics
            .AsNoTracking()
            .Include(t => t.Outcomes)
            .Include(t => t.ExamSubject)
            .ThenInclude(s => s.ExamSection)
            .ThenInclude(s => s.ExamVariant)
            .Where(t => !t.IsDeleted
                        && !t.ExamSubject.IsDeleted
                        && !t.ExamSubject.ExamSection.IsDeleted
                        && !t.ExamSubject.ExamSection.ExamVariant.IsDeleted
                        && t.ExamSubject.ExamSection.ExamVariant.ExamDefinitionId == definition.Id
                        && (variant == null || t.ExamSubject.ExamSection.ExamVariant.Code == variant))
            .OrderBy(t => t.ExamSubject.Code)
            .ThenBy(t => t.Code)
            .ToListAsync(ct);

        var result = new List<CentralExamQualityTopicCoverageDto>();
        foreach (var topic in topics)
        {
            var scoped = VisibleQuestions(userId)
                .Where(q => q.ExamDefinitionId == definition.Id
                            && (q.ExamTopicId == topic.Id || q.ExamOutcomeId != null && topic.Outcomes.Select(o => o.Id).Contains(q.ExamOutcomeId.Value)));

            var published = await scoped.CountAsync(q => q.QualityStatus == "published", ct);
            var draft = await scoped.CountAsync(q => q.OwnerUserId == userId && q.QualityStatus == "draft", ct);
            var needsReview = await scoped.CountAsync(q => q.OwnerUserId == userId && q.QualityStatus == "needs_review", ct);
            var snapshotDifficulties = await _db.QuestionItemAnalyticsSnapshots
                .AsNoTracking()
                .Where(s => !s.IsDeleted && s.ExamTopicId == topic.Id && s.SampleSizeStatus != "none")
                .Select(s => s.DifficultyEstimate)
                .ToListAsync(ct);

            result.Add(new CentralExamQualityTopicCoverageDto
            {
                ExamSubjectId = topic.ExamSubjectId,
                SubjectCode = topic.ExamSubject.Code,
                ExamTopicId = topic.Id,
                TopicCode = topic.Code,
                PublishedQuestionCount = published,
                PracticeReadyCount = published,
                CallerDraftCount = draft,
                CallerNeedsReviewCount = needsReview,
                CoverageStatus = CoverageStatus(published),
                AverageDifficultyEstimate = snapshotDifficulties.Count == 0 ? null : MostCommon(snapshotDifficulties)
            });
        }

        return result;
    }

    private IQueryable<QuestionItem> VisibleExamQuestions(Guid userId, string examCode, string? variantCode)
    {
        var code = NormalizeCode(examCode);
        var variant = NormalizeCodeOrNull(variantCode);
        return VisibleQuestions(userId)
            .Where(q => q.ExamDefinition.Code == code
                        && (variant == null || q.ExamVariant != null && q.ExamVariant.Code == variant));
    }

    private IQueryable<QuestionItem> VisibleQuestions(Guid userId) =>
        _db.QuestionItems
            .AsNoTracking()
            .Where(q => !q.IsDeleted && (q.OwnerUserId == null || q.OwnerUserId == userId));

    private async Task<ExamDefinition?> FindExamDefinitionAsync(Guid userId, string examCode, CancellationToken ct)
    {
        var code = NormalizeCode(examCode);
        await _examFramework.CreateSystemSkeletonAsync(code, ct);
        return await _db.ExamDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => !e.IsDeleted && e.Code == code && (e.OwnerUserId == null || e.OwnerUserId == userId), ct);
    }

    private static List<QuestionOptionAnalyticsDto> BuildOptionSnapshots(
        QuestionItem question,
        IReadOnlyList<AnswerFact> facts,
        int attemptCount,
        string sampleSize,
        DateTime now)
    {
        var selections = facts
            .Where(a => !string.IsNullOrWhiteSpace(a.SelectedOptionKey))
            .GroupBy(a => NormalizeOptionKey(a.SelectedOptionKey))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var correctSelectionCount = facts.Count(a => !a.IsBlank && a.IsCorrect);

        return question.Options
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.OptionKey)
            .Select(option =>
            {
                var key = NormalizeOptionKey(option.OptionKey);
                var selected = selections.GetValueOrDefault(key) ?? [];
                var selectionCount = selected.Count;
                var dto = new QuestionOptionAnalyticsDto
                {
                    OptionKey = option.OptionKey,
                    SelectionCount = selectionCount,
                    CorrectSelectionCount = option.IsCorrect ? selectionCount : 0,
                    WrongSelectionCount = option.IsCorrect ? 0 : selectionCount,
                    SelectionRate = attemptCount == 0 ? 0 : Math.Round(selectionCount / (decimal)attemptCount, 4),
                    IsCorrectOption = option.IsCorrect,
                    DistractorSignal = DistractorSignal(option.IsCorrect, selectionCount, correctSelectionCount, attemptCount, sampleSize)
                };
                return dto;
            })
            .ToList();
    }

    private async Task ReplaceReviewSignalsAsync(
        Guid questionId,
        int attemptCount,
        decimal correctnessRate,
        decimal blankRate,
        IReadOnlyList<QuestionOptionAnalyticsDto> options,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await _db.QuestionQualityReviewSignals
            .Where(s => s.QuestionItemId == questionId && !s.IsDeleted && s.ResolvedAt == null)
            .ToListAsync(ct);
        foreach (var signal in existing)
        {
            signal.IsDeleted = true;
            signal.ResolvedAt = now;
        }

        var sample = SampleSizeStatus(attemptCount);
        var signals = new List<QuestionQualityReviewSignal>();
        if (sample is "none" or "very_low" or "low")
        {
            signals.Add(Signal(questionId, "low_sample_size", "info", "Analytics sample is still small; treat item calibration as preliminary.", new { attemptCount }, now));
        }

        if (attemptCount >= 5 && blankRate >= 0.35m)
        {
            signals.Add(Signal(questionId, "high_blank_rate", "warning", "Blank rate is high enough to review wording or required prerequisite clarity.", new { attemptCount, blankRate }, now));
        }

        if (attemptCount >= 20 && correctnessRate >= 0.9m)
        {
            signals.Add(Signal(questionId, "too_easy", "warning", "Item may be very easy for this audience; review placement and blueprint fit.", new { attemptCount, correctnessRate }, now));
        }

        if (attemptCount >= 20 && correctnessRate <= 0.25m)
        {
            signals.Add(Signal(questionId, "low_success_rate", "warning", "Item may be too hard or unclear; review explanation, stem, and outcome fit.", new { attemptCount, correctnessRate }, now));
        }

        foreach (var option in options.Where(o => !o.IsCorrectOption && o.DistractorSignal is "unused" or "over_attracting" or "confusing"))
        {
            var type = option.DistractorSignal == "unused" ? "weak_distractor" : "over_attracting_distractor";
            signals.Add(Signal(questionId, type, "warning", $"Option {option.OptionKey} has a distractor quality signal: {option.DistractorSignal}.", new { option.OptionKey, option.SelectionCount, option.SelectionRate }, now));
        }

        _db.QuestionQualityReviewSignals.AddRange(signals);
    }

    private static QuestionQualityReviewSignal Signal(Guid questionId, string type, string severity, string message, object evidence, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        QuestionItemId = questionId,
        SignalType = type,
        Severity = severity,
        Message = message,
        EvidenceJson = JsonSerializer.Serialize(evidence, JsonOptions),
        CreatedAt = now
    };

    private static QuestionItemAnalyticsDto ToDto(QuestionItemAnalyticsSnapshot snapshot, IReadOnlyList<QuestionQualityReviewSignalDto> signals) => new()
    {
        QuestionItemId = snapshot.QuestionItemId,
        ExamDefinitionId = snapshot.ExamDefinitionId,
        ExamVariantId = snapshot.ExamVariantId,
        ExamSectionId = snapshot.ExamSectionId,
        ExamSubjectId = snapshot.ExamSubjectId,
        ExamTopicId = snapshot.ExamTopicId,
        ExamOutcomeId = snapshot.ExamOutcomeId,
        AttemptCount = snapshot.AttemptCount,
        AnsweredCount = snapshot.AnsweredCount,
        CorrectCount = snapshot.CorrectCount,
        WrongCount = snapshot.WrongCount,
        BlankCount = snapshot.BlankCount,
        CorrectnessRate = snapshot.CorrectnessRate,
        BlankRate = snapshot.BlankRate,
        DifficultyEstimate = snapshot.DifficultyEstimate,
        DiscriminationStatus = snapshot.DiscriminationStatus,
        QualitySignal = snapshot.QualitySignal,
        SampleSizeStatus = snapshot.SampleSizeStatus,
        LastCalculatedAt = snapshot.LastCalculatedAt,
        Options = snapshot.OptionSnapshots
            .Where(o => !o.IsDeleted)
            .OrderBy(o => o.OptionKey)
            .Select(o => new QuestionOptionAnalyticsDto
            {
                OptionKey = o.OptionKey,
                SelectionCount = o.SelectionCount,
                CorrectSelectionCount = o.CorrectSelectionCount,
                WrongSelectionCount = o.WrongSelectionCount,
                SelectionRate = o.SelectionRate,
                IsCorrectOption = o.IsCorrectOption,
                DistractorSignal = o.DistractorSignal
            })
            .ToList(),
        ReviewSignals = signals.ToList()
    };

    private static string SampleSizeStatus(int attemptCount) => attemptCount switch
    {
        0 => "none",
        <= 4 => "very_low",
        <= 19 => "low",
        <= 99 => "usable",
        _ => "strong"
    };

    private static string DifficultyEstimate(int answeredCount, decimal correctnessRate)
    {
        if (answeredCount < 5) return "insufficient_data";
        if (correctnessRate >= 0.9m) return "very_easy";
        if (correctnessRate >= 0.75m) return "easy";
        if (correctnessRate >= 0.45m) return "medium";
        if (correctnessRate >= 0.25m) return "hard";
        return "very_hard";
    }

    private static string DistractorSignal(bool isCorrect, int selectionCount, int correctSelectionCount, int attemptCount, string sampleSize)
    {
        if (isCorrect) return "not_available";
        if (sampleSize is not ("usable" or "strong")) return "not_available";
        if (selectionCount == 0 || selectionCount / (decimal)Math.Max(1, attemptCount) <= 0.02m) return "unused";
        if (selectionCount > correctSelectionCount) return "over_attracting";
        if (selectionCount / (decimal)Math.Max(1, attemptCount) >= 0.35m) return "confusing";
        return "plausible";
    }

    private static string QualitySignal(int attemptCount, decimal correctnessRate, decimal blankRate, IReadOnlyList<QuestionOptionAnalyticsDto> options)
    {
        if (attemptCount == 0) return "insufficient_data";
        if (blankRate >= 0.35m && attemptCount >= 5) return "high_blank_rate";
        if (attemptCount >= 20 && correctnessRate >= 0.9m) return "likely_too_easy";
        if (attemptCount >= 20 && correctnessRate <= 0.25m) return "likely_too_hard";
        if (options.Any(o => !o.IsCorrectOption && o.DistractorSignal is "unused" or "over_attracting" or "confusing")) return "distractor_issue";
        return attemptCount < 20 ? "insufficient_data" : "healthy";
    }

    private static string DiscriminationStatus(string sampleSize, IReadOnlyList<QuestionOptionAnalyticsDto> options)
    {
        if (sampleSize == "none") return "not_available";
        if (sampleSize is "very_low" or "low") return "insufficient_data";
        return options.Any(o => !o.IsCorrectOption && o.DistractorSignal is "over_attracting" or "confusing") ? "weak" : "acceptable";
    }

    private static string CoverageStatus(int publishedCount) => publishedCount switch
    {
        0 => "no_content",
        < 5 => "low_content",
        < 20 => "usable",
        _ => "strong"
    };

    private static string MostCommon(IEnumerable<string> values) =>
        values.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;

    private static string NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? NormalizeCodeOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string NormalizeOptionKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private sealed record AnswerFact(string? SelectedOptionKey, string? CorrectOptionKey, bool IsCorrect, bool IsBlank);
}

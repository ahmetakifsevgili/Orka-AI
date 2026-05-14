using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class CentralExamDenemeService : ICentralExamDenemeService
{
    private const string KpssCode = "KPSS";
    private const string BlueprintCode = "KPSS_MINI_TURKCE_PARAGRAF";
    private const string BlueprintName = "KPSS Mini Deneme - Turkce Paragraf";
    private const string GenelYetenekCode = "GENEL_YETENEK";
    private const string TurkceCode = "TURKCE";
    private const string ParagrafCode = "PARAGRAF";
    private const int DefaultQuestionCount = 5;
    private const string InsufficientContent = "Bu mini deneme icin yeterli yayina hazir soru yok. Taslak veya incelemedeki sorular denemeye alinmaz.";
    private const string SafeVerificationLabel = "Mini deneme resmi OSYM simulasyonu degildir; mevcut Orka soru bankasi ve dogrulanabilir kaynak metadata'si ile calisir.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrkaDbContext _db;
    private readonly IExamFrameworkService _examFramework;
    private readonly ILearningSignalService _signals;

    public CentralExamDenemeService(
        OrkaDbContext db,
        IExamFrameworkService examFramework,
        ILearningSignalService signals)
    {
        _db = db;
        _examFramework = examFramework;
        _signals = signals;
    }

    public async Task<IReadOnlyList<CentralExamDenemeBlueprintDto>> GetDenemeBlueprintsAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        CancellationToken ct = default)
    {
        if (!string.Equals(NormalizeCodeOrNull(examCode), KpssCode, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var blueprint = await EnsureKpssBlueprintAsync(ct);
        var dto = await BuildBlueprintDtoAsync(userId, blueprint.Id, variantCode, ct);
        return dto is null ? [] : [dto];
    }

    public async Task<CentralExamDenemeBlueprintDto?> GetDenemeBlueprintAsync(
        Guid userId,
        string blueprintCode,
        string? variantCode = null,
        CancellationToken ct = default)
    {
        if (!string.Equals(NormalizeCodeOrNull(blueprintCode), BlueprintCode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var blueprint = await EnsureKpssBlueprintAsync(ct);
        return await BuildBlueprintDtoAsync(userId, blueprint.Id, variantCode, ct);
    }

    public async Task<CentralExamDenemeSessionDto> StartDenemeAsync(
        Guid userId,
        string blueprintCode,
        CentralExamDenemeStartRequestDto request,
        CancellationToken ct = default)
    {
        if (!string.Equals(NormalizeCodeOrNull(blueprintCode), BlueprintCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Mini deneme blueprint bulunamadi.");
        }

        var blueprint = await EnsureKpssBlueprintAsync(ct);
        var tree = await EnsureKpssTreeAsync(userId, request.VariantCode, ct);
        var sections = await LoadBlueprintSectionsAsync(blueprint.Id, ct);
        var selected = new List<QuestionItem>();
        foreach (var section in sections)
        {
            var paths = ResolvePaths(tree, section.SectionCode, section.SubjectCode, section.TopicCode, request.VariantCode).ToList();
            var questions = await QueryPracticeReadyQuestions(userId, tree.Id, paths)
                .Include(q => q.Options)
                .Include(q => q.Explanations)
                .Include(q => q.OutcomeLinks)
                .OrderBy(q => q.UpdatedAt)
                .ThenBy(q => q.Id)
                .Take(section.QuestionCount)
                .ToListAsync(ct);

            if (questions.Count < section.QuestionCount)
            {
                return new CentralExamDenemeSessionDto
                {
                    DenemeAttemptId = Guid.Empty,
                    BlueprintCode = blueprint.Code,
                    BlueprintName = blueprint.Name,
                    Status = "insufficient_content",
                    EmptyState = InsufficientContent,
                    DurationMinutes = blueprint.DurationMinutes,
                    TotalQuestions = 0,
                    ExamContext = BuildAggregateContext(tree, paths)
                };
            }

            selected.AddRange(questions);
        }

        selected = selected
            .GroupBy(q => q.Id)
            .Select(g => g.First())
            .OrderBy(q => q.UpdatedAt)
            .ThenBy(q => q.Id)
            .ToList();

        var allPaths = sections
            .SelectMany(section => ResolvePaths(tree, section.SectionCode, section.SubjectCode, section.TopicCode, request.VariantCode))
            .ToList();
        var context = BuildAggregateContext(tree, allPaths);
        var attempt = new CentralExamDenemeAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BlueprintId = blueprint.Id,
            ExamDefinitionId = context.ExamDefinitionId ?? tree.Id,
            ExamCode = context.ExamCode ?? KpssCode,
            ExamVariantId = context.ExamVariantId,
            VariantCode = context.VariantCode,
            Status = "started",
            StartedAt = DateTime.UtcNow,
            DurationMinutes = blueprint.DurationMinutes,
            TotalQuestions = selected.Count,
            BlankCount = selected.Count,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var item in selected.Select((question, index) => new { question, index }))
        {
            var questionContext = BuildQuestionContext(item.question, tree, allPaths);
            var correct = item.question.Options.FirstOrDefault(o => o.IsCorrect);
            attempt.Answers.Add(new CentralExamDenemeAnswer
            {
                Id = Guid.NewGuid(),
                DenemeAttemptId = attempt.Id,
                QuestionItemId = item.question.Id,
                ExamSectionId = questionContext.ExamSectionId,
                ExamSubjectId = questionContext.ExamSubjectId,
                ExamTopicId = questionContext.ExamTopicId,
                ExamOutcomeId = questionContext.ExamOutcomeId,
                SectionCode = questionContext.SectionCode,
                SubjectCode = questionContext.SubjectCode,
                TopicCode = questionContext.TopicCode,
                OutcomeCode = questionContext.OutcomeCode,
                QuestionType = item.question.QuestionType,
                Difficulty = item.question.Difficulty,
                CorrectOptionKey = correct?.OptionKey,
                OptionKeysJson = JsonSerializer.Serialize(
                    item.question.Options.Select(o => o.OptionKey).Where(k => !string.IsNullOrWhiteSpace(k)).ToArray(),
                    JsonOptions),
                IsBlank = true,
                Explanation = SafeExplanation(item.question),
                SourceTitle = item.question.SourceTitle,
                SourceUrl = item.question.SourceUrl,
                SortOrder = item.index,
                CreatedAt = DateTime.UtcNow
            });
        }

        _db.CentralExamDenemeAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct);

        return new CentralExamDenemeSessionDto
        {
            DenemeAttemptId = attempt.Id,
            BlueprintCode = blueprint.Code,
            BlueprintName = blueprint.Name,
            Status = "ready",
            DurationMinutes = blueprint.DurationMinutes,
            TotalQuestions = selected.Count,
            ExamContext = context,
            Questions = selected.Select(q => ToDenemeQuestion(q, tree, allPaths)).ToList()
        };
    }

    public async Task<CentralExamDenemeResultDto> SubmitDenemeAsync(
        Guid userId,
        CentralExamDenemeSubmitRequestDto request,
        CancellationToken ct = default)
    {
        if (request.DenemeAttemptId == Guid.Empty)
        {
            throw new ArgumentException("Deneme attempt id is required.");
        }

        var attempt = await _db.CentralExamDenemeAttempts
            .Include(a => a.Blueprint)
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == request.DenemeAttemptId && a.UserId == userId && !a.IsDeleted, ct);
        if (attempt is null)
        {
            throw new ArgumentException("Deneme attempt was not found.");
        }

        if (attempt.Status == "submitted")
        {
            return await BuildResultAsync(userId, attempt.Id, includeResults: true, ct)
                   ?? new CentralExamDenemeResultDto { DenemeAttemptId = attempt.Id, Status = attempt.Status };
        }

        var answerMap = request.Answers
            .Where(a => a.QuestionId != Guid.Empty)
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => NormalizeOptionKey(g.Last().SelectedOptionKey));
        var attemptQuestionIds = attempt.Answers.Select(a => a.QuestionItemId).ToHashSet();
        if (answerMap.Keys.Any(id => !attemptQuestionIds.Contains(id)))
        {
            throw new ArgumentException("Submitted deneme question is not part of this attempt.");
        }

        foreach (var answer in attempt.Answers)
        {
            var selected = answerMap.TryGetValue(answer.QuestionItemId, out var selectedOption) ? selectedOption : null;
            if (!string.IsNullOrWhiteSpace(selected) && !AllowedOptionKeys(answer).Contains(selected))
            {
                throw new ArgumentException("Selected option is not valid for this deneme question.");
            }

            answer.SelectedOptionKey = selected;
            answer.IsBlank = string.IsNullOrWhiteSpace(selected);
            answer.IsCorrect = !answer.IsBlank && string.Equals(selected, answer.CorrectOptionKey, StringComparison.OrdinalIgnoreCase);
            answer.SubmittedAt = DateTime.UtcNow;
        }

        attempt.AnsweredCount = attempt.Answers.Count(a => !a.IsBlank);
        attempt.CorrectCount = attempt.Answers.Count(a => a.IsCorrect);
        attempt.WrongCount = attempt.Answers.Count(a => !a.IsCorrect && !a.IsBlank);
        attempt.BlankCount = attempt.Answers.Count(a => a.IsBlank);
        attempt.Status = "submitted";
        attempt.SubmittedAt = DateTime.UtcNow;
        attempt.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await RecordDenemeLearningSignalsAsync(attempt, ct);

        return await BuildResultAsync(userId, attempt.Id, includeResults: true, ct)
               ?? new CentralExamDenemeResultDto { DenemeAttemptId = attempt.Id, Status = attempt.Status };
    }

    public async Task<CentralExamDenemeResultDto?> GetDenemeAttemptAsync(
        Guid userId,
        Guid attemptId,
        CancellationToken ct = default) =>
        await BuildResultAsync(userId, attemptId, includeResults: true, ct);

    private async Task<CentralExamDenemeBlueprint> EnsureKpssBlueprintAsync(CancellationToken ct)
    {
        var existing = await _db.CentralExamDenemeBlueprints
            .Include(b => b.Sections)
            .FirstOrDefaultAsync(b => b.Code == BlueprintCode && b.OwnerUserId == null && !b.IsDeleted, ct);
        if (existing is not null)
        {
            return existing;
        }

        var tree = await _examFramework.CreateSystemSkeletonAsync(ct);
        var firstPath = ResolvePaths(tree, GenelYetenekCode, TurkceCode, ParagrafCode, "KPSS_LISANS").FirstOrDefault()
                        ?? ResolvePaths(tree, GenelYetenekCode, TurkceCode, ParagrafCode, null).First();
        var blueprint = new CentralExamDenemeBlueprint
        {
            Id = Guid.NewGuid(),
            ExamDefinitionId = tree.Id,
            ExamVariantId = null,
            OwnerUserId = null,
            Code = BlueprintCode,
            Name = BlueprintName,
            Description = "KPSS Turkce Paragraf icin dar kapsamli mini pratik deneme. Resmi OSYM simulasyonu degildir.",
            Visibility = "system",
            VerificationStatus = "unverified",
            OfficialClaimAllowed = false,
            DurationMinutes = null,
            TotalQuestionCount = DefaultQuestionCount,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Sections =
            [
                new CentralExamDenemeBlueprintSection
                {
                    Id = Guid.NewGuid(),
                    ExamSectionId = firstPath.Section.Id,
                    ExamSubjectId = firstPath.Subject.Id,
                    ExamTopicId = firstPath.Topic.Id,
                    SectionCode = GenelYetenekCode,
                    SubjectCode = TurkceCode,
                    TopicCode = ParagrafCode,
                    QuestionCount = DefaultQuestionCount,
                    SortOrder = 0,
                    DifficultyMixJson = "{}"
                }
            ]
        };

        _db.CentralExamDenemeBlueprints.Add(blueprint);
        await _db.SaveChangesAsync(ct);
        return blueprint;
    }

    private async Task<CentralExamDenemeBlueprintDto?> BuildBlueprintDtoAsync(
        Guid userId,
        Guid blueprintId,
        string? variantCode,
        CancellationToken ct)
    {
        var blueprint = await _db.CentralExamDenemeBlueprints
            .AsNoTracking()
            .Include(b => b.Sections.Where(s => !s.IsDeleted))
            .FirstOrDefaultAsync(b => b.Id == blueprintId && !b.IsDeleted && (b.OwnerUserId == null || b.OwnerUserId == userId), ct);
        if (blueprint is null)
        {
            return null;
        }

        var tree = await EnsureKpssTreeAsync(userId, variantCode, ct);
        var sectionDtos = new List<CentralExamDenemeBlueprintSectionDto>();
        foreach (var section in blueprint.Sections.OrderBy(s => s.SortOrder))
        {
            var paths = ResolvePaths(tree, section.SectionCode, section.SubjectCode, section.TopicCode, variantCode).ToList();
            var count = paths.Count == 0 ? 0 : await QueryPracticeReadyQuestions(userId, tree.Id, paths).CountAsync(ct);
            sectionDtos.Add(new CentralExamDenemeBlueprintSectionDto
            {
                Id = section.Id,
                SortOrder = section.SortOrder,
                QuestionCount = section.QuestionCount,
                AvailableQuestionCount = count,
                Label = $"{section.SectionCode} / {section.SubjectCode} / {section.TopicCode}",
                ExamContext = BuildAggregateContext(tree, paths)
            });
        }

        var available = sectionDtos.Sum(s => Math.Min(s.AvailableQuestionCount, s.QuestionCount));
        return new CentralExamDenemeBlueprintDto
        {
            Id = blueprint.Id,
            Code = blueprint.Code,
            Name = blueprint.Name,
            Description = blueprint.Description,
            Visibility = blueprint.Visibility,
            VerificationStatus = blueprint.VerificationStatus,
            CanClaimOfficial = false,
            UserSafeVerificationLabel = SafeVerificationLabel,
            DurationMinutes = blueprint.DurationMinutes,
            TotalQuestionCount = blueprint.TotalQuestionCount,
            AvailableQuestionCount = available,
            HasEnoughQuestions = sectionDtos.All(s => s.AvailableQuestionCount >= s.QuestionCount),
            EmptyState = sectionDtos.All(s => s.AvailableQuestionCount >= s.QuestionCount) ? string.Empty : InsufficientContent,
            ExamContext = BuildAggregateContext(tree, sectionDtos.SelectMany(s => ResolvePaths(tree, s.ExamContext.SectionCode, s.ExamContext.SubjectCode, s.ExamContext.TopicCode, variantCode)).ToList()),
            Sections = sectionDtos
        };
    }

    private async Task<IReadOnlyList<CentralExamDenemeBlueprintSection>> LoadBlueprintSectionsAsync(Guid blueprintId, CancellationToken ct) =>
        await _db.CentralExamDenemeBlueprintSections
            .AsNoTracking()
            .Where(s => s.BlueprintId == blueprintId && !s.IsDeleted)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

    private async Task<CentralExamDenemeResultDto?> BuildResultAsync(
        Guid userId,
        Guid attemptId,
        bool includeResults,
        CancellationToken ct)
    {
        var attempt = await _db.CentralExamDenemeAttempts
            .AsNoTracking()
            .Include(a => a.Blueprint)
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId && !a.IsDeleted, ct);
        if (attempt is null)
        {
            return null;
        }

        var context = ToContext(attempt);
        var results = includeResults && attempt.Status == "submitted"
            ? attempt.Answers.OrderBy(a => a.SortOrder).Select(a => ToQuestionResult(a, context)).ToList()
            : [];
        var breakdown = results
            .GroupBy(r => new
            {
                r.ExamContext.ExamSectionId,
                r.ExamContext.SectionCode,
                r.ExamContext.ExamSubjectId,
                r.ExamContext.SubjectCode,
                r.ExamContext.ExamTopicId,
                r.ExamContext.TopicCode
            })
            .Select(group => new CentralExamDenemeBreakdownDto
            {
                ExamSectionId = group.Key.ExamSectionId,
                SectionCode = group.Key.SectionCode,
                ExamSubjectId = group.Key.ExamSubjectId,
                SubjectCode = group.Key.SubjectCode,
                ExamTopicId = group.Key.ExamTopicId,
                TopicCode = group.Key.TopicCode,
                Label = string.Join(" / ", new[] { group.Key.SectionCode, group.Key.SubjectCode, group.Key.TopicCode }.Where(v => !string.IsNullOrWhiteSpace(v))),
                TotalQuestions = group.Count(),
                CorrectCount = group.Count(r => r.IsCorrect),
                WrongCount = group.Count(r => !r.IsCorrect && !r.IsBlank),
                BlankCount = group.Count(r => r.IsBlank)
            })
            .ToList();
        var weakAnswers = attempt.Answers.Where(a => a.IsBlank || !a.IsCorrect).OrderBy(a => a.SortOrder).ToList();

        return new CentralExamDenemeResultDto
        {
            DenemeAttemptId = attempt.Id,
            BlueprintCode = attempt.Blueprint.Code,
            BlueprintName = attempt.Blueprint.Name,
            Status = attempt.Status,
            DurationMinutes = attempt.DurationMinutes,
            Summary = new CentralExamDenemeSummaryDto
            {
                TotalQuestions = attempt.TotalQuestions,
                AnsweredCount = attempt.AnsweredCount,
                CorrectCount = attempt.CorrectCount,
                WrongCount = attempt.WrongCount,
                BlankCount = attempt.BlankCount,
                CorrectnessRatio = attempt.TotalQuestions == 0 ? 0 : Math.Round(attempt.CorrectCount / (decimal)attempt.TotalQuestions, 4)
            },
            ExamContext = context,
            Results = results,
            Breakdown = breakdown,
            NextAction = BuildNextAction(context, weakAnswers),
            LearningSignal = BuildLearningSignalDto(weakAnswers),
            StudyContext = BuildStudyContext(context, weakAnswers),
            TutorRemediationContext = BuildTutorRemediationContext(weakAnswers)
        };
    }

    private async Task<ExamDefinitionDto> EnsureKpssTreeAsync(Guid userId, string? variantCode, CancellationToken ct)
    {
        await _examFramework.CreateSystemSkeletonAsync(ct);
        var tree = await _examFramework.GetTreeAsync(userId, KpssCode, NormalizeCodeOrNull(variantCode), ct);
        if (tree is null || tree.Visibility != "system")
        {
            tree = await _examFramework.CreateSystemSkeletonAsync(ct);
        }

        return tree;
    }

    private IQueryable<QuestionItem> QueryPracticeReadyQuestions(Guid userId, Guid examDefinitionId, IReadOnlyList<ResolvedExamPath> paths) =>
        ScopedQuestions(examDefinitionId, paths)
            .Where(q => q.QualityStatus == "published" && (q.OwnerUserId == null || q.OwnerUserId == userId));

    private IQueryable<QuestionItem> ScopedQuestions(Guid examDefinitionId, IReadOnlyList<ResolvedExamPath> paths)
    {
        var sectionIds = paths.Select(p => p.Section.Id).Distinct().ToArray();
        var subjectIds = paths.Select(p => p.Subject.Id).Distinct().ToArray();
        var topicIds = paths.Select(p => p.Topic.Id).Distinct().ToArray();
        var outcomeIds = paths.Select(p => p.Outcome?.Id).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();

        var scoped = _db.QuestionItems
            .AsNoTracking()
            .Where(q => !q.IsDeleted && q.ExamDefinitionId == examDefinitionId);

        if (topicIds.Length > 0 || outcomeIds.Length > 0)
        {
            return scoped.Where(q =>
                (q.ExamTopicId.HasValue && topicIds.Contains(q.ExamTopicId.Value))
                || (q.ExamOutcomeId.HasValue && outcomeIds.Contains(q.ExamOutcomeId.Value))
                || q.OutcomeLinks.Any(l => !l.IsDeleted && outcomeIds.Contains(l.ExamOutcomeId)));
        }

        if (subjectIds.Length > 0)
        {
            return scoped.Where(q => q.ExamSubjectId.HasValue && subjectIds.Contains(q.ExamSubjectId.Value));
        }

        return scoped.Where(q => q.ExamSectionId.HasValue && sectionIds.Contains(q.ExamSectionId.Value));
    }

    private async Task RecordDenemeLearningSignalsAsync(CentralExamDenemeAttempt attempt, CancellationToken ct)
    {
        var score = attempt.TotalQuestions == 0 ? 0 : (int)Math.Round(attempt.CorrectCount * 100m / attempt.TotalQuestions);
        await _signals.RecordSignalAsync(
            attempt.UserId,
            topicId: null,
            sessionId: null,
            LearningSignalTypes.CentralExamDenemeAnswered,
            skillTag: BuildConceptKey(ToContext(attempt)),
            topicPath: BuildStudyPath(ToContext(attempt), null),
            score: score,
            isPositive: score >= 70,
            payloadJson: JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.central-exam-deneme.v1",
                source = "central_exam_mini_deneme",
                denemeAttemptId = attempt.Id,
                blueprintId = attempt.BlueprintId,
                examContext = ToContext(attempt),
                summary = new { attempt.TotalQuestions, attempt.AnsweredCount, attempt.CorrectCount, attempt.WrongCount, attempt.BlankCount }
            }, JsonOptions),
            ct: ct);

        foreach (var answer in attempt.Answers.Where(a => a.IsBlank || !a.IsCorrect))
        {
            var context = ToContext(attempt, answer);
            var resultType = answer.IsBlank ? "blank" : "wrong";
            var confidence = BuildConfidence(attempt, answer);
            var misconception = BuildMisconception(context, answer, resultType, confidence);
            var remediation = BuildRemediation(context, answer, resultType, confidence);
            var payload = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.central-exam-deneme.v1",
                source = "central_exam_mini_deneme",
                denemeAttemptId = attempt.Id,
                blueprintId = attempt.BlueprintId,
                questionId = answer.QuestionItemId,
                resultType,
                difficulty = answer.Difficulty,
                questionType = answer.QuestionType,
                conceptKey = remediation.ConceptKey,
                conceptTag = remediation.Label,
                examContext = context,
                learningSignalConfidence = confidence,
                misconceptionSignal = misconception,
                remediationSeed = remediation,
                evidenceBasis = remediation.EvidenceBasis
            }, JsonOptions);

            await _signals.RecordSignalAsync(
                attempt.UserId,
                topicId: null,
                sessionId: null,
                LearningSignalTypes.CentralExamDenemeWeaknessDetected,
                skillTag: remediation.ConceptKey,
                topicPath: BuildStudyPath(context, answer.OutcomeCode),
                score: 0,
                isPositive: false,
                payloadJson: payload,
                ct: ct);
        }
    }

    private static CentralExamDenemeQuestionDto ToDenemeQuestion(QuestionItem question, ExamDefinitionDto tree, IReadOnlyList<ResolvedExamPath> paths) => new()
    {
        QuestionId = question.Id,
        Stem = question.Stem,
        Difficulty = question.Difficulty,
        CognitiveSkill = question.CognitiveSkill,
        SourceTitle = question.SourceTitle,
        SourceUrl = question.SourceUrl,
        ExamContext = BuildQuestionContext(question, tree, paths),
        Options = question.Options
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.OptionKey)
            .Select(o => new CentralExamDenemeOptionDto
            {
                OptionKey = o.OptionKey,
                Text = o.Text,
                SortOrder = o.SortOrder
            })
            .ToList()
    };

    private static PracticeQuestionResultDto ToQuestionResult(CentralExamDenemeAnswer answer, ExamLearningContextDto aggregateContext) => new()
    {
        QuestionId = answer.QuestionItemId,
        SelectedOptionKey = answer.SelectedOptionKey,
        CorrectOptionKey = answer.CorrectOptionKey,
        IsCorrect = answer.IsCorrect,
        IsBlank = answer.IsBlank,
        Explanation = answer.Explanation,
        SourceTitle = answer.SourceTitle,
        SourceUrl = answer.SourceUrl,
        ExamContext = new ExamLearningContextDto
        {
            ExamDefinitionId = aggregateContext.ExamDefinitionId,
            ExamCode = aggregateContext.ExamCode,
            ExamVariantId = aggregateContext.ExamVariantId,
            VariantCode = aggregateContext.VariantCode,
            ExamSectionId = answer.ExamSectionId,
            SectionCode = answer.SectionCode,
            ExamSubjectId = answer.ExamSubjectId,
            SubjectCode = answer.SubjectCode,
            ExamTopicId = answer.ExamTopicId,
            TopicCode = answer.TopicCode,
            ExamOutcomeId = answer.ExamOutcomeId,
            OutcomeCode = answer.OutcomeCode
        }
    };

    private static ExamLearningContextDto BuildQuestionContext(QuestionItem question, ExamDefinitionDto tree, IReadOnlyList<ResolvedExamPath> paths)
    {
        var path = paths.FirstOrDefault(p =>
            question.ExamTopicId == p.Topic.Id
            || question.ExamSubjectId == p.Subject.Id
            || question.ExamSectionId == p.Section.Id
            || question.ExamOutcomeId == p.Outcome?.Id
            || question.OutcomeLinks.Any(l => !l.IsDeleted && l.ExamOutcomeId == p.Outcome?.Id))
            ?? paths.FirstOrDefault();

        return new ExamLearningContextDto
        {
            ExamDefinitionId = tree.Id,
            ExamCode = tree.Code,
            ExamVariantId = question.ExamVariantId ?? path?.Variant.Id,
            VariantCode = path?.Variant.Code,
            ExamSectionId = question.ExamSectionId ?? path?.Section.Id,
            SectionCode = path?.Section.Code,
            ExamSubjectId = question.ExamSubjectId ?? path?.Subject.Id,
            SubjectCode = path?.Subject.Code,
            ExamTopicId = question.ExamTopicId ?? path?.Topic.Id,
            TopicCode = path?.Topic.Code,
            ExamOutcomeId = question.ExamOutcomeId ?? path?.Outcome?.Id,
            OutcomeCode = path?.Outcome?.Code
        };
    }

    private static ExamLearningContextDto BuildAggregateContext(ExamDefinitionDto tree, IReadOnlyList<ResolvedExamPath> paths)
    {
        var first = paths.FirstOrDefault();
        return new ExamLearningContextDto
        {
            ExamDefinitionId = tree.Id,
            ExamCode = tree.Code,
            ExamVariantId = paths.Select(p => p.Variant.Id).Distinct().Count() == 1 ? first?.Variant.Id : null,
            VariantCode = paths.Select(p => p.Variant.Code).Distinct().Count() == 1 ? first?.Variant.Code : null,
            ExamSectionId = first?.Section.Id,
            SectionCode = first?.Section.Code,
            ExamSubjectId = first?.Subject.Id,
            SubjectCode = first?.Subject.Code,
            ExamTopicId = first?.Topic.Id,
            TopicCode = first?.Topic.Code,
            ExamOutcomeId = first?.Outcome?.Id,
            OutcomeCode = first?.Outcome?.Code
        };
    }

    private static ExamLearningContextDto ToContext(CentralExamDenemeAttempt attempt, CentralExamDenemeAnswer? answer = null) => new()
    {
        ExamDefinitionId = attempt.ExamDefinitionId,
        ExamCode = attempt.ExamCode,
        ExamVariantId = attempt.ExamVariantId,
        VariantCode = attempt.VariantCode,
        ExamSectionId = answer?.ExamSectionId,
        SectionCode = answer?.SectionCode,
        ExamSubjectId = answer?.ExamSubjectId,
        SubjectCode = answer?.SubjectCode,
        ExamTopicId = answer?.ExamTopicId,
        TopicCode = answer?.TopicCode,
        ExamOutcomeId = answer?.ExamOutcomeId,
        OutcomeCode = answer?.OutcomeCode
    };

    private static CentralExamDenemeNextActionDto BuildNextAction(
        ExamLearningContextDto context,
        IReadOnlyList<CentralExamDenemeAnswer> weakAnswers)
    {
        if (weakAnswers.Count == 0)
        {
            return new CentralExamDenemeNextActionDto
            {
                ActionType = "practice_quiz",
                Title = "Mini deneme ritmini koru",
                Reason = "Bu mini denemede belirgin zayif sinyal gorunmuyor.",
                ConfidenceStatus = "usable",
                ExamContext = context
            };
        }

        var focus = FocusLabel(weakAnswers[0]);
        return new CentralExamDenemeNextActionDto
        {
            ActionType = weakAnswers.Count >= 2 ? "tutor_remediation" : "practice_quiz",
            Title = $"{focus}: mini deneme telafisi",
            Reason = weakAnswers.Count >= 2
                ? "Mini denemede ayni hatta birden fazla zayif sinyal var."
                : "Mini denemede zayif sinyal olabilir; kisa tekrar iyi olur.",
            ConfidenceStatus = weakAnswers.Count >= 2 ? "usable" : "observed_only",
            ExamContext = ToContextForAnswer(context, weakAnswers[0])
        };
    }

    private static CentralExamLearningSignalDto BuildLearningSignalDto(IReadOnlyList<CentralExamDenemeAnswer> weakAnswers) => new()
    {
        Status = weakAnswers.Count >= 2 ? "usable" : weakAnswers.Count == 0 ? "usable" : "observed_only",
        SignalCount = weakAnswers.Count,
        EvidenceBasis = weakAnswers.Count == 0 ? ["central_exam_mini_deneme", "all_correct"] : ["central_exam_mini_deneme", "wrong_or_blank_answer"],
        WeakAreas = weakAnswers.Select(FocusLabel).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray()
    };

    private static CentralExamStudyContextDto BuildStudyContext(
        ExamLearningContextDto context,
        IReadOnlyList<CentralExamDenemeAnswer> weakAnswers)
    {
        var labels = weakAnswers.Select(FocusLabel).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        var path = BuildStudyPath(context, weakAnswers.FirstOrDefault()?.OutcomeCode);
        return new CentralExamStudyContextDto
        {
            PathLabel = path,
            SuggestedWikiPath = path,
            ExamContext = weakAnswers.Count > 0 ? ToContextForAnswer(context, weakAnswers[0]) : context,
            FocusLabels = labels
        };
    }

    private static string BuildTutorRemediationContext(IReadOnlyList<CentralExamDenemeAnswer> weakAnswers)
    {
        if (weakAnswers.Count == 0)
        {
            return "Bu mini denemede belirgin telafi sinyali yok; kisa ek pratikle pekistirme onerilir.";
        }

        var focus = FocusLabel(weakAnswers[0]);
        return $"{focus} konusunda kisa tekrar ve adim adim cozum onerilir.";
    }

    private static LearningSignalConfidenceDto BuildConfidence(
        CentralExamDenemeAttempt attempt,
        CentralExamDenemeAnswer answer)
    {
        var weakCount = attempt.Answers.Count(a => a.IsBlank || !a.IsCorrect);
        return new LearningSignalConfidenceDto
        {
            Status = weakCount >= 2 ? "usable" : "observed_only",
            Confidence = weakCount >= 2 ? 0.74m : 0.50m,
            Reasons = answer.IsBlank
                ? ["central_exam_mini_deneme", "blank_answer"]
                : ["central_exam_mini_deneme", "wrong_answer"]
        };
    }

    private static MisconceptionSignalDto BuildMisconception(
        ExamLearningContextDto context,
        CentralExamDenemeAnswer answer,
        string resultType,
        LearningSignalConfidenceDto confidence) => new()
    {
        Category = resultType == "blank" ? "application_gap" : "concept_confusion",
        UserSafeLabel = resultType == "blank"
            ? "Bu mini denemede cevap verilmeyen sinyal var."
            : "Bu mini denemede yanlis cevap sinyali var.",
        Confidence = confidence.Confidence,
        ConfidenceStatus = confidence.Status,
        TopicId = null,
        ConceptKey = BuildConceptKey(context),
        Label = FocusLabel(answer),
        SafeHint = "Kisa bir telafi ve benzer mini pratikle kontrol etmek guvenli olur.",
        EvidenceBasis = confidence.Reasons
    };

    private static RemediationSeedDto BuildRemediation(
        ExamLearningContextDto context,
        CentralExamDenemeAnswer answer,
        string resultType,
        LearningSignalConfidenceDto confidence) => new()
    {
        ConceptKey = BuildConceptKey(context),
        Label = FocusLabel(answer),
        TopicId = null,
        Reason = resultType == "blank"
            ? "Mini denemede bos cevap sinyali var; once kisa konu hatirlatmasi sonra pratik iyi olur."
            : "Mini denemede yanlis cevap sinyali var; adim adim cozumle telafi iyi olur.",
        Confidence = confidence.Confidence,
        ConfidenceStatus = confidence.Status,
        MisconceptionCategory = resultType == "blank" ? "application_gap" : "concept_confusion",
        UserSafeMisconceptionLabel = resultType == "blank"
            ? "Cevap verilmeyen mini deneme sinyali"
            : "Yanlis mini deneme sinyali",
        FirstAction = "practice_quiz",
        SecondaryActions = ["tutor_explain", "wiki_review"],
        EvidenceBasis = confidence.Reasons
    };

    private static ExamLearningContextDto ToContextForAnswer(ExamLearningContextDto context, CentralExamDenemeAnswer answer) => new()
    {
        ExamDefinitionId = context.ExamDefinitionId,
        ExamCode = context.ExamCode,
        ExamVariantId = context.ExamVariantId,
        VariantCode = context.VariantCode,
        ExamSectionId = answer.ExamSectionId,
        SectionCode = answer.SectionCode,
        ExamSubjectId = answer.ExamSubjectId,
        SubjectCode = answer.SubjectCode,
        ExamTopicId = answer.ExamTopicId,
        TopicCode = answer.TopicCode,
        ExamOutcomeId = answer.ExamOutcomeId,
        OutcomeCode = answer.OutcomeCode
    };

    private static IReadOnlySet<string> AllowedOptionKeys(CentralExamDenemeAnswer answer)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(answer.OptionKeysJson, JsonOptions)?
                .Select(NormalizeOptionKey)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string FocusLabel(CentralExamDenemeAnswer answer) =>
        FirstNonEmpty(answer.OutcomeCode, answer.TopicCode, answer.SubjectCode, "KPSS Turkce Paragraf");

    private static string BuildConceptKey(ExamLearningContextDto context) =>
        string.Join(":",
            new[] { context.ExamCode, context.VariantCode, context.SectionCode, context.SubjectCode, context.TopicCode, context.OutcomeCode }
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim().ToLowerInvariant()));

    private static string BuildStudyPath(ExamLearningContextDto context, string? outcomeCode) =>
        string.Join(" > ",
            new[] { context.ExamCode, context.SectionCode, context.SubjectCode, context.TopicCode, outcomeCode }
                .Where(v => !string.IsNullOrWhiteSpace(v)));

    private static string SafeExplanation(QuestionItem question)
    {
        var safeExplanation = question.Explanations
            .Where(e => !e.IsDeleted && e.IsSafeForLearners)
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.ExplanationText)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(safeExplanation) ? question.Explanation : safeExplanation;
    }

    private static string? NormalizeCodeOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Replace('-', '_').Replace(' ', '_').ToUpperInvariant();
    }

    private static string? NormalizeOptionKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static IEnumerable<ResolvedExamPath> ResolvePaths(
        ExamDefinitionDto tree,
        string? sectionCode,
        string? subjectCode,
        string? topicCode,
        string? variantCode)
    {
        var normalizedVariant = NormalizeCodeOrNull(variantCode);
        var normalizedSection = NormalizeCodeOrNull(sectionCode);
        var normalizedSubject = NormalizeCodeOrNull(subjectCode);
        var normalizedTopic = NormalizeCodeOrNull(topicCode);
        return tree.Variants
            .Where(v => normalizedVariant is null || v.Code == normalizedVariant)
            .SelectMany(variant => variant.Sections
                .Where(section => normalizedSection is null || section.Code == normalizedSection)
                .SelectMany(section => section.Subjects
                    .Where(subject => normalizedSubject is null || subject.Code == normalizedSubject)
                    .SelectMany(subject => FlattenTopics(subject.Topics)
                        .Where(topic => normalizedTopic is null || topic.Code == normalizedTopic)
                        .Select(topic => new ResolvedExamPath(
                            variant,
                            section,
                            subject,
                            topic,
                            topic.Outcomes.OrderBy(o => o.SortOrder).FirstOrDefault())))));
    }

    private static IEnumerable<ExamTopicDto> FlattenTopics(IEnumerable<ExamTopicDto> topics)
    {
        foreach (var topic in topics)
        {
            yield return topic;
            foreach (var child in FlattenTopics(topic.Children))
            {
                yield return child;
            }
        }
    }

    private sealed record ResolvedExamPath(
        ExamVariantDto Variant,
        ExamSectionDto Section,
        ExamSubjectDto Subject,
        ExamTopicDto Topic,
        ExamOutcomeDto? Outcome);
}

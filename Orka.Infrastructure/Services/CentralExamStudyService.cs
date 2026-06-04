using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class CentralExamStudyService : ICentralExamStudyService
{
    private const string KpssCode = "KPSS";
    private const string TurkceCode = "TURKCE";
    private const string ParagrafCode = "PARAGRAF";
    private const string GenelYetenekCode = "GENEL_YETENEK";
    private const string KpssUnverifiedLabel = "Resmi müfredat iddiası değildir; doğrulanmış kaynak eklendiğinde resmi kaynak etiketi gösterilir.";
    private const string KpssUnverifiedLabelAscii = "Resmi müfredat iddiası değildir";
    private const string NoPracticeEmptyState = "Bu alanda henüz yayına hazır pratik sorusu yok. İçerik eklendiğinde burada çözülebilir hale gelir.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrkaDbContext _db;
    private readonly IExamFrameworkService _examFramework;
    private readonly ILearningSignalService _signals;

    public CentralExamStudyService(
        OrkaDbContext db,
        IExamFrameworkService examFramework,
        ILearningSignalService signals)
    {
        _db = db;
        _examFramework = examFramework;
        _signals = signals;
    }

    public async Task<IReadOnlyList<CentralExamDto>> GetCentralExamsAsync(Guid userId, CancellationToken ct = default)
    {
        var kpss = await GetKpssStudyHomeAsync(userId, null, ct);
        var yks = await GetStudyHomeAsync(userId, "YKS", null, ct);
        var lgs = await GetStudyHomeAsync(userId, "LGS", null, ct);
        var yds = await GetStudyHomeAsync(userId, "YDS", null, ct);
        return
        [
            ToCentralExamCard(kpss, "available"),
            ToCentralExamCard(yks!, "scaffolded"),
            ToCentralExamCard(lgs!, "scaffolded"),
            ToCentralExamCard(yds!, "scaffolded")
        ];
    }

    public async Task<CentralExamStudyHomeDto> GetKpssStudyHomeAsync(
        Guid userId,
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var tree = await EnsureKpssTreeAsync(userId, variantCode, ct);
        var paths = ResolveTurkceParagrafPaths(tree, variantCode).ToList();
        var entry = await BuildTurkceParagrafEntryAsync(userId, tree, paths, ct);
        var counts = await BuildCountsAsync(userId, tree.Id, paths, ct);

        return new CentralExamStudyHomeDto
        {
            ExamCode = tree.Code,
            DisplayName = tree.Name,
            Description = "Merkezi Sınavlar içinde KPSS için sabit çalışma başlangıcı.",
            VerificationStatus = tree.VerificationStatus,
            CanClaimOfficial = tree.CanClaimOfficial,
            UserSafeVerificationLabel = SafeVerificationLabel(tree.UserSafeVerificationLabel),
            Countdown = await GetKpssCountdownAsync(userId, variantCode, ct),
            SupportedVariants = tree.Variants.Select(v => new CentralExamVariantDto
            {
                VariantCode = v.Code,
                DisplayName = v.Name,
                AvailabilityStatus = "available"
            }).ToList(),
            Sections = await BuildSectionsAsync(userId, tree, ct),
            PracticeReadyCounts = counts,
            RecommendedEntryPoint = entry,
            Capabilities = new CentralExamCapabilityDto
            {
                HasQuestionBank = counts.PracticeReadyCount > 0,
                HasPractice = true,
                HasMiniDeneme = true,
                HasCountdown = true,
                HasStudyPlan = true
            },
            EmptyState = entry.HasPracticeReadyQuestions ? string.Empty : NoPracticeEmptyState,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<CentralExamStudyHomeDto?> GetStudyHomeAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var normalizedExamCode = NormalizeCodeOrNull(examCode);
        if (normalizedExamCode is null)
        {
            return null;
        }

        if (normalizedExamCode == KpssCode)
        {
            return await GetKpssStudyHomeAsync(userId, variantCode, ct);
        }

        if (normalizedExamCode is not ("YKS" or "LGS" or "YDS"))
        {
            return null;
        }

        var tree = await EnsureExamTreeAsync(userId, normalizedExamCode, variantCode, ct);
        var counts = await BuildExamWideCountsAsync(userId, tree.Id, ct);
        return new CentralExamStudyHomeDto
        {
            ExamCode = tree.Code,
            DisplayName = tree.Name,
            Description = $"{tree.Code} için Orka merkezi sınav hazırlık iskeleti. Bu alan henüz pratik veya mini deneme akışı açmaz.",
            VerificationStatus = tree.VerificationStatus,
            CanClaimOfficial = tree.CanClaimOfficial,
            UserSafeVerificationLabel = SafeVerificationLabel(tree.UserSafeVerificationLabel),
            Countdown = BuildUnconfiguredCountdown(tree.Code),
            SupportedVariants = tree.Variants.Select(v => new CentralExamVariantDto
            {
                VariantCode = v.Code,
                DisplayName = v.Name,
                AvailabilityStatus = "scaffolded"
            }).ToList(),
            Sections = await BuildSectionsAsync(userId, tree, ct),
            PracticeReadyCounts = counts,
            RecommendedEntryPoint = null,
            Capabilities = new CentralExamCapabilityDto
            {
                HasQuestionBank = counts.PracticeReadyCount > 0,
                HasPractice = false,
                HasMiniDeneme = false,
                HasCountdown = false,
                HasStudyPlan = false
            },
            EmptyState = $"{tree.Code} hazırlık iskeleti temsilidir; resmi müfredat iddiası değildir ve henüz yayına hazır pratik akışı yok.",
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<CentralExamCountdownDto> GetKpssCountdownAsync(
        Guid userId,
        string? variantCode = null,
        CancellationToken ct = default)
    {
        _ = userId;
        _ = variantCode;
        ct.ThrowIfCancellationRequested();
        await _examFramework.CreateSystemSkeletonAsync(ct);

        return new CentralExamCountdownDto
        {
            ExamCode = KpssCode,
            ExamDate = null,
            DaysRemaining = null,
            VerificationStatus = "not_configured",
            UserSafeLabel = "KPSS sınav tarihi doğrulanmış kaynakla yapılandırılmadı."
        };
    }

    public async Task<CentralExamPracticeEntryDto> GetKpssTurkceParagrafEntryAsync(
        Guid userId,
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var tree = await EnsureKpssTreeAsync(userId, variantCode, ct);
        var paths = ResolveTurkceParagrafPaths(tree, variantCode).ToList();
        return await BuildTurkceParagrafEntryAsync(userId, tree, paths, ct);
    }

    public async Task<PracticeSessionDto> StartKpssTurkceParagrafPracticeAsync(
        Guid userId,
        PracticeStartRequestDto request,
        CancellationToken ct = default)
    {
        var tree = await EnsureKpssTreeAsync(userId, request.VariantCode, ct);
        var paths = ResolveTurkceParagrafPaths(tree, request.VariantCode).ToList();
        var limit = Math.Clamp(request.Limit <= 0 ? 5 : request.Limit, 1, 20);
        var questions = await QueryPracticeReadyQuestions(userId, tree.Id, paths)
            .Include(q => q.Options)
            .ThenInclude(o => o.ContentBlocks.Where(b => !b.IsDeleted))
            .ThenInclude(b => b.Asset)
            .Include(q => q.Explanations)
            .Include(q => q.OutcomeLinks)
            .Include(q => q.ContentBlocks.Where(b => !b.IsDeleted))
            .ThenInclude(b => b.Asset)
            .Include(q => q.StimulusLinks)
            .ThenInclude(l => l.QuestionStimulus)
            .OrderBy(q => q.UpdatedAt)
            .ThenBy(q => q.Id)
            .Take(limit)
            .ToListAsync(ct);

        var context = BuildAggregateContext(tree, paths);
        if (questions.Count == 0)
        {
            return new PracticeSessionDto
            {
                PracticeSetId = Guid.Empty,
                PracticeAttemptId = null,
                Status = "empty",
                EmptyState = NoPracticeEmptyState,
                TotalQuestions = 0,
                ExamContext = context
            };
        }

        var attempt = new CentralExamPracticeAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ExamDefinitionId = context.ExamDefinitionId ?? tree.Id,
            ExamCode = context.ExamCode ?? KpssCode,
            ExamVariantId = context.ExamVariantId,
            VariantCode = context.VariantCode,
            ExamSectionId = context.ExamSectionId,
            SectionCode = context.SectionCode,
            ExamSubjectId = context.ExamSubjectId,
            SubjectCode = context.SubjectCode,
            ExamTopicId = context.ExamTopicId,
            TopicCode = context.TopicCode,
            Status = "started",
            StartedAt = DateTime.UtcNow,
            TotalQuestions = questions.Count,
            BlankCount = questions.Count,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var item in questions.Select((question, index) => new { question, index }))
        {
            var questionContext = BuildQuestionContext(item.question, tree, paths);
            var correct = item.question.Options.FirstOrDefault(o => o.IsCorrect);
            attempt.Answers.Add(new CentralExamPracticeAnswer
            {
                Id = Guid.NewGuid(),
                PracticeAttemptId = attempt.Id,
                QuestionItemId = item.question.Id,
                ExamOutcomeId = questionContext.ExamOutcomeId,
                ExamTopicId = questionContext.ExamTopicId,
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

        _db.CentralExamPracticeAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct);

        return new PracticeSessionDto
        {
            PracticeSetId = attempt.Id,
            PracticeAttemptId = attempt.Id,
            Status = "ready",
            TotalQuestions = questions.Count,
            ExamContext = context,
            Questions = questions.Select(q => ToPracticeQuestion(q, tree, paths)).ToList()
        };
    }

    public async Task<PracticeResultDto> SubmitKpssTurkceParagrafPracticeAsync(
        Guid userId,
        PracticeSubmitRequestDto request,
        CancellationToken ct = default)
    {
        if (!request.PracticeSetId.HasValue || request.PracticeSetId.Value == Guid.Empty)
        {
            throw new ArgumentException("Practice attempt id is required.");
        }

        var attempt = await _db.CentralExamPracticeAttempts
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == request.PracticeSetId.Value && a.UserId == userId && !a.IsDeleted, ct);
        if (attempt is null)
        {
            throw new ArgumentException("Practice attempt was not found.");
        }

        if (attempt.Status == "submitted")
        {
            return await BuildPracticeResultAsync(userId, attempt.Id, includeResults: true, ct)
                   ?? new PracticeResultDto { PracticeAttemptId = attempt.Id, Status = attempt.Status };
        }

        var answerMap = request.Answers
            .Where(a => a.QuestionId != Guid.Empty)
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => NormalizeOptionKey(g.Last().SelectedOptionKey));
        var attemptQuestionIds = attempt.Answers.Select(a => a.QuestionItemId).ToHashSet();
        if (answerMap.Keys.Any(id => !attemptQuestionIds.Contains(id)))
        {
            throw new ArgumentException("Submitted practice question is not part of this practice attempt.");
        }

        foreach (var answer in attempt.Answers)
        {
            var selected = answerMap.TryGetValue(answer.QuestionItemId, out var selectedOption) ? selectedOption : null;
            if (!string.IsNullOrWhiteSpace(selected) && !AllowedOptionKeys(answer).Contains(selected))
            {
                throw new ArgumentException("Selected option is not valid for this practice question.");
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

        await RecordPracticeLearningSignalsAsync(attempt, ct);

        return await BuildPracticeResultAsync(userId, attempt.Id, includeResults: true, ct)
               ?? new PracticeResultDto { PracticeAttemptId = attempt.Id, Status = attempt.Status };
    }

    public async Task<PracticeResultDto?> GetPracticeAttemptAsync(
        Guid userId,
        Guid practiceAttemptId,
        CancellationToken ct = default) =>
        await BuildPracticeResultAsync(userId, practiceAttemptId, includeResults: true, ct);

    private async Task<PracticeResultDto?> BuildPracticeResultAsync(
        Guid userId,
        Guid practiceAttemptId,
        bool includeResults,
        CancellationToken ct)
    {
        var attempt = await _db.CentralExamPracticeAttempts
            .AsNoTracking()
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == practiceAttemptId && a.UserId == userId && !a.IsDeleted, ct);
        if (attempt is null)
        {
            return null;
        }

        var context = ToContext(attempt);
        var questionMap = includeResults && attempt.Status == "submitted"
            ? await LoadPracticeQuestionMapAsync(attempt.Answers.Select(a => a.QuestionItemId), ct)
            : new Dictionary<Guid, QuestionItem>();
        var results = includeResults && attempt.Status == "submitted"
            ? attempt.Answers.OrderBy(a => a.SortOrder).Select(a => ToQuestionResult(a, context, questionMap.GetValueOrDefault(a.QuestionItemId))).ToList()
            : [];
        var breakdown = results
            .GroupBy(r => new { r.ExamContext.ExamTopicId, r.ExamContext.TopicCode })
            .Select(group => new PracticeTopicBreakdownDto
            {
                ExamTopicId = group.Key.ExamTopicId,
                TopicCode = group.Key.TopicCode,
                Label = group.Key.TopicCode ?? "KPSS Turkce Paragraf",
                TotalQuestions = group.Count(),
                CorrectCount = group.Count(r => r.IsCorrect),
                WrongCount = group.Count(r => !r.IsCorrect && !r.IsBlank),
                BlankCount = group.Count(r => r.IsBlank)
            })
            .ToList();
        var weakAnswers = attempt.Answers.Where(a => a.IsBlank || !a.IsCorrect).OrderBy(a => a.SortOrder).ToList();

        return new PracticeResultDto
        {
            PracticeAttemptId = attempt.Id,
            Status = attempt.Status,
            TotalQuestions = attempt.TotalQuestions,
            AnsweredCount = attempt.AnsweredCount,
            CorrectCount = attempt.CorrectCount,
            WrongCount = attempt.WrongCount,
            BlankCount = attempt.BlankCount,
            ExamContext = context,
            Results = results,
            TopicBreakdown = breakdown,
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

    private async Task<ExamDefinitionDto> EnsureExamTreeAsync(
        Guid userId,
        string examCode,
        string? variantCode,
        CancellationToken ct)
    {
        await _examFramework.CreateSystemSkeletonAsync(examCode, ct);
        var tree = await _examFramework.GetTreeAsync(userId, examCode, NormalizeCodeOrNull(variantCode), ct);
        if (tree is null || tree.Visibility != "system")
        {
            tree = await _examFramework.CreateSystemSkeletonAsync(examCode, ct);
        }

        return tree;
    }

    private async Task<List<CentralExamSectionDto>> BuildSectionsAsync(Guid userId, ExamDefinitionDto tree, CancellationToken ct)
    {
        var result = new List<CentralExamSectionDto>();
        foreach (var section in tree.Variants.SelectMany(v => v.Sections).GroupBy(s => s.Code).Select(g => g.First()).OrderBy(s => s.SortOrder))
        {
            var sectionDto = new CentralExamSectionDto
            {
                Id = section.Id,
                Code = section.Code,
                Name = section.Name
            };

            foreach (var subject in tree.Variants.SelectMany(v => v.Sections)
                         .Where(s => s.Code == section.Code)
                         .SelectMany(s => s.Subjects)
                         .GroupBy(s => s.Code)
                         .Select(g => g.First())
                         .OrderBy(s => s.SortOrder))
            {
                sectionDto.Subjects.Add(new CentralExamSubjectDto
                {
                    Id = subject.Id,
                    Code = subject.Code,
                    Name = subject.Name,
                    Topics = await BuildTopicDtosAsync(userId, tree, subject.Topics, ct)
                });
            }

            result.Add(sectionDto);
        }

        return result;
    }

    private async Task<List<CentralExamTopicDto>> BuildTopicDtosAsync(
        Guid userId,
        ExamDefinitionDto tree,
        IReadOnlyList<ExamTopicDto> topics,
        CancellationToken ct)
    {
        var result = new List<CentralExamTopicDto>();
        foreach (var topic in topics.OrderBy(t => t.SortOrder).ThenBy(t => t.Code))
        {
            var paths = ResolvePathsByTopicCode(tree, topic.Code).ToList();
            result.Add(new CentralExamTopicDto
            {
                Id = topic.Id,
                Code = topic.Code,
                Name = topic.Name,
                PracticeReadyCount = paths.Count == 0 ? 0 : await QueryPracticeReadyQuestions(userId, tree.Id, paths).CountAsync(ct),
                Children = await BuildTopicDtosAsync(userId, tree, topic.Children, ct)
            });
        }

        return result;
    }

    private async Task<CentralExamPracticeEntryDto> BuildTurkceParagrafEntryAsync(
        Guid userId,
        ExamDefinitionDto tree,
        IReadOnlyList<ResolvedExamPath> paths,
        CancellationToken ct)
    {
        var count = paths.Count == 0 ? 0 : await QueryPracticeReadyQuestions(userId, tree.Id, paths).CountAsync(ct);
        return new CentralExamPracticeEntryDto
        {
            ExamCode = KpssCode,
            Slug = "kpss-turkce-paragraf",
            Title = "KPSS Turkce Paragraf",
            Description = "Turkce paragraf ve anlam sorulari icin dar kapsamli pratik girisi.",
            PracticeReadyCount = count,
            HasPracticeReadyQuestions = count > 0,
            EmptyState = count == 0 ? NoPracticeEmptyState : string.Empty,
            RecommendedAction = count > 0 ? "start_practice" : "import_or_publish_questions",
            ExamContext = BuildAggregateContext(tree, paths)
        };
    }

    private async Task<CentralExamQuestionCountDto> BuildCountsAsync(
        Guid userId,
        Guid examDefinitionId,
        IReadOnlyList<ResolvedExamPath> paths,
        CancellationToken ct)
    {
        var scoped = ScopedQuestions(examDefinitionId, paths);
        return new CentralExamQuestionCountDto
        {
            PracticeReadyCount = await QueryPracticeReadyQuestions(userId, examDefinitionId, paths).CountAsync(ct),
            SystemPublishedCount = await scoped.CountAsync(q => q.OwnerUserId == null && q.QualityStatus == "published", ct),
            UserPublishedCount = await scoped.CountAsync(q => q.OwnerUserId == userId && q.QualityStatus == "published", ct),
            CallerDraftCount = await scoped.CountAsync(q => q.OwnerUserId == userId && q.QualityStatus == "draft", ct),
            CallerNeedsReviewCount = await scoped.CountAsync(q => q.OwnerUserId == userId && q.QualityStatus == "needs_review", ct)
        };
    }

    private async Task<CentralExamQuestionCountDto> BuildExamWideCountsAsync(
        Guid userId,
        Guid examDefinitionId,
        CancellationToken ct)
    {
        var scoped = _db.QuestionItems
            .AsNoTracking()
            .Where(q => !q.IsDeleted && q.ExamDefinitionId == examDefinitionId);

        return new CentralExamQuestionCountDto
        {
            PracticeReadyCount = await scoped.CountAsync(q => q.QualityStatus == "published" && (q.OwnerUserId == null || q.OwnerUserId == userId), ct),
            SystemPublishedCount = await scoped.CountAsync(q => q.OwnerUserId == null && q.QualityStatus == "published", ct),
            UserPublishedCount = await scoped.CountAsync(q => q.OwnerUserId == userId && q.QualityStatus == "published", ct),
            CallerDraftCount = await scoped.CountAsync(q => q.OwnerUserId == userId && q.QualityStatus == "draft", ct),
            CallerNeedsReviewCount = await scoped.CountAsync(q => q.OwnerUserId == userId && q.QualityStatus == "needs_review", ct)
        };
    }

    private IQueryable<QuestionItem> QueryPracticeReadyQuestions(Guid userId, Guid examDefinitionId, IReadOnlyList<ResolvedExamPath> paths)
    {
        var answeredQuestionIds = _db.CentralExamPracticeAnswers
            .Where(a => _db.CentralExamPracticeAttempts.Any(att => att.Id == a.PracticeAttemptId && att.UserId == userId))
            .Select(a => a.QuestionItemId);

        return ScopedQuestions(examDefinitionId, paths)
            .Where(q => q.QualityStatus == "published" && (q.OwnerUserId == null || q.OwnerUserId == userId) && !answeredQuestionIds.Contains(q.Id));
    }

    private IQueryable<QuestionItem> ScopedQuestions(Guid examDefinitionId, IReadOnlyList<ResolvedExamPath> paths)
    {
        var subjectIds = paths.Select(p => p.Subject.Id).Distinct().ToArray();
        var topicIds = paths.Select(p => p.Topic.Id).Distinct().ToArray();
        var outcomeIds = paths.Select(p => p.Outcome?.Id).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();

        return _db.QuestionItems
            .AsNoTracking()
            .Where(q => !q.IsDeleted
                        && q.ExamDefinitionId == examDefinitionId
                        && ((q.ExamSubjectId.HasValue && subjectIds.Contains(q.ExamSubjectId.Value))
                            || (q.ExamTopicId.HasValue && topicIds.Contains(q.ExamTopicId.Value))
                            || (q.ExamOutcomeId.HasValue && outcomeIds.Contains(q.ExamOutcomeId.Value))
                            || q.OutcomeLinks.Any(l => !l.IsDeleted && outcomeIds.Contains(l.ExamOutcomeId))));
    }

    private async Task<Guid?> ResolveStandardTopicIdAsync(Guid? examOutcomeId, CancellationToken ct)
    {
        if (!examOutcomeId.HasValue) return null;

        var mapping = await _db.CurriculumOutcomeMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ExamOutcomeId == examOutcomeId.Value && !m.IsDeleted, ct);

        return mapping?.CurriculumNodeId;
    }

    private async Task RecordPracticeLearningSignalsAsync(CentralExamPracticeAttempt attempt, CancellationToken ct)
    {
        Guid? overallTopicId = null;
        foreach (var answer in attempt.Answers)
        {
            var resolved = await ResolveStandardTopicIdAsync(answer.ExamOutcomeId, ct);
            if (resolved.HasValue)
            {
                overallTopicId = resolved;
                break;
            }
        }

        var score = attempt.TotalQuestions == 0 ? 0 : (int)Math.Round(attempt.CorrectCount * 100m / attempt.TotalQuestions);
        await _signals.RecordSignalAsync(
            attempt.UserId,
            topicId: overallTopicId,
            sessionId: null,
            LearningSignalTypes.CentralExamPracticeAnswered,
            skillTag: BuildConceptKey(ToContext(attempt)),
            topicPath: BuildStudyPath(ToContext(attempt), null),
            score: score,
            isPositive: score >= 70,
            payloadJson: JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.central-exam-practice.v1",
                source = "central_exam_practice",
                practiceAttemptId = attempt.Id,
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
                schemaVersion = "orka.central-exam-practice.v1",
                source = "central_exam_practice",
                practiceAttemptId = attempt.Id,
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

            var answerTopicId = await ResolveStandardTopicIdAsync(answer.ExamOutcomeId, ct);

            await _signals.RecordSignalAsync(
                attempt.UserId,
                topicId: answerTopicId,
                sessionId: null,
                LearningSignalTypes.CentralExamWeaknessDetected,
                skillTag: remediation.ConceptKey,
                topicPath: BuildStudyPath(context, answer.OutcomeCode),
                score: 0,
                isPositive: false,
                payloadJson: payload,
                ct: ct);
        }
    }

    private async Task<Dictionary<Guid, QuestionItem>> LoadPracticeQuestionMapAsync(IEnumerable<Guid> questionIds, CancellationToken ct)
    {
        var ids = questionIds.Distinct().ToArray();
        return await _db.QuestionItems
            .AsNoTracking()
            .Where(q => ids.Contains(q.Id))
            .Include(q => q.Options)
            .ThenInclude(o => o.ContentBlocks.Where(b => !b.IsDeleted))
            .ThenInclude(b => b.Asset)
            .Include(q => q.ContentBlocks.Where(b => !b.IsDeleted))
            .ThenInclude(b => b.Asset)
            .Include(q => q.StimulusLinks)
            .ThenInclude(l => l.QuestionStimulus)
            .ToDictionaryAsync(q => q.Id, ct);
    }

    private static PracticeQuestionDto ToPracticeQuestion(QuestionItem question, ExamDefinitionDto tree, IReadOnlyList<ResolvedExamPath> paths) => new()
    {
        QuestionId = question.Id,
        Stem = question.Stem,
        Difficulty = question.Difficulty,
        CognitiveSkill = question.CognitiveSkill,
        SourceTitle = question.SourceTitle,
        SourceUrl = question.SourceUrl,
        ExamContext = BuildQuestionContext(question, tree, paths),
        Stimuli = ToPracticeStimuli(question),
        ContentBlocks = ToPracticeContentBlocks(question.ContentBlocks),
        Options = question.Options
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.OptionKey)
            .Select(o => new PracticeOptionDto
            {
                OptionKey = o.OptionKey,
                Text = o.Text,
                SortOrder = o.SortOrder,
                ContentBlocks = ToPracticeContentBlocks(o.ContentBlocks)
            })
            .ToList()
    };

    private static PracticeQuestionResultDto ToQuestionResult(
        CentralExamPracticeAnswer answer,
        ExamLearningContextDto aggregateContext,
        QuestionItem? question) => new()
    {
        QuestionId = answer.QuestionItemId,
        Stem = question?.Stem ?? string.Empty,
        SelectedOptionKey = answer.SelectedOptionKey,
        CorrectOptionKey = answer.CorrectOptionKey,
        IsCorrect = answer.IsCorrect,
        IsBlank = answer.IsBlank,
        Explanation = answer.Explanation,
        SourceTitle = answer.SourceTitle,
        SourceUrl = answer.SourceUrl,
        Stimuli = question is null ? [] : ToPracticeStimuli(question),
        ContentBlocks = question is null ? [] : ToPracticeContentBlocks(question.ContentBlocks),
        Options = question is null
            ? []
            : question.Options
                .OrderBy(o => o.SortOrder)
                .ThenBy(o => o.OptionKey)
                .Select(o => new PracticeOptionDto
                {
                    OptionKey = o.OptionKey,
                    Text = o.Text,
                    SortOrder = o.SortOrder,
                    ContentBlocks = ToPracticeContentBlocks(o.ContentBlocks)
                })
                .ToList(),
        ExamContext = new ExamLearningContextDto
        {
            ExamDefinitionId = aggregateContext.ExamDefinitionId,
            ExamCode = aggregateContext.ExamCode,
            ExamVariantId = aggregateContext.ExamVariantId,
            VariantCode = aggregateContext.VariantCode,
            ExamSectionId = aggregateContext.ExamSectionId,
            SectionCode = aggregateContext.SectionCode,
            ExamSubjectId = aggregateContext.ExamSubjectId,
            SubjectCode = aggregateContext.SubjectCode,
            ExamTopicId = answer.ExamTopicId ?? aggregateContext.ExamTopicId,
            TopicCode = answer.TopicCode ?? aggregateContext.TopicCode,
            ExamOutcomeId = answer.ExamOutcomeId,
            OutcomeCode = answer.OutcomeCode
        }
    };

    private static List<PracticeStimulusDto> ToPracticeStimuli(QuestionItem question) =>
        question.StimulusLinks
            .Where(l => l.QuestionStimulus is not null && !l.QuestionStimulus.IsDeleted)
            .OrderBy(l => l.SortOrder)
            .Select(l => new PracticeStimulusDto
            {
                Title = l.QuestionStimulus.Title,
                StimulusType = l.QuestionStimulus.StimulusType,
                ContentText = l.QuestionStimulus.ContentText,
                ContentJson = LearnerSafeContentJson.Sanitize(l.QuestionStimulus.ContentJson),
                SortOrder = l.SortOrder
            })
            .ToList();

    private static List<PracticeContentBlockDto> ToPracticeContentBlocks(IEnumerable<QuestionContentBlock> blocks) =>
        blocks
            .Where(b => !b.IsDeleted)
            .OrderBy(b => b.SortOrder)
            .Select(b => new PracticeContentBlockDto
            {
                BlockType = b.BlockType,
                Text = b.Text,
                ContentJson = LearnerSafeContentJson.Sanitize(b.ContentJson),
                AssetType = b.Asset?.AssetType,
                FileName = b.Asset?.FileName,
                MimeType = b.Asset?.MimeType,
                SortOrder = b.SortOrder,
                AltText = b.AltText ?? b.Asset?.AltText,
                Caption = b.Caption ?? b.Asset?.Caption,
                LongDescription = b.LongDescription ?? b.Asset?.LongDescription
            })
            .ToList();

    private static List<PracticeContentBlockDto> ToPracticeContentBlocks(IEnumerable<QuestionOptionContentBlock> blocks) =>
        blocks
            .Where(b => !b.IsDeleted)
            .OrderBy(b => b.SortOrder)
            .Select(b => new PracticeContentBlockDto
            {
                BlockType = b.BlockType,
                Text = b.Text,
                ContentJson = LearnerSafeContentJson.Sanitize(b.ContentJson),
                AssetType = b.Asset?.AssetType,
                FileName = b.Asset?.FileName,
                MimeType = b.Asset?.MimeType,
                SortOrder = b.SortOrder,
                AltText = b.AltText ?? b.Asset?.AltText,
                Caption = b.Caption ?? b.Asset?.Caption,
                LongDescription = b.Asset?.LongDescription
            })
            .ToList();

    private static ExamLearningContextDto BuildQuestionContext(QuestionItem question, ExamDefinitionDto tree, IReadOnlyList<ResolvedExamPath> paths)
    {
        var path = paths.FirstOrDefault(p =>
            question.ExamTopicId == p.Topic.Id
            || question.ExamSubjectId == p.Subject.Id
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
            ExamVariantId = paths.Count == 1 ? first?.Variant.Id : null,
            VariantCode = paths.Count == 1 ? first?.Variant.Code : null,
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

    private static ExamLearningContextDto ToContext(CentralExamPracticeAttempt attempt, CentralExamPracticeAnswer? answer = null) => new()
    {
        ExamDefinitionId = attempt.ExamDefinitionId,
        ExamCode = attempt.ExamCode,
        ExamVariantId = attempt.ExamVariantId,
        VariantCode = attempt.VariantCode,
        ExamSectionId = attempt.ExamSectionId,
        SectionCode = attempt.SectionCode,
        ExamSubjectId = attempt.ExamSubjectId,
        SubjectCode = attempt.SubjectCode,
        ExamTopicId = answer?.ExamTopicId ?? attempt.ExamTopicId,
        TopicCode = answer?.TopicCode ?? attempt.TopicCode,
        ExamOutcomeId = answer?.ExamOutcomeId,
        OutcomeCode = answer?.OutcomeCode
    };

    private static CentralExamNextActionDto BuildNextAction(
        ExamLearningContextDto context,
        IReadOnlyList<CentralExamPracticeAnswer> weakAnswers)
    {
        if (weakAnswers.Count == 0)
        {
            return new CentralExamNextActionDto
            {
                ActionType = "practice_quiz",
                Title = "Paragraf pratigine devam et",
                Reason = "Bu sette belirgin zayif sinyal gorunmuyor; ayni hatta kisa pratikle ritmi koru.",
                ConfidenceStatus = "usable",
                ExamContext = context
            };
        }

        var focus = FocusLabel(weakAnswers[0]);
        return new CentralExamNextActionDto
        {
            ActionType = weakAnswers.Count >= 2 ? "tutor_remediation" : "practice_quiz",
            Title = $"{focus}: kisa telafi",
            Reason = weakAnswers.Count >= 2
                ? "Bu konuda birden fazla zayif pratik sinyali var."
                : "Bu konuda zayif pratik sinyali olabilir; kisa tekrar iyi olur.",
            ConfidenceStatus = weakAnswers.Count >= 2 ? "usable" : "observed_only",
            ExamContext = ToContextForAnswer(context, weakAnswers[0])
        };
    }

    private static CentralExamLearningSignalDto BuildLearningSignalDto(IReadOnlyList<CentralExamPracticeAnswer> weakAnswers) => new()
    {
        Status = weakAnswers.Count >= 2 ? "usable" : weakAnswers.Count == 0 ? "usable" : "observed_only",
        SignalCount = weakAnswers.Count,
        EvidenceBasis = weakAnswers.Count == 0 ? ["central_exam_practice", "all_correct"] : ["central_exam_practice", "wrong_or_blank_answer"],
        WeakAreas = weakAnswers.Select(FocusLabel).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray()
    };

    private static CentralExamStudyContextDto BuildStudyContext(
        ExamLearningContextDto context,
        IReadOnlyList<CentralExamPracticeAnswer> weakAnswers)
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

    private static string BuildTutorRemediationContext(IReadOnlyList<CentralExamPracticeAnswer> weakAnswers)
    {
        if (weakAnswers.Count == 0)
        {
            return "Bu pratikte belirgin telafi sinyali yok; kisa ek pratikle pekistirme onerilir.";
        }

        var focus = FocusLabel(weakAnswers[0]);
        return $"{focus} konusunda kisa tekrar ve adim adim cozum onerilir.";
    }

    private static LearningSignalConfidenceDto BuildConfidence(
        CentralExamPracticeAttempt attempt,
        CentralExamPracticeAnswer answer)
    {
        var weakCount = attempt.Answers.Count(a => a.IsBlank || !a.IsCorrect);
        return new LearningSignalConfidenceDto
        {
            Status = weakCount >= 2 ? "usable" : "observed_only",
            Confidence = weakCount >= 2 ? 0.72m : 0.48m,
            Reasons = answer.IsBlank
                ? ["central_exam_practice", "blank_answer"]
                : ["central_exam_practice", "wrong_answer"]
        };
    }

    private static MisconceptionSignalDto BuildMisconception(
        ExamLearningContextDto context,
        CentralExamPracticeAnswer answer,
        string resultType,
        LearningSignalConfidenceDto confidence) => new()
    {
        Category = resultType == "blank" ? "application_gap" : "concept_confusion",
        UserSafeLabel = resultType == "blank"
            ? "Bu konuda cevap verilmeyen pratik sinyali var."
            : "Bu konuda yanlis cevap sinyali var.",
        Confidence = confidence.Confidence,
        ConfidenceStatus = confidence.Status,
        TopicId = null,
        ConceptKey = BuildConceptKey(context),
        Label = FocusLabel(answer),
        SafeHint = "Kisa bir telafi ve benzer pratikle kontrol etmek guvenli olur.",
        EvidenceBasis = confidence.Reasons
    };

    private static RemediationSeedDto BuildRemediation(
        ExamLearningContextDto context,
        CentralExamPracticeAnswer answer,
        string resultType,
        LearningSignalConfidenceDto confidence) => new()
    {
        ConceptKey = BuildConceptKey(context),
        Label = FocusLabel(answer),
        TopicId = null,
        Reason = resultType == "blank"
            ? "Bu konuda bos cevap sinyali var; once kisa konu hatirlatmasi sonra pratik iyi olur."
            : "Bu konuda yanlis cevap sinyali var; adim adim cozumle telafi iyi olur.",
        Confidence = confidence.Confidence,
        ConfidenceStatus = confidence.Status,
        MisconceptionCategory = resultType == "blank" ? "application_gap" : "concept_confusion",
        UserSafeMisconceptionLabel = resultType == "blank"
            ? "Cevap verilmeyen pratik sinyali"
            : "Yanlis cevap sinyali",
        FirstAction = "practice_quiz",
        SecondaryActions = ["tutor_explain", "wiki_review"],
        EvidenceBasis = confidence.Reasons
    };

    private static ExamLearningContextDto ToContextForAnswer(ExamLearningContextDto context, CentralExamPracticeAnswer answer) => new()
    {
        ExamDefinitionId = context.ExamDefinitionId,
        ExamCode = context.ExamCode,
        ExamVariantId = context.ExamVariantId,
        VariantCode = context.VariantCode,
        ExamSectionId = context.ExamSectionId,
        SectionCode = context.SectionCode,
        ExamSubjectId = context.ExamSubjectId,
        SubjectCode = context.SubjectCode,
        ExamTopicId = answer.ExamTopicId ?? context.ExamTopicId,
        TopicCode = answer.TopicCode ?? context.TopicCode,
        ExamOutcomeId = answer.ExamOutcomeId,
        OutcomeCode = answer.OutcomeCode
    };

    private static IReadOnlySet<string> AllowedOptionKeys(CentralExamPracticeAnswer answer)
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

    private static string FocusLabel(CentralExamPracticeAnswer answer) =>
        FirstNonEmpty(answer.OutcomeCode, answer.TopicCode, "KPSS Turkce Paragraf");

    private static string BuildConceptKey(ExamLearningContextDto context) =>
        string.Join(":",
            new[] { context.ExamCode, context.VariantCode, context.SubjectCode, context.TopicCode, context.OutcomeCode }
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim().ToLowerInvariant()));

    private static string BuildStudyPath(ExamLearningContextDto context, string? outcomeCode) =>
        string.Join(" > ",
            new[]
            {
                context.ExamCode,
                context.SubjectCode,
                context.TopicCode,
                outcomeCode
            }.Where(v => !string.IsNullOrWhiteSpace(v)));

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

    private static string SafeVerificationLabel(string? label) =>
        string.IsNullOrWhiteSpace(label) ? KpssUnverifiedLabel : label.Trim();

    private static CentralExamDto ToCentralExamCard(CentralExamStudyHomeDto home, string availabilityStatus) => new()
    {
        ExamCode = home.ExamCode,
        DisplayName = home.DisplayName,
        Description = home.Description,
        AvailabilityStatus = availabilityStatus,
        VerificationStatus = home.VerificationStatus,
        CanClaimOfficial = home.CanClaimOfficial,
        UserSafeVerificationLabel = home.UserSafeVerificationLabel,
        SupportedVariants = home.SupportedVariants,
        Capabilities = home.Capabilities
    };

    private static CentralExamCountdownDto BuildUnconfiguredCountdown(string examCode) => new()
    {
        ExamCode = examCode,
        ExamDate = null,
        DaysRemaining = null,
        VerificationStatus = "not_configured",
        UserSafeLabel = $"{examCode} sınav tarihi doğrulanmış kaynakla yapılandırılmadı."
    };

    private static CentralExamDto ComingSoon(string code, string displayName) => new()
    {
        ExamCode = code,
        DisplayName = displayName,
        Description = "Bu merkezi sınav alanı için içerik motoru daha sonra bağlanacak.",
        AvailabilityStatus = "coming_soon",
        VerificationStatus = "unverified",
        CanClaimOfficial = false,
        UserSafeVerificationLabel = "Henüz doğrulanmış resmi kapsam iddiası yok.",
        Capabilities = new CentralExamCapabilityDto()
    };

    private static IEnumerable<ResolvedExamPath> ResolveTurkceParagrafPaths(ExamDefinitionDto tree, string? variantCode)
    {
        var normalizedVariant = NormalizeCodeOrNull(variantCode);
        return tree.Variants
            .Where(v => normalizedVariant is null || v.Code == normalizedVariant)
            .SelectMany(variant => variant.Sections
                .Where(section => section.Code == GenelYetenekCode)
                .SelectMany(section => section.Subjects
                    .Where(subject => subject.Code == TurkceCode)
                    .SelectMany(subject => FlattenTopics(subject.Topics)
                        .Where(topic => topic.Code == ParagrafCode)
                        .Select(topic => new ResolvedExamPath(
                            variant,
                            section,
                            subject,
                            topic,
                            topic.Outcomes.OrderBy(o => o.SortOrder).FirstOrDefault())))));
    }

    private static IEnumerable<ResolvedExamPath> ResolvePathsByTopicCode(ExamDefinitionDto tree, string topicCode)
    {
        return tree.Variants.SelectMany(variant => variant.Sections.SelectMany(section => section.Subjects.SelectMany(subject =>
            FlattenTopics(subject.Topics)
                .Where(topic => topic.Code == topicCode)
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

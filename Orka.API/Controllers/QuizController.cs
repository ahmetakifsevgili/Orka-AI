using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.API.Services;
using Orka.Core.DTOs;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;
using System.Security.Claims;
using System.Text.Json;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/quiz")]
[EnableRateLimiting("QuizLimiter")]
public class QuizController : ControllerBase
{
    private readonly OrkaDbContext _db;
    private readonly IQuizAttemptRecorder _quizRecorder;
    private readonly IDeepPlanAgent _deepPlan;
    private readonly IPlanDiagnosticService _planDiagnostic;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly IStudyIntentAnalyzer _studyIntentAnalyzer;
    private readonly IAdaptiveAssessmentSessionService _adaptiveAssessment;
    private readonly ILogger<QuizController> _logger;
    private readonly ResourceOwnershipGuard _ownership;

    public QuizController(
        OrkaDbContext db,
        IQuizAttemptRecorder quizRecorder,
        IDeepPlanAgent deepPlan,
        IPlanDiagnosticService planDiagnostic,
        IBackgroundTaskQueue backgroundQueue,
        IStudyIntentAnalyzer studyIntentAnalyzer,
        IAdaptiveAssessmentSessionService adaptiveAssessment,
        ILogger<QuizController> logger,
        ResourceOwnershipGuard ownership)
    {
        _db = db;
        _quizRecorder = quizRecorder;
        _deepPlan = deepPlan;
        _planDiagnostic = planDiagnostic;
        _backgroundQueue = backgroundQueue;
        _studyIntentAnalyzer = studyIntentAnalyzer;
        _adaptiveAssessment = adaptiveAssessment;
        _logger = logger;
        _ownership = ownership;
    }

    [HttpGet("generate")]
    public async Task<IActionResult> Generate([FromQuery] Guid? topicId)
    {
        if (topicId == null) return BadRequest(new { error = "topicId zorunlu." });

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId.Value, HttpContext.RequestAborted))
            return NotFound(new { error = "Konu bulunamadı." });

        var topic = await _db.Topics.FindAsync(topicId);
        if (topic == null) return NotFound(new { error = "Konu bulunamadı." });

        try
        {
            var rawJson = await _deepPlan.GenerateBaselineQuizAsync(topic.Title, topicId.Value, "tr", 5);

            // LLM bazen JSON blokları (```json ... ```) içine sarar veya metin ekler, temizleyelim
            var cleaned = rawJson.Trim();
            if (cleaned.Contains("```"))
            {
                var lines = cleaned.Split('\n');
                cleaned = string.Join("\n", lines.Where(l => !l.Trim().StartsWith("```")));
            }

            var s = cleaned.IndexOf('[');
            var e = cleaned.LastIndexOf(']');
            if (s >= 0 && e > s) cleaned = cleaned[s..(e + 1)];

            using var doc = JsonDocument.Parse(cleaned);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return BadRequest(new { error = "Quiz üretimi geçersiz format." });
            }

            var quizRun = new QuizRun
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                QuizType = "lesson",
                Status = "active",
                TotalQuestions = 0,
                CreatedAt = DateTime.UtcNow
            };
            _db.QuizRuns.Add(quizRun);

            var graphSnapshot = new ConceptGraphSnapshot
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                IntentHash = $"legacy-quiz:{topicId.Value:N}",
                ApprovedResearchIntent = $"Legacy quiz generation for {topic.Title}",
                TopicTitle = topic.Title,
                Domain = "legacy_quiz",
                SourceConfidence = "low",
                SourceBundleHash = string.Empty,
                GraphJson = "{}",
                CreatedAt = DateTime.UtcNow
            };
            _db.ConceptGraphSnapshots.Add(graphSnapshot);
            var conceptsByKey = new Dictionary<string, LearningConcept>(StringComparer.OrdinalIgnoreCase);

            var questionList = doc.RootElement.EnumerateArray().ToList();
            quizRun.TotalQuestions = questionList.Count;

            var sanitizedQuestions = new List<object>();
            var order = 1;

            foreach (var q in questionList)
            {
                var assessmentItemId = Guid.NewGuid();

                var conceptKey = NormalizeLegacyConceptKey(q.TryGetProperty("conceptTag", out var ck) ? ck.GetString() : null, order);
                var conceptLabel = q.TryGetProperty("conceptTag", out var cl) ? cl.GetString() ?? conceptKey : conceptKey;
                var qType = q.TryGetProperty("questionType", out var qt) ? qt.GetString() ?? "conceptual" : "conceptual";
                var diff = q.TryGetProperty("difficulty", out var df) ? df.GetString() ?? "orta" : "orta";
                var cog = q.TryGetProperty("questionType", out var cg) ? cg.GetString() ?? "conceptual" : "conceptual";
                if (!conceptsByKey.TryGetValue(conceptKey, out var learningConcept))
                {
                    learningConcept = new LearningConcept
                    {
                        Id = Guid.NewGuid(),
                        ConceptGraphSnapshotId = graphSnapshot.Id,
                        StableKey = conceptKey,
                        Label = conceptLabel,
                        Description = $"Legacy quiz concept for {topic.Title}",
                        DifficultyBand = "core",
                        Order = conceptsByKey.Count + 1,
                        CreatedAt = DateTime.UtcNow
                    };
                    conceptsByKey[conceptKey] = learningConcept;
                    _db.LearningConcepts.Add(learningConcept);
                }

                var assessmentItem = new AssessmentItem
                {
                    Id = assessmentItemId,
                    UserId = userId,
                    TopicId = topicId,
                    QuizRunId = quizRun.Id,
                    ConceptGraphSnapshotId = graphSnapshot.Id,
                    LearningConceptId = learningConcept.Id,
                    AssessmentItemKey = $"legacy-quiz:{quizRun.Id:N}:{order}",
                    ConceptKey = conceptKey,
                    ConceptLabel = conceptLabel,
                    QuestionType = qType,
                    CognitiveSkill = cog,
                    Difficulty = diff,
                    Order = order++,
                    GeneratedQuestionJson = q.ToString(),
                    CreatedAt = DateTime.UtcNow
                };

                _db.AssessmentItems.Add(assessmentItem);

                var questionText = q.TryGetProperty("question", out var qtext) ? qtext.GetString() ?? "" : "";
                var skillTag = q.TryGetProperty("skillTag", out var stag) ? stag.GetString() ?? "" : "";
                var conceptTag = q.TryGetProperty("conceptTag", out var ctag) ? ctag.GetString() ?? "" : "";
                var learningObjective = q.TryGetProperty("learningObjective", out var lobj) ? lobj.GetString() ?? "" : "";
                var qTypeStr = q.TryGetProperty("type", out var qtype) ? qtype.GetString() ?? "multiple_choice" : "multiple_choice";

                var sanitizedOptions = new List<object>();
                if (q.TryGetProperty("options", out var optionsArray) && optionsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var opt in optionsArray.EnumerateArray())
                    {
                        var optText = opt.TryGetProperty("text", out var otext) ? otext.GetString() ?? "" : "";
                        var optionId = opt.TryGetProperty("id", out var oid)
                            ? oid.GetString()
                            : opt.TryGetProperty("optionKey", out var ok)
                                ? ok.GetString()
                                : null;
                        sanitizedOptions.Add(new { id = optionId, text = optText, isCorrect = false });
                    }
                }

                sanitizedQuestions.Add(new
                {
                    id = assessmentItemId,
                    assessmentItemId,
                    conceptGraphSnapshotId = graphSnapshot.Id,
                    learningConceptId = learningConcept.Id,
                    type = qTypeStr,
                    question = questionText,
                    options = sanitizedOptions,
                    skillTag,
                    difficulty = diff,
                    conceptTag,
                    learningObjective,
                    topic = topic.Title
                });
            }

            graphSnapshot.GraphJson = JsonSerializer.Serialize(new
            {
                topicTitle = topic.Title,
                source = "legacy_quiz_generate",
                concepts = conceptsByKey.Values
                    .OrderBy(c => c.Order)
                    .Select(c => new { stableKey = c.StableKey, label = c.Label, order = c.Order })
            });

            await _db.SaveChangesAsync(HttpContext.RequestAborted);

            return Ok(new { topicId, quizRunId = quizRun.Id, questions = sanitizedQuestions });
        }
        catch (Exception ex)
        {
            _logger.LogError("Quiz uretimi basarisiz. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(topicId, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { error = "Quiz üretilemedi." });
        }
    }

    [HttpPost("attempt")]
    public async Task<IActionResult> RecordAttempt([FromBody] RecordQuizAttemptRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        if (!await _ownership.OptionalTopicBelongsToUserAsync(userId, request.TopicId, HttpContext.RequestAborted) ||
            !await _ownership.OptionalSessionBelongsToUserAsync(userId, request.SessionId, HttpContext.RequestAborted) ||
            !await _ownership.OptionalQuizRunBelongsToUserAsync(userId, request.QuizRunId, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        try
        {
            StripClientSuppliedAnswerKey(request);
            var result = await _quizRecorder.RecordAsync(userId, request, HttpContext.RequestAborted);
            var attempt = result.Attempt;
            var xpResult = result.Xp;

            return Ok(new
            {
                attempt.Id,
                attempt.QuizRunId,
                attempt.TopicId,
                attempt.SkillTag,
                attempt.QuestionHash,
                knowledgeTracingStateId = TryGuid(ExtractMetadata(attempt.SourceRefsJson, "knowledgeTracingStateId")),
                masteryProbability = TryDecimal(ExtractMetadata(attempt.SourceRefsJson, "masteryProbability")),
                itemQualityStatus = ExtractMetadata(attempt.SourceRefsJson, "itemQualityStatus"),
                xp = xpResult is null
                    ? null
                    : new
                    {
                        xpResult.Awarded,
                        xpResult.XpAwarded,
                        xpResult.TotalXP,
                        xpResult.CurrentStreak,
                        Badges = xpResult.NewlyEarnedBadges
                    },
                review = result.Review,
                mistake = result.Mistake,
                misconceptionSignal = result.MisconceptionSignal,
                learningSignalConfidence = result.LearningSignalConfidence,
                remediationSeed = result.RemediationSeed,
                learningImpact = result.LearningImpact
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizController] Quiz sonucu kaydedilemedi. UserRef={UserRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { error = "Quiz sonucu kaydedilemedi." });
        }
    }

    [HttpPost("adaptive/start")]
    public async Task<IActionResult> StartAdaptive([FromBody] AdaptiveAssessmentStartRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        if (!await _ownership.OptionalTopicBelongsToUserAsync(userId, request.TopicId, HttpContext.RequestAborted) ||
            !await _ownership.OptionalSessionBelongsToUserAsync(userId, request.SessionId, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        try
        {
            var result = await _adaptiveAssessment.StartAsync(userId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizController] Adaptive assessment start failed. UserRef={UserRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { error = "Adaptive practice could not be started." });
        }
    }

    [HttpGet("adaptive/{adaptiveSessionId:guid}/next")]
    public async Task<IActionResult> GetAdaptiveNext(Guid adaptiveSessionId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        if (!await _ownership.AdaptiveSessionBelongsToUserAsync(userId, adaptiveSessionId, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        try
        {
            var result = await _adaptiveAssessment.GetNextAsync(userId, adaptiveSessionId, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizController] Adaptive assessment next failed. UserRef={UserRef} SessionRef={SessionRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(adaptiveSessionId, "session"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { error = "Next adaptive question could not be selected." });
        }
    }

    [HttpPost("adaptive/{adaptiveSessionId:guid}/answer")]
    public async Task<IActionResult> RecordAdaptiveAnswer(Guid adaptiveSessionId, [FromBody] AdaptiveAssessmentAnswerRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        if (!await _ownership.AdaptiveSessionBelongsToUserAsync(userId, adaptiveSessionId, HttpContext.RequestAborted) ||
            !await _ownership.OptionalTopicBelongsToUserAsync(userId, request.TopicId, HttpContext.RequestAborted) ||
            !await _ownership.OptionalSessionBelongsToUserAsync(userId, request.SessionId, HttpContext.RequestAborted) ||
            !await _ownership.OptionalQuizRunBelongsToUserAsync(userId, request.QuizRunId, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        try
        {
            StripClientSuppliedAnswerKey(request);
            var result = await _adaptiveAssessment.RecordAnswerAsync(userId, adaptiveSessionId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizController] Adaptive assessment answer failed. UserRef={UserRef} SessionRef={SessionRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(adaptiveSessionId, "session"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { error = "Adaptive answer could not be recorded." });
        }
    }

    [HttpPost("plan-diagnostic/start")]
    public async Task<IActionResult> StartPlanDiagnostic()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            using var requestDoc = await JsonDocument.ParseAsync(HttpContext.Request.Body, cancellationToken: HttpContext.RequestAborted);
            var request = ParseStartPlanDiagnosticRequest(requestDoc.RootElement);

            if (!await _ownership.TopicBelongsToUserAsync(userId, request.TopicId, HttpContext.RequestAborted) ||
                !await _ownership.OptionalSessionBelongsToUserAsync(userId, request.SessionId, HttpContext.RequestAborted))
            {
                return NotFound();
            }

            var result = await _planDiagnostic.StartAsync(userId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[QuizController] Plan diagnostic start rejected. UserRef={UserRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return BadRequest(new
            {
                error = "Plan diagnostic request could not be accepted.",
                message = "Study plan diagnostic request failed validation."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizController] Plan diagnostic start failed. UserRef={UserRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { error = "Plan diagnostic could not be started." });
        }
    }

    [HttpPost("plan-diagnostic/start-async")]
    public async Task<IActionResult> StartPlanDiagnosticAsync()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            using var requestDoc = await JsonDocument.ParseAsync(HttpContext.Request.Body, cancellationToken: HttpContext.RequestAborted);
            var request = ParseStartPlanDiagnosticRequest(requestDoc.RootElement);

            if (!await _ownership.TopicBelongsToUserAsync(userId, request.TopicId, HttpContext.RequestAborted) ||
                !await _ownership.OptionalSessionBelongsToUserAsync(userId, request.SessionId, HttpContext.RequestAborted))
            {
                return NotFound();
            }

            var accepted = await _planDiagnostic.StartQueuedAsync(userId, request, HttpContext.RequestAborted);

            await _backgroundQueue.QueueAsync(new BackgroundTaskItem(
                JobType: "plan-diagnostic-start",
                UserId: userId,
                CorrelationId: accepted.PlanRequestId.ToString("N"),
                Work: _ => Task.CompletedTask,
                MaxAttempts: 1,
                Timeout: TimeSpan.FromMinutes(10),
                ScopedWork: async (sp, ct) =>
                {
                    var service = sp.GetRequiredService<IPlanDiagnosticService>();
                    await service.RunQueuedStartAsync(userId, accepted.PlanRequestId, ct);
                }), HttpContext.RequestAborted);

            return Accepted(accepted);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[QuizController] Async plan diagnostic start rejected. UserRef={UserRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return BadRequest(new
            {
                error = "Plan diagnostic request could not be accepted.",
                message = "Study plan diagnostic request failed validation."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizController] Async plan diagnostic start failed. UserRef={UserRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { error = "Plan diagnostic could not be queued." });
        }
    }

    [HttpGet("plan-diagnostic/{planRequestId:guid}/status")]
    public async Task<IActionResult> GetPlanDiagnosticStatus(Guid planRequestId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _planDiagnostic.GetStartStatusAsync(userId, planRequestId, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizController] Plan diagnostic status failed. UserRef={UserRef} PlanRequestRef={PlanRequestRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(planRequestId, "plan"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { error = "Plan diagnostic status could not be loaded." });
        }
    }

    [HttpPost("plan-diagnostic/intent")]
    public async Task<IActionResult> AnalyzePlanIntent([FromBody] AnalyzeStudyIntentRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        if (!await _ownership.OptionalTopicBelongsToUserAsync(userId, request.TopicId, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        try
        {
            var result = await _studyIntentAnalyzer.AnalyzeAsync(userId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("[QuizController] Study intent validation failed. UserRef={UserRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return BadRequest(new { error = "Study intent request is invalid." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[QuizController] Study intent analysis rejected. UserRef={UserRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return BadRequest(new { error = "Study intent request is required." });
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizController] Study intent analysis failed. UserRef={UserRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { error = "Study intent could not be analyzed." });
        }
    }

    [HttpPost("plan-diagnostic/{planRequestId:guid}/attempt")]
    public async Task<IActionResult> RecordPlanDiagnosticAttempt(Guid planRequestId, [FromBody] RecordQuizAttemptRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            StripClientSuppliedAnswerKey(request);
            var result = await _planDiagnostic.RecordAnswerAsync(userId, planRequestId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizController] Plan diagnostic answer failed. UserRef={UserRef} PlanRequestRef={PlanRequestRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(planRequestId, "plan"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { error = "Plan diagnostic answer could not be recorded." });
        }
    }

    [HttpPost("plan-diagnostic/finalize")]
    public async Task<IActionResult> FinalizePlanDiagnostic([FromBody] FinalizePlanDiagnosticRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        if (request.PlanRequestId == Guid.Empty)
        {
            return BadRequest(new
            {
                error = "Plan diagnostic could not be finalized.",
                message = "planRequestId is required."
            });
        }

        try
        {
            var result = await _planDiagnostic.FinalizeAsync(userId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizController] Plan diagnostic finalize failed. UserRef={UserRef} PlanRequestRef={PlanRequestRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(request.PlanRequestId, "plan"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { error = "Plan diagnostic could not be finalized." });
        }
    }

    [HttpPost("plan-diagnostic/{planRequestId:guid}/skip")]
    public async Task<IActionResult> SkipPlanDiagnostic(Guid planRequestId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _planDiagnostic.SkipAndGenerateAsync(userId, planRequestId, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuizController] Plan diagnostic skip failed. UserRef={UserRef} PlanRequestRef={PlanRequestRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(planRequestId, "plan"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return StatusCode(500, new { error = "Plan diagnostic could not be skipped safely." });
        }
    }

    [HttpGet("history/{topicId}")]
    public async Task<ActionResult<IEnumerable<QuizAttemptDto>>> GetQuizHistory(Guid topicId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        if (!await _ownership.TopicBelongsToUserAsync(userId, topicId, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        var attempts = await _db.QuizAttempts
            .AsNoTracking()
            .Where(a => a.TopicId == topicId && a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new QuizAttemptDto
            {
                Id = a.Id,
                QuizRunId = a.QuizRunId,
                QuestionId = a.QuestionId,
                Question = a.Question,
                UserAnswer = a.UserAnswer,
                IsCorrect = a.IsCorrect,
                Explanation = a.Explanation,
                SkillTag = a.SkillTag,
                AssessmentItemId = a.AssessmentItemId,
                ConceptKey = ExtractMetadata(a.SourceRefsJson, "conceptKey"),
                ConceptTag = ExtractMetadata(a.SourceRefsJson, "conceptTag"),
                CognitiveSkill = ExtractMetadata(a.SourceRefsJson, "cognitiveSkill"),
                MisconceptionTarget = ExtractMetadata(a.SourceRefsJson, "misconceptionTarget"),
                EvidenceExpected = ExtractMetadata(a.SourceRefsJson, "evidenceExpected"),
                ScoringRule = ExtractMetadata(a.SourceRefsJson, "scoringRule"),
                LearningOutcomeIdsJson = ExtractMetadata(a.SourceRefsJson, "learningOutcomeIds"),
                KnowledgeTracingStateId = TryGuid(ExtractMetadata(a.SourceRefsJson, "knowledgeTracingStateId")),
                MasteryProbability = TryDecimal(ExtractMetadata(a.SourceRefsJson, "masteryProbability")),
                ItemQualityStatus = ExtractMetadata(a.SourceRefsJson, "itemQualityStatus"),
                LearningObjective = ExtractMetadata(a.SourceRefsJson, "learningObjective"),
                QuestionType = ExtractMetadata(a.SourceRefsJson, "questionType"),
                MistakeCategory = ExtractMetadata(a.SourceRefsJson, "mistakeCategory"),
                AssessmentMode = ExtractMetadata(a.SourceRefsJson, "assessmentMode"),
                SourceReadiness = ExtractMetadata(a.SourceRefsJson, "sourceReadiness"),
                WikiReviewHint = ExtractMetadata(a.SourceRefsJson, "wikiNotebookSectionKey"),
                TopicPath = a.TopicPath,
                Difficulty = a.Difficulty,
                CognitiveType = a.CognitiveType,
                QuestionHash = a.QuestionHash,
                ResponseTimeMs = a.ResponseTimeMs,
                WasSkipped = a.WasSkipped,
                ConfidenceSelfRating = a.ConfidenceSelfRating,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return Ok(attempts);
    }

    [HttpGet("stats")]
    public async Task<ActionResult> GetGlobalStats()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var totalAttempts = await _db.QuizAttempts.CountAsync(a => a.UserId == userId);
        var correctAttempts = await _db.QuizAttempts.CountAsync(a => a.UserId == userId && a.IsCorrect);

        var accuracy = totalAttempts > 0 ? (double)correctAttempts / totalAttempts : 0;

        // Son 7 günün günlük başarısı
        var last7Days = Enumerable.Range(0, 7)
            .Select(i => DateTime.UtcNow.Date.AddDays(-i))
            .Reverse()
            .ToList();

        var startDate = last7Days.First();
        var endDate = last7Days.Last().AddDays(1);
        var groupedAttempts = await _db.QuizAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .GroupBy(a => a.CreatedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Total = g.Count(),
                Correct = g.Count(a => a.IsCorrect)
            })
            .ToListAsync();

        var byDay = groupedAttempts.ToDictionary(x => x.Date);
        var dailyProgress = last7Days.Select(day =>
        {
            byDay.TryGetValue(day, out var item);
            var dayTotal = item?.Total ?? 0;
            var dayCorrect = item?.Correct ?? 0;
            return new
            {
                Date = day.ToString("MM/dd"),
                Total = dayTotal,
                Correct = dayCorrect,
                Accuracy = dayTotal > 0 ? Math.Round((double)dayCorrect / dayTotal * 100, 1) : 0
            };
        }).ToList<object>();

        return Ok(new
        {
            TotalQuizzes = totalAttempts,
            CorrectAnswers = correctAttempts,
            Accuracy = Math.Round(accuracy * 100, 2),
            DailyProgress = dailyProgress
        });
    }

    private static string? ExtractMetadata(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(key, out var value))
            {
                return value.ValueKind == System.Text.Json.JsonValueKind.String
                    ? value.GetString()
                    : value.GetRawText();
            }

            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static Guid? TryGuid(string? value) =>
        Guid.TryParse(value?.Trim('"'), out var id) ? id : null;

    private static void StripClientSuppliedAnswerKey(RecordQuizAttemptRequest request)
    {
        request.IsCorrect = null;
        request.Explanation = null;
    }

    private static decimal? TryDecimal(string? value) =>
        decimal.TryParse(value?.Trim('"'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static StartPlanDiagnosticRequest ParseStartPlanDiagnosticRequest(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Plan diagnostic request body is required.");
        }

        return new StartPlanDiagnosticRequest
        {
            TopicId = ReadGuid(body, "topicId") ?? Guid.Empty,
            SessionId = ReadGuid(body, "sessionId"),
            TopicTitle = ReadString(body, "topicTitle"),
            UserLevel = ReadString(body, "userLevel"),
            IntentRequestId = ReadGuid(body, "intentRequestId"),
            RawStudyRequest = ReadString(body, "rawStudyRequest"),
            ApprovedMainTopic = ReadString(body, "approvedMainTopic"),
            ApprovedFocusArea = ReadString(body, "approvedFocusArea"),
            ApprovedStudyGoal = ReadString(body, "approvedStudyGoal"),
            ApprovedResearchIntent = ReadString(body, "approvedResearchIntent")
        };
    }

    private static string? ReadString(JsonElement body, string camelName)
    {
        if (!TryGetPropertyCaseInsensitive(body, camelName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static Guid? ReadGuid(JsonElement body, string camelName)
    {
        var text = ReadString(body, camelName);
        return Guid.TryParse(text, out var value) ? value : null;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement body, string camelName, out JsonElement value)
    {
        foreach (var property in body.EnumerateObject())
        {
            if (property.Name.Equals(camelName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeLegacyConceptKey(string? value, int order)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? $"legacy-concept-{order}" : value.Trim();
        var chars = raw
            .ToLowerInvariant()
            .Select(ch => ch is >= 'a' and <= 'z' || ch is >= '0' and <= '9' ? ch : '-')
            .ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        normalized = normalized.Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return $"legacy-concept-{order}";
        }

        return normalized.Length > 120 ? normalized[..120] : normalized;
    }
}

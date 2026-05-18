using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class AssessmentBlueprintService : IAssessmentBlueprintService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "diagnostic_check",
        "micro_quiz",
        "misconception_probe",
        "retrieval_practice",
        "readiness_check",
        "review_check"
    };

    private readonly OrkaDbContext _db;
    private readonly IPlanSequencingService? _planSequencing;
    private readonly ISourceEvidenceLifecycleService? _sourceLifecycle;
    private readonly IActiveLessonSnapshotService? _snapshots;

    public AssessmentBlueprintService(
        OrkaDbContext db,
        IPlanSequencingService? planSequencing = null,
        ISourceEvidenceLifecycleService? sourceLifecycle = null,
        IActiveLessonSnapshotService? snapshots = null)
    {
        _db = db;
        _planSequencing = planSequencing;
        _sourceLifecycle = sourceLifecycle;
        _snapshots = snapshots;
    }

    public async Task<AssessmentBlueprintDto> BuildBlueprintForPlanStepAsync(
        Guid userId,
        AssessmentBlueprintRequestDto request,
        CancellationToken ct = default)
    {
        var topic = await ResolveTopicAsync(userId, request.TopicId, ct);
        if (topic == null)
        {
            throw new InvalidOperationException("Topic not found.");
        }

        PlanQualityEvaluationDto? plan = null;
        if (_planSequencing != null && request.PlanQualitySnapshotId.HasValue)
        {
            plan = await _planSequencing.GetPlanQualitySnapshotAsync(userId, request.PlanQualitySnapshotId.Value, ct);
        }
        else if (_planSequencing != null)
        {
            plan = await _planSequencing.GetLatestPlanQualitySnapshotAsync(userId, topic.Id, request.SessionId, ct);
        }

        var step = ResolvePlanStep(plan, request.PlanStepId, request.ConceptKey);
        if (step == null)
        {
            return await BuildDiagnosticBlueprintAsync(userId, topic.Id, request.SessionId, ct);
        }

        var source = await GetSourceAsync(userId, topic.Id, request.SessionId, ct);
        var requestedMode = string.Equals(request.AssessmentMode, "diagnostic_check", StringComparison.OrdinalIgnoreCase)
            ? null
            : request.AssessmentMode;
        var mode = NormalizeMode(step.QuizHook.HookType, requestedMode, "micro_quiz");
        return new AssessmentBlueprintDto
        {
            TopicId = topic.Id,
            SessionId = request.SessionId,
            PlanQualitySnapshotId = plan?.SnapshotId,
            PlanStepId = step.StepId,
            AssessmentMode = mode,
            UserSafeModeLabel = ModeLabel(mode),
            TargetConcepts =
            [
                new AssessmentBlueprintConceptDto
                {
                    ConceptKey = FirstNonEmpty(step.ConceptKey, request.ConceptKey, topic.Title) ?? string.Empty,
                    Label = FirstNonEmpty(step.ConceptLabel, step.Title, topic.Title) ?? string.Empty,
                    Role = "target",
                    DifficultyBand = FirstNonEmpty(step.DifficultyBand, "core")!,
                    ConfidenceStatus = step.LearnerState is "unknown" or "evidence_insufficient" ? "observed_only" : "usable"
                }
            ],
            PrerequisiteConceptKeys = step.PrerequisiteConceptKeys,
            MisconceptionTargets = BuildMisconceptionTargets(
                step.ConceptKey,
                step.TargetMisconceptions.Count > 0 ? step.TargetMisconceptions : step.QuizHook.TargetMisconceptions,
                request.MisconceptionKey,
                mode),
            DifficultyBand = FirstNonEmpty(step.DifficultyBand, "core")!,
            ItemCountTarget = Math.Clamp(request.ItemCountTarget ?? (mode == "micro_quiz" ? 3 : 5), 1, 12),
            CognitiveSkillMix = CognitiveMix(mode),
            EvidenceMode = SourceMode(source?.EvidenceStatus ?? step.Evidence.SourceReadiness),
            ExplanationRequirement = "required_after_submit",
            RemediationRequirement = mode == "misconception_probe" ? "required_after_submit" : "safe_hint_after_submit",
            LeakageSafetyRequirements = LeakageRules(),
            Warnings = BuildBlueprintWarnings(source?.EvidenceStatus, plan?.QualityStatus)
        };
    }

    public async Task<AssessmentBlueprintDto> BuildDiagnosticBlueprintAsync(
        Guid userId,
        Guid topicId,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var topic = await ResolveTopicAsync(userId, topicId, ct)
                    ?? throw new InvalidOperationException("Topic not found.");
        var graph = await LatestGraphAsync(userId, topicId, ct);
        var source = await GetSourceAsync(userId, topicId, sessionId, ct);
        var concepts = graph == null
            ? [new AssessmentBlueprintConceptDto { ConceptKey = StableKey(topic.Title), Label = topic.Title, Role = "target", ConfidenceStatus = "observed_only" }]
            : await _db.LearningConcepts
                .AsNoTracking()
                .Where(c => c.ConceptGraphSnapshotId == graph.Id)
                .OrderBy(c => c.Order)
                .Take(6)
                .Select(c => new AssessmentBlueprintConceptDto
                {
                    ConceptKey = c.StableKey,
                    Label = c.Label,
                    Role = c.Order == 0 ? "prerequisite_or_foundation" : "target",
                    DifficultyBand = c.DifficultyBand,
                    ConfidenceStatus = "observed_only"
                })
                .ToListAsync(ct);

        return new AssessmentBlueprintDto
        {
            TopicId = topicId,
            SessionId = sessionId,
            AssessmentMode = "diagnostic_check",
            UserSafeModeLabel = ModeLabel("diagnostic_check"),
            TargetConcepts = concepts,
            PrerequisiteConceptKeys = concepts.Where(c => c.Role.Contains("prerequisite", StringComparison.OrdinalIgnoreCase)).Select(c => c.ConceptKey).ToArray(),
            MisconceptionTargets = await BuildGraphMisconceptionTargetsAsync(graph?.Id, concepts.Select(c => c.ConceptKey).ToArray(), ct),
            DifficultyBand = "mixed",
            ItemCountTarget = 8,
            CognitiveSkillMix = CognitiveMix("diagnostic_check"),
            EvidenceMode = SourceMode(source?.EvidenceStatus ?? "evidence_insufficient"),
            ExplanationRequirement = "required_after_submit",
            RemediationRequirement = "safe_hint_after_submit",
            LeakageSafetyRequirements = LeakageRules(),
            Warnings = graph == null ? ["concept_graph_missing_fallback_used"] : BuildBlueprintWarnings(source?.EvidenceStatus, null)
        };
    }

    public async Task<AssessmentBlueprintDto> BuildMisconceptionProbeBlueprintAsync(
        Guid userId,
        Guid topicId,
        string conceptKey,
        string? misconceptionKey = null,
        CancellationToken ct = default)
    {
        var topic = await ResolveTopicAsync(userId, topicId, ct)
                    ?? throw new InvalidOperationException("Topic not found.");
        var source = await GetSourceAsync(userId, topicId, null, ct);
        var concept = await LatestConceptAsync(userId, topicId, conceptKey, ct);
        var label = concept?.Label ?? conceptKey;
        var targets = BuildMisconceptionTargets(conceptKey, DeserializeList(concept?.MisconceptionsJson), misconceptionKey, "misconception_probe");

        return new AssessmentBlueprintDto
        {
            TopicId = topic.Id,
            AssessmentMode = "misconception_probe",
            UserSafeModeLabel = ModeLabel("misconception_probe"),
            TargetConcepts =
            [
                new AssessmentBlueprintConceptDto
                {
                    ConceptKey = conceptKey,
                    Label = label,
                    Role = "target",
                    DifficultyBand = concept?.DifficultyBand ?? "core",
                    ConfidenceStatus = concept == null ? "observed_only" : "usable"
                }
            ],
            MisconceptionTargets = targets,
            DifficultyBand = concept?.DifficultyBand ?? "core",
            ItemCountTarget = 3,
            CognitiveSkillMix = CognitiveMix("misconception_probe"),
            EvidenceMode = SourceMode(source?.EvidenceStatus ?? "evidence_insufficient"),
            ExplanationRequirement = "required_after_submit",
            RemediationRequirement = "required_after_submit",
            LeakageSafetyRequirements = LeakageRules(),
            Warnings = targets.Count == 0 ? ["misconception_target_low_confidence"] : BuildBlueprintWarnings(source?.EvidenceStatus, null)
        };
    }

    public async Task<AssessmentQualityEvaluationDto> EvaluateAssessmentContractAsync(
        Guid userId,
        AssessmentQualityEvaluationRequestDto request,
        CancellationToken ct = default)
    {
        if (request.TopicId.HasValue && await ResolveTopicAsync(userId, request.TopicId, ct) == null)
        {
            throw new InvalidOperationException("Topic not found.");
        }

        var blueprint = NormalizeBlueprint(request.Blueprint, request);
        var items = request.Items.ToList();
        var blocking = new List<AssessmentQualityIssueDto>();
        var warnings = new List<AssessmentQualityIssueDto>();

        EvaluateBlueprint(blueprint, blocking, warnings);
        EvaluateItems(blueprint, items, blocking, warnings);

        var targetConcepts = blueprint.TargetConcepts.Select(c => c.ConceptKey).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var itemConcepts = items.Select(i => i.ConceptKey).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var conceptCoverage = targetConcepts.Length == 0 ? 0m : Ratio(itemConcepts.Count(c => targetConcepts.Contains(c, StringComparer.OrdinalIgnoreCase)), targetConcepts.Length);
        var misconceptionScore = blueprint.AssessmentMode == "misconception_probe"
            ? Ratio(items.Count(i => i.DistractorRationales.Any(r => !string.IsNullOrWhiteSpace(r.MisconceptionKey) || !string.IsNullOrWhiteSpace(r.Rationale))), Math.Max(1, items.Count))
            : blueprint.MisconceptionTargets.Count > 0 ? 0.85m : 0.65m;
        var distractorScore = Ratio(items.Count(i => i.DistractorRationales.Count >= 2 || blueprint.AssessmentMode != "misconception_probe"), Math.Max(1, items.Count));
        var leakageScore = blocking.Any(i => i.Code.Contains("leak", StringComparison.OrdinalIgnoreCase)) ? 0m : 1m;
        var remediationScore = Ratio(items.Count(i => !string.IsNullOrWhiteSpace(i.Explanation)), Math.Max(1, items.Count));

        if (conceptCoverage < 0.50m)
        {
            blocking.Add(Issue("concept_coverage_low", "blocking", "Olcum hedef kavramlari yeterince kapsamiyor."));
        }

        if (blueprint.AssessmentMode == "misconception_probe" && misconceptionScore < 0.70m)
        {
            blocking.Add(Issue("misconception_rationale_missing", "blocking", "Yanilgi yoklama sorularinda celdirici gerekcesi eksik."));
        }

        var status = blocking.Count > 0
            ? "needs_revision"
            : warnings.Count > 0 ? "usable" : "strong";

        var dto = new AssessmentQualityEvaluationDto
        {
            SnapshotId = Guid.NewGuid(),
            TopicId = request.TopicId ?? blueprint.TopicId,
            SessionId = request.SessionId ?? blueprint.SessionId,
            QualityStatus = status,
            ConceptCoverageScore = conceptCoverage,
            MisconceptionTargetingScore = misconceptionScore,
            DistractorQualityScore = distractorScore,
            LeakageSafetyScore = leakageScore,
            RemediationAlignmentScore = remediationScore,
            BlockingIssues = blocking,
            WarningIssues = warnings,
            Blueprint = blueprint,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var snapshot = new AssessmentQualitySnapshot
        {
            Id = dto.SnapshotId,
            UserId = userId,
            TopicId = dto.TopicId,
            SessionId = dto.SessionId,
            QuizRunId = request.QuizRunId,
            AssessmentDraftId = request.AssessmentDraftId,
            PlanQualitySnapshotId = request.PlanQualitySnapshotId ?? blueprint.PlanQualitySnapshotId,
            ActiveLessonSnapshotId = request.ActiveLessonSnapshotId,
            StudentContextSnapshotId = request.StudentContextSnapshotId,
            QualityStatus = status,
            ConceptCoverageScore = dto.ConceptCoverageScore,
            MisconceptionTargetingScore = dto.MisconceptionTargetingScore,
            DistractorQualityScore = dto.DistractorQualityScore,
            LeakageSafetyScore = dto.LeakageSafetyScore,
            RemediationAlignmentScore = dto.RemediationAlignmentScore,
            BlockingIssuesJson = JsonSerializer.Serialize(blocking, JsonOptions),
            WarningIssuesJson = JsonSerializer.Serialize(warnings, JsonOptions),
            AssessmentContractJson = JsonSerializer.Serialize(new { blueprint, items = items.Take(20) }, JsonOptions),
            CreatedAt = dto.CreatedAt.UtcDateTime
        };
        _db.AssessmentQualitySnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);
        return dto;
    }

    public async Task<AssessmentQualityEvaluationDto?> GetAssessmentQualitySnapshotAsync(
        Guid userId,
        Guid snapshotId,
        CancellationToken ct = default)
    {
        var entity = await _db.AssessmentQualitySnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == snapshotId && s.UserId == userId && !s.IsDeleted, ct);
        return entity == null ? null : ToDto(entity);
    }

    public async Task<AssessmentQualityEvaluationDto?> GetLatestAssessmentQualitySnapshotAsync(
        Guid userId,
        Guid topicId,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        if (await ResolveTopicAsync(userId, topicId, ct) == null)
        {
            return null;
        }

        var query = _db.AssessmentQualitySnapshots.AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId && !s.IsDeleted);
        if (sessionId.HasValue) query = query.Where(s => s.SessionId == sessionId);
        var entity = await query.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        return entity == null ? null : ToDto(entity);
    }

    private async Task<Topic?> ResolveTopicAsync(Guid userId, Guid? topicId, CancellationToken ct)
    {
        if (!topicId.HasValue || topicId.Value == Guid.Empty)
        {
            return null;
        }

        return await _db.Topics.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == topicId.Value && t.UserId == userId, ct);
    }

    private async Task<ConceptGraphSnapshot?> LatestGraphAsync(Guid userId, Guid topicId, CancellationToken ct) =>
        await _db.ConceptGraphSnapshots.AsNoTracking()
            .Where(g => g.UserId == userId && g.TopicId == topicId)
            .OrderByDescending(g => g.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<LearningConcept?> LatestConceptAsync(Guid userId, Guid topicId, string conceptKey, CancellationToken ct)
    {
        var graph = await LatestGraphAsync(userId, topicId, ct);
        if (graph == null) return null;
        return await _db.LearningConcepts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConceptGraphSnapshotId == graph.Id && c.StableKey == conceptKey, ct);
    }

    private async Task<SourceEvidenceBundleDto?> GetSourceAsync(Guid userId, Guid topicId, Guid? sessionId, CancellationToken ct) =>
        _sourceLifecycle == null
            ? null
            : await _sourceLifecycle.GetLatestSourceEvidenceBundleAsync(userId, topicId, sessionId, ct);

    private static PlanStepContractDto? ResolvePlanStep(PlanQualityEvaluationDto? plan, string? stepId, string? conceptKey)
    {
        if (plan?.PlanContract.Steps.Count > 0 != true)
        {
            return null;
        }

        return plan.PlanContract.Steps.FirstOrDefault(s =>
                   (!string.IsNullOrWhiteSpace(stepId) && s.StepId.Equals(stepId, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(conceptKey) && s.ConceptKey.Equals(conceptKey, StringComparison.OrdinalIgnoreCase)))
               ?? plan.PlanContract.Steps.First();
    }

    private async Task<IReadOnlyList<AssessmentMisconceptionTargetDto>> BuildGraphMisconceptionTargetsAsync(Guid? graphId, IReadOnlyList<string> conceptKeys, CancellationToken ct)
    {
        if (!graphId.HasValue)
        {
            return [];
        }

        var concepts = await _db.LearningConcepts.AsNoTracking()
            .Where(c => c.ConceptGraphSnapshotId == graphId.Value && conceptKeys.Contains(c.StableKey))
            .OrderBy(c => c.Order)
            .Take(6)
            .ToListAsync(ct);
        return concepts
            .SelectMany(c => DeserializeList(c.MisconceptionsJson).Take(2).Select(m => new AssessmentMisconceptionTargetDto
            {
                ConceptKey = c.StableKey,
                MisconceptionKey = StableKey(m),
                UserSafeLabel = m,
                ConfidenceStatus = "observed_only"
            }))
            .ToArray();
    }

    private static IReadOnlyList<AssessmentMisconceptionTargetDto> BuildMisconceptionTargets(
        string? conceptKey,
        IReadOnlyList<string> misconceptions,
        string? requested,
        string mode)
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(requested)) keys.Add(requested);
        keys.AddRange(misconceptions.Where(m => !string.IsNullOrWhiteSpace(m)).Take(3));
        if (keys.Count == 0 && mode == "misconception_probe") keys.Add("inferred_misconception");

        return keys.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(k => new AssessmentMisconceptionTargetDto
            {
                ConceptKey = conceptKey ?? string.Empty,
                MisconceptionKey = StableKey(k),
                UserSafeLabel = k == "inferred_misconception" ? "Olası yanilgi, dusuk guvenle yoklanacak" : k,
                ConfidenceStatus = k == "inferred_misconception" ? "observed_only" : "usable"
            })
            .ToArray();
    }

    private static AssessmentBlueprintDto NormalizeBlueprint(AssessmentBlueprintDto blueprint, AssessmentQualityEvaluationRequestDto request)
    {
        blueprint.TopicId ??= request.TopicId;
        blueprint.SessionId ??= request.SessionId;
        blueprint.PlanQualitySnapshotId ??= request.PlanQualitySnapshotId;
        blueprint.AssessmentMode = NormalizeMode(blueprint.AssessmentMode, null, "diagnostic_check");
        blueprint.UserSafeModeLabel = string.IsNullOrWhiteSpace(blueprint.UserSafeModeLabel)
            ? ModeLabel(blueprint.AssessmentMode)
            : blueprint.UserSafeModeLabel;
        blueprint.LeakageSafetyRequirements = blueprint.LeakageSafetyRequirements.Count == 0
            ? LeakageRules()
            : blueprint.LeakageSafetyRequirements;
        return blueprint;
    }

    private static void EvaluateBlueprint(AssessmentBlueprintDto blueprint, List<AssessmentQualityIssueDto> blocking, List<AssessmentQualityIssueDto> warnings)
    {
        if (!SupportedModes.Contains(blueprint.AssessmentMode))
        {
            blocking.Add(Issue("assessment_mode_unsupported", "blocking", "Olcum modu desteklenmiyor."));
        }

        if (blueprint.TargetConcepts.Count == 0 || blueprint.TargetConcepts.All(c => string.IsNullOrWhiteSpace(c.ConceptKey)))
        {
            blocking.Add(Issue("target_concept_missing", "blocking", "Olcum hedef kavram tasimiyor."));
        }

        if (blueprint.EvidenceMode is "evidence_insufficient" or "stale" or "degraded" or "unknown")
        {
            warnings.Add(Issue("source_readiness_limited", "warning", "Kaynak kaniti sinirli; sonuc kaynak kesinligi gibi sunulamaz."));
        }

        if (blueprint.AssessmentMode == "misconception_probe" && blueprint.MisconceptionTargets.Count == 0)
        {
            blocking.Add(Issue("misconception_target_missing", "blocking", "Yanilgi yoklama modu hedef yanilgi tasimiyor."));
        }
    }

    private static void EvaluateItems(AssessmentBlueprintDto blueprint, IReadOnlyList<AssessmentItemContractDto> items, List<AssessmentQualityIssueDto> blocking, List<AssessmentQualityIssueDto> warnings)
    {
        if (items.Count == 0)
        {
            warnings.Add(Issue("assessment_items_missing", "warning", "Henüz degerlendirilecek soru yok; blueprint hazirlik seviyesinde."));
            return;
        }

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ConceptKey))
            {
                blocking.Add(Issue("item_concept_missing", "blocking", "Soru hedef kavram tasimiyor.", item.ItemId));
            }

            if (LooksGeneric(item.Stem))
            {
                blocking.Add(Issue("generic_item", "blocking", "Soru konu/kavram yerine genel ifade olmus.", item.ItemId));
            }

            if (LooksProductTrivia(item.Stem) || item.OptionTexts.Any(LooksProductTrivia))
            {
                blocking.Add(Issue("product_trivia_item", "blocking", "Soru Orka ic isleyisini degil konu bilgisini olcmeli.", item.ItemId));
            }

            if (item.PublicDtoContainsCorrectAnswer || item.OptionTexts.Any(LeaksCorrectnessLabel) || LeaksCorrectnessLabel(item.Stem))
            {
                blocking.Add(Issue("answer_leakage", "blocking", "Soru veya secenek cevap anahtari sızdırıyor.", item.ItemId));
            }

            if (string.IsNullOrWhiteSpace(item.Explanation))
            {
                warnings.Add(Issue("explanation_missing", "warning", "Cevap sonrasi aciklama eksik.", item.ItemId));
            }

            if (blueprint.AssessmentMode == "misconception_probe" && item.DistractorRationales.Count == 0)
            {
                blocking.Add(Issue("distractor_rationale_missing", "blocking", "Yanilgi yoklama sorusunda celdirici gerekcesi eksik.", item.ItemId));
            }
        }
    }

    private static AssessmentQualityEvaluationDto ToDto(AssessmentQualitySnapshot entity)
    {
        var contract = DeserializeContract(entity.AssessmentContractJson);
        return new AssessmentQualityEvaluationDto
        {
            SnapshotId = entity.Id,
            TopicId = entity.TopicId,
            SessionId = entity.SessionId,
            QualityStatus = entity.QualityStatus,
            ConceptCoverageScore = entity.ConceptCoverageScore,
            MisconceptionTargetingScore = entity.MisconceptionTargetingScore,
            DistractorQualityScore = entity.DistractorQualityScore,
            LeakageSafetyScore = entity.LeakageSafetyScore,
            RemediationAlignmentScore = entity.RemediationAlignmentScore,
            BlockingIssues = DeserializeIssues(entity.BlockingIssuesJson),
            WarningIssues = DeserializeIssues(entity.WarningIssuesJson),
            Blueprint = contract?.Blueprint ?? new AssessmentBlueprintDto { TopicId = entity.TopicId, SessionId = entity.SessionId },
            CreatedAt = entity.CreatedAt
        };
    }

    private static AssessmentContractEnvelope? DeserializeContract(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<AssessmentContractEnvelope>(json, JsonOptions); }
        catch { return null; }
    }

    private static IReadOnlyList<AssessmentQualityIssueDto> DeserializeIssues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<AssessmentQualityIssueDto>();
        try { return JsonSerializer.Deserialize<List<AssessmentQualityIssueDto>>(json, JsonOptions) ?? []; }
        catch { return Array.Empty<AssessmentQualityIssueDto>(); }
    }

    private static IReadOnlyList<string> BuildBlueprintWarnings(string? sourceStatus, string? planStatus)
    {
        var warnings = new List<string>();
        if (sourceStatus is null or "evidence_insufficient" or "stale" or "degraded" or "unknown")
        {
            warnings.Add("source_readiness_limited");
        }

        if (planStatus is "needs_revision" or "insufficient")
        {
            warnings.Add("plan_quality_limited");
        }

        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string NormalizeMode(string? primary, string? secondary, string fallback)
    {
        var value = FirstNonEmpty(primary, secondary, fallback) ?? fallback;
        value = value.Trim().ToLowerInvariant();
        return SupportedModes.Contains(value) ? value : fallback;
    }

    private static string ModeLabel(string mode) => mode switch
    {
        "diagnostic_check" => "Seviye ve on kosul kontrolu",
        "micro_quiz" => "Kisa kavram kontrolu",
        "misconception_probe" => "Yanilgi yoklama",
        "retrieval_practice" => "Hatirlama pratigi",
        "readiness_check" => "Hazirlik kontrolu",
        "review_check" => "Tekrar kontrolu",
        _ => "Kisa olcum"
    };

    private static IReadOnlyList<string> CognitiveMix(string mode) => mode switch
    {
        "diagnostic_check" => ["conceptual", "procedural", "application", "analysis", "misconception_probe"],
        "misconception_probe" => ["misconception_probe", "application"],
        "retrieval_practice" => ["conceptual", "application"],
        "readiness_check" => ["application", "analysis"],
        "review_check" => ["conceptual", "retrieval"],
        _ => ["conceptual", "application"]
    };

    private static IReadOnlyList<string> LeakageRules() =>
    [
        "no_correct_answer_before_submit",
        "no_correctness_labels_in_options",
        "explanation_after_submit_only"
    ];

    private static string SourceMode(string? status) => status switch
    {
        "source_grounded" => "source_grounded",
        "wiki_backed" => "wiki_backed",
        "mixed" => "mixed",
        "degraded" => "degraded",
        "stale" => "stale",
        _ => "evidence_insufficient"
    };

    private static bool LooksGeneric(string? text)
    {
        var normalized = Normalize(text);
        return string.IsNullOrWhiteSpace(normalized) ||
               normalized is "review basics" or "study harder" or "what is correct" or "choose the best answer" ||
               normalized.Contains("generic pipeline", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksProductTrivia(string? text)
    {
        var normalized = Normalize(text);
        return normalized.Contains("orka ide", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("api endpoint", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("internal pipeline", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("sandbox", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LeaksCorrectnessLabel(string? text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        return Regex.IsMatch(normalized, @"^(a\)|b\)|c\)|d\))?\s*(dogru|yanlis|correct|wrong)\s*[:\-.]", RegexOptions.IgnoreCase) ||
               normalized.Contains("correct answer is", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("dogru cevap", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant()
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ı', 'i')
            .Replace('ö', 'o')
            .Replace('ç', 'c');

    private static AssessmentQualityIssueDto Issue(string code, string severity, string message, string? itemId = null) => new()
    {
        Code = code,
        Severity = severity,
        UserSafeMessage = message,
        ItemId = itemId
    };

    private static decimal Ratio(int numerator, int denominator) =>
        denominator <= 0 ? 0m : Math.Clamp(Math.Round(numerator / (decimal)denominator, 4), 0m, 1m);

    private static string StableKey(string value)
    {
        var normalized = Regex.Replace(Normalize(value), @"[^a-z0-9]+", "-", RegexOptions.IgnoreCase).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "concept" : normalized.Length <= 64 ? normalized : normalized[..64].Trim('-');
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? []; }
        catch { return []; }
    }

    private sealed class AssessmentContractEnvelope
    {
        public AssessmentBlueprintDto? Blueprint { get; set; }
    }
}

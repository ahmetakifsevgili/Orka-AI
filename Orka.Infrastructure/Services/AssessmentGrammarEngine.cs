using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnyAscii;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class AssessmentGrammarEngine : IAssessmentGrammarEngine
{
    private const string DiagnosticQuestionBankExamCode = "ORKA_LEARNING";
    private const string DiagnosticQuestionBankSource = "diagnostic_assessment_item";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly string[] CognitiveSkills =
    [
        "conceptual",
        "procedural",
        "application",
        "analysis",
        "misconception_probe"
    ];

    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService? _redis;
    private readonly IAssessmentQualityService? _quality;
    private readonly ILogger<AssessmentGrammarEngine> _logger;

    public AssessmentGrammarEngine(
        OrkaDbContext db,
        IRedisMemoryService? redis,
        ILogger<AssessmentGrammarEngine> logger,
        IAssessmentQualityService? quality = null)
    {
        _db = db;
        _redis = redis;
        _quality = quality;
        _logger = logger;
    }

    public async Task<AssessmentGrammarDraftDto> BuildOrLoadDraftAsync(
        Guid userId,
        Guid? topicId,
        Guid planRequestId,
        Guid quizRunId,
        ConceptGraphDto graph,
        int requestedQuestionCount,
        CancellationToken ct = default)
    {
        var draftId = planRequestId;
        var cacheKey = $"orka:v2:assessment-draft:{planRequestId:N}";
        var existing = await _db.AssessmentItems
            .AsNoTracking()
            .Where(i => i.UserId == userId && i.PlanRequestId == planRequestId)
            .OrderBy(i => i.Order)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            var grammar = ToGrammar(draftId, graph, existing, requestedQuestionCount);
            var existingQuality = await EvaluateQualityAsync(userId, topicId, planRequestId, quizRunId, grammar, graph, ct);
            return new AssessmentGrammarDraftDto
            {
                Grammar = grammar,
                DraftId = draftId,
                CacheKey = cacheKey,
                QualityRunId = existingQuality?.Id,
                QualityStatus = existingQuality?.QualityStatus ?? "unknown",
                QualityCacheKey = $"orka:v2:assessment-quality:{draftId:N}",
                CacheHit = true
            };
        }

        AssessmentGrammarDto? cached = null;
        if (_redis != null)
        {
            try
            {
                var payload = await _redis.GetJsonAsync(cacheKey);
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    cached = JsonSerializer.Deserialize<AssessmentGrammarDto>(payload, JsonOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[AssessmentGrammar] Redis read skipped. KeyRef={KeyRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeTextRef(cacheKey, "cache"),
                    LogPrivacyGuard.SafeExceptionType(ex));
            }
        }

        var grammarToSave = cached ?? BuildGrammar(draftId, graph, Math.Clamp(requestedQuestionCount, 15, 25));
        grammarToSave.DraftId = draftId;
        grammarToSave.ConceptGraphSnapshotId = graph.SnapshotId ?? grammarToSave.ConceptGraphSnapshotId;
        await SaveItemsAsync(userId, topicId, planRequestId, graph, grammarToSave, ct);
        var quality = await EvaluateQualityAsync(userId, topicId, planRequestId, quizRunId, grammarToSave, graph, ct);

        if (_redis != null)
        {
            try
            {
                await _redis.SetJsonAsync(cacheKey, JsonSerializer.Serialize(grammarToSave, JsonOptions), TimeSpan.FromHours(2));
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[AssessmentGrammar] Redis write skipped. KeyRef={KeyRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeTextRef(cacheKey, "cache"),
                    LogPrivacyGuard.SafeExceptionType(ex));
            }
        }

        return new AssessmentGrammarDraftDto
        {
            Grammar = grammarToSave,
            DraftId = draftId,
            CacheKey = cacheKey,
            QualityRunId = quality?.Id,
            QualityStatus = quality?.QualityStatus ?? "unknown",
            QualityCacheKey = $"orka:v2:assessment-quality:{draftId:N}",
            CacheHit = cached != null
        };
    }

    public string BuildPromptBlock(AssessmentGrammarDto grammar)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[ASSESSMENT GRAMMAR]");
        sb.AppendLine($"ConceptGraphSnapshotId: {grammar.ConceptGraphSnapshotId}");
        sb.AppendLine($"RequestedQuestionCount: {grammar.RequestedQuestionCount}");
        sb.AppendLine("Instruction: Generate each diagnostic question from the item specs below. Every returned question must carry the exact assessmentItemId and conceptKey.");
        sb.AppendLine("ItemSpecs:");
        foreach (var item in grammar.Items.OrderBy(i => i.Order))
        {
            sb.AppendLine($"- assessmentItemId={item.AssessmentItemId}; conceptKey={item.ConceptKey}; concept={item.ConceptLabel}; cognitiveSkill={item.CognitiveSkill}; difficulty={item.Difficulty}; misconceptionTarget={item.MisconceptionTarget}; evidenceExpected={item.EvidenceExpected}; outcomes={string.Join(",", item.LearningOutcomeKeys)}; scoringRule={item.ScoringRule}");
        }
        return sb.ToString();
    }

    public async Task<string> AttachQuestionMetadataAsync(
        string quizJson,
        AssessmentGrammarDto grammar,
        CancellationToken ct = default)
    {
        return await AttachQuestionMetadataCoreAsync(quizJson, grammar, allowOrderFallback: false, ct);
    }

    public async Task<string> AttachQuestionMetadataWithOrderFallbackAsync(
        string quizJson,
        AssessmentGrammarDto grammar,
        CancellationToken ct = default)
    {
        return await AttachQuestionMetadataCoreAsync(quizJson, grammar, allowOrderFallback: true, ct);
    }

    private async Task<string> AttachQuestionMetadataCoreAsync(
        string quizJson,
        AssessmentGrammarDto grammar,
        bool allowOrderFallback,
        CancellationToken ct = default)
    {
        var array = JsonNode.Parse(DiagnosticQuizQualityGate.ExtractJsonArray(quizJson)) as JsonArray;
        if (array == null)
        {
            return quizJson;
        }

        var items = grammar.Items.OrderBy(i => i.Order).ToList();
        var byId = items.ToDictionary(item => item.AssessmentItemId, item => item);
        var byKey = items
            .Where(item => !string.IsNullOrWhiteSpace(item.AssessmentItemKey))
            .ToDictionary(item => item.AssessmentItemKey, item => item, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<Guid>();
        for (var i = 0; i < array.Count && items.Count > 0; i++)
        {
            if (array[i] is not JsonObject question) continue;
            var spec = ResolveQuestionSpec(question, i, items, byId, byKey, seen, allowOrderFallback);
            question["assessmentItemId"] = spec.AssessmentItemId;
            question["assessmentItemKey"] = spec.AssessmentItemKey;
            question["conceptKey"] = spec.ConceptKey;
            question["conceptTag"] = spec.ConceptKey;
            question["skillTag"] = spec.ConceptKey;
            question["cognitiveSkill"] = spec.CognitiveSkill;
            question["questionType"] = spec.CognitiveSkill;
            question["difficulty"] = spec.Difficulty;
            var misconceptionTarget = string.IsNullOrWhiteSpace(spec.MisconceptionTarget)
                ? "evidence_insufficient"
                : NormalizeSpecificMisconceptionTarget(spec.MisconceptionTarget, spec.ConceptKey);
            question["misconceptionTarget"] = misconceptionTarget;
            question["expectedMisconceptionCategory"] = GetString(question, "expectedMisconceptionCategory", misconceptionTarget);
            question["evidenceExpected"] = spec.EvidenceExpected;
            question["scoringRule"] = spec.ScoringRule;
            question["learningOutcomeIds"] = ToJsonArray(spec.LearningOutcomeKeys);

            var existingRefs = question["sourceRefs"] as JsonObject ?? new JsonObject();
            existingRefs["assessmentItemId"] = spec.AssessmentItemId;
            existingRefs["assessmentItemKey"] = spec.AssessmentItemKey;
            existingRefs["conceptKey"] = spec.ConceptKey;
            existingRefs["conceptTag"] = GetString(question, "conceptTag", spec.ConceptKey);
            existingRefs["learningOutcomeIds"] = ToJsonArray(spec.LearningOutcomeKeys);
            existingRefs["cognitiveSkill"] = spec.CognitiveSkill;
            existingRefs["difficulty"] = spec.Difficulty;
            existingRefs["misconceptionTarget"] = misconceptionTarget;
            existingRefs["evidenceExpected"] = spec.EvidenceExpected;
            existingRefs["scoringRule"] = spec.ScoringRule;
            question["sourceRefs"] = existingRefs;
        }

        var enriched = array.ToJsonString(JsonOptions);
        var entitiesById = await _db.AssessmentItems
            .Where(item => item.PlanRequestId == grammar.DraftId)
            .ToDictionaryAsync(item => item.Id, ct);
        for (var i = 0; i < array.Count && items.Count > 0; i++)
        {
            if (array[i] is not JsonObject question) continue;
            var specId = ReadGuid(question, "assessmentItemId");
            if (specId.HasValue && entitiesById.TryGetValue(specId.Value, out var entity))
            {
                entity.GeneratedQuestionJson = array[i]?.ToJsonString(JsonOptions);
            }
        }
        await MaterializeDiagnosticQuestionItemsAsync(entitiesById.Values, ct);
        await _db.SaveChangesAsync(ct);
        return enriched;
    }

    private async Task MaterializeDiagnosticQuestionItemsAsync(
        IEnumerable<AssessmentItem> assessmentItems,
        CancellationToken ct)
    {
        var items = assessmentItems
            .Where(item => !string.IsNullOrWhiteSpace(item.GeneratedQuestionJson))
            .OrderBy(item => item.Order)
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        var examDefinition = await EnsureDiagnosticQuestionBankExamAsync(ct);
        var itemIds = items.Select(item => item.Id).ToList();
        var existingByAssessmentItemId = await _db.QuestionItems
            .Include(q => q.Options)
            .Include(q => q.Tags)
            .Where(q => q.AssessmentItemId.HasValue && itemIds.Contains(q.AssessmentItemId.Value))
            .ToDictionaryAsync(q => q.AssessmentItemId!.Value, ct);
        var now = DateTime.UtcNow;

        foreach (var item in items)
        {
            var projection = ParseGeneratedQuestionForBank(item);
            if (projection.Options.Count < 2)
            {
                continue;
            }

            if (!existingByAssessmentItemId.TryGetValue(item.Id, out var question))
            {
                question = new QuestionItem
                {
                    Id = item.Id,
                    OwnerUserId = item.UserId,
                    AssessmentItemId = item.Id,
                    CreatedAt = now
                };
                _db.QuestionItems.Add(question);
            }

            question.ExamDefinitionId = examDefinition.Id;
            question.LearningTopicId = item.TopicId;
            question.ConceptGraphSnapshotId = item.ConceptGraphSnapshotId;
            question.LearningConceptId = item.LearningConceptId;
            question.QuizRunId = item.QuizRunId;
            question.PlanRequestId = item.PlanRequestId;
            question.QuestionBankSource = DiagnosticQuestionBankSource;
            question.ConceptKey = EmptyToNull(item.ConceptKey);
            question.ConceptLabel = EmptyToNull(item.ConceptLabel);
            question.MisconceptionTarget = EmptyToNull(item.MisconceptionTarget);
            question.EvidenceExpected = EmptyToNull(item.EvidenceExpected);
            question.ScoringRuleJson = EmptyToNull(item.ScoringRuleJson);
            question.CalibrationStatus ??= "uncalibrated";
            question.VisualReadinessStatus = "not_required";
            question.QuestionType = "multiple_choice";
            question.Stem = projection.Stem;
            question.Difficulty = NormalizeBankDifficulty(item.Difficulty);
            question.CognitiveSkill = EmptyToNull(item.CognitiveSkill) ?? "conceptual";
            question.QualityStatus = "diagnostic_ready";
            question.LicenseStatus = "user_provided";
            question.SourceOrigin = "diagnostic_engine";
            question.SourceTitle = "Orka diagnostic assessment item";
            question.Explanation = projection.Explanation ?? string.Empty;
            question.UpdatedAt = now;

            question.Options.Clear();
            foreach (var option in projection.Options)
            {
                question.Options.Add(new QuestionOption
                {
                    OptionKey = option.OptionKey,
                    Text = option.Text,
                    IsCorrect = option.IsCorrect,
                    Rationale = option.Rationale,
                    MisconceptionKey = option.MisconceptionKey,
                    DiagnosticSignalJson = option.DiagnosticSignalJson,
                    SortOrder = option.SortOrder
                });
            }

            question.Tags.Clear();
            foreach (var tag in BuildDiagnosticQuestionTags(item))
            {
                question.Tags.Add(new QuestionTag { Tag = tag, CreatedAt = now });
            }
        }
    }

    private async Task<ExamDefinition> EnsureDiagnosticQuestionBankExamAsync(CancellationToken ct)
    {
        var existing = await _db.ExamDefinitions
            .FirstOrDefaultAsync(e => e.Code == DiagnosticQuestionBankExamCode && !e.IsDeleted, ct);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTime.UtcNow;
        var exam = new ExamDefinition
        {
            Id = Guid.NewGuid(),
            Code = DiagnosticQuestionBankExamCode,
            Name = "Orka Learning Question Bank",
            Description = "System container for plan-bound diagnostic and adaptive practice questions.",
            ExamFamily = "orka_learning",
            Visibility = "system",
            VerificationStatus = "model_assisted",
            OfficialClaimAllowed = false,
            SourceTitle = "Orka learning system",
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.ExamDefinitions.Add(exam);
        return exam;
    }

    private static DiagnosticBankQuestionProjection ParseGeneratedQuestionForBank(AssessmentItem item)
    {
        if (string.IsNullOrWhiteSpace(item.GeneratedQuestionJson))
        {
            return new DiagnosticBankQuestionProjection(FallbackDiagnosticStem(item), string.Empty, []);
        }

        try
        {
            var node = JsonNode.Parse(item.GeneratedQuestionJson);
            PublicTextNormalizer.RepairJsonStrings(node);
            var question = node switch
            {
                JsonObject obj => obj,
                JsonArray array => array.OfType<JsonObject>().FirstOrDefault(),
                _ => null
            };
            if (question is null)
            {
                return new DiagnosticBankQuestionProjection(FallbackDiagnosticStem(item), string.Empty, []);
            }

            var stem = FirstJsonString(question, "question", "stem", "prompt", "title") ?? FallbackDiagnosticStem(item);
            var explanation = FirstJsonString(question, "explanation", "rationale") ?? string.Empty;
            var correctHint = FirstJsonString(question, "correctAnswer", "correct_answer", "answer", "correctOption", "correctOptionId", "correct_option_id");
            var options = ParseGeneratedOptions(question["options"] as JsonArray, correctHint);
            return new DiagnosticBankQuestionProjection(CleanBankText(stem), CleanBankText(explanation), options);
        }
        catch
        {
            return new DiagnosticBankQuestionProjection(FallbackDiagnosticStem(item), string.Empty, []);
        }
    }

    private static List<DiagnosticBankOptionProjection> ParseGeneratedOptions(JsonArray? options, string? correctHint)
    {
        if (options is null || options.Count == 0)
        {
            return [];
        }

        var parsed = new List<DiagnosticBankOptionProjection>();
        for (var index = 0; index < options.Count; index++)
        {
            var node = options[index];
            var fallbackKey = ((char)('A' + Math.Min(parsed.Count, 25))).ToString(CultureInfo.InvariantCulture);
            var key = fallbackKey;
            var text = string.Empty;
            var rationale = string.Empty;
            var misconceptionKey = string.Empty;
            var diagnosticSignal = string.Empty;
            var explicitCorrect = false;

            if (node is JsonValue)
            {
                text = node.GetValue<string?>() ?? string.Empty;
            }
            else if (node is JsonObject obj)
            {
                key = FirstJsonString(obj, "id", "optionId", "key", "label", "value") ?? fallbackKey;
                text = FirstJsonString(obj, "text", "value", "label", "option", "id") ?? string.Empty;
                rationale = FirstJsonString(obj, "rationale", "reason", "diagnosticRationale") ?? string.Empty;
                misconceptionKey = FirstJsonString(obj, "misconceptionKey", "misconception", "diagnosticSignalIfChosen") ?? string.Empty;
                diagnosticSignal = FirstJsonString(obj, "diagnosticSignalIfChosen", "diagnosticSignal", "signal") ?? misconceptionKey;
                explicitCorrect = ReadBool(obj, "isCorrect") || ReadBool(obj, "correct");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            parsed.Add(new DiagnosticBankOptionProjection(
                CleanBankText(key),
                CleanBankText(text),
                explicitCorrect || MatchesCorrectHint(correctHint, key, text, fallbackKey),
                CleanBankText(rationale),
                CleanBankText(misconceptionKey),
                string.IsNullOrWhiteSpace(diagnosticSignal)
                    ? null
                    : JsonSerializer.Serialize(new { signal = CleanBankText(diagnosticSignal) }, JsonOptions),
                parsed.Count));
        }

        if (parsed.Count > 0 && !parsed.Any(o => o.IsCorrect) && !string.IsNullOrWhiteSpace(correctHint))
        {
            var match = parsed.FirstOrDefault(o => MatchesCorrectHint(correctHint, o.OptionKey, o.Text, o.OptionKey));
            if (match is not null)
            {
                var index = parsed.IndexOf(match);
                parsed[index] = match with { IsCorrect = true };
            }
        }

        return parsed;
    }

    private static IReadOnlyList<string> BuildDiagnosticQuestionTags(AssessmentItem item)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.ConceptKey)) tags.Add($"concept:{item.ConceptKey}");
        if (!string.IsNullOrWhiteSpace(item.CognitiveSkill)) tags.Add($"skill:{item.CognitiveSkill}");
        if (!string.IsNullOrWhiteSpace(item.MisconceptionTarget)) tags.Add($"misconception:{item.MisconceptionTarget}");
        if (!string.IsNullOrWhiteSpace(item.EvidenceExpected)) tags.Add("evidence:diagnostic");
        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizeBankDifficulty(string? difficulty)
    {
        var value = EmptyToNull(difficulty)?.ToLowerInvariant();
        return value switch
        {
            "easy" or "kolay" or "beginner" or "prerequisite" => "easy",
            "hard" or "zor" or "advanced" or "challenge" => "hard",
            _ => "medium"
        };
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CleanBankText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string? FirstJsonString(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!obj.TryGetPropertyValue(key, out var node) || node is null)
            {
                continue;
            }

            if (node is JsonValue value)
            {
                try
                {
                    var text = value.GetValue<string?>();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
                catch
                {
                    var text = value.ToJsonString(JsonOptions).Trim('"');
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }

            var serialized = node.ToJsonString(JsonOptions);
            if (!string.IsNullOrWhiteSpace(serialized) && serialized != "null")
            {
                return serialized.Trim('"');
            }
        }

        return null;
    }

    private static bool ReadBool(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue value)
        {
            return false;
        }

        try { return value.GetValue<bool>(); }
        catch
        {
            var text = value.ToString();
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool MatchesCorrectHint(string? correctHint, string? optionKey, string? optionText, string? fallbackKey)
    {
        if (string.IsNullOrWhiteSpace(correctHint))
        {
            return false;
        }

        var normalized = NormalizeComparison(correctHint);
        return normalized == NormalizeComparison(optionKey) ||
               normalized == NormalizeComparison(optionText) ||
               normalized == NormalizeComparison(fallbackKey);
    }

    private static string NormalizeComparison(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Trim('"').ToLowerInvariant();

    private static string FallbackDiagnosticStem(AssessmentItem item)
    {
        var concept = !string.IsNullOrWhiteSpace(item.ConceptLabel)
            ? item.ConceptLabel
            : !string.IsNullOrWhiteSpace(item.ConceptKey)
                ? item.ConceptKey
                : "this concept";

        return $"Diagnostic check for {concept}.";
    }

    private static AssessmentItemSpecDto ResolveQuestionSpec(
        JsonObject question,
        int index,
        IReadOnlyList<AssessmentItemSpecDto> orderedItems,
        IReadOnlyDictionary<Guid, AssessmentItemSpecDto> byId,
        IReadOnlyDictionary<string, AssessmentItemSpecDto> byKey,
        HashSet<Guid> seen,
        bool allowOrderFallback)
    {
        var id = ReadGuid(question, "assessmentItemId");
        AssessmentItemSpecDto? spec = null;
        if (id.HasValue && byId.TryGetValue(id.Value, out var byIdSpec))
        {
            spec = byIdSpec;
        }
        else
        {
            var key = GetString(question, "assessmentItemKey", string.Empty);
            if (!string.IsNullOrWhiteSpace(key) && byKey.TryGetValue(key, out var byKeySpec))
            {
                spec = byKeySpec;
            }
        }

        if (spec == null)
        {
            if (!allowOrderFallback)
            {
                throw new InvalidOperationException("Diagnostic question is missing a valid assessmentItemId/assessmentItemKey.");
            }

            spec = orderedItems[index % orderedItems.Count];
        }

        if (!seen.Add(spec.AssessmentItemId))
        {
            throw new InvalidOperationException($"Duplicate diagnostic assessmentItemId: {spec.AssessmentItemId}.");
        }

        return spec;
    }

    private static Guid? ReadGuid(JsonObject question, string propertyName)
    {
        var value = GetString(question, propertyName, string.Empty);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private async Task<AssessmentQualityDto?> EvaluateQualityAsync(
        Guid userId,
        Guid? topicId,
        Guid planRequestId,
        Guid quizRunId,
        AssessmentGrammarDto grammar,
        ConceptGraphDto graph,
        CancellationToken ct)
    {
        if (_quality == null) return null;
        try
        {
            return await _quality.EvaluateAndSaveAsync(userId, topicId, planRequestId, quizRunId, grammar, graph, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[AssessmentGrammar] Quality evaluation skipped. DraftRef={DraftRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(grammar.DraftId, "draft"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return null;
        }
    }

    private static AssessmentGrammarDto BuildGrammar(Guid draftId, ConceptGraphDto graph, int questionCount)
    {
        var concepts = graph.Concepts.Count > 0 ? graph.Concepts : [new LearningConceptDto { StableKey = "core", Label = graph.TopicTitle, Order = 0 }];
        var items = new List<AssessmentItemSpecDto>();
        for (var i = 0; i < questionCount; i++)
        {
            var concept = concepts[i % concepts.Count];
            var cognitive = SelectCognitiveSkill(i, questionCount);
            var difficulty = i < questionCount * 0.3 ? "easy" : i < questionCount * 0.75 ? "medium" : "hard";
            var misconception = BuildSpecificMisconceptionTarget(concept, i);
            var assessmentItemId = Guid.NewGuid();
            items.Add(new AssessmentItemSpecDto
            {
                AssessmentItemId = assessmentItemId,
                AssessmentItemKey = $"{graph.IntentHash}:{concept.StableKey}:{i + 1:00}",
                ConceptKey = concept.StableKey,
                ConceptLabel = concept.Label,
                CognitiveSkill = cognitive,
                Difficulty = difficulty,
                MisconceptionTarget = cognitive == "misconception_probe" ? misconception : "evidence_insufficient",
                EvidenceExpected = BuildEvidenceExpected(concept, cognitive),
                LearningOutcomeKeys = concept.LearningOutcomeKeys.Count > 0 ? concept.LearningOutcomeKeys : [$"{concept.StableKey}-outcome"],
                OptionQualityRules =
                [
                    "one clearly correct option",
                    "three plausible distractors grounded in the target misconception or nearby concepts",
                    "no correctness labels in option text",
                    "no Orka UI, sandbox, or internal pipeline wording"
                ],
                ScoringRule = "selected_option_exact_match",
                Order = i
            });
        }

        return new AssessmentGrammarDto
        {
            DraftId = draftId,
            ConceptGraphSnapshotId = graph.SnapshotId ?? Guid.Empty,
            IntentHash = graph.IntentHash,
            RequestedQuestionCount = questionCount,
            Items = items,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static string SelectCognitiveSkill(int index, int questionCount)
    {
        if (questionCount >= 15 && index % 3 == 2)
        {
            return "misconception_probe";
        }

        var nonProbe = CognitiveSkills.Where(skill => skill != "misconception_probe").ToArray();
        return nonProbe[index % nonProbe.Length];
    }

    private async Task SaveItemsAsync(
        Guid userId,
        Guid? topicId,
        Guid planRequestId,
        ConceptGraphDto graph,
        AssessmentGrammarDto grammar,
        CancellationToken ct)
    {
        var conceptIds = await _db.LearningConcepts
            .Where(c => c.ConceptGraphSnapshotId == grammar.ConceptGraphSnapshotId)
            .ToDictionaryAsync(c => c.StableKey, c => c.Id, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var item in grammar.Items)
        {
            _db.AssessmentItems.Add(new AssessmentItem
            {
                Id = item.AssessmentItemId,
                UserId = userId,
                TopicId = topicId,
                QuizRunId = null,
                PlanRequestId = planRequestId,
                ConceptGraphSnapshotId = grammar.ConceptGraphSnapshotId,
                LearningConceptId = conceptIds.TryGetValue(item.ConceptKey, out var conceptId) ? conceptId : null,
                AssessmentItemKey = item.AssessmentItemKey,
                ConceptKey = item.ConceptKey,
                ConceptLabel = item.ConceptLabel,
                QuestionType = item.CognitiveSkill,
                CognitiveSkill = item.CognitiveSkill,
                Difficulty = item.Difficulty,
                MisconceptionTarget = string.IsNullOrWhiteSpace(item.MisconceptionTarget) ? "evidence_insufficient" : NormalizeSpecificMisconceptionTarget(item.MisconceptionTarget, item.ConceptKey),
                EvidenceExpected = item.EvidenceExpected,
                OptionQualityRulesJson = JsonSerializer.Serialize(item.OptionQualityRules, JsonOptions),
                ScoringRuleJson = JsonSerializer.Serialize(new { type = item.ScoringRule }, JsonOptions),
                LearningOutcomeKeysJson = JsonSerializer.Serialize(item.LearningOutcomeKeys, JsonOptions),
                PromptSpecJson = JsonSerializer.Serialize(item, JsonOptions),
                Order = item.Order,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private static AssessmentGrammarDto ToGrammar(Guid draftId, ConceptGraphDto graph, IReadOnlyList<AssessmentItem> items, int requestedQuestionCount) => new()
    {
        DraftId = draftId,
        ConceptGraphSnapshotId = graph.SnapshotId ?? items.First().ConceptGraphSnapshotId,
        IntentHash = graph.IntentHash,
        RequestedQuestionCount = requestedQuestionCount,
        Items = items.Select(item =>
        {
            var spec = DeserializeSpec(item.PromptSpecJson);
            if (spec != null) return spec;
            return new AssessmentItemSpecDto
            {
                AssessmentItemId = item.Id,
                AssessmentItemKey = item.AssessmentItemKey,
                ConceptKey = item.ConceptKey,
                ConceptLabel = item.ConceptLabel,
                CognitiveSkill = item.CognitiveSkill,
                Difficulty = item.Difficulty,
                MisconceptionTarget = string.IsNullOrWhiteSpace(item.MisconceptionTarget) ? "evidence_insufficient" : NormalizeSpecificMisconceptionTarget(item.MisconceptionTarget, item.ConceptKey),
                EvidenceExpected = item.EvidenceExpected,
                LearningOutcomeKeys = DeserializeList(item.LearningOutcomeKeysJson),
                OptionQualityRules = DeserializeList(item.OptionQualityRulesJson),
                ScoringRule = "selected_option_exact_match",
                Order = item.Order
            };
        }).ToList()
    };

    private static AssessmentItemSpecDto? DeserializeSpec(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AssessmentItemSpecDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string BuildEvidenceExpected(LearningConceptDto concept, string cognitive) => cognitive switch
    {
        "conceptual" => $"recognizes the core meaning of {concept.Label}",
        "procedural" => $"chooses the correct step or sequence for {concept.Label}",
        "application" => $"applies {concept.Label} in a small scenario",
        "analysis" => $"compares evidence or traces a consequence for {concept.Label}",
        "misconception_probe" => $"rejects a common misconception about {concept.Label}",
        _ => $"shows evidence of understanding {concept.Label}"
    };

    private static string BuildSpecificMisconceptionTarget(LearningConceptDto concept, int index)
    {
        var raw = concept.Misconceptions.Count > 0
            ? concept.Misconceptions[index % concept.Misconceptions.Count]
            : string.Empty;
        return NormalizeSpecificMisconceptionTarget(raw, concept.StableKey);
    }

    private static string NormalizeSpecificMisconceptionTarget(string? value, string conceptKey)
    {
        var normalized = Slug(value);
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized is "commonmistakes" or "common-mistakes" or "common-misconception" or "misconception" or "conceptual" or "procedural")
        {
            normalized = $"{Slug(conceptKey)}-diagnostic-misconception";
        }

        return normalized;
    }

    private static string Slug(string? value)
    {
        var text = (value ?? string.Empty).ToLowerInvariant().Transliterate();
        var sb = new StringBuilder(text.Length);
        var previousDash = false;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                previousDash = false;
            }
            else if (!previousDash)
            {
                sb.Append('-');
                previousDash = true;
            }
        }

        return sb.ToString().Trim('-');
    }

    private static string GetString(JsonObject node, string key, string fallback)
    {
        var token = node[key];
        if (token is null)
        {
            return fallback;
        }

        if (token is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return string.IsNullOrWhiteSpace(text) ? fallback : text;
            }

            if (value.TryGetValue<Guid>(out var guid))
            {
                return guid == Guid.Empty ? fallback : guid.ToString();
            }

            if (value.TryGetValue<int>(out var integer))
            {
                return integer.ToString(CultureInfo.InvariantCulture);
            }
        }

        var serialized = token.ToJsonString().Trim('"');
        return string.IsNullOrWhiteSpace(serialized) ? fallback : serialized;
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }
        return array;
    }

    private sealed record DiagnosticBankQuestionProjection(
        string Stem,
        string? Explanation,
        List<DiagnosticBankOptionProjection> Options);

    private sealed record DiagnosticBankOptionProjection(
        string OptionKey,
        string Text,
        bool IsCorrect,
        string? Rationale,
        string? MisconceptionKey,
        string? DiagnosticSignalJson,
        int SortOrder);
}

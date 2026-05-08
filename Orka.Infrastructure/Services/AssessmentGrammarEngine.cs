using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class AssessmentGrammarEngine : IAssessmentGrammarEngine
{
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
                _logger.LogDebug(ex, "[AssessmentGrammar] Redis read skipped. Key={Key}", cacheKey);
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
                _logger.LogDebug(ex, "[AssessmentGrammar] Redis write skipped. Key={Key}", cacheKey);
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
        var array = JsonNode.Parse(DiagnosticQuizQualityGate.ExtractJsonArray(quizJson)) as JsonArray;
        if (array == null)
        {
            return quizJson;
        }

        var items = grammar.Items.OrderBy(i => i.Order).ToList();
        for (var i = 0; i < array.Count && items.Count > 0; i++)
        {
            if (array[i] is not JsonObject question) continue;
            var spec = items[i % items.Count];
            question["assessmentItemId"] = spec.AssessmentItemId;
            question["assessmentItemKey"] = spec.AssessmentItemKey;
            question["conceptKey"] = spec.ConceptKey;
            question["conceptTag"] = GetString(question, "conceptTag", spec.ConceptKey);
            question["skillTag"] = GetString(question, "skillTag", spec.ConceptKey);
            question["cognitiveSkill"] = spec.CognitiveSkill;
            question["questionType"] = GetString(question, "questionType", spec.CognitiveSkill);
            question["difficulty"] = spec.Difficulty;
            question["misconceptionTarget"] = spec.MisconceptionTarget;
            question["expectedMisconceptionCategory"] = GetString(question, "expectedMisconceptionCategory", spec.MisconceptionTarget);
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
            existingRefs["misconceptionTarget"] = spec.MisconceptionTarget;
            existingRefs["evidenceExpected"] = spec.EvidenceExpected;
            existingRefs["scoringRule"] = spec.ScoringRule;
            question["sourceRefs"] = existingRefs;
        }

        var enriched = array.ToJsonString(JsonOptions);
        var byId = await _db.AssessmentItems
            .Where(item => item.PlanRequestId == grammar.DraftId)
            .ToDictionaryAsync(item => item.Id, ct);
        for (var i = 0; i < array.Count && items.Count > 0; i++)
        {
            var spec = items[i % items.Count];
            if (byId.TryGetValue(spec.AssessmentItemId, out var entity))
            {
                entity.GeneratedQuestionJson = array[i]?.ToJsonString(JsonOptions);
            }
        }
        await _db.SaveChangesAsync(ct);
        return enriched;
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
            _logger.LogDebug(ex, "[AssessmentGrammar] Quality evaluation skipped. DraftId={DraftId}", grammar.DraftId);
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
            var cognitive = CognitiveSkills[i % CognitiveSkills.Length];
            var difficulty = i < questionCount * 0.3 ? "kolay" : i < questionCount * 0.75 ? "orta" : "zor";
            var misconception = concept.Misconceptions.Count > 0
                ? concept.Misconceptions[i % concept.Misconceptions.Count]
                : "common misconception";
            var assessmentItemId = Guid.NewGuid();
            items.Add(new AssessmentItemSpecDto
            {
                AssessmentItemId = assessmentItemId,
                AssessmentItemKey = $"{graph.IntentHash}:{concept.StableKey}:{i + 1:00}",
                ConceptKey = concept.StableKey,
                ConceptLabel = concept.Label,
                CognitiveSkill = cognitive,
                Difficulty = difficulty,
                MisconceptionTarget = cognitive == "misconception_probe" ? misconception : string.Empty,
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
                MisconceptionTarget = item.MisconceptionTarget,
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
                MisconceptionTarget = item.MisconceptionTarget,
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

    private static string GetString(JsonObject node, string key, string fallback)
    {
        var value = node[key]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(value) ? fallback : value!;
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
}

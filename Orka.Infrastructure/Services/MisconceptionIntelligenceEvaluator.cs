using System.Text.Json;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Chat;
using Orka.Core.Entities;

namespace Orka.Infrastructure.Services;

public sealed record MisconceptionIntelligenceResult(
    MisconceptionSignalDto MisconceptionSignal,
    LearningSignalConfidenceDto LearningSignalConfidence,
    RemediationSeedDto RemediationSeed);

public static class MisconceptionIntelligenceEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static MisconceptionIntelligenceResult FromQuizAttempt(
        QuizAttempt attempt,
        MistakeClassificationResult? mistake,
        KnowledgeTracingStateDto? tracingState,
        EvidenceQualityDto? evidenceQuality = null)
    {
        var metadata = ReadMetadata(attempt.SourceRefsJson);
        var conceptKey = FirstNonEmpty(
            tracingState?.ConceptKey,
            GetString(metadata, "conceptKey"),
            GetString(metadata, "conceptTag"),
            mistake?.ConceptTag,
            mistake?.SkillTag,
            attempt.SkillTag);
        var label = FirstNonEmpty(
            tracingState?.Label,
            GetString(metadata, "conceptLabel"),
            GetString(metadata, "conceptTag"),
            conceptKey,
            "Zayıf kavram")!;
        evidenceQuality ??= TryReadEvidenceQuality(metadata);

        var category = NormalizeCategory(
            mistake?.Category ?? GetString(metadata, "mistakeCategory") ?? GetString(metadata, "misconceptionTarget"),
            metadata,
            evidenceQuality);
        var confidenceValue = mistake?.Confidence ?? 0.35;
        var gate = LearningSignalConfidenceGate.Evaluate(
            isWrong: !attempt.IsCorrect,
            hasConcept: !string.IsNullOrWhiteSpace(conceptKey),
            classifierConfidence: (decimal)confidenceValue,
            repeatedWrongCount: tracingState?.IncorrectCount ?? 0,
            tracingConfidence: tracingState?.Confidence,
            evidenceQuality: evidenceQuality,
            category: category);
        var confidence = gate.Confidence;
        var evidenceBasis = BuildEvidenceBasis(attempt, mistake, tracingState, evidenceQuality, category);

        var signal = new MisconceptionSignalDto
        {
            Category = category,
            UserSafeLabel = UserSafeLabel(category),
            Confidence = confidence,
            ConfidenceStatus = gate.Status,
            TopicId = attempt.TopicId,
            ConceptKey = conceptKey ?? string.Empty,
            Label = label,
            SafeHint = SafeHint(category),
            EvidenceBasis = evidenceBasis
        };

        return new MisconceptionIntelligenceResult(
            signal,
            gate,
            BuildRemediationSeed(signal));
    }

    public static MisconceptionIntelligenceResult FromMistakeClassification(
        Guid? topicId,
        string? conceptKey,
        string? label,
        MistakeClassificationResult result,
        EvidenceQualityDto? evidenceQuality = null)
    {
        var category = NormalizeCategory(result.Category, null, evidenceQuality);
        var gate = LearningSignalConfidenceGate.Evaluate(
            isWrong: true,
            hasConcept: !string.IsNullOrWhiteSpace(conceptKey) || !string.IsNullOrWhiteSpace(result.ConceptTag),
            classifierConfidence: (decimal)result.Confidence,
            repeatedWrongCount: 0,
            tracingConfidence: null,
            evidenceQuality: evidenceQuality,
            category: category);

        var basis = new List<string> { "mistake_classifier" };
        if (!string.IsNullOrWhiteSpace(result.ConceptTag)) basis.Add("concept_hint");
        if (evidenceQuality?.Status is "weak" or "missing") basis.Add("evidence_limited");

        var signal = new MisconceptionSignalDto
        {
            Category = category,
            UserSafeLabel = UserSafeLabel(category),
            Confidence = gate.Confidence,
            ConfidenceStatus = gate.Status,
            TopicId = topicId,
            ConceptKey = FirstNonEmpty(conceptKey, result.ConceptTag, result.SkillTag) ?? string.Empty,
            Label = FirstNonEmpty(label, result.ConceptTag, result.SkillTag, "Zayıf kavram")!,
            SafeHint = SafeHint(category),
            EvidenceBasis = basis
        };

        return new MisconceptionIntelligenceResult(signal, gate, BuildRemediationSeed(signal));
    }

    public static MisconceptionIntelligenceResult FromEvaluatorProjection(
        Guid? topicId,
        int score,
        string? evaluatorFeedback,
        string? conceptKey = null)
    {
        var category = NormalizeCategory(FeedbackCategoryHint(evaluatorFeedback), null, null);
        var baseConfidence = score <= 4 ? 0.55m : 0.42m;
        var gate = LearningSignalConfidenceGate.Evaluate(
            isWrong: true,
            hasConcept: !string.IsNullOrWhiteSpace(conceptKey),
            classifierConfidence: baseConfidence,
            repeatedWrongCount: 0,
            tracingConfidence: null,
            evidenceQuality: null,
            category: category);

        var signal = new MisconceptionSignalDto
        {
            Category = category,
            UserSafeLabel = UserSafeLabel(category),
            Confidence = gate.Confidence,
            ConfidenceStatus = gate.Status,
            TopicId = topicId,
            ConceptKey = conceptKey ?? string.Empty,
            Label = string.IsNullOrWhiteSpace(conceptKey) ? "Tutor cevabı" : conceptKey!,
            SafeHint = "Tutor cevabı için düşük güvenli bir öğrenme sinyali oluştu; kesin teşhis olarak kullanılmaz.",
            EvidenceBasis = ["evaluator_projection"]
        };

        return new MisconceptionIntelligenceResult(signal, gate, BuildRemediationSeed(signal));
    }

    public static MisconceptionIntelligenceResult FromWeakConcept(
        Guid? topicId,
        string conceptKey,
        string label,
        decimal masteryProbability,
        decimal confidence,
        int incorrectCount,
        string? remediationNeed)
    {
        var category = string.Equals(remediationNeed, "prerequisite", StringComparison.OrdinalIgnoreCase)
            ? "missing_prerequisite"
            : masteryProbability < 0.45m ? "concept_confusion" : "unknown";
        var gate = LearningSignalConfidenceGate.Evaluate(
            isWrong: true,
            hasConcept: !string.IsNullOrWhiteSpace(conceptKey),
            classifierConfidence: confidence,
            repeatedWrongCount: incorrectCount,
            tracingConfidence: confidence,
            evidenceQuality: null,
            category: category);
        var signal = new MisconceptionSignalDto
        {
            Category = category,
            UserSafeLabel = UserSafeLabel(category),
            Confidence = gate.Confidence,
            ConfidenceStatus = gate.Status,
            TopicId = topicId,
            ConceptKey = conceptKey,
            Label = string.IsNullOrWhiteSpace(label) ? conceptKey : label,
            SafeHint = SafeHint(category),
            EvidenceBasis = incorrectCount >= 2 ? ["knowledge_tracing", "repeated_wrong_concept"] : ["knowledge_tracing"]
        };
        return new MisconceptionIntelligenceResult(signal, gate, BuildRemediationSeed(signal));
    }

    public static RemediationSeedDto BuildRemediationSeed(MisconceptionSignalDto signal)
    {
        var (first, secondary) = signal.Category switch
        {
            "missing_prerequisite" => ("prerequisite_review", new[] { "wiki_review", "tutor_explain", "practice_quiz" }),
            "source_misread" => ("source_check", new[] { "wiki_review", "tutor_explain", "practice_quiz" }),
            "definition_gap" => ("wiki_review", new[] { "tutor_explain", "practice_quiz" }),
            "calculation_error" or "procedure_gap" or "application_gap" or "careless_slip" => ("practice_quiz", new[] { "tutor_explain", "wiki_review" }),
            "concept_confusion" => ("tutor_explain", new[] { "wiki_review", "practice_quiz" }),
            _ => ("tutor_explain", new[] { "wiki_review", "practice_quiz" })
        };

        return new RemediationSeedDto
        {
            ConceptKey = signal.ConceptKey,
            Label = signal.Label,
            TopicId = signal.TopicId,
            Reason = signal.SafeHint,
            Confidence = signal.Confidence,
            ConfidenceStatus = signal.ConfidenceStatus,
            MisconceptionCategory = signal.Category,
            UserSafeMisconceptionLabel = signal.UserSafeLabel,
            FirstAction = first,
            SecondaryActions = secondary,
            EvidenceBasis = signal.EvidenceBasis
        };
    }

    public static string NormalizeCategory(string? category, IReadOnlyDictionary<string, string?>? metadata = null, EvidenceQualityDto? evidenceQuality = null)
    {
        var normalized = (category ?? string.Empty).Trim().ToLowerInvariant();
        var mapped = normalized switch
        {
            "conceptual" or "conceptualmisunderstanding" or "concept_confusion" => "concept_confusion",
            "prerequisite" or "missing_prerequisite" => "missing_prerequisite",
            "formulamisuse" or "calculation" or "calculation_error" => "calculation_error",
            "vocabulary" or "definition" or "definition_gap" => "definition_gap",
            "procedural" or "procedure" or "codesyntax" or "code_runtime" or "coderuntime" or "procedure_gap" => "procedure_gap",
            "codelogic" or "application" or "application_gap" => "application_gap",
            "careless" or "careless_slip" => "careless_slip",
            "misreadquestion" or "source_misread" => HasSourceContext(metadata, evidenceQuality) ? "source_misread" : "application_gap",
            _ => "unknown"
        };

        if (mapped == "source_misread" && IsEvidenceMissingOrWeak(evidenceQuality))
        {
            return "source_misread";
        }

        return mapped;
    }

    private static IReadOnlyList<string> BuildEvidenceBasis(
        QuizAttempt attempt,
        MistakeClassificationResult? mistake,
        KnowledgeTracingStateDto? tracingState,
        EvidenceQualityDto? evidenceQuality,
        string category)
    {
        var basis = new List<string> { "wrong_quiz_attempt" };
        if (mistake != null) basis.Add("mistake_classifier");
        if (tracingState != null) basis.Add("knowledge_tracing");
        if ((tracingState?.IncorrectCount ?? 0) >= 2) basis.Add("repeated_wrong_concept");
        if (!string.IsNullOrWhiteSpace(attempt.SourceRefsJson)) basis.Add("quiz_metadata");
        if (category == "source_misread" && evidenceQuality != null) basis.Add("source_context");
        if (evidenceQuality?.Status is "partial" or "weak" or "missing") basis.Add("evidence_limited");
        return basis.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray();
    }

    private static IReadOnlyDictionary<string, string?> ReadMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            }

            return doc.RootElement.EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static EvidenceQualityDto? TryReadEvidenceQuality(IReadOnlyDictionary<string, string?> metadata)
    {
        if (!metadata.TryGetValue("evidenceQuality", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EvidenceQualityDto>(raw, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, string?> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static bool HasSourceContext(IReadOnlyDictionary<string, string?>? metadata, EvidenceQualityDto? evidenceQuality)
    {
        if (evidenceQuality is { SourceCount: > 0 } or { RetrievedEvidenceCount: > 0 })
        {
            return true;
        }

        if (metadata == null)
        {
            return false;
        }

        return metadata.Keys.Any(key =>
            key.Contains("source", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("citation", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("wiki", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEvidenceMissingOrWeak(EvidenceQualityDto? evidenceQuality) =>
        evidenceQuality?.Status is "weak" or "missing";

    private static string FeedbackCategoryHint(string? feedback)
    {
        var text = (feedback ?? string.Empty).ToLowerInvariant();
        if (text.Contains("definition") || text.Contains("tanım") || text.Contains("terim")) return "definition_gap";
        if (text.Contains("prerequisite") || text.Contains("ön koşul") || text.Contains("onkosul")) return "missing_prerequisite";
        if (text.Contains("step") || text.Contains("adım") || text.Contains("procedure")) return "procedure_gap";
        if (text.Contains("source") || text.Contains("citation") || text.Contains("kaynak")) return "source_misread";
        if (text.Contains("formula") || text.Contains("calculation") || text.Contains("hesap")) return "calculation_error";
        if (text.Contains("careless") || text.Contains("dikkat")) return "careless_slip";
        if (text.Contains("concept") || text.Contains("kavram")) return "concept_confusion";
        return "unknown";
    }

    private static string UserSafeLabel(string category) => category switch
    {
        "concept_confusion" => "Kavram karışıklığı olabilir",
        "missing_prerequisite" => "Ön koşul eksikliği olabilir",
        "calculation_error" => "Hesaplama adımı karışmış olabilir",
        "definition_gap" => "Tanım boşluğu olabilir",
        "application_gap" => "Uygulama bağlantısı eksik olabilir",
        "procedure_gap" => "İşlem sırası karışmış olabilir",
        "careless_slip" => "Dikkat hatası olabilir",
        "source_misread" => "Kaynak okuma bağlantısı zayıf olabilir",
        _ => "Yanılgı sinyali belirsiz"
    };

    private static string SafeHint(string category) => category switch
    {
        "concept_confusion" => "Kavramı kısa örnekle yeniden kurmak iyi olur.",
        "missing_prerequisite" => "Ön koşulu hızlıca gözden geçirmek iyi olur.",
        "calculation_error" => "Benzer bir pratik soruyla adımı kontrol etmek iyi olur.",
        "definition_gap" => "Tanımı ve bir örneği Wiki’den tekrar etmek iyi olur.",
        "application_gap" => "Kavramı yeni bir örneğe uygulayarak kontrol etmek iyi olur.",
        "procedure_gap" => "İşlem sırasını kısa kontrol listesiyle tekrar etmek iyi olur.",
        "careless_slip" => "Son adımı işaret, birim ve kopyalama açısından kontrol etmek iyi olur.",
        "source_misread" => "Kaynak parçasını tekrar kontrol etmek iyi olur.",
        _ => "Tutor’dan kısa bir telafi açıklaması almak iyi olur."
    };
}

public static class LearningSignalConfidenceGate
{
    public static LearningSignalConfidenceDto Evaluate(
        bool isWrong,
        bool hasConcept,
        decimal classifierConfidence,
        int repeatedWrongCount,
        decimal? tracingConfidence,
        EvidenceQualityDto? evidenceQuality,
        string? category)
    {
        var reasons = new List<string>();
        if (!isWrong)
        {
            return new LearningSignalConfidenceDto
            {
                Status = "ignored",
                Confidence = 0m,
                Reasons = ["not_wrong_answer"]
            };
        }

        if (!hasConcept)
        {
            reasons.Add("missing_concept_mapping");
        }
        else
        {
            reasons.Add("concept_mapped");
        }

        if (classifierConfidence >= 0.70m) reasons.Add("classifier_confident");
        if (repeatedWrongCount >= 2) reasons.Add("repeated_wrong_concept");
        if (tracingConfidence >= 0.60m) reasons.Add("knowledge_tracing_confident");
        if (evidenceQuality?.Status is "partial" or "weak" or "missing") reasons.Add("evidence_limited");

        var rawConfidence = new[] { classifierConfidence, tracingConfidence ?? 0m, repeatedWrongCount >= 2 ? 0.75m : 0m }.Max();
        var confidence = Math.Clamp(rawConfidence, 0m, 1m);
        var sourceMisreadLimited = string.Equals(category, "source_misread", StringComparison.OrdinalIgnoreCase) &&
                                   evidenceQuality?.Status is "weak" or "missing";
        if (sourceMisreadLimited)
        {
            confidence = Math.Min(confidence, 0.45m);
            reasons.Add("source_misread_evidence_capped");
        }

        var status = hasConcept && !sourceMisreadLimited &&
                     (classifierConfidence >= 0.70m || repeatedWrongCount >= 2 || tracingConfidence >= 0.60m)
            ? "usable"
            : hasConcept || classifierConfidence > 0m
                ? "observed_only"
                : "ignored";

        return new LearningSignalConfidenceDto
        {
            Status = status,
            Confidence = Math.Round(confidence, 2),
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray()
        };
    }
}

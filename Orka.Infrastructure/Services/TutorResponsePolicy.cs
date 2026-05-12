using Orka.Core.DTOs;
using Orka.Core.DTOs.Chat;

namespace Orka.Infrastructure.Services;

public sealed record TutorResponsePolicyDecision(
    string TutorResponseMode,
    string EvidencePolicy,
    string PersonalizationMode,
    string MasteryBasis,
    IReadOnlyList<string> WeakConceptHints);

public static class TutorResponsePolicy
{
    public static TutorResponsePolicyDecision Decide(TutorTurnStateDto state)
    {
        var evidencePolicy = EvidencePolicyFor(state.EvidenceQuality, state.GroundingStatus, state.SourceEvidenceCount);
        var personalizationMode = PersonalizationModeFor(state.MasteryProbability, state.Confidence);
        var masteryBasis = MasteryBasisFor(state);
        var weakConceptHints = BuildWeakConceptHints(state);
        var responseMode = ResponseModeFor(
            state.UserMessage,
            state.EvidenceQuality,
            state.MasteryProbability,
            state.Confidence,
            state.LearnerState,
            state.DirectAnswerRisk);

        return new TutorResponsePolicyDecision(
            responseMode,
            evidencePolicy,
            personalizationMode,
            masteryBasis,
            weakConceptHints);
    }

    public static string EvidencePolicyFor(EvidenceQualityDto? evidenceQuality, string? groundingStatus, int sourceEvidenceCount)
    {
        var status = Normalize(evidenceQuality?.Status);
        return status switch
        {
            "strong" => "cite_available_sources",
            "partial" => "evidence_limited_caution",
            "weak" => "evidence_weak_verify",
            "missing" => "model_ok_no_source_claim",
            _ when sourceEvidenceCount > 0 && string.Equals(groundingStatus, "source_grounded", StringComparison.OrdinalIgnoreCase)
                => "cite_available_sources_with_caution",
            _ => "unknown_source_caution"
        };
    }

    public static string ResponseModeFor(
        string userMessage,
        EvidenceQualityDto? evidenceQuality,
        decimal? masteryProbability,
        decimal? confidence,
        string? learnerState,
        bool directAnswerRisk)
    {
        var normalized = TutorSignalHeuristics.Normalize(userMessage);
        var evidenceStatus = Normalize(evidenceQuality?.Status);
        var conciseIntent = TutorSignalHeuristics.ContainsAny(normalized, "kisa", "kısa", "ozet", "özet", "tek cumle", "tek cümle", "hizli", "hızlı");
        var learningIntent = TutorSignalHeuristics.ContainsAny(normalized, "anlat", "acikla", "açıkla", "neden", "coz", "çöz", "ogren", "öğren", "konu", "ders", "kaynak", "wiki", "dokuman", "doküman");
        if (evidenceStatus is "partial" or "weak" || (evidenceStatus == "missing" && learningIntent && !conciseIntent))
            return "evidence_limited";
        if (directAnswerRisk ||
            string.Equals(learnerState, "needs_remediation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(learnerState, "weak_mastery", StringComparison.OrdinalIgnoreCase) ||
            masteryProbability < 0.45m ||
            confidence < 0.45m)
            return "recovery";
        if (conciseIntent)
            return "concise";
        if (TutorSignalHeuristics.ContainsAny(normalized, "derin", "detay", "ayrinti", "ayrıntı", "ileri", "neden") &&
            masteryProbability >= 0.60m &&
            confidence >= 0.50m)
            return "deep";

        return "standard";
    }

    public static string PersonalizationModeFor(decimal? masteryProbability, decimal? confidence)
    {
        if (!masteryProbability.HasValue && !confidence.HasValue)
            return "unknown";
        if (masteryProbability < 0.45m || confidence < 0.45m)
            return "beginner";
        if (masteryProbability >= 0.75m && confidence >= 0.60m)
            return "advanced";
        return "intermediate";
    }

    public static bool ShouldSuppressNextCheck(
        IEnumerable<string>? providerWarnings,
        string? fallbackReason,
        string? tutorResponseMode,
        string? evidenceStatus)
    {
        if (!string.IsNullOrWhiteSpace(fallbackReason))
            return true;

        var warnings = providerWarnings?.Where(w => !string.IsNullOrWhiteSpace(w)).Select(Normalize).ToArray() ?? Array.Empty<string>();
        if (warnings.Any(w => w.Contains("quota", StringComparison.Ordinal) ||
                              w.Contains("provider", StringComparison.Ordinal) ||
                              w.Contains("timeout", StringComparison.Ordinal) ||
                              w.Contains("auth", StringComparison.Ordinal)))
            return true;

        if (Normalize(tutorResponseMode) == "concise" && Normalize(evidenceStatus) is "unknown" or "")
            return true;

        return false;
    }

    public static IReadOnlyList<string> BuildWeakConceptHints(TutorTurnStateDto state) =>
        state.RecentMistakes
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

    public static string NextCheckFor(TutorTurnStateDto state, string teachingMode, TutorResponsePolicyDecision decision)
    {
        var evidenceStatus = Normalize(state.EvidenceQuality?.Status);
        if (decision.TutorResponseMode == "concise" && !state.DirectAnswerRisk)
            return string.Empty;
        if (evidenceStatus == "missing")
            return "Bu konu için kaynak ekleyip cevabı birlikte doğrulayalım mı?";
        if (evidenceStatus is "weak" or "partial")
            return "Cevaptaki hangi adımı kaynakla kontrol etmek istersin?";

        var concept = string.IsNullOrWhiteSpace(state.ActiveConceptLabel) ? "bu kavram" : state.ActiveConceptLabel;
        return teachingMode switch
        {
            "challenge" => $"{concept} için bir karşı örnek veya daha zor bir uygulama adımı önerebilir misin?",
            "code_lab" => "Bu kod görevinin beklenen çıktısını çalıştırmadan önce tahmin eder misin?",
            "visualize" => $"Diyagramdaki hangi ok {concept} için en kritik ilişkiyi gösteriyor?",
            "remediate" => $"{concept} kısmını kendi cümlenle bir örnekle açıklar mısın?",
            _ => $"{concept} için en küçük doğru örneği birlikte kurabilir miyiz?"
        };
    }

    private static string MasteryBasisFor(TutorTurnStateDto state)
    {
        if (state.RecentMistakes.Count > 0 ||
            string.Equals(state.RemediationNeed, "high", StringComparison.OrdinalIgnoreCase) ||
            state.LearnerState.Contains("remediation", StringComparison.OrdinalIgnoreCase))
            return "weak_concept_signals";
        if (state.Confidence < 0.45m)
            return "low_confidence";
        if (state.MasteryProbability.HasValue)
            return "mastery_probability";
        return "default";
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class MistakeClassifierService : IMistakeClassifierService
{
    private readonly ILearningSignalService _signals;
    private readonly ILogger<MistakeClassifierService> _logger;

    public MistakeClassifierService(
        ILearningSignalService signals,
        ILogger<MistakeClassifierService> logger)
    {
        _signals = signals;
        _logger = logger;
    }

    public Task<MistakeClassificationResult> ClassifyAsync(MistakeClassificationRequest request, CancellationToken ct = default)
    {
        var text = Normalize($"{request.Question}\n{request.ExpectedAnswer}\n{request.StudentAnswer}\n{request.Explanation}\n{request.CodePhase}\n{request.CompileError}\n{request.RuntimeError}");
        var category = "Unknown";
        var confidence = 0.35;
        var reason = "No strong mistake pattern was detected.";
        var hint = "Ask one small check question and review the nearest prerequisite.";
        var reviewPressure = 1;
        var suggestFlashcard = false;

        if (ContainsAny(text, "compile", "compiler", "syntax", "cs100", "expected", "invalid syntax", "; expected", "derleme", "sözdiz", "sozdiz"))
        {
            category = "CodeSyntax";
            confidence = 0.92;
            reason = "Compile/syntax error terms were detected.";
            hint = "Focus on syntax, missing tokens, imports, type names, and one minimal compiling example.";
            reviewPressure = 3;
            suggestFlashcard = true;
        }
        else if (ContainsAny(text, "runtime", "exception", "nullreference", "indexerror", "out of range", "division by zero", "timeout", "sonsuz", "zaman aşımı", "zaman asimi"))
        {
            category = ContainsAny(text, "timeout", "sonsuz", "zaman") ? "CodeLogic" : "CodeRuntime";
            confidence = 0.88;
            reason = "Runtime/exception/timeout pattern was detected.";
            hint = "Trace the failing state step by step and ask for one dry-run practice.";
            reviewPressure = 3;
            suggestFlashcard = true;
        }
        else if (ContainsAny(text, "formula", "formül", "formul", "denklem", "equation", "yanlış formül", "wrong formula"))
        {
            category = "FormulaMisuse";
            confidence = 0.82;
            reason = "Formula/equation misuse terms were detected.";
            hint = "Review when the formula applies and contrast it with a near-miss example.";
            reviewPressure = 3;
            suggestFlashcard = true;
        }
        else if (ContainsAny(text, "kavram", "concept", "neden", "why", "mantık", "mantik", "principle"))
        {
            category = "Conceptual";
            confidence = 0.78;
            reason = "Conceptual-language pattern was detected.";
            hint = "Rebuild the concept using a concrete analogy before doing procedures.";
            reviewPressure = 3;
            suggestFlashcard = true;
        }
        else if (ContainsAny(text, "adım", "adim", "step", "procedure", "sıra", "sira", "algorithm"))
        {
            category = "Procedural";
            confidence = 0.76;
            reason = "Step/order/procedure pattern was detected.";
            hint = "Practice the procedure as a short checklist and stop after each step.";
            reviewPressure = 2;
            suggestFlashcard = true;
        }
        else if (ContainsAny(text, "dikkat", "careless", "işaret", "isaret", "eksi", "plus", "minus", "copy"))
        {
            category = "Careless";
            confidence = 0.72;
            reason = "Careless arithmetic/copy/sign pattern was detected.";
            hint = "Use a final verification pass focused on signs, units, and copied values.";
            reviewPressure = 1;
            suggestFlashcard = false;
        }
        else if (ContainsAny(text, "kelime", "terim", "vocabulary", "definition", "tanım", "tanim"))
        {
            category = "Vocabulary";
            confidence = 0.74;
            reason = "Vocabulary/definition pattern was detected.";
            hint = "Create a term-definition card and ask the student to use it in one sentence.";
            reviewPressure = 2;
            suggestFlashcard = true;
        }
        else if (ContainsAny(text, "soruyu", "misread", "yanlış ok", "yanlis ok", "not asked"))
        {
            category = "MisreadQuestion";
            confidence = 0.75;
            reason = "Question-misread pattern was detected.";
            hint = "Ask the student to underline what the question actually asks before solving.";
            reviewPressure = 1;
            suggestFlashcard = false;
        }

        var result = new MistakeClassificationResult(
            category,
            Label(category),
            confidence,
            reason,
            request.SkillTag,
            request.ConceptTag,
            hint,
            reviewPressure,
            suggestFlashcard,
            new Dictionary<string, string>
            {
                ["classifier"] = "deterministic_fallback",
                ["codePhase"] = request.CodePhase ?? string.Empty,
                ["hasSourceOrWikiContext"] = string.IsNullOrWhiteSpace(request.SourceOrWikiContext) ? "false" : "true"
            });

        return Task.FromResult(result);
    }

    public async Task<MistakeClassificationResult> ClassifyAndRecordAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        MistakeClassificationRequest request,
        CancellationToken ct = default)
    {
        var result = await ClassifyAsync(request, ct);
        try
        {
            await _signals.RecordSignalAsync(
                userId,
                topicId,
                sessionId,
                LearningSignalTypes.MistakeClassified,
                result.SkillTag,
                topicPath: result.ConceptTag ?? result.SkillTag,
                score: (int)Math.Round(result.Confidence * 100),
                isPositive: false,
                payloadJson: JsonSerializer.Serialize(new
                {
                    result.Category,
                    result.CategoryLabel,
                    result.Confidence,
                    result.Reason,
                    result.RemediationHint,
                    result.SuggestedReviewPressure,
                    result.SuggestFlashcard,
                    result.Metadata
                }),
                ct: ct);

            if (result.Category != "Unknown")
            {
                await _signals.RecordSignalAsync(
                    userId,
                    topicId,
                    sessionId,
                    LearningSignalTypes.MisconceptionDetected,
                    result.SkillTag,
                    topicPath: result.ConceptTag ?? result.SkillTag,
                    score: result.SuggestedReviewPressure,
                    isPositive: false,
                    payloadJson: JsonSerializer.Serialize(new
                    {
                        result.Category,
                        result.RemediationHint,
                        reviewPressure = result.SuggestedReviewPressure,
                        result.SuggestFlashcard
                    }),
                    ct: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[MistakeClassifier] Signal write failed safely.");
        }

        return result;
    }

    private static string Normalize(string value) => value.ToLowerInvariant();

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string Label(string category) => category switch
    {
        "Conceptual" => "Conceptual misunderstanding",
        "Procedural" => "Procedure/step-order issue",
        "Careless" => "Careless slip",
        "Vocabulary" => "Vocabulary/definition gap",
        "MisreadQuestion" => "Question was misread",
        "FormulaMisuse" => "Formula misuse",
        "CodeSyntax" => "Code syntax/compile issue",
        "CodeRuntime" => "Code runtime issue",
        "CodeLogic" => "Code logic/control-flow issue",
        _ => "Unknown mistake pattern"
    };
}

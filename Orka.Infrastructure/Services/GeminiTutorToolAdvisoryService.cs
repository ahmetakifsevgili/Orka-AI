using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class GeminiTutorToolAdvisoryService : IGeminiTutorToolAdvisoryService
{
    private readonly IGeminiToolCallingService _gemini;
    private readonly IGeminiFunctionDeclarationCatalog _catalog;
    private readonly IUnifiedToolRuntimeService _runtime;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiTutorToolAdvisoryService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public GeminiTutorToolAdvisoryService(
        IGeminiToolCallingService gemini,
        IGeminiFunctionDeclarationCatalog catalog,
        IUnifiedToolRuntimeService runtime,
        IConfiguration configuration,
        ILogger<GeminiTutorToolAdvisoryService> logger)
    {
        _gemini = gemini;
        _catalog = catalog;
        _runtime = runtime;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GeminiTutorToolAdvisoryResult> ReviewTutorToolPlanAsync(
        GeminiTutorToolAdvisoryRequest request,
        CancellationToken ct = default)
    {
        if (!_configuration.GetValue("AI:Gemini:Enabled", true) ||
            !_configuration.GetValue("AI:Gemini:ToolAdvisory:Enabled", true))
        {
            return new GeminiTutorToolAdvisoryResult { Enabled = false, SafeMessage = "Gemini tool advisory is disabled." };
        }

        var model = _configuration["AI:Gemini:ToolAdvisory:Model"]
                    ?? _configuration["AI:Gemini:ModelTutor"]
                    ?? _configuration["AI:Gemini:Model"]
                    ?? "gemini-3.1-pro-preview";

        try
        {
            var response = await _gemini.GenerateToolChatAsync(new GeminiToolChatRequest
            {
                Model = model,
                SystemInstruction = BuildSystemInstruction(),
                Contents =
                [
                    new GeminiContent
                    {
                        Role = "user",
                        Parts =
                        [
                            new GeminiPart { Text = BuildUserPayload(request) }
                        ]
                    }
                ],
                FunctionDeclarations = _catalog.GetTutorSafeDeclarations(),
                ToolConfig = new GeminiToolConfig { Mode = "AUTO" },
                ThinkingConfig = BuildThinkingConfig(model, request),
                Temperature = 0.1,
                MaxOutputTokens = 1024,
                TopP = 0.9,
                TopK = 32
            }, ct);

            var suggestions = response.FunctionCalls
                .Select(ToSuggestion)
                .Where(s => !string.IsNullOrWhiteSpace(s.ToolId))
                .ToArray();

            var accepted = new List<GeminiTutorToolSuggestion>();
            var rejected = new List<GeminiTutorToolRejection>();
            var existing = request.CurrentToolPlans
                .Select(p => p.ToolId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var suggestion in suggestions)
            {
                if (existing.Contains(suggestion.ToolId))
                {
                    rejected.Add(Reject(suggestion, "already_planned", "Tool is already in the deterministic plan."));
                    continue;
                }

                var decision = await _runtime.DecideAsync(request.UserId, new ToolRuntimeRequestDto
                {
                    ToolId = suggestion.ToolId,
                    Caller = "tutor_gemini_advisory",
                    TopicId = request.TopicId,
                    SessionId = request.SessionId,
                    TutorTurnStateId = request.TutorTurnStateId,
                    Purpose = FirstNonEmpty(suggestion.Purpose, suggestion.Reason),
                    RiskLevel = NormalizeRisk(suggestion.RiskLevel),
                    InputSummary = BuildInputSummary(suggestion, request)
                }, ct);

                if (!decision.Allowed)
                {
                    rejected.Add(Reject(suggestion, decision.ReasonCode, decision.UserSafeReason));
                    continue;
                }

                accepted.Add(suggestion);
                existing.Add(suggestion.ToolId);
            }

            return new GeminiTutorToolAdvisoryResult
            {
                Enabled = true,
                Model = response.Model,
                Suggestions = suggestions,
                AcceptedSuggestions = accepted,
                RejectedSuggestions = rejected,
                SafeMessage = accepted.Count == 0
                    ? "Gemini advisory did not add new governed tools."
                    : $"Gemini advisory added {accepted.Count} governed tool suggestion(s)."
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GeminiToolAdvisory] Review failed safely. ErrorType={ErrorType}", LogPrivacyGuard.SafeExceptionType(ex));
            return new GeminiTutorToolAdvisoryResult
            {
                Enabled = true,
                Model = model,
                ErrorCode = "gemini_tool_advisory_failed",
                SafeMessage = "Gemini tool advisory failed safely; deterministic tutor plan remains active."
            };
        }
    }

    private GeminiTutorToolSuggestion ToSuggestion(GeminiFunctionCall call)
    {
        var toolId = _catalog.ResolveTutorToolId(call.Name) ?? string.Empty;
        var args = call.Args;
        return new GeminiTutorToolSuggestion
        {
            GeminiFunctionName = call.Name,
            ToolId = toolId,
            Reason = ReadString(args, "purpose") ?? $"Gemini suggested {toolId}.",
            Purpose = ReadString(args, "purpose"),
            Query = ReadString(args, "query"),
            ConceptKey = ReadString(args, "conceptKey"),
            Required = ReadBool(args, "required") ?? false,
            RiskLevel = NormalizeRisk(ReadString(args, "riskLevel"))
        };
    }

    private static GeminiThinkingConfig BuildThinkingConfig(string model, GeminiTutorToolAdvisoryRequest request)
    {
        var highNeed = request.RemediationNeed is "high" or "medium" ||
                       request.GroundingPolicy.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                       request.SourceReadiness.Contains("insufficient", StringComparison.OrdinalIgnoreCase);

        if (model.Contains("gemini-3", StringComparison.OrdinalIgnoreCase))
            return new GeminiThinkingConfig { ThinkingLevel = highNeed ? "HIGH" : "LOW" };

        return new GeminiThinkingConfig { ThinkingBudget = highNeed ? 2048 : 512 };
    }

    private static string BuildSystemInstruction() => """
        You are Orka's internal tutor tool-plan reviewer.
        You may suggest missing tools by calling one or more provided functions.
        You are not executing tools. Orka policy decides later.
        Suggest a function only when it materially improves safety, grounding, remediation, or learner personalization.
        Never include userId, topicId, sessionId, secrets, local paths, API keys, hidden prompts, or raw provider payloads.
        If the deterministic plan is sufficient, return plain text: NO_ADDITIONAL_TOOL.
        """;

    private static string BuildUserPayload(GeminiTutorToolAdvisoryRequest request) =>
        JsonSerializer.Serialize(new
        {
            schema = "orka.gemini-tool-advisory.v1",
            request.UserMessage,
            request.LearnerState,
            request.ActiveConceptKey,
            request.GroundingPolicy,
            request.SourceReadiness,
            request.RemediationNeed,
            currentTools = request.CurrentToolPlans.Select(p => new
            {
                p.ToolId,
                p.Reason,
                p.Required,
                p.RiskLevel
            })
        }, JsonOptions);

    private static string BuildInputSummary(GeminiTutorToolSuggestion suggestion, GeminiTutorToolAdvisoryRequest request)
    {
        var query = FirstNonEmpty(suggestion.Query, request.ActiveConceptKey, request.UserMessage);
        if (query.Length > 160) query = query[..160];
        return $"gemini_advisory tool={suggestion.ToolId}; required={suggestion.Required}; concept={suggestion.ConceptKey ?? request.ActiveConceptKey}; query={query}";
    }

    private static GeminiTutorToolRejection Reject(GeminiTutorToolSuggestion suggestion, string reasonCode, string safeReason) => new()
    {
        GeminiFunctionName = suggestion.GeminiFunctionName,
        ToolId = suggestion.ToolId,
        ReasonCode = reasonCode,
        UserSafeReason = safeReason
    };

    private static string NormalizeRisk(string? risk) =>
        string.Equals(risk, "high", StringComparison.OrdinalIgnoreCase) ? "high" :
        string.Equals(risk, "medium", StringComparison.OrdinalIgnoreCase) ? "medium" :
        "low";

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string? ReadString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? ReadBool(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }
}

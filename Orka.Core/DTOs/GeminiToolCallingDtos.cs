using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orka.Core.DTOs;

public sealed class GeminiToolChatRequest
{
    public string Model { get; set; } = "gemini-3.5-flash";
    public string? SystemInstruction { get; set; }
    public IReadOnlyList<GeminiContent> Contents { get; set; } = Array.Empty<GeminiContent>();
    public IReadOnlyList<GeminiFunctionDeclaration> FunctionDeclarations { get; set; } = Array.Empty<GeminiFunctionDeclaration>();
    public GeminiToolConfig ToolConfig { get; set; } = new();
    public GeminiThinkingConfig? ThinkingConfig { get; set; }
    public double Temperature { get; set; } = 0.1;
    public int MaxOutputTokens { get; set; } = 1024;
    public double TopP { get; set; } = 0.95;
    public int TopK { get; set; } = 40;
}

public sealed class GeminiToolChatResponse
{
    public string Model { get; set; } = string.Empty;
    public string FinishReason { get; set; } = string.Empty;
    public GeminiContent? ModelContent { get; set; }
    public IReadOnlyList<GeminiFunctionCall> FunctionCalls { get; set; } = Array.Empty<GeminiFunctionCall>();
    public string? Text { get; set; }
    public int? PromptTokenCount { get; set; }
    public int? CandidatesTokenCount { get; set; }
    public int? ThoughtsTokenCount { get; set; }
    public int? TotalTokenCount { get; set; }
    public string? RawResponsePreview { get; set; }
}

public sealed class GeminiContent
{
    public string Role { get; set; } = "user";
    public IReadOnlyList<GeminiPart> Parts { get; set; } = Array.Empty<GeminiPart>();
}

public sealed class GeminiPart
{
    public string? Text { get; set; }
    public GeminiFunctionCall? FunctionCall { get; set; }
    public GeminiFunctionResponse? FunctionResponse { get; set; }
    public string? ThoughtSignature { get; set; }
}

public sealed class GeminiFunctionDeclaration
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonElement Parameters { get; set; }
}

public sealed class GeminiFunctionCall
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public JsonElement Args { get; set; }
    public string? ThoughtSignature { get; set; }
}

public sealed class GeminiFunctionResponse
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public JsonElement Response { get; set; }
}

public sealed class GeminiToolConfig
{
    public string Mode { get; set; } = "AUTO";
    public IReadOnlyList<string> AllowedFunctionNames { get; set; } = Array.Empty<string>();
}

public sealed class GeminiThinkingConfig
{
    public string? ThinkingLevel { get; set; }
    public int? ThinkingBudget { get; set; }
}

public sealed class GeminiTutorToolAdvisoryRequest
{
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid TutorTurnStateId { get; set; }
    public string UserMessage { get; set; } = string.Empty;
    public string LearnerState { get; set; } = "unknown";
    public string ActiveConceptKey { get; set; } = string.Empty;
    public string GroundingPolicy { get; set; } = "unknown";
    public string SourceReadiness { get; set; } = "unknown";
    public string RemediationNeed { get; set; } = "unknown";
    public IReadOnlyList<GeminiTutorToolPlanSnapshot> CurrentToolPlans { get; set; } = Array.Empty<GeminiTutorToolPlanSnapshot>();
}

public sealed class GeminiTutorToolPlanSnapshot
{
    public string ToolId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string RiskLevel { get; set; } = "low";
}

public sealed class GeminiTutorToolAdvisoryResult
{
    public bool Enabled { get; set; }
    public string Model { get; set; } = string.Empty;
    public IReadOnlyList<GeminiTutorToolSuggestion> Suggestions { get; set; } = Array.Empty<GeminiTutorToolSuggestion>();
    public IReadOnlyList<GeminiTutorToolSuggestion> AcceptedSuggestions { get; set; } = Array.Empty<GeminiTutorToolSuggestion>();
    public IReadOnlyList<GeminiTutorToolRejection> RejectedSuggestions { get; set; } = Array.Empty<GeminiTutorToolRejection>();
    public string? ErrorCode { get; set; }
    public string? SafeMessage { get; set; }
}

public sealed class GeminiTutorToolSuggestion
{
    public string GeminiFunctionName { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string RiskLevel { get; set; } = "low";
    public string? Purpose { get; set; }
    public string? Query { get; set; }
    public string? ConceptKey { get; set; }
}

public sealed class GeminiTutorToolRejection
{
    public string GeminiFunctionName { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = "unknown";
    public string UserSafeReason { get; set; } = "Tool suggestion was not accepted.";
}

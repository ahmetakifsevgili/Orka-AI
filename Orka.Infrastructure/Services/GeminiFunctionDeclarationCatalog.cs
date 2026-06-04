using System.Text.Json;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class GeminiFunctionDeclarationCatalog : IGeminiFunctionDeclarationCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyList<TutorSafeTool> TutorTools =
    [
        new("source_search", "orka_source_search", "Check learner-owned source/notebook evidence before making source-grounded claims.", "low"),
        new("wiki_search", "orka_wiki_search", "Check Orka Wiki learning memory for the active topic.", "low"),
        new("ide_last_result", "orka_ide_last_result", "Read the latest IDE or code execution result already present in the session.", "medium"),
        new("review_query", "orka_review_query", "Check spaced-repetition review pressure and due review items.", "low"),
        new("flashcard_query", "orka_flashcard_query", "Find active flashcards related to the learner's current concept.", "low"),
        new("wolfram_alpha", "orka_wolfram_alpha", "Request bounded mathematical verification for formulas, equations, or computations.", "medium"),
        new("weather", "orka_geo_weather_context", "Use public geography/weather context for educational examples.", "low"),
        new("news", "orka_news_search", "Search current news only when the learner explicitly asks for current events.", "medium"),
        new("crypto", "orka_crypto_market_data", "Fetch educational market facts without financial advice.", "medium"),
        new("visual_generation", "orka_visual_generation", "Suggest a visual learning artifact or image prompt.", "medium"),
        new("mermaid_graph", "orka_mermaid_graph", "Suggest a local Mermaid diagram for process, architecture, or concept relationships.", "low"),
        new("knowledge_entity", "orka_knowledge_entity", "Fetch public entity evidence cards for educational context.", "low"),
        new("geo_context", "orka_geo_context", "Fetch public geographic context evidence cards.", "low"),
        new("socioeconomic_context", "orka_socioeconomic_context", "Fetch public socioeconomic context evidence cards.", "medium"),
        new("science_context", "orka_science_context", "Fetch public science evidence cards.", "low"),
        new("research_context", "orka_research_context", "Fetch research or academic context evidence.", "medium"),
        new("forum_signal", "orka_forum_signal", "Fetch public misconception/forum pattern signals, not factual authority.", "medium")
    ];

    private readonly Dictionary<string, TutorSafeTool> _byFunction;
    private readonly Dictionary<string, TutorSafeTool> _byTool;
    private readonly IReadOnlyList<GeminiFunctionDeclaration> _declarations;

    public GeminiFunctionDeclarationCatalog()
    {
        _byFunction = TutorTools.ToDictionary(t => t.FunctionName, StringComparer.OrdinalIgnoreCase);
        _byTool = TutorTools.ToDictionary(t => t.ToolId, StringComparer.OrdinalIgnoreCase);
        _declarations = TutorTools.Select(ToDeclaration).ToArray();
    }

    public IReadOnlyList<GeminiFunctionDeclaration> GetTutorSafeDeclarations() => _declarations;

    public string? ResolveTutorToolId(string geminiFunctionName) =>
        _byFunction.TryGetValue(geminiFunctionName, out var tool) ? tool.ToolId : null;

    public string? ResolveGeminiFunctionName(string tutorToolId) =>
        _byTool.TryGetValue(tutorToolId, out var tool) ? tool.FunctionName : null;

    private static GeminiFunctionDeclaration ToDeclaration(TutorSafeTool tool)
    {
        var schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["purpose"] = new { type = "string", description = "Short reason this tool is helpful for this tutor turn." },
                ["query"] = new { type = "string", description = "Learner-facing query or concept to use. Do not include secrets or local paths." },
                ["conceptKey"] = new { type = "string", description = "Current concept key if known; otherwise empty." },
                ["required"] = new { type = "boolean", description = "True only if the tutor should not answer confidently without this tool." },
                ["riskLevel"] = new { type = "string", @enum = new[] { "low", "medium", "high" }, description = "Expected risk level for this tool suggestion." }
            },
            required = new[] { "purpose", "query", "required", "riskLevel" }
        };

        return new GeminiFunctionDeclaration
        {
            Name = tool.FunctionName,
            Description = $"{tool.Description} Server-owned userId/topicId/sessionId are never provided by the model.",
            Parameters = JsonSerializer.SerializeToElement(schema, JsonOptions)
        };
    }

    private sealed record TutorSafeTool(string ToolId, string FunctionName, string Description, string DefaultRisk);
}

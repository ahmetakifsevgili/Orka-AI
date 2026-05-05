using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class ToolCapabilityService : IToolCapabilityService
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public ToolCapabilityService(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public IReadOnlyList<ToolCapabilityDto> GetCapabilities(bool includeInternal = false)
    {
        var tools = new List<ToolCapabilityDto>
        {
            Enabled("sources_query", "Sources / Notebook query", "learning", "Low", false,
                "Queries user-owned uploaded sources; deleted chunks are ignored.", "topicId/question", "answer + citations"),
            Enabled("review_query", "Review / SRS query", "learning", "Low", false,
                "Reads durable ReviewItem state for the authenticated user.", "topicId?", "due review summary"),
            Enabled("flashcards", "Flashcards", "learning", "Low", false,
                "Reads and updates durable flashcards and linked review items.", "topicId/card", "flashcard DTO"),
            Enabled("daily_challenge", "Daily Challenge", "learning", "Low", false,
                "Uses durable daily challenge service with XP idempotency.", "topicId?", "challenge DTO"),
            Enabled("bookmarks", "Bookmarks", "learning", "Low", false,
                "Creates and lists user-owned bookmarks across topics, sources, wiki, flashcards and review.", "bookmark DTO", "bookmark DTO"),
            Enabled("learning_mode", "Learning mode advisor", "pedagogy", "Low", false,
                "Advisory-only teaching mode selection; does not mutate data.", "intent/weakness", "mode recommendation"),
            Enabled("agent_decision", "Agent decision trace", "orchestration", "Low", false,
                "Advisory routing trace for Supervisor/Tutor decisions.", "action/reason", "decision trace"),
            Enabled("mermaid", "Mermaid diagram text", "visualization", "Low", false,
                "Text-only fenced Mermaid output; frontend renders or falls back to code block.", "diagram prompt", "markdown code block"),
            Beta("visual_generation", "Pollinations visual generation", "visualization", "Medium", true, false, "AI:VisualGeneration:Enabled",
                "Provider-style image URL generation is illustrative and beta-visible only.", "prompt/altText", "markdown image URL"),
            Provider("tavily_web_search", "Tavily web search", "research", "Medium", ["AI:Tavily:ApiKey"],
                "External web search; source URLs must be kept separate from user documents.", "query", "source evidence"),
            Enabled("wikipedia", "Wikipedia concept lookup", "research", "Low", true,
                "Public encyclopedia lookup for stable definitions; not a substitute for uploaded docs.", "query", "title/url/snippet"),
            Enabled("academic_search", "Academic search", "research", "Medium", true,
                "Provider/public academic lookup used for credibility signals when available.", "query", "source evidence"),
            ProviderBeta("youtube_pedagogy", "YouTube teaching reference", "pedagogy_reference", "Medium", false,
                "AI:YouTube:Enabled", ["AI:YouTube:ApiKey", "YouTube:ApiKey"],
                "Pedagogy/style/examples only by default; factual proof requires transcript/source evidence.", "topic/video", "pedagogy reference"),
            Provider("wolfram_alpha", "Wolfram Alpha", "computation", "High", ["AI:WolframAlpha:AppId", "WolframAlpha:AppId"],
                "Disabled stub unless AppId exists; exact computation tool, not general chat.", "query", "computed result"),
            Tool("ide_execution", "IDE / Piston sandbox execution", "code_execution", "Enabled", "High", false, true, null,
                "sandbox_api_fallback", "code/language/stdin", "stdout/stderr/phase/safeTutorSummary",
                "CORE_ENABLED_BEHIND_AUTH_AND_SANDBOX",
                "Student IDE execution is active through authenticated /api/code/run and /api/code/execute using Judge0/Piston sandbox. SK auto-execution stays disabled."),
            ProviderBeta("weather", "Weather / geography data", "external_info", "Medium", false,
                "Tools:Weather:Enabled", ["Tools:Weather:ApiKey", "OpenWeatherMap:ApiKey"],
                "Beta external info utility; not core learning evidence.", "location/coordinates", "weather report"),
            Provider("news", "News search", "external_info", "Medium", ["AI:NewsAPI:ApiKey", "NewsAPI:ApiKey"],
                "Current news requires provider evidence and dates; disabled when key is absent.", "query", "article list"),
            Beta("crypto", "Crypto market data", "external_info", "High", true, false, "Tools:Crypto:Enabled",
                "Educational market data only; must not provide financial advice.", "coin ids", "market facts")
        };

        if (includeInternal)
        {
            tools.Add(AdminDev("test_cleanup", "Test cleanup", "dev_admin", "High", "Tools:DevCleanup:Enabled",
                "Not ported publicly. Destructive cleanup is dev/admin-only if ever reintroduced.", "scope", "cleanup result"));
            tools.Add(Disabled("cost_tracking", "Cost tracking hardening", "observability", "Medium", "PRODUCTION_HARDENING",
                "Token estimator exists; durable per-provider cost ledger remains production hardening.", "agent/provider/model", "cost event"));
            tools.Add(Disabled("background_workers", "SRS/Daily/Push workers", "background", "Medium", "PRODUCTION_HARDENING",
                "Background queue is active; scheduled SRS/daily/push worker hardening remains production hardening.", "job", "job telemetry"));
        }

        return tools
            .OrderBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.ToolId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ToolCapabilityDto? GetCapability(string toolId, bool includeInternal = false) =>
        GetCapabilities(includeInternal)
            .FirstOrDefault(t => t.ToolId.Equals(toolId, StringComparison.OrdinalIgnoreCase));

    private ToolCapabilityDto Enabled(
        string id,
        string displayName,
        string category,
        string risk,
        bool externalProvider,
        string notes,
        string inputSchema,
        string outputSchema) =>
        Tool(id, displayName, category, "Enabled", risk, false, externalProvider, null, "metadata_only", inputSchema, outputSchema, "INTEGRATED_AND_TESTED", notes);

    private ToolCapabilityDto Beta(
        string id,
        string displayName,
        string category,
        string risk,
        bool externalProvider,
        bool requiresAdmin,
        string configKey,
        string notes,
        string inputSchema,
        string outputSchema)
    {
        var configured = IsEnabled(configKey);
        return Tool(id, displayName, category, configured ? "Beta" : "Disabled", risk, requiresAdmin, externalProvider,
            configKey, configured ? "degraded_metadata" : "disabled_stub", inputSchema, outputSchema,
            configured ? "INTEGRATED_BEHIND_GATE" : "DISABLED_WITH_RUNTIME_STUB", notes);
    }

    private ToolCapabilityDto Provider(
        string id,
        string displayName,
        string category,
        string risk,
        string[] configKeys,
        string notes,
        string inputSchema,
        string outputSchema)
    {
        var configured = HasAny(configKeys);
        return Tool(id, displayName, category, configured ? "Enabled" : "Disabled", risk, false, true,
            configKeys[0], configured ? "provider_fallback" : "disabled_stub", inputSchema, outputSchema,
            configured ? "INTEGRATED_BEHIND_GATE" : "DISABLED_WITH_RUNTIME_STUB", notes);
    }

    private ToolCapabilityDto ProviderBeta(
        string id,
        string displayName,
        string category,
        string risk,
        bool requiresAdmin,
        string enabledKey,
        string[] providerKeys,
        string notes,
        string inputSchema,
        string outputSchema)
    {
        var enabled = IsEnabled(enabledKey);
        var configured = HasAny(providerKeys);
        var ready = enabled && configured;
        var fallback = !enabled ? "disabled_stub" : configured ? "degraded_metadata" : "provider_missing";
        var decision = ready ? "INTEGRATED_BEHIND_GATE" : "DISABLED_WITH_RUNTIME_STUB";

        return Tool(id, displayName, category, ready ? "Beta" : "Disabled", risk, requiresAdmin, true,
            providerKeys[0], fallback, inputSchema, outputSchema, decision, notes);
    }

    private ToolCapabilityDto AdminDev(
        string id,
        string displayName,
        string category,
        string risk,
        string configKey,
        string notes,
        string inputSchema,
        string outputSchema)
    {
        var enabled = IsEnabled(configKey) && _environment.IsDevelopment();
        return Tool(id, displayName, category, enabled ? "DevOnly" : "Disabled", risk, true, true,
            configKey, "disabled_stub", inputSchema, outputSchema,
            enabled ? "BETA_ADMIN_OR_DEV_ONLY" : "DISABLED_WITH_RUNTIME_STUB", notes);
    }

    private ToolCapabilityDto Disabled(
        string id,
        string displayName,
        string category,
        string risk,
        string decision,
        string notes,
        string inputSchema,
        string outputSchema) =>
        Tool(id, displayName, category, "Disabled", risk, true, false, null, "disabled_stub", inputSchema, outputSchema, decision, notes);

    private ToolCapabilityDto Tool(
        string id,
        string displayName,
        string category,
        string status,
        string risk,
        bool requiresAdmin,
        bool externalProvider,
        string? configKey,
        string fallbackMode,
        string inputSchema,
        string outputSchema,
        string decision,
        string notes) =>
        new(
            id,
            displayName,
            category,
            status,
            risk,
            RequiresAuth: true,
            RequiresAdmin: requiresAdmin,
            RequiresExternalProvider: externalProvider,
            ConfigKey: configKey,
            TimeoutMs: DefaultTimeout(category, risk),
            CostTracked: externalProvider || category is "code_execution" or "research" or "visualization",
            TelemetryEnabled: true,
            FallbackMode: fallbackMode,
            InputSchema: inputSchema,
            OutputSchema: outputSchema,
            Decision: decision,
            Notes: notes);

    private bool HasAny(params string[] keys) => keys.Any(key => !string.IsNullOrWhiteSpace(_configuration[key]));

    private bool IsEnabled(string key) =>
        bool.TryParse(_configuration[key], out var value) && value;

    private static int DefaultTimeout(string category, string risk) =>
        category switch
        {
            "code_execution" => 30000,
            "research" => 20000,
            "external_info" => 15000,
            "visualization" => 12000,
            _ => risk == "High" ? 10000 : 5000
        };
}

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public class GeminiToolCallingService : IGeminiToolCallingService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiToolCallingService> _logger;
    private readonly bool _useVertexAi;
    private readonly string? _apiKey;
    private readonly string _baseUrl;
    private readonly bool _enabled;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GeminiToolCallingService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GeminiToolCallingService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Gemini");
        _configuration = configuration;
        _logger = logger;
        _baseUrl = configuration["AI:Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta/models";
        _enabled = configuration.GetValue("AI:Gemini:Enabled", true);
        _useVertexAi = configuration.GetValue<bool>("AI:Gemini:UseVertexAi") ||
                       _baseUrl.Contains("aiplatform.googleapis.com", StringComparison.OrdinalIgnoreCase);
        _apiKey = configuration["AI:Gemini:ApiKey"];
    }

    private void EnsureEnabled()
    {
        if (!_enabled)
            throw new ProviderConfigurationException("Gemini", "AI:Gemini:Enabled");
    }

    public async Task<GeminiToolChatResponse> GenerateToolChatAsync(
        GeminiToolChatRequest request,
        CancellationToken ct = default)
    {
        EnsureEnabled();

        if (request.FunctionDeclarations.Count == 0)
            throw new InvalidOperationException("Gemini tool calling requires at least one function declaration.");

        var cleanModel = GetCleanModel(request.Model);
        var endpoint = _useVertexAi
            ? $"{_baseUrl.TrimEnd('/')}/{cleanModel}:generateContent"
            : $"{_baseUrl.TrimEnd('/')}/{cleanModel}:generateContent?key={_apiKey}";

        var body = BuildRequestBody(request);
        var json = JsonSerializer.Serialize(body, JsonOptions);
        AiDebugLogger.LogRequest("GEMINI_TOOL", $"URL: {_baseUrl.TrimEnd('/')}/{cleanModel}:generateContent\nModel: {cleanModel}\n{BuildSafeLogPayload(json)}");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.ExpectContinue = false;
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        if (_useVertexAi)
        {
            var token = await GetAccessTokenAsync(ct);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await _httpClient.SendAsync(httpRequest, ct);
        var respStr = await response.Content.ReadAsStringAsync(ct);
        AiDebugLogger.LogResponse("GEMINI_TOOL", $"Status: {(int)response.StatusCode}\n{BuildSafeLogPayload(respStr)[..Math.Min(BuildSafeLogPayload(respStr).Length, 500)]}");

        if (!response.IsSuccessStatusCode)
        {
            var safeError = AiDebugLogger.BuildSafeLogPreview("GEMINI_TOOL", "ERROR", BuildSafeLogPayload(respStr));
            _logger.LogError("Gemini tool calling failed. Status={Status} Error={Error}", response.StatusCode, safeError[..Math.Min(safeError.Length, 300)]);
            throw AiProviderFailureMapper.FromResponse("Gemini", cleanModel, response, safeError);
        }

        return ParseResponse(cleanModel, respStr);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var credential = await GoogleCredential.GetApplicationDefaultAsync();
        if (credential.IsCreateScopedRequired)
            credential = credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");

        return await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
    }

    private string GetCleanModel(string model)
    {
        if (_baseUrl.TrimEnd('/').EndsWith("/models", StringComparison.OrdinalIgnoreCase) &&
            model.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            return model["models/".Length..];
        }

        return model;
    }

    private static object BuildRequestBody(GeminiToolChatRequest request)
    {
        var generationConfig = new Dictionary<string, object?>
        {
            ["temperature"] = request.Temperature,
            ["maxOutputTokens"] = request.MaxOutputTokens,
            ["topP"] = request.TopP,
            ["topK"] = request.TopK,
            ["candidateCount"] = 1
        };

        var thinking = BuildThinkingConfig(request);
        if (thinking.Count > 0)
            generationConfig["thinkingConfig"] = thinking;

        var functionConfig = new Dictionary<string, object?>
        {
            ["mode"] = NormalizeFunctionMode(request.ToolConfig.Mode)
        };
        if (request.ToolConfig.AllowedFunctionNames.Count > 0)
            functionConfig["allowedFunctionNames"] = request.ToolConfig.AllowedFunctionNames;

        var body = new Dictionary<string, object?>
        {
            ["contents"] = request.Contents.Select(ToWireContent).ToArray(),
            ["tools"] = new[]
            {
                new
                {
                    functionDeclarations = request.FunctionDeclarations.Select(d => new
                    {
                        name = d.Name,
                        description = d.Description,
                        parameters = d.Parameters
                    }).ToArray()
                }
            },
            ["toolConfig"] = new
            {
                functionCallingConfig = functionConfig
            },
            ["generationConfig"] = generationConfig
        };

        if (!string.IsNullOrWhiteSpace(request.SystemInstruction))
        {
            body["systemInstruction"] = new
            {
                parts = new[] { new { text = request.SystemInstruction } }
            };
        }

        return body;
    }

    private static Dictionary<string, object?> BuildThinkingConfig(GeminiToolChatRequest request)
    {
        var thinking = new Dictionary<string, object?>();
        var model = request.Model;
        if (request.ThinkingConfig?.ThinkingLevel is { Length: > 0 } level &&
            IsGemini3OrLater(model))
        {
            thinking["thinkingLevel"] = level.ToUpperInvariant();
        }
        else if (request.ThinkingConfig?.ThinkingBudget is { } budget &&
                 !IsGemini3OrLater(model))
        {
            thinking["thinkingBudget"] = budget;
        }

        return thinking;
    }

    private static bool IsGemini3OrLater(string model) =>
        model.Contains("gemini-3", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFunctionMode(string? mode) =>
        string.IsNullOrWhiteSpace(mode) ? "AUTO" : mode.Trim().ToUpperInvariant();

    private static object ToWireContent(GeminiContent content) => new
    {
        role = content.Role,
        parts = content.Parts.Select(ToWirePart).ToArray()
    };

    private static object ToWirePart(GeminiPart part)
    {
        var wire = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(part.Text))
            wire["text"] = part.Text;

        if (part.FunctionCall != null)
        {
            wire["functionCall"] = new
            {
                id = part.FunctionCall.Id,
                name = part.FunctionCall.Name,
                args = part.FunctionCall.Args
            };
        }

        if (part.FunctionResponse != null)
        {
            wire["functionResponse"] = new
            {
                id = part.FunctionResponse.Id,
                name = part.FunctionResponse.Name,
                response = part.FunctionResponse.Response
            };
        }

        if (!string.IsNullOrWhiteSpace(part.ThoughtSignature))
            wire["thoughtSignature"] = part.ThoughtSignature;

        return wire;
    }

    internal static GeminiToolChatResponse ParseResponse(string model, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var candidate = root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0
            ? candidates[0]
            : throw new InvalidOperationException("Gemini tool response has no candidates.");

        var content = candidate.TryGetProperty("content", out var contentEl)
            ? ParseContent(contentEl)
            : null;

        var functionCalls = content?.Parts
            .Where(p => p.FunctionCall != null)
            .Select(p => p.FunctionCall!)
            .ToArray() ?? Array.Empty<GeminiFunctionCall>();

        var text = content?.Parts.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Text))?.Text;

        return new GeminiToolChatResponse
        {
            Model = root.TryGetProperty("modelVersion", out var modelVersion) ? modelVersion.GetString() ?? model : model,
            FinishReason = candidate.TryGetProperty("finishReason", out var finish) ? finish.GetString() ?? string.Empty : string.Empty,
            ModelContent = content,
            FunctionCalls = functionCalls,
            Text = text,
            PromptTokenCount = TryReadInt(root, "usageMetadata", "promptTokenCount"),
            CandidatesTokenCount = TryReadInt(root, "usageMetadata", "candidatesTokenCount"),
            ThoughtsTokenCount = TryReadInt(root, "usageMetadata", "thoughtsTokenCount"),
            TotalTokenCount = TryReadInt(root, "usageMetadata", "totalTokenCount")
        };
    }

    private static GeminiContent ParseContent(JsonElement content)
    {
        var parts = new List<GeminiPart>();
        if (content.TryGetProperty("parts", out var partsEl))
        {
            foreach (var partEl in partsEl.EnumerateArray())
                parts.Add(ParsePart(partEl));
        }

        return new GeminiContent
        {
            Role = content.TryGetProperty("role", out var role) ? role.GetString() ?? "model" : "model",
            Parts = parts
        };
    }

    private static GeminiPart ParsePart(JsonElement part)
    {
        GeminiFunctionCall? call = null;
        GeminiFunctionResponse? response = null;
        var thoughtSignature = part.TryGetProperty("thoughtSignature", out var sig)
            ? sig.GetString()
            : part.TryGetProperty("thought_signature", out var snakeSig) ? snakeSig.GetString() : null;

        if (part.TryGetProperty("functionCall", out var callEl))
        {
            call = new GeminiFunctionCall
            {
                Id = callEl.TryGetProperty("id", out var id) ? id.GetString() : null,
                Name = callEl.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                Args = callEl.TryGetProperty("args", out var args) ? args.Clone() : JsonSerializer.SerializeToElement(new { }),
                ThoughtSignature = thoughtSignature
            };
        }

        if (part.TryGetProperty("functionResponse", out var responseEl))
        {
            response = new GeminiFunctionResponse
            {
                Id = responseEl.TryGetProperty("id", out var id) ? id.GetString() : null,
                Name = responseEl.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                Response = responseEl.TryGetProperty("response", out var payload) ? payload.Clone() : JsonSerializer.SerializeToElement(new { })
            };
        }

        return new GeminiPart
        {
            Text = part.TryGetProperty("text", out var text) ? text.GetString() : null,
            FunctionCall = call,
            FunctionResponse = response,
            ThoughtSignature = thoughtSignature
        };
    }

    private static int? TryReadInt(JsonElement root, string parent, string name)
    {
        if (!root.TryGetProperty(parent, out var p)) return null;
        if (!p.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    }

    private static string BuildSafeLogPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return payload;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return RedactThoughtSignatures(doc.RootElement).GetRawText();
        }
        catch
        {
            return payload.Replace("thoughtSignature", "thoughtSignature_redacted", StringComparison.OrdinalIgnoreCase)
                .Replace("thought_signature", "thought_signature_redacted", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static JsonElement RedactThoughtSignatures(JsonElement element)
    {
        object? redacted = Redact(element);
        return JsonSerializer.SerializeToElement(redacted, JsonOptions);
    }

    private static object? Redact(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                p => p.Name,
                p => p.NameEquals("thoughtSignature") || p.NameEquals("thought_signature")
                    ? (object?)"[redacted]"
                    : Redact(p.Value),
                StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(Redact).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}

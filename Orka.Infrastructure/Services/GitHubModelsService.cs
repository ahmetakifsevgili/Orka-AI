using System.Net.Http.Headers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Exceptions;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

/// <summary>
/// GitHub Models chat completions provider.
/// Uses the OpenAI-compatible HTTP API directly so the app's configured HttpClient,
/// proxy, timeout and resilience policies are actually honored.
/// </summary>
public sealed class GitHubModelsService : IGitHubModelsService
{
    private readonly HttpClient _httpClient;
    private readonly string _token;
    private readonly string _chatCompletionsUrl;
    private readonly bool _preferNodeBridge;
    private readonly ILogger<GitHubModelsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string NodeBridgeScript = """
let input = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", chunk => input += chunk);
process.stdin.on("end", async () => {
  try {
    const response = await fetch(process.env.GITHUB_MODELS_URL, {
      method: "POST",
      headers: {
        "Accept": "application/json",
        "Content-Type": "application/json",
        "Authorization": `Bearer ${process.env.GITHUB_MODELS_TOKEN}`
      },
      body: input
    });
    const body = await response.text();
    if (!response.ok) {
      console.error(JSON.stringify({ status: response.status, body: body.slice(0, 1000) }));
      process.exit(2);
    }
    process.stdout.write(body);
  } catch (error) {
    console.error(error && error.stack ? error.stack : String(error));
    process.exit(1);
  }
});
""";

    public GitHubModelsService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GitHubModelsService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("GitHubModels");
        _token = configuration["AI:GitHubModels:Token"] ?? throw new ArgumentException("AI:GitHubModels:Token eksik.");
        _chatCompletionsUrl = ResolveChatCompletionsUrl(configuration["AI:GitHubModels:BaseUrl"]);
        _preferNodeBridge = configuration.GetValue("AI:GitHubModels:PreferNodeBridge", OperatingSystem.IsWindows());
        _logger = logger;
    }

    public async Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        string model,
        CancellationToken ct = default,
        int? maxOutputTokens = null)
    {
        var requestBody = BuildRequestBody(systemPrompt, userMessage, model, maxOutputTokens, stream: false);
        var jsonBody = JsonSerializer.Serialize(requestBody, JsonOptions);

        AiDebugLogger.LogRequest("GITHUBMODELS", $"URL: {_chatCompletionsUrl}\nModel: {model}\n{jsonBody}");

        if (_preferNodeBridge)
        {
            _logger.LogInformation("[GitHubModels] Using Node fetch bridge before .NET HTTP. Model={Model}", model);
            var bridgedBody = await CallNodeBridgeAsync(jsonBody, model, ct);
            return ExtractChatContent(bridgedBody);
        }

        try
        {
            using var request = BuildRequest(jsonBody);
            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            AiDebugLogger.LogResponse("GITHUBMODELS", $"Status: {(int)response.StatusCode}\n{body[..Math.Min(body.Length, 500)]}");

            if (!response.IsSuccessStatusCode)
            {
                body = AiDebugLogger.BuildSafeLogPreview("GITHUBMODELS", "ERROR", body);
                _logger.LogError("[GitHubModels] API error. Status={Status} Model={Model} Body={Body}", response.StatusCode, model, body);
                throw AiProviderFailureMapper.FromResponse("GitHubModels", model, response, body);
            }

            return ExtractChatContent(body);
        }
        catch (Exception ex) when (ShouldTryNodeBridge(ex))
        {
            _logger.LogWarning("[GitHubModels] .NET HTTP transport failed; trying Node fetch bridge. Model={Model} ExceptionType={ExceptionType}", model, ex.GetType().Name);
            var bridgedBody = await CallNodeBridgeAsync(jsonBody, model, ct);
            return ExtractChatContent(bridgedBody);
        }
        catch (Exception ex) when (ex is not AiProviderCallException)
        {
            AiDebugLogger.LogError("GITHUBMODELS", ex.Message);
            _logger.LogError("[GitHubModels] provider request failed. Model={Model} ExceptionType={ExceptionType}", model, ex.GetType().Name);
            throw AiProviderFailureMapper.FromException("GitHubModels", model, ex);
        }
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string systemPrompt,
        string userMessage,
        string model,
        [EnumeratorCancellation] CancellationToken ct = default,
        int? maxOutputTokens = null)
    {
        var requestBody = BuildRequestBody(systemPrompt, userMessage, model, maxOutputTokens, stream: true);
        var jsonBody = JsonSerializer.Serialize(requestBody, JsonOptions);

        using var request = BuildRequest(jsonBody);
        HttpResponseMessage? response = null;
        string? bridgedBody = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ShouldTryNodeBridge(ex))
        {
            _logger.LogWarning("[GitHubModels] .NET streaming transport failed; using Node bridge non-stream response. Model={Model} ExceptionType={ExceptionType}", model, ex.GetType().Name);
            bridgedBody = await CallNodeBridgeAsync(
                JsonSerializer.Serialize(BuildRequestBody(systemPrompt, userMessage, model, maxOutputTokens, stream: false), JsonOptions),
                model,
                ct);
        }

        if (bridgedBody != null)
        {
            yield return ExtractChatContent(bridgedBody);
            yield break;
        }

        using (response!)
        {
            if (!response!.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw AiProviderFailureMapper.FromResponse("GitHubModels", model, response, errorBody);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal)) continue;

                var data = line[6..].Trim();
                if (data == "[DONE]") break;

                var delta = ExtractStreamingDelta(data);
                if (!string.IsNullOrWhiteSpace(delta))
                {
                    yield return delta;
                }
            }
        }
    }

    private HttpRequestMessage BuildRequest(string jsonBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _chatCompletionsUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("OrkaLocalDev/1.0");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return request;
    }

    private static object BuildRequestBody(
        string systemPrompt,
        string userMessage,
        string model,
        int? maxOutputTokens,
        bool stream)
    {
        return new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = string.IsNullOrWhiteSpace(systemPrompt) ? "Sen yardimci bir asistansin." : systemPrompt },
                new { role = "user", content = string.IsNullOrWhiteSpace(userMessage) ? "Merhaba." : userMessage }
            },
            temperature = 0.7,
            max_completion_tokens = maxOutputTokens ?? 4096,
            stream
        };
    }

    private static string ResolveChatCompletionsUrl(string? configuredBaseUrl)
    {
        var value = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? "https://models.github.ai/inference"
            : configuredBaseUrl.Trim();
        value = value.TrimEnd('/');
        return value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"{value}/chat/completions";
    }

    private static string ExtractChatContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join("", content.EnumerateArray().Select(ExtractContentPart)),
            _ => string.Empty
        };
    }

    private static string ExtractContentPart(JsonElement part)
    {
        if (part.ValueKind == JsonValueKind.String) return part.GetString() ?? string.Empty;
        if (part.ValueKind != JsonValueKind.Object) return string.Empty;
        if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) return text.GetString() ?? string.Empty;
        if (part.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) return content.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static string ExtractStreamingDelta(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");
        if (!delta.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        return content.ValueKind == JsonValueKind.String ? content.GetString() ?? string.Empty : string.Empty;
    }

    private static bool ShouldTryNodeBridge(Exception ex)
    {
        if (ex is AiProviderCallException) return false;
        if (ex is HttpRequestException) return true;
        var name = ex.GetType().Name;
        return name.Contains("BrokenCircuit", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("TimeoutRejected", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> CallNodeBridgeAsync(string jsonBody, string model, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveNodeExecutablePath(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var bridgeScriptPath = ResolveNodeBridgeScriptPath();
        if (bridgeScriptPath != null)
        {
            psi.ArgumentList.Add(bridgeScriptPath);
        }
        else
        {
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(NodeBridgeScript);
        }
        ConfigureNodeBridgeEnvironment(psi, _chatCompletionsUrl, _token);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Node bridge could not be started.");
        await process.StandardInput.WriteAsync(jsonBody.AsMemory(), ct);
        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            var safeError = AiDebugLogger.BuildSafeLogPreview("GITHUBMODELS", "NODE_BRIDGE_ERROR", error);
            var bridgeDiagnostic = BuildNodeBridgeDiagnostic(error);
            _logger.LogError("[GitHubModels] Node bridge failed. Model={Model} ExitCode={ExitCode} Error={Error} SafeDiagnostic={SafeDiagnostic}", model, process.ExitCode, safeError, bridgeDiagnostic);
            throw MapNodeBridgeFailure(model, error, process.ExitCode);
        }

        AiDebugLogger.LogResponse("GITHUBMODELS", $"NodeBridge Status: 200\n{output[..Math.Min(output.Length, 500)]}");
        return output;
    }

    private static void ConfigureNodeBridgeEnvironment(ProcessStartInfo psi, string url, string token)
    {
        var path = Environment.GetEnvironmentVariable("PATH")
            ?? Environment.GetEnvironmentVariable("Path")
            ?? string.Empty;
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot")
            ?? Environment.GetEnvironmentVariable("SYSTEMROOT")
            ?? @"C:\Windows";
        var windir = Environment.GetEnvironmentVariable("windir")
            ?? Environment.GetEnvironmentVariable("WINDIR")
            ?? systemRoot;
        var temp = Environment.GetEnvironmentVariable("TEMP")
            ?? Environment.GetEnvironmentVariable("TMP")
            ?? Path.GetTempPath();

        psi.Environment.Clear();
        SetEnvIfNotEmpty(psi, "PATH", path);
        SetEnvIfNotEmpty(psi, "SystemRoot", systemRoot);
        SetEnvIfNotEmpty(psi, "WINDIR", windir);
        SetEnvIfNotEmpty(psi, "TEMP", temp);
        SetEnvIfNotEmpty(psi, "TMP", temp);
        psi.Environment["NO_PROXY"] = "localhost,127.0.0.1";
        psi.Environment["GITHUB_MODELS_URL"] = url;
        psi.Environment["GITHUB_MODELS_TOKEN"] = token;
    }

    private static void SetEnvIfNotEmpty(ProcessStartInfo psi, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            psi.Environment[name] = value;
    }

    private static string? ResolveNodeBridgeScriptPath()
    {
        foreach (var start in CandidateSearchRoots())
        {
            var directory = start;
            while (!string.IsNullOrWhiteSpace(directory))
            {
                var candidate = Path.Combine(directory, "scripts", "provider-bridge", "github-models-bridge.mjs");
                if (File.Exists(candidate))
                    return candidate;

                directory = Directory.GetParent(directory)?.FullName;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateSearchRoots()
    {
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
    }

    private static string ResolveNodeExecutablePath()
    {
        const string windowsNode = @"C:\Program Files\nodejs\node.exe";
        return File.Exists(windowsNode) ? windowsNode : "node";
    }

    private string BuildNodeBridgeDiagnostic(string? error)
    {
        var value = error ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_token))
            value = value.Replace(_token, "[secret]", StringComparison.Ordinal);

        value = Regex.Replace(value, @"(?i)(authorization\s*:\s*bearer\s+)[^\s""'}]+", "$1[secret]");
        value = Regex.Replace(value, @"(?i)((?:api[_-]?key|token|secret)\s*[:=]\s*)[^\s""'}]+", "$1[secret]");
        value = Regex.Replace(value, @"[A-Z]:\\[^\s""']+", "[local_path]");
        value = value.Replace("\r", " ").Replace("\n", " | ").Trim();

        if (string.IsNullOrWhiteSpace(value))
            return "empty";

        return value.Length <= 800 ? value : value[..800];
    }

    private static AiProviderCallException MapNodeBridgeFailure(string model, string? error, int exitCode)
    {
        if (TryReadNodeBridgeHttpError(error, out var statusCode, out var body))
        {
            using var response = new HttpResponseMessage(statusCode);
            return AiProviderFailureMapper.FromResponse("GitHubModels", model, response, body);
        }

        return AiProviderFailureMapper.FromException("GitHubModels", model, new InvalidOperationException($"GitHubModels Node bridge failed with exit code {exitCode}."));
    }

    private static bool TryReadNodeBridgeHttpError(string? error, out System.Net.HttpStatusCode statusCode, out string body)
    {
        statusCode = default;
        body = string.Empty;

        if (string.IsNullOrWhiteSpace(error))
            return false;

        var firstLine = error
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.TrimStart().StartsWith('{'));
        if (firstLine == null)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(firstLine);
            if (!doc.RootElement.TryGetProperty("status", out var statusElement) || !statusElement.TryGetInt32(out var status))
                return false;

            statusCode = (System.Net.HttpStatusCode)status;
            body = doc.RootElement.TryGetProperty("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.String
                ? bodyElement.GetString() ?? string.Empty
                : string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

}

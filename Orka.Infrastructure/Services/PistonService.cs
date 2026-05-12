using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Executes student code through the Judge0 CE sandbox. This service never runs host shell commands.
/// </summary>
public class PistonService : IPistonService
{
    private const string BaseUrl = "https://ce.judge0.com";
    private const string SubmitUrl = $"{BaseUrl}/submissions?base64_encoded=false&wait=true";
    private const string LangsUrl = $"{BaseUrl}/languages";
    private const int OutputLimit = 12_000;

    private readonly HttpClient _http;
    private readonly ILogger<PistonService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Dictionary<string, int> LanguageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        { "csharp", 51 },
        { "python", 100 },
        { "javascript", 102 },
        { "typescript", 101 },
        { "java", 91 },
        { "go", 107 },
        { "rust", 108 },
        { "cpp", 105 },
        { "c", 103 },
        { "kotlin", 111 },
        { "php", 98 },
        { "ruby", 72 },
        { "bash", 46 },
        { "swift", 83 },
        { "r", 99 },
        { "scala", 112 }
    };

    public PistonService(IHttpClientFactory factory, ILogger<PistonService> logger)
    {
        _http = factory.CreateClient("Piston");
        _logger = logger;
    }

    public async Task<IReadOnlyList<PistonRuntime>> GetRuntimesAsync()
    {
        try
        {
            var response = await _http.GetAsync(LangsUrl);
            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOptions) ?? [];
            var supported = new List<PistonRuntime>();
            foreach (var kvp in LanguageIds)
            {
                var entry = json.FirstOrDefault(j =>
                    j.TryGetProperty("id", out var id) && id.GetInt32() == kvp.Value);

                if (entry.ValueKind != JsonValueKind.Undefined)
                {
                    var name = entry.TryGetProperty("name", out var n) ? n.GetString() ?? kvp.Key : kvp.Key;
                    supported.Add(new PistonRuntime(kvp.Key, ExtractVersion(name), []));
                }
                else
                {
                    supported.Add(new PistonRuntime(kvp.Key, "latest", []));
                }
            }

            return supported.OrderBy(r => r.Language).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Judge0 language list could not be loaded.");
            return [];
        }
    }

    public async Task<PistonResult> ExecuteAsync(string code, string language = "csharp", string? stdin = null)
    {
        if (!LanguageIds.TryGetValue(language, out var langId))
        {
            _logger.LogWarning("Unsupported code language requested: {Language}", language);
            return new PistonResult(
                "",
                $"'{language}' dili desteklenmiyor. Desteklenen diller: {string.Join(", ", LanguageIds.Keys)}",
                false,
                Phase: "blocked",
                SafeTutorSummary: "Dil desteklenmiyor; ogrenciye desteklenen dillerden birini secmesini soyle.");
        }

        if ((stdin?.Length ?? 0) > 10_000)
        {
            return new PistonResult(
                "",
                "stdin 10.000 karakteri gecemez.",
                false,
                Phase: "blocked",
                SafeTutorSummary: "Standart girdi cok uzun; ogrenciden girdiyi kucultmesini iste.");
        }

        var payload = new
        {
            source_code = code,
            language_id = langId,
            stdin = stdin ?? ""
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _http.PostAsJsonAsync(SubmitUrl, payload, JsonOptions);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                var status = (int)response.StatusCode;
                _logger.LogWarning("Judge0 API returned {Status}. Body length={BodyLength}", status, body.Length);

                var message = status == 429
                    ? "Cok fazla istek gonderildi. Lutfen birkac saniye bekleyip tekrar deneyin."
                    : $"Kod çalıştırma servisi hata döndürdü ({status}). Lütfen tekrar deneyin.";

                return new PistonResult(
                    "",
                    message,
                    false,
                    Phase: "provider_missing",
                    DurationMs: sw.ElapsedMilliseconds,
                    SafeTutorSummary: "Kod çalıştırma sağlayıcısı kullanılamadı; sonucu uydurma.");
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            return ParseJudge0Response(json, language, sw.ElapsedMilliseconds);
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("Judge0 request timed out.");
            return new PistonResult(
                "",
                "Kod çalıştırma zaman aşımına uğradı. Kodunuz çok uzun veya sonsuz döngü içeriyor olabilir.",
                false,
                Phase: "timeout",
                DurationMs: sw.ElapsedMilliseconds,
                SafeTutorSummary: "Kod zaman asimina dustu; sonsuz dongu veya karmasiklik analizini ogret.");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Judge0 network error.");
            return new PistonResult(
                "",
                "Kod çalıştırma servisine bağlanılamadı. İnternet bağlantınızı kontrol edin.",
                false,
                Phase: "provider_missing",
                DurationMs: sw.ElapsedMilliseconds,
                SafeTutorSummary: "Kod çalıştırma sağlayıcısı kullanılamadı; sonucu uydurma.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Judge0 unexpected error.");
            return new PistonResult(
                "",
                "Kod çalıştırma servisinde beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.",
                false,
                Phase: "provider_missing",
                DurationMs: sw.ElapsedMilliseconds,
                SafeTutorSummary: "Kod çalıştırma sağlayıcısında hata oluştu; sonucu uydurma.");
        }
    }

    private PistonResult ParseJudge0Response(JsonElement json, string language, long durationMs)
    {
        var stdout = LimitOutput(GetString(json, "stdout") ?? "", out var stdoutTruncated);
        var stderr = LimitOutput(GetString(json, "stderr") ?? "", out var stderrTruncated);
        var compileOutput = LimitOutput(GetString(json, "compile_output") ?? "", out var compileTruncated);
        var message = GetString(json, "message") ?? "";
        var truncated = stdoutTruncated || stderrTruncated || compileTruncated;

        var statusId = 0;
        if (json.TryGetProperty("status", out var statusObj) &&
            statusObj.TryGetProperty("id", out var sid))
        {
            statusId = sid.GetInt32();
        }

        var exitCode = json.TryGetProperty("exit_code", out var exit) && exit.ValueKind == JsonValueKind.Number
            ? exit.GetInt32()
            : (int?)null;

        if (statusId == 6)
        {
            var errMsg = string.IsNullOrWhiteSpace(compileOutput) ? stderr : compileOutput;
            return new PistonResult(
                "",
                errMsg.TrimEnd(),
                false,
                Phase: "compile",
                CompileError: errMsg.TrimEnd(),
                ExitCode: exitCode,
                DurationMs: durationMs,
                Truncated: truncated,
                SafeTutorSummary: "Derleme hatasi var; syntax, tip, import veya eksik sembol kavramina odaklan.",
                Runtime: language);
        }

        if (statusId == 5)
        {
            return new PistonResult(
                stdout.TrimEnd(),
                "Zaman asimi: Kodunuz cok uzun surdu veya sonsuz dongu iceriyor.",
                false,
                Phase: "timeout",
                ExitCode: exitCode,
                DurationMs: durationMs,
                Truncated: truncated,
                SafeTutorSummary: "Kod zaman asimina dustu; sonsuz dongu veya algoritma karmasikligini ogret.",
                Runtime: language);
        }

        if (statusId >= 7 && statusId != 13)
        {
            var errMsg = !string.IsNullOrWhiteSpace(stderr)
                ? stderr
                : (!string.IsNullOrWhiteSpace(message) ? message : "Runtime hatasi olustu.");

            return new PistonResult(
                stdout.TrimEnd(),
                errMsg.TrimEnd(),
                false,
                Phase: "run",
                RuntimeError: errMsg.TrimEnd(),
                ExitCode: exitCode,
                DurationMs: durationMs,
                Truncated: truncated,
                SafeTutorSummary: "Runtime hatasi var; calisma zamani durumu, exception, null veya indeks sorununu acikla.",
                Runtime: language);
        }

        if (statusId == 13)
        {
            return new PistonResult(
                "",
                "Servis hatasi olustu. Lutfen tekrar deneyin.",
                false,
                Phase: "provider_missing",
                ExitCode: exitCode,
                DurationMs: durationMs,
                Truncated: truncated,
                SafeTutorSummary: "Kod çalıştırma servisi iç hata verdi; sonucu uydurma.",
                Runtime: language);
        }

        var success = statusId == 3 && string.IsNullOrWhiteSpace(stderr) && string.IsNullOrWhiteSpace(compileOutput);
        return new PistonResult(
            stdout.TrimEnd(),
            stderr.TrimEnd(),
            success,
            Phase: "run",
            ExitCode: exitCode,
            DurationMs: durationMs,
            Truncated: truncated,
            SafeTutorSummary: success
                ? "Kod basariyla calisti; stdout uzerinden sonucu yorumla ve bir sonraki pratik adimi oner."
                : "Kod calisti ama basari durumu net degil; stdout/stderr'i dikkatle ayir.",
            Runtime: language);
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string LimitOutput(string value, out bool truncated)
    {
        if (value.Length <= OutputLimit)
        {
            truncated = false;
            return value;
        }

        truncated = true;
        return value[..OutputLimit] + "\n...[truncated]";
    }

    private static string ExtractVersion(string name)
    {
        var start = name.IndexOf('(');
        var end = name.IndexOf(')');
        return start >= 0 && end > start ? name[(start + 1)..end] : "latest";
    }
}

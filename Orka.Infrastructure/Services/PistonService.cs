using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Judge0 CE (Community Edition) API üzerinden sandbox ortamında kod çalıştırır.
/// Public instance: https://ce.judge0.com — API anahtarı gerektirmez.
///
/// Judge0 CE dökümantasyonu: https://github.com/judge0/judge0/blob/master/docs/api/submissions/README.md
/// Status kodları: 3=Accepted, 5=TLE, 6=CompilationError, 7-12=RuntimeError, 13=InternalError
/// </summary>
public class PistonService : IPistonService
{
    private readonly HttpClient _http;
    private readonly ILogger<PistonService> _logger;

    private const string BaseUrl    = "https://ce.judge0.com";
    private const string SubmitUrl  = $"{BaseUrl}/submissions?base64_encoded=false&wait=true";
    private const string LangsUrl   = $"{BaseUrl}/languages";

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Orka dil adı → Judge0 CE language_id eşlemesi.
    /// En güncel versiyonlar tercih edilir; yoksa stable sürüm kullanılır.
    /// https://ce.judge0.com/languages
    /// </summary>
    private static readonly Dictionary<string, int> LanguageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        { "csharp",     51  },  // C# (Mono 6.6.0)
        { "python",     100 },  // Python 3.12.5
        { "javascript", 102 },  // JavaScript Node.js 22
        { "typescript", 101 },  // TypeScript 5.6
        { "java",       91  },  // Java JDK 17
        { "go",         107 },  // Go 1.23
        { "rust",       108 },  // Rust 1.85
        { "cpp",        105 },  // C++ GCC 14
        { "c",          103 },  // C GCC 14
        { "kotlin",     111 },  // Kotlin 2.1
        { "php",        98  },  // PHP 8.3
        { "ruby",       72  },  // Ruby 2.7
        { "bash",       46  },  // Bash 5.0
        { "swift",      83  },  // Swift 5.2
        { "r",          99  },  // R 4.4
        { "scala",      112 },  // Scala 3.4
    };

    public PistonService(IHttpClientFactory factory, ILogger<PistonService> logger)
    {
        _http   = factory.CreateClient("Piston");
        _logger = logger;
    }

    public async Task<IReadOnlyList<PistonRuntime>> GetRuntimesAsync()
    {
        try
        {
            var response = await _http.GetAsync(LangsUrl);
            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadFromJsonAsync<JsonElement[]>(_jsonOpts) ?? [];

            // Judge0 dil listesini Orka'nın PistonRuntime formatına dönüştür
            // Yalnızca LanguageIds içindeki dilleri döndür (desteklediğimiz diller)
            var supported = new List<PistonRuntime>();
            foreach (var kvp in LanguageIds)
            {
                var entry = json.FirstOrDefault(j =>
                    j.TryGetProperty("id", out var id) && id.GetInt32() == kvp.Value);
                if (entry.ValueKind != JsonValueKind.Undefined)
                {
                    var name = entry.TryGetProperty("name", out var n) ? n.GetString() ?? kvp.Key : kvp.Key;
                    // "Python (3.12.5)" → version kısmını çıkar
                    var version = ExtractVersion(name);
                    supported.Add(new PistonRuntime(kvp.Key, version, []));
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
            _logger.LogError(ex, "Judge0 language listesi alınamadı");
            return [];
        }
    }

    public async Task<PistonResult> ExecuteAsync(string code, string language = "csharp", string? stdin = null)
    {
        if (!LanguageIds.TryGetValue(language, out var langId))
        {
            _logger.LogWarning("Desteklenmeyen dil: {Language}", language);
            return new PistonResult("",
                $"'{language}' dili desteklenmiyor. Desteklenen diller: {string.Join(", ", LanguageIds.Keys)}",
                false);
        }

        var payload = new
        {
            source_code = code,
            language_id = langId,
            stdin       = stdin ?? ""
        };

        try
        {
            var response = await _http.PostAsJsonAsync(SubmitUrl, payload, _jsonOpts);

            if (!response.IsSuccessStatusCode)
            {
                var body   = await response.Content.ReadAsStringAsync();
                var status = (int)response.StatusCode;
                _logger.LogWarning("Judge0 API hata: {Status} — {Body}", status, body);

                if (status == 429)
                    return new PistonResult("",
                        "Çok fazla istek gönderildi. Lütfen birkaç saniye bekleyip tekrar deneyin.", false);

                return new PistonResult("",
                    $"Kod çalıştırma servisi hata döndürdü ({status}). Lütfen tekrar deneyin.", false);
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
            return ParseJudge0Response(json, language);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Judge0 isteği zaman aşımına uğradı");
            return new PistonResult("",
                "Kod çalıştırma zaman aşımına uğradı. Kodunuz çok uzun veya sonsuz döngü içeriyor olabilir.", false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Judge0 ağ hatası");
            return new PistonResult("",
                "Kod çalıştırma servisine bağlanılamadı. İnternet bağlantınızı kontrol edin.", false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Judge0 beklenmedik hata");
            return new PistonResult("", $"Servis hatası: {ex.Message}", false);
        }
    }

    /// <summary>
    /// Judge0 CE yanıtını Orka'nın PistonResult formatına dönüştürür.
    ///
    /// Judge0 status ID'leri:
    ///   1 = In Queue  2 = Processing  3 = Accepted (başarı)
    ///   4 = Wrong Answer  5 = Time Limit Exceeded
    ///   6 = Compilation Error  7 = Runtime Error (SIGSEGV)
    ///   8 = SIGXFSZ  9 = SIGFPE  10 = SIGABRT  11 = NZEC  12 = Other
    ///   13 = Internal Error  14 = Exec Format Error
    /// </summary>
    private PistonResult ParseJudge0Response(JsonElement json, string language)
    {
        var stdout        = GetString(json, "stdout")         ?? "";
        var stderr        = GetString(json, "stderr")         ?? "";
        var compileOutput = GetString(json, "compile_output") ?? "";
        var message       = GetString(json, "message")        ?? "";

        var statusId = 0;
        if (json.TryGetProperty("status", out var statusObj) &&
            statusObj.TryGetProperty("id", out var sid))
            statusId = sid.GetInt32();

        // Derleme hatası: compile_output'u stderr olarak sun
        if (statusId == 6)
        {
            var errMsg = string.IsNullOrWhiteSpace(compileOutput) ? stderr : compileOutput;
            _logger.LogDebug("{Lang} derleme hatası: {Err}", language, errMsg);
            return new PistonResult("", errMsg.TrimEnd(), false);
        }

        // Zaman aşımı
        if (statusId == 5)
            return new PistonResult(stdout.TrimEnd(),
                "Zaman aşımı: Kodunuz çok uzun sürdü veya sonsuz döngü içeriyor.", false);

        // Runtime hatası (7-12, 14)
        if (statusId >= 7 && statusId != 13)
        {
            var errMsg = !string.IsNullOrWhiteSpace(stderr)
                ? stderr
                : (!string.IsNullOrWhiteSpace(message) ? message : "Runtime hatası oluştu.");
            return new PistonResult(stdout.TrimEnd(), errMsg.TrimEnd(), false);
        }

        // Internal error (13)
        if (statusId == 13)
        {
            _logger.LogWarning("Judge0 internal error — message: {Msg}", message);
            return new PistonResult("", "Servis hatası oluştu. Lütfen tekrar deneyin.", false);
        }

        // Accepted (3) — stdout varsa başarı
        var success = statusId == 3 && string.IsNullOrWhiteSpace(stderr) && string.IsNullOrWhiteSpace(compileOutput);
        return new PistonResult(stdout.TrimEnd(), stderr.TrimEnd(), success);
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>"Python (3.12.5)" → "3.12.5"</summary>
    private static string ExtractVersion(string name)
    {
        var start = name.IndexOf('(');
        var end   = name.IndexOf(')');
        return start >= 0 && end > start ? name[(start + 1)..end] : "latest";
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Piston v2 API ile sandbox ortamında kod çalıştırır.
/// Desteklenen diller: csharp, python, javascript, java ve daha fazlası.
/// Public API — API anahtarı gerektirmez.
/// </summary>
public class PistonService : IPistonService
{
    private readonly HttpClient _http;
    private readonly ILogger<PistonService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PistonService(IHttpClientFactory factory, ILogger<PistonService> logger)
    {
        _http   = factory.CreateClient("Piston");
        _logger = logger;
    }

    public async Task<PistonResult> ExecuteAsync(string code, string language = "csharp")
    {
        var payload = new
        {
            language = language,
            version  = "*",   // En güncel sürümü kullan
            files    = new[] { new { content = code } }
        };

        try
        {
            var response = await _http.PostAsJsonAsync(
                "https://emkc.org/api/v2/piston/execute", payload);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Piston API hata döndü: {Status} — {Body}",
                    response.StatusCode, body);
                return new PistonResult("", $"Piston API hatası: {response.StatusCode}", false);
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);

            // Piston yanıt formatı: { "run": { "stdout": "...", "stderr": "...", "code": 0 } }
            if (!json.TryGetProperty("run", out var run))
                return new PistonResult("", "Piston yanıtı beklenmedik formatta.", false);

            var stdout   = run.TryGetProperty("stdout", out var so) ? so.GetString() ?? "" : "";
            var stderr   = run.TryGetProperty("stderr", out var se) ? se.GetString() ?? "" : "";
            var exitCode = run.TryGetProperty("code",   out var co) ? co.GetInt32()       : -1;

            var success = exitCode == 0 && string.IsNullOrWhiteSpace(stderr);
            return new PistonResult(stdout.TrimEnd(), stderr.TrimEnd(), success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Piston servisi çağrısı sırasında beklenmedik hata");
            return new PistonResult("", $"Servis hatası: {ex.Message}", false);
        }
    }
}

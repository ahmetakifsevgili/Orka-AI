using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Cohere Embedding Servisi — Semantik arama ve RAG pipeline için vektör üretir.
///
/// Model   : cohere-embed-v3-multilingual (embed-multilingual-v3.0)
/// Endpoint: https://api.cohere.com/v2/embed
/// Boyut   : 1024 float (normalize edilmiş)
///
/// Kullanım alanları:
///   - Wiki sayfaları arasında semantik benzerlik
///   - Kullanıcı sorusunu en yakın wiki bölümüne yönlendirme (RAG)
///   - Konu tekrarı tespiti
/// </summary>
public class CohereEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<CohereEmbeddingService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string EmbedEndpoint = "https://api.cohere.com/v1/embed";

    public CohereEmbeddingService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CohereEmbeddingService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("CohereEmbed");
        _apiKey     = configuration["AI:Cohere:ApiKey"] ?? throw new ArgumentException("AI:Cohere:ApiKey eksik.");
        _model      = configuration["AI:Cohere:EmbedModel"] ?? "embed-multilingual-v3.0";
        _logger     = logger;
    }

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(
        string text,
        string inputType = "search_query",
        CancellationToken ct = default)
    {
        var result = await EmbedBatchAsync([text], inputType, ct);
        return result[0];
    }

    /// <inheritdoc/>
    public async Task<float[][]> EmbedBatchAsync(
        IEnumerable<string> texts,
        string inputType = "search_document",
        CancellationToken ct = default)
    {
        var textList = texts.ToList();

        var body = new
        {
            texts      = textList,
            model      = _model,
            input_type = inputType
        };

        var json = JsonSerializer.Serialize(body);

        using var request = new HttpRequestMessage(HttpMethod.Post, EmbedEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug("[CohereEmbed] {Count} metin embed ediliyor, model={Model}", textList.Count, _model);

        var response = await _httpClient.SendAsync(request, ct);
        var respStr  = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("[CohereEmbed] Hata: {Status} — {Body}", response.StatusCode, respStr);
            throw new HttpRequestException($"Cohere Embed hatası: {response.StatusCode} — {respStr}");
        }

        using var doc = JsonDocument.Parse(respStr);

        // v1 Yanıt: { "embeddings": [[...], [...]] }
        var embeddings = doc.RootElement
            .GetProperty("embeddings")
            .EnumerateArray()
            .Select(arr => arr.EnumerateArray()
                .Select(v => v.GetSingle())
                .ToArray())
            .ToArray();

        return embeddings;
    }

    /// <inheritdoc/>
    public float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vektörler aynı boyutta olmalıdır.");

        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0) return 0f;
        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }
}

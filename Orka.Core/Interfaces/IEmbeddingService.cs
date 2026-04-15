namespace Orka.Core.Interfaces;

/// <summary>
/// Metin embedding (vektörel temsil) servisi.
/// Implementasyon: Cohere embed-multilingual-v3.0
/// Kullanım: Semantic Search, RAG pipeline, wiki benzerlik araması.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Tek bir metni vektöre dönüştürür.
    /// </summary>
    /// <param name="text">Embed edilecek metin</param>
    /// <param name="inputType">
    ///   "search_query"    → kullanıcı sorgularını embed etmek için
    ///   "search_document" → belgeleri/wiki sayfalarını embed etmek için
    /// </param>
    Task<float[]> EmbedAsync(
        string text,
        string inputType = "search_query",
        CancellationToken ct = default);

    /// <summary>
    /// Birden fazla metni tek API çağrısında toplu embed eder.
    /// </summary>
    Task<float[][]> EmbedBatchAsync(
        IEnumerable<string> texts,
        string inputType = "search_document",
        CancellationToken ct = default);

    /// <summary>
    /// İki vektör arasındaki cosine benzerliğini hesaplar (0..1).
    /// </summary>
    float CosineSimilarity(float[] a, float[] b);
}

namespace Orka.Core.Interfaces;

/// <summary>
/// Gelen dosyaları (PDF, TXT, MD) Korteks Swarm'ın anlayabileceği
/// düz Markdown metne çeviren servis.
/// </summary>
public interface IDocumentExtractorService
{
    /// <summary>
    /// Akıştan metin çıkarır.
    /// </summary>
    /// <param name="fileStream">Dosya akışı.</param>
    /// <param name="contentType">MIME tipi (application/pdf, text/plain vb.)</param>
    /// <param name="maxPages">PDF için maksimum sayfa limiti (maliyet kontrolü).</param>
    /// <returns>Çıkarılan düz metin / Markdown.</returns>
    Task<string> ExtractTextAsync(Stream fileStream, string contentType, int maxPages = 50);

    /// <summary>
    /// Dosya MIME tipinin desteklenip desteklenmediğini kontrol eder.
    /// </summary>
    bool IsSupported(string contentType);
}

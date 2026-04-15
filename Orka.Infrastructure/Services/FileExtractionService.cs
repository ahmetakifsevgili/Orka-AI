using System.Text;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Kullanıcıdan gelen dosyaları düz metne çevirir.
/// Desteklenen formatlar: PDF, TXT, MD.
/// Sonuç Korteks'in sistem promptuna context olarak enjekte edilir.
/// </summary>
public class FileExtractionService
{
    private readonly ILogger<FileExtractionService> _logger;

    // Korteks prompt'una enjekte edilecek max karakter — token limitini zorlamaz
    private const int MaxExtractChars = 8000;

    public FileExtractionService(ILogger<FileExtractionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Yüklenen dosyanın içeriğini düz metne çevirir.
    /// </summary>
    /// <param name="fileName">Orijinal dosya adı (uzantı tespiti için)</param>
    /// <param name="fileBytes">Dosya içeriği</param>
    /// <returns>Düz metin veya hata mesajı</returns>
    public string Extract(string fileName, byte[] fileBytes)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        try
        {
            return ext switch
            {
                ".pdf"  => ExtractPdf(fileBytes),
                ".txt"  => ExtractText(fileBytes),
                ".md"   => ExtractText(fileBytes),
                _       => $"[Desteklenmeyen dosya formatı: {ext}. PDF, TXT veya MD yükleyin.]"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FileExtraction] {File} okunamadı.", fileName);
            return $"[Dosya okunamadı: {ex.Message}]";
        }
    }

    // ── PDF ──────────────────────────────────────────────────────────────────

    private string ExtractPdf(byte[] bytes)
    {
        using var doc = PdfDocument.Open(bytes);
        var sb = new StringBuilder();

        foreach (var page in doc.GetPages())
        {
            sb.AppendLine(page.Text);
            if (sb.Length > MaxExtractChars) break;
        }

        var text = sb.ToString().Trim();

        if (string.IsNullOrWhiteSpace(text))
            return "[PDF metni çıkarılamadı — taranmış/görüntü tabanlı PDF olabilir.]";

        return text.Length > MaxExtractChars
            ? text[..MaxExtractChars] + $"\n\n[...metin kesildi, toplam PDF içeriğinin ilk {MaxExtractChars} karakteri gösterildi]"
            : text;
    }

    // ── TXT / MD ─────────────────────────────────────────────────────────────

    private static string ExtractText(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).Trim();
        return text.Length > MaxExtractChars
            ? text[..MaxExtractChars] + $"\n\n[...metin kesildi, ilk {MaxExtractChars} karakter gösterildi]"
            : text;
    }
}

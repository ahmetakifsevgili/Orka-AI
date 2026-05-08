using System.Text;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace Orka.Infrastructure.Services;

public record ExtractedPage(int PageNumber, string Text);

public record ExtractedDocument(IReadOnlyList<ExtractedPage> Pages, string? ErrorMessage = null)
{
    public int PageCount => Pages.Count;
    public string FullText => string.Join("\n\n", Pages.Select(p => $"[page:{p.PageNumber}]\n{p.Text}"));
}

/// <summary>
/// Kullanıcıdan gelen dosyaları düz metne çevirir.
/// Desteklenen formatlar: PDF, TXT, MD.
/// </summary>
public class FileExtractionService
{
    private readonly ILogger<FileExtractionService> _logger;

    private const int MaxExtractChars = 8000;

    public FileExtractionService(ILogger<FileExtractionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Korteks geriye uyumluluğu için düz metin döndürür.
    /// </summary>
    public string Extract(string fileName, byte[] fileBytes)
    {
        var doc = ExtractWithPages(fileName, fileBytes);
        if (!string.IsNullOrWhiteSpace(doc.ErrorMessage)) return doc.ErrorMessage;

        var text = doc.FullText.Trim();
        return text.Length > MaxExtractChars
            ? text[..MaxExtractChars] + $"\n\n[...metin kesildi, ilk {MaxExtractChars} karakter gösterildi]"
            : text;
    }

    /// <summary>
    /// NotebookLM kaynak pinning için sayfa numarasını koruyarak metin çıkarır.
    /// </summary>
    public ExtractedDocument ExtractWithPages(string fileName, byte[] fileBytes)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        try
        {
            return ext switch
            {
                ".pdf" => ExtractPdfPages(fileBytes),
                ".txt" => ExtractTextPages(fileBytes),
                ".md" => ExtractTextPages(fileBytes),
                _ => new ExtractedDocument([], $"[Desteklenmeyen dosya formatı: {ext}. PDF, TXT veya MD yükleyin.]")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FileExtraction] {File} okunamadı.", fileName);
            return new ExtractedDocument([], "[Dosya okunamadi. Dosya bicimini veya icerigini kontrol edip tekrar deneyin.]");
        }
    }

    private static ExtractedDocument ExtractPdfPages(byte[] bytes)
    {
        using var doc = PdfDocument.Open(bytes);
        var pages = new List<ExtractedPage>();

        foreach (var page in doc.GetPages())
        {
            var text = page.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
                pages.Add(new ExtractedPage(page.Number, text));
        }

        return pages.Count == 0
            ? new ExtractedDocument([], "[PDF metni çıkarılamadı; taranmış/görüntü tabanlı PDF olabilir.]")
            : new ExtractedDocument(pages);
    }

    private static ExtractedDocument ExtractTextPages(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).Trim();
        return string.IsNullOrWhiteSpace(text)
            ? new ExtractedDocument([], "[Dosyada okunabilir metin bulunamadı.]")
            : new ExtractedDocument([new ExtractedPage(1, text)]);
    }
}

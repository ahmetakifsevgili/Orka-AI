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
/// KullanÄ±cÄ±dan gelen dosyalarÄ± dÃ¼z metne Ã§evirir.
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
    /// Korteks geriye uyumluluÄŸu iÃ§in dÃ¼z metin dÃ¶ndÃ¼rÃ¼r.
    /// </summary>
    public string Extract(string fileName, byte[] fileBytes)
    {
        var doc = ExtractWithPages(fileName, fileBytes);
        if (!string.IsNullOrWhiteSpace(doc.ErrorMessage)) return doc.ErrorMessage;

        var text = doc.FullText.Trim();
        return text.Length > MaxExtractChars
            ? text[..MaxExtractChars] + $"\n\n[...metin kesildi, ilk {MaxExtractChars} karakter gÃ¶sterildi]"
            : text;
    }

    /// <summary>
    /// NotebookLM kaynak pinning iÃ§in sayfa numarasÄ±nÄ± koruyarak metin Ã§Ä±karÄ±r.
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
                _ => new ExtractedDocument([], $"[Desteklenmeyen dosya formatÄ±: {ext}. PDF, TXT veya MD yÃ¼kleyin.]")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FileExtraction] {File} okunamadÄ±.", fileName);
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
            ? new ExtractedDocument([], "[PDF metni Ã§Ä±karÄ±lamadÄ±; taranmÄ±ÅŸ/gÃ¶rÃ¼ntÃ¼ tabanlÄ± PDF olabilir.]")
            : new ExtractedDocument(pages);
    }

    private static ExtractedDocument ExtractTextPages(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).Trim();
        return string.IsNullOrWhiteSpace(text)
            ? new ExtractedDocument([], "[Dosyada okunabilir metin bulunamadÄ±.]")
            : new ExtractedDocument([new ExtractedPage(1, text)]);
    }
}

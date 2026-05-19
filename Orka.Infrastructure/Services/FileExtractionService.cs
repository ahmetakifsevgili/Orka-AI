using System.Text;
using Microsoft.Extensions.Logging;
using Orka.Core.Exceptions;
using Orka.Infrastructure.Utilities;
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
    private readonly UploadContentSafetyGuard _guard;

    private const int MaxExtractChars = 8000;

    public FileExtractionService(ILogger<FileExtractionService> logger, UploadContentSafetyGuard guard)
    {
        _logger = logger;
        _guard = guard;
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
        _guard.ValidateBytes(fileName, null, fileBytes);

        try
        {
            var document = ext switch
            {
                ".pdf" => ExtractPdfPages(fileBytes),
                ".txt" => ExtractTextPages(fileBytes),
                ".md" => ExtractTextPages(fileBytes),
                _ => new ExtractedDocument([], $"[Desteklenmeyen dosya formatı: {ext}. PDF, TXT veya MD yükleyin.]")
            };

            _guard.ValidateExtractedDocument(document.Pages);
            return document;
        }
        catch (ContentSafetyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[FileExtraction] File okunamadi. FileRef={FileRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeTextRef(fileName, "file"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return new ExtractedDocument([], "[Dosya okunamadi. Dosya bicimini veya icerigini kontrol edip tekrar deneyin.]");
        }
    }

    private ExtractedDocument ExtractPdfPages(byte[] bytes)
    {
        using var doc = PdfDocument.Open(bytes);
        if (doc.NumberOfPages > _guard.Options.MaxPdfPages)
            throw ContentSafetyException.PayloadTooLarge("PDF sayfa limitini asiyor.");

        var pages = new List<ExtractedPage>();
        var totalChars = 0;

        foreach (var page in doc.GetPages())
        {
            var text = page.Text?.Trim() ?? string.Empty;
            totalChars += text.Length;
            if (totalChars > _guard.Options.MaxExtractedChars)
                throw ContentSafetyException.PayloadTooLarge("Kaynak metni isleme limitini asiyor.");

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

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// PdfPig tabanlı doküman metin çıkarıcı.
/// PDF, TXT ve Markdown dosyalarını Korteks Swarm'ın anlayabileceği
/// düz Markdown metne çevirir. Tüm çalışma in-memory'de gerçekleşir —
/// disk yazımı yoktur. Docker ve prod ortamlarıyla tam uyumludur.
/// </summary>
public class DocumentExtractorService : IDocumentExtractorService
{
    private static readonly string[] SupportedTypes =
    [
        "application/pdf",
        "text/plain",
        "text/markdown",
        "text/x-markdown"
    ];

    public bool IsSupported(string contentType)
    {
        var ct = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return SupportedTypes.Any(s => ct.StartsWith(s));
    }

    public async Task<string> ExtractTextAsync(Stream fileStream, string contentType, int maxPages = 50)
    {
        var ct = contentType.Split(';')[0].Trim().ToLowerInvariant();

        if (ct == "application/pdf")
            return await ExtractPdfAsync(fileStream, maxPages);

        // TXT / MD — direkt oku
        using var reader = new System.IO.StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync();

        // MD dosyasını olduğu gibi gönder; TXT'yi minimal Markdown'a çevir
        if (ct is "text/markdown" or "text/x-markdown")
            return text;

        // Plain text → satır satır oku, uzun paragrafları koru
        return $"```\n{text.Trim()}\n```";
    }

    // ── PDF ──────────────────────────────────────────────────────────────────

    private static Task<string> ExtractPdfAsync(Stream stream, int maxPages)
    {
        // PdfPig stream'i senkron okur; await için Task.Run kullanıyoruz
        return Task.Run(() =>
        {
            // Belleğe al (stream seek edilebilir olmayabilir)
            byte[] bytes;
            using (var ms = new System.IO.MemoryStream())
            {
                stream.CopyTo(ms);
                bytes = ms.ToArray();
            }

            using var doc = PdfDocument.Open(bytes);
            var sb = new StringBuilder();

            var pagesToProcess = Math.Min(doc.NumberOfPages, maxPages);

            sb.AppendLine($"# PDF Belgesi — {pagesToProcess} / {doc.NumberOfPages} Sayfa");
            sb.AppendLine();

            if (doc.NumberOfPages > maxPages)
                sb.AppendLine($"> ⚠️ Belge {doc.NumberOfPages} sayfa içeriyor. İlk {maxPages} sayfa işlendi.");

            for (int i = 1; i <= pagesToProcess; i++)
            {
                var page = doc.GetPage(i);
                var pageText = ExtractPageText(page);

                if (string.IsNullOrWhiteSpace(pageText)) continue;

                sb.AppendLine($"## Sayfa {i}");
                sb.AppendLine();
                sb.AppendLine(pageText.Trim());
                sb.AppendLine();
            }

            return sb.ToString();
        });
    }

    private static string ExtractPageText(Page page)
    {
        // PdfPig word-level extraction → glyph → natural reading order
        var words = page.GetWords();

        if (!words.Any()) return string.Empty;

        var sb = new StringBuilder();
        double? lastBaseline = null;
        const double LineBreakThreshold = 2.0; // pt cinsinden satır yüksekliği toleransı

        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var baseline = Math.Round(word.BoundingBox.Bottom, 1);

            if (lastBaseline.HasValue && Math.Abs(lastBaseline.Value - baseline) > LineBreakThreshold)
                sb.AppendLine(); // Yeni satır

            sb.Append(word.Text);
            sb.Append(' ');
            lastBaseline = baseline;
        }

        return sb.ToString();
    }
}

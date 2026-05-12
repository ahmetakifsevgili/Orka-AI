using System.Text;
using Microsoft.Extensions.Options;
using Orka.Core.Exceptions;

namespace Orka.Infrastructure.Services;

public sealed class UploadContentSafetyGuard
{
    private static readonly HashSet<string> PdfMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/octet-stream",
        string.Empty
    };

    private static readonly HashSet<string> TextMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/markdown",
        "application/octet-stream",
        string.Empty
    };

    private readonly UploadContentSafetyOptions _options;

    public UploadContentSafetyGuard(IOptions<ContentSafetyOptions> options)
    {
        _options = options.Value.Uploads;
    }

    public UploadContentSafetyOptions Options => _options;

    public void ValidateMetadata(string fileName, string? contentType, long length)
    {
        if (length <= 0)
            throw ContentSafetyException.BadRequest("Dosya zorunlu.");

        if (length > _options.MaxFileBytes)
            throw ContentSafetyException.PayloadTooLarge("Dosya boyutu limitini asiyor.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not ".pdf" and not ".txt" and not ".md")
            throw ContentSafetyException.BadRequest("Sadece PDF, TXT veya MD destekleniyor.");

        var mime = NormalizeMime(contentType);
        var allowed = ext == ".pdf" ? PdfMimeTypes : TextMimeTypes;
        if (!allowed.Contains(mime))
            throw ContentSafetyException.BadRequest("Dosya turu desteklenmiyor.");
    }

    public void ValidateBytes(string fileName, string? contentType, byte[] bytes)
    {
        ValidateMetadata(fileName, contentType, bytes.LongLength);

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext == ".pdf")
        {
            if (bytes.Length < 5 || bytes[0] != 0x25 || bytes[1] != 0x50 || bytes[2] != 0x44 || bytes[3] != 0x46 || bytes[4] != 0x2D)
                throw ContentSafetyException.BadRequest("PDF dosya imzasi gecersiz.");
            return;
        }

        ValidateTextBytes(bytes);
    }

    public void ValidateExtractedDocument(IReadOnlyList<ExtractedPage> pages)
    {
        if (pages.Count > _options.MaxPdfPages)
            throw ContentSafetyException.PayloadTooLarge("PDF sayfa limitini asiyor.");

        var totalChars = 0;
        foreach (var page in pages)
        {
            totalChars += page.Text?.Length ?? 0;
            if (totalChars > _options.MaxExtractedChars)
                throw ContentSafetyException.PayloadTooLarge("Kaynak metni isleme limitini asiyor.");
        }
    }

    public void ValidateChunkCount(int chunkCount)
    {
        if (chunkCount > _options.MaxChunksPerSource)
            throw ContentSafetyException.PayloadTooLarge("Kaynak cok fazla parca uretiyor.");

        if (chunkCount > _options.MaxEmbeddingChunksPerUpload)
            throw ContentSafetyException.TooManyRequests("Embedding isleme kotasi asildi.");
    }

    private static string NormalizeMime(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return string.Empty;
        return contentType.Split(';', 2)[0].Trim();
    }

    private static void ValidateTextBytes(byte[] bytes)
    {
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw ContentSafetyException.BadRequest("Metin dosyasi UTF-8 olmalidir.");
        }

        var sampleLength = Math.Min(bytes.Length, 4096);
        var nulCount = 0;
        var controlCount = 0;
        for (var i = 0; i < sampleLength; i++)
        {
            var b = bytes[i];
            if (b == 0) nulCount++;
            if (b < 32 && b is not 9 and not 10 and not 13) controlCount++;
        }

        if (nulCount > 0 || controlCount > sampleLength / 20)
            throw ContentSafetyException.BadRequest("Metin dosyasi binary icerik gibi gorunuyor.");
    }
}

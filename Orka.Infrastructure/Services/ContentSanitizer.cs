using System;
using System.Text.RegularExpressions;

namespace Orka.Infrastructure.Services;

/// <summary>
/// AI cevap metni için post-processing temizleyici. Şu an tek iş: markdown
/// görsel URL'lerini Wikipedia Commons whitelist'i ile doğrula. Prompt'ta
/// "sadece Wikimedia" kuralı var ama model bazen uydurabiliyor; DB'ye
/// yanlış URL yazılmasın diye burada ikinci savunma hattı.
/// </summary>
public static class ContentSanitizer
{
    private static readonly Regex ImageRegex = new(
        @"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)(?:\s+""[^""]*"")?\)",
        RegexOptions.Compiled);

    private static readonly string[] AllowedHosts =
    {
        "upload.wikimedia.org",
        "commons.wikimedia.org",
        "tr.wikipedia.org",
        "en.wikipedia.org",
        // Orka TutorAgent kendi görsellerini Pollinations.ai ile üretiyor — whitelist'te zorunlu.
        "image.pollinations.ai",
        "pollinations.ai"
    };

    public static string SanitizeImages(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;

        return ImageRegex.Replace(content, m =>
        {
            var alt = m.Groups["alt"].Value;
            var url = m.Groups["url"].Value;

            if (IsAllowed(url)) return m.Value;

            // Yasak URL: alt-text'i italik metin olarak bırak, görseli kaldır.
            return string.IsNullOrWhiteSpace(alt) ? string.Empty : $"*{alt}*";
        });
    }

    private static bool IsAllowed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != "https") return false;

        var host = uri.Host.ToLowerInvariant();
        foreach (var allowed in AllowedHosts)
        {
            if (host == allowed) return true;
        }
        return false;
    }
}

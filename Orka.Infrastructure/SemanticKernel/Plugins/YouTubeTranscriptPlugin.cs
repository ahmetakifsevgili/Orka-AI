using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// YouTubeTranscriptPlugin: egitim videosu arama ve transcript cekme araci.
/// Cikti her zaman kaynak etiketi tasir: [youtube:videoId], [youtube:disabled] veya [youtube:degraded].
/// </summary>
public partial class YouTubeTranscriptPlugin
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<YouTubeTranscriptPlugin> _logger;

    private const int MaxTranscriptChars = 3000;

    public YouTubeTranscriptPlugin(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<YouTubeTranscriptPlugin> logger)
    {
        _httpClient = httpClientFactory.CreateClient("YouTube");
        _apiKey = configuration["AI:YouTube:ApiKey"] ?? configuration["YouTube:ApiKey"] ?? string.Empty;
        _logger = logger;
    }

    [KernelFunction, Description(
        "YouTube'da egitim videosu arar. Sonuclardaki videoId ile GetVideoTranscript cagrilabilir. " +
        "Egitim konulari icin kullan: programlama, matematik, fizik, tarih vb.")]
    public async Task<string> SearchYouTubeVideos(
        [Description("Aranacak egitim konusu, ornek: Python for dongusu veya Newton hareket yasalari")] string query)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("[YouTube] API key bulunamadi. YouTube aramasi atlandi.");
            return "[youtube:disabled] YouTube aramasi devre disi: API key yapilandirilmamis.";
        }

        try
        {
            var encodedQuery = Uri.EscapeDataString(query + " tutorial egitim");
            var url = "https://www.googleapis.com/youtube/v3/search" +
                      $"?part=snippet&type=video&q={encodedQuery}" +
                      "&maxResults=3&order=relevance&relevanceLanguage=tr" +
                      $"&videoCategoryId=27&key={_apiKey}";

            _logger.LogInformation("[YouTube] Video araniyor: {Query}", query);

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[YouTube] API hata: {Status} - {Body}",
                    response.StatusCode, errorBody.Length > 200 ? errorBody[..200] : errorBody);
                return $"[youtube:degraded] YouTube aramasi gecici olarak kullanilamiyor. Status={(int)response.StatusCode}.";
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var items = doc.RootElement.GetProperty("items");
            if (items.GetArrayLength() == 0)
            {
                return "[youtube:degraded] YouTube'da bu konuda video bulunamadi.";
            }

            var videoInfos = new List<(string Id, string Title, string Channel, string Description)>();
            foreach (var item in items.EnumerateArray())
            {
                var videoId = item.GetProperty("id").GetProperty("videoId").GetString() ?? string.Empty;
                var snippet = item.GetProperty("snippet");
                var title = snippet.GetProperty("title").GetString() ?? string.Empty;
                var channel = snippet.GetProperty("channelTitle").GetString() ?? string.Empty;
                var desc = snippet.GetProperty("description").GetString() ?? string.Empty;
                if (desc.Length > 200) desc = desc[..200] + "...";
                videoInfos.Add((videoId, title, channel, desc));
            }

            var statsMap = await FetchVideoStatisticsAsync(videoInfos.Select(v => v.Id).ToList());
            var sorted = videoInfos
                .OrderByDescending(v => statsMap.TryGetValue(v.Id, out var s) ? s.ViewCount : 0)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("**YouTube egitim videolari ([youtube] kaynaklari):**");
            sb.AppendLine();

            foreach (var video in sorted)
            {
                var hasStats = statsMap.TryGetValue(video.Id, out var stats);
                sb.AppendLine($"- **{video.Title}** [youtube:{video.Id}]");
                sb.AppendLine($"  Kanal: {video.Channel}");
                sb.AppendLine($"  VideoId: `{video.Id}`");
                if (hasStats && stats != null)
                {
                    sb.AppendLine($"  Izlenme: {FormatCount(stats.ViewCount)} | Begeni: {FormatCount(stats.LikeCount)}");
                }
                sb.AppendLine($"  Kaynak: https://youtube.com/watch?v={video.Id}");
                if (!string.IsNullOrWhiteSpace(video.Description))
                {
                    sb.AppendLine($"  Aciklama: {video.Description}");
                }
                sb.AppendLine();
            }

            _logger.LogInformation("[YouTube] {Count} video bulundu.", sorted.Count);
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[YouTube] Arama sirasinda hata.");
            return "[youtube:degraded] YouTube aramasi gecici olarak kullanilamiyor.";
        }
    }

    [KernelFunction, Description(
        "Belirtilen YouTube videosunun transcript/caption verisini ceker. " +
        "Transcript yoksa video meta bilgisini kaynak etiketiyle dondurur.")]
    public async Task<string> GetVideoTranscript(
        [Description("YouTube video ID, ornek: dQw4w9WgXcQ")] string videoId,
        [Description("Tercih edilen dil: tr veya en. Varsayilan: tr")] string lang = "tr")
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return "[youtube:degraded] VideoId bos oldugu icin transcript alinamadi.";
        }

        try
        {
            _logger.LogInformation("[YouTube] Transcript cekiliyor: VideoId={VideoId}, Lang={Lang}", videoId, lang);

            var transcript = await FetchCaptionTrackAsync(videoId, lang);
            if (string.IsNullOrWhiteSpace(transcript) && lang == "tr")
            {
                _logger.LogInformation("[YouTube] Turkce transcript yok, Ingilizce deneniyor.");
                transcript = await FetchCaptionTrackAsync(videoId, "en");
            }

            if (!string.IsNullOrWhiteSpace(transcript))
            {
                if (transcript.Length > MaxTranscriptChars)
                {
                    transcript = transcript[..MaxTranscriptChars] + "\n\n[...transcript devami kirpildi]";
                }

                return $"**[youtube:{videoId}] YouTube transcript**\n" +
                       $"Kaynak: https://youtube.com/watch?v={videoId}\n\n" +
                       transcript;
            }

            _logger.LogInformation("[YouTube] Transcript bulunamadi, meta bilgi fallback kullaniliyor.");
            return await FetchVideoMetadataFallbackAsync(videoId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[YouTube] Transcript cekme hatasi. VideoId={VideoId}", videoId);
            return $"[youtube:degraded] YouTube transcript gecici olarak alinamadi. Kaynak: https://youtube.com/watch?v={videoId}";
        }
    }

    private async Task<string?> FetchCaptionTrackAsync(string videoId, string lang)
    {
        try
        {
            var pageUrl = $"https://www.youtube.com/watch?v={videoId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
            request.Headers.Add("Accept-Language", lang == "tr" ? "tr-TR,tr;q=0.9" : "en-US,en;q=0.9");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var html = await response.Content.ReadAsStringAsync();
            var captionMatch = CaptionTracksRegex().Match(html);
            if (!captionMatch.Success) return null;

            var captionsJson = captionMatch.Groups[1].Value
                .Replace("\\u0026", "&")
                .Replace("\\/", "/");

            using var captionsDoc = JsonDocument.Parse(captionsJson);

            string? captionUrl = null;
            foreach (var track in captionsDoc.RootElement.EnumerateArray())
            {
                if (!track.TryGetProperty("baseUrl", out var baseUrlProp)) continue;
                var trackLang = track.TryGetProperty("languageCode", out var lc) ? lc.GetString() : string.Empty;
                if (trackLang == lang)
                {
                    captionUrl = baseUrlProp.GetString();
                    break;
                }
            }

            if (captionUrl == null)
            {
                foreach (var track in captionsDoc.RootElement.EnumerateArray())
                {
                    if (track.TryGetProperty("baseUrl", out var fallbackUrl))
                    {
                        captionUrl = fallbackUrl.GetString();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(captionUrl)) return null;

            var xmlResponse = await _httpClient.GetAsync(captionUrl);
            if (!xmlResponse.IsSuccessStatusCode) return null;

            var xmlContent = await xmlResponse.Content.ReadAsStringAsync();
            var textMatches = CaptionTextRegex().Matches(xmlContent);
            if (textMatches.Count == 0) return null;

            var sb = new StringBuilder();
            double currentBlockStart = -1;
            const double blockSizeSeconds = 60.0;

            foreach (Match match in textMatches)
            {
                var startStr = match.Groups[1].Value;
                var text = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value)
                    .Replace("\n", " ")
                    .Trim();

                if (string.IsNullOrWhiteSpace(text)) continue;

                if (double.TryParse(startStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var startSec))
                {
                    if (currentBlockStart < 0 || startSec >= currentBlockStart + blockSizeSeconds)
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        var timeSpan = TimeSpan.FromSeconds(startSec);
                        sb.Append($"[{timeSpan:mm\\:ss}] ");
                        currentBlockStart = startSec;
                    }
                }

                sb.Append(text).Append(' ');
            }

            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[YouTube] Caption track parse hatasi. VideoId={VideoId}", videoId);
            return null;
        }
    }

    private async Task<string> FetchVideoMetadataFallbackAsync(string videoId)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return $"[youtube:{videoId}] Transcript bulunamadi; YouTube API devre disi. Kaynak: https://youtube.com/watch?v={videoId}";
        }

        try
        {
            var url = "https://www.googleapis.com/youtube/v3/videos" +
                      $"?part=snippet,statistics&id={videoId}&key={_apiKey}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return $"[youtube:degraded] Transcript veya metadata alinamadi. Kaynak: https://youtube.com/watch?v={videoId}";
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var items = doc.RootElement.GetProperty("items");
            if (items.GetArrayLength() == 0)
            {
                return $"[youtube:degraded] Video bulunamadi. VideoId={videoId}.";
            }

            var snippet = items[0].GetProperty("snippet");
            var title = snippet.GetProperty("title").GetString();
            var channel = snippet.GetProperty("channelTitle").GetString();
            var description = snippet.GetProperty("description").GetString() ?? string.Empty;
            var stats = items[0].GetProperty("statistics");
            var views = stats.TryGetProperty("viewCount", out var vc) ? vc.GetString() : "?";

            if (description.Length > 800)
            {
                description = description[..800] + "...";
            }

            return $"**[youtube:{videoId}] YouTube video metadatasi**\n" +
                   $"Baslik: {title}\n" +
                   $"Kanal: {channel}\n" +
                   $"Izlenme: {views}\n" +
                   $"Kaynak: https://youtube.com/watch?v={videoId}\n\n" +
                   $"Video aciklamasi:\n{description}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[YouTube] Meta bilgi cekme hatasi.");
            return $"[youtube:degraded] Transcript ve meta bilgi gecici olarak alinamadi. Kaynak: https://youtube.com/watch?v={videoId}";
        }
    }

    private record VideoStats(long ViewCount, long LikeCount);

    private async Task<Dictionary<string, VideoStats>> FetchVideoStatisticsAsync(List<string> videoIds)
    {
        var result = new Dictionary<string, VideoStats>();
        if (videoIds.Count == 0 || string.IsNullOrWhiteSpace(_apiKey)) return result;

        try
        {
            var ids = string.Join(",", videoIds);
            var url = "https://www.googleapis.com/youtube/v3/videos" +
                      $"?part=statistics&id={ids}&key={_apiKey}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return result;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? string.Empty;
                var stats = item.GetProperty("statistics");
                var views = stats.TryGetProperty("viewCount", out var vc)
                    ? long.TryParse(vc.GetString(), out var v) ? v : 0 : 0;
                var likes = stats.TryGetProperty("likeCount", out var lc)
                    ? long.TryParse(lc.GetString(), out var l) ? l : 0 : 0;
                result[id] = new VideoStats(views, likes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[YouTube] Video istatistikleri cekilirken hata.");
        }

        return result;
    }

    private static string FormatCount(long count) => count switch
    {
        >= 1_000_000 => $"{count / 1_000_000.0:F1}M",
        >= 1_000 => $"{count / 1_000.0:F1}K",
        _ => count.ToString()
    };

    [GeneratedRegex(@"""captionTracks"":\s*(\[.*?\])", RegexOptions.Singleline)]
    private static partial Regex CaptionTracksRegex();

    [GeneratedRegex(@"<text[^>]*start=""([\d\.]+)""[^>]*>(.*?)</text>", RegexOptions.Singleline)]
    private static partial Regex CaptionTextRegex();
}

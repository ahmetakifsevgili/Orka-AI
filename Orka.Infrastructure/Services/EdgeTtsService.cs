using System.Text.RegularExpressions;
using EdgeTtsSharp;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public class EdgeTtsService : IEdgeTtsService
{
    private readonly ILogger<EdgeTtsService> _logger;
    private static readonly Regex SpeakerRegex =
        new(@"\[(HOCA|ASISTAN|ASİSTAN|KONUK)\]\s*:\s*(.+?)(?=\n\s*\[(?:HOCA|ASISTAN|ASİSTAN|KONUK)\]\s*:|\z)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public EdgeTtsService(ILogger<EdgeTtsService> logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> SynthesizeDialogueAsync(string script, CancellationToken ct = default)
    {
        var segments = SpeakerRegex.Matches(script)
            .Select(m => (Speaker: m.Groups[1].Value.ToUpperInvariant(), Text: m.Groups[2].Value.Trim()))
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .ToList();

        if (segments.Count == 0)
        {
            // Eğer script regex'e uymazsa, tamamını HOCA olarak okutalım.
            segments.Add(("HOCA", script.Replace("```", "").Trim()));
        }

        var combined = new MemoryStream();

        foreach (var (speaker, text) in segments)
        {
            ct.ThrowIfCancellationRequested();

            var voiceName = speaker switch
            {
                "ASISTAN" or "ASİSTAN" => "tr-TR-EmelNeural",
                "KONUK"   => "tr-TR-EmelNeural",
                _          => "tr-TR-AhmetNeural"   // HOCA (varsayılan)
            };

            try
            {
                var voice = await EdgeTts.GetVoice(voiceName);
                await using var segStream = voice.GetAudioStream(text);
                await segStream.CopyToAsync(combined, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EdgeTtsService] Segment TTS hatası. Speaker={Speaker} Voice={Voice}", speaker, voiceName);
            }
        }

        if (combined.Length == 0)
            throw new InvalidOperationException("Edge-TTS hiçbir segment için ses üretemedi.");

        return combined.ToArray();
    }
}

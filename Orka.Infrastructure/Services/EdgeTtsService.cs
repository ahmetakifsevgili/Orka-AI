using EdgeTtsSharp;
using EdgeTtsSharp.Structures;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public class EdgeTtsService : IEdgeTtsService
{
    private readonly ILogger<EdgeTtsService> _logger;

    public EdgeTtsService(ILogger<EdgeTtsService> logger)
    {
        _logger = logger;
    }

    public Task<byte[]> SynthesizeDialogueAsync(string script, CancellationToken ct = default) =>
        SynthesizeDialogueAsync(script, null, ct);

    public async Task<byte[]> SynthesizeDialogueAsync(string script, string? ttsQuality, CancellationToken ct = default)
    {
        var segments = AudioDialogueFormatter.ParseSegments(script);
        var combined = new MemoryStream();
        var settings = PlaybackSettingsFor(ttsQuality);

        foreach (var (speaker, text) in segments)
        {
            ct.ThrowIfCancellationRequested();

            var voiceName = speaker switch
            {
                "ASISTAN" => "tr-TR-EmelNeural",
                "KONUK" => "tr-TR-EmelNeural",
                _ => "tr-TR-AhmetNeural"
            };

            try
            {
                var voice = await EdgeTts.GetVoice(voiceName);
                await using var segStream = voice.GetAudioStream(text, settings, ct);
                await segStream.CopyToAsync(combined, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[EdgeTtsService] Segment TTS failed. Speaker={Speaker} VoiceRef={VoiceRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeMessage(speaker, 32),
                    LogPrivacyGuard.SafeTextRef(voiceName, "voice"),
                    LogPrivacyGuard.SafeExceptionType(ex));
            }
        }

        if (combined.Length == 0)
            throw new InvalidOperationException("Edge-TTS hicbir segment icin ses uretemedi.");

        return combined.ToArray();
    }

    private static PlaybackSettings PlaybackSettingsFor(string? ttsQuality)
    {
        var key = string.IsNullOrWhiteSpace(ttsQuality)
            ? "standard"
            : ttsQuality.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

        return key switch
        {
            "draft" => new PlaybackSettings { Rate = 12, Volume = 0f },
            "studio" => new PlaybackSettings { Rate = -4, Volume = 0f },
            _ => new PlaybackSettings { Rate = 0, Volume = 0f }
        };
    }
}

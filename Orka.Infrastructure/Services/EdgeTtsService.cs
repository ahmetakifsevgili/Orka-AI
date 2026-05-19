using EdgeTtsSharp;
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

    public async Task<byte[]> SynthesizeDialogueAsync(string script, CancellationToken ct = default)
    {
        var segments = AudioDialogueFormatter.ParseSegments(script);
        var combined = new MemoryStream();

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
                await using var segStream = voice.GetAudioStream(text);
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
}

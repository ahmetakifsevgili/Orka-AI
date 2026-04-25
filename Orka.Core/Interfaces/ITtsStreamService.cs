using System.Collections.Generic;
using System.Threading;

namespace Orka.Core.Interfaces;

/// <summary>
/// A future-proof backend Audio Streaming service for Orka V2.
/// Consumes text chunks from the LLM and yields raw binary audio streams to the frontend.
/// </summary>
public interface ITtsStreamService
{
    /// <summary>
    /// Processes an ongoing LLM string stream and converts it into a continuous binary audio stream.
    /// Uses semantic chunking (sentence boundaries) to achieve zero-latency.
    /// </summary>
    IAsyncEnumerable<byte[]> GetAudioStreamAsync(IAsyncEnumerable<string> textChunks, CancellationToken cancellationToken = default, TtsVoice voice = TtsVoice.Hoca);
    
    /// <summary>
    /// Produces a binary audio stream for a static completed text.
    /// </summary>
    IAsyncEnumerable<byte[]> GetAudioStreamAsync(string text, CancellationToken cancellationToken = default, TtsVoice voice = TtsVoice.Hoca);
}

/// <summary>
/// Orka Sesli Sınıf Ses Karakterleri.
/// Hoca = Erkek (AhmetNeural) - Ders anlatır, otoriter, bilgili.
/// Asistan = Kadın (EmelNeural) - Quiz yönetimi, yönlendirme, sıcak ton.
/// </summary>
public enum TtsVoice
{
    /// <summary>Erkek ses - Ders anlatan hoca (tr-TR-AhmetNeural)</summary>
    Hoca,
    /// <summary>Kadın ses - Yönlendirici asistan (tr-TR-EmelNeural)</summary>
    Asistan
}

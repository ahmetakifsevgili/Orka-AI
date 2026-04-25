using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.SemanticKernel.Audio;

/// <summary>
/// Python edge-tts kütüphanesini (v7.2.8+) subprocess olarak çağırarak
/// Microsoft Edge TTS servisine bağlanır.
/// 
/// NEDEN SUBPROCESS?
/// Microsoft, Edge Read Aloud WebSocket API'sine periyodik olarak DRM güncellemesi yapıyor.
/// Python edge-tts topluluğu bu değişiklikleri saatler içinde çözüp güncelleme yayınlıyor.
/// Biz de `pip install --upgrade edge-tts` ile tek komutla güncel kalıyoruz.
/// C# ile birebir WebSocket portlaması yapmak, her Microsoft güncellemesinde kırılmaya mahkum.
/// 
/// Mimari: C# -> Python subprocess (edge-tts CLI) -> stdout (binary MP3) -> C# IAsyncEnumerable yield
/// </summary>
public class EdgeTtsStreamService : ITtsStreamService
{
    private readonly ILogger<EdgeTtsStreamService> _logger;
    private readonly string _pythonPath;
    
    // Throttle concurrent Edge-TTS python processes to prevent high CPU/RAM usage
    private static readonly SemaphoreSlim _throttleSemaphore = new SemaphoreSlim(2, 2);

    public EdgeTtsStreamService(ILogger<EdgeTtsStreamService> logger)
    {
        _logger = logger;
        _pythonPath = FindPythonPath();
    }
    
    private static string VoiceToEdgeName(TtsVoice voice) => voice switch
    {
        TtsVoice.Hoca => "tr-TR-AhmetNeural",
        TtsVoice.Asistan => "tr-TR-EmelNeural",
        _ => "tr-TR-AhmetNeural"
    };

    private static string FindPythonPath()
    {
        // Önce bilinen konumları dene
        var knownPaths = new[]
        {
            @"C:\Users\ahmet\AppData\Local\Python\pythoncore-3.14-64\python.exe",
            "python",
            "python3"
        };
        
        foreach (var path in knownPaths)
        {
            if (File.Exists(path)) return path;
        }
        
        // PATH'ten bulmayı dene
        return "python";
    }

    public async IAsyncEnumerable<byte[]> GetAudioStreamAsync(
        string text, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        TtsVoice voice = TtsVoice.Hoca)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var voiceName = VoiceToEdgeName(voice);
        
        // ÖNEMLİ BUGFIX: Komut satırına çift tırnaklı, özel karakterli cümle atınca cmd patlıyordu.
        // O yüzden cümlenin içeriğini mecburi bir temp dosyaya yazıp --file parametresiyle çalıştırıyoruz.
        string tempFilePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFilePath, text, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EdgeTts Temp dosya yazılamadı.");
            yield break;
        }

        var psi = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = $"-m edge_tts --voice {voiceName} --file \"{tempFilePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        await _throttleSemaphore.WaitAsync(cancellationToken);
        Process? process = null;
        try
        {
            process = Process.Start(psi);
            if (process == null) 
            {
                _logger.LogError("edge-tts Python process başlatılamadı.");
                yield break;
            }

            using var registration = cancellationToken.Register(() =>
            {
                try { process?.Kill(true); } catch { /* swallow */ }
            });

            var stream = process.StandardOutput.BaseStream;
            var buffer = new byte[8192]; // 8KB chunks
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                var chunk = new byte[bytesRead];
                Array.Copy(buffer, chunk, bytesRead);
                yield return chunk;
            }

            await process.WaitForExitAsync(cancellationToken);
            
            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError("edge-tts process hata ile çıktı (exit {Code}): {Error}", process.ExitCode, stderr);
                }
            }
        }
        finally
        {
            _throttleSemaphore.Release();
            if (process != null && !process.HasExited)
            {
                try { process.Kill(true); } catch { /* swallow */ }
            }
            process?.Dispose();

            // Sesi ürettik (veya iptal oldu), geçici dosyayı temizle
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch { /* Temp dosya silinemediyse yut */ }
        }
    }

    /// <summary>
    /// LLM'den gelen token stream'ini cümlelere bölerek ses akışına çevirir.
    /// </summary>
    public async IAsyncEnumerable<byte[]> GetAudioStreamAsync(
        IAsyncEnumerable<string> textChunks, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        TtsVoice voice = TtsVoice.Hoca)
    {
        var sentenceBuffer = new System.Text.StringBuilder();
         
        await foreach (var chunk in textChunks.WithCancellation(cancellationToken))
        {
            sentenceBuffer.Append(chunk);
             
            if (chunk.Contains(".") || chunk.Contains("?") || chunk.Contains("!") || chunk.Contains("\n"))
            {
                var sentence = sentenceBuffer.ToString().Trim();
                sentenceBuffer.Clear();

                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    await foreach (var audioBytes in GetAudioStreamAsync(sentence, cancellationToken, voice))
                    {
                        yield return audioBytes;
                    }
                }
            }
        }

        // Kalan metni de ses yap
        if (sentenceBuffer.Length > 0)
        {
            var leftOver = sentenceBuffer.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(leftOver))
            {
                await foreach (var audioBytes in GetAudioStreamAsync(leftOver, cancellationToken, voice))
                {
                    yield return audioBytes;
                }
            }
        }
    }
}

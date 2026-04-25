using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Orka.Infrastructure.SemanticKernel.Audio;

/// <summary>
/// Manages active classroom sessions for BOTH voice and text modes.
/// Provides CancellationToken management, barge-in interruption,
/// and pending message storage for race-condition-safe context reconstruction.
/// </summary>
public class ClassroomSessionManager
{
    private readonly ILogger<ClassroomSessionManager> _logger;
    private readonly ConcurrentDictionary<Guid, ActiveClassroomSession> _activeSessions = new();

    public ClassroomSessionManager(ILogger<ClassroomSessionManager> logger)
    {
        _logger = logger;
    }

    // ─── SES (VOICE) MODU ───────────────────────────────────────────────────

    /// <summary>Ses modu için aktif oturum başlatır ve iptal token'ı döndürür.</summary>
    public CancellationToken StartSession(Guid sessionId)
    {
        var cts = new CancellationTokenSource();
        var session = new ActiveClassroomSession { CancellationTokenSource = cts, Mode = SessionMode.Voice };
        
        _activeSessions.AddOrUpdate(sessionId, session, (key, old) => {
            old.CancellationTokenSource.Cancel();
            return session;
        });

        _logger.LogInformation("[ClassroomSessionManager] Voice session started: {SessionId}", sessionId);
        return cts.Token;
    }

    /// <summary>Ses modunda kesinti (barge-in) yapar. elapsedMs TTS konumunu izler.</summary>
    public bool InterruptSession(Guid sessionId, int elapsedAudioMs)
    {
        if (_activeSessions.TryGetValue(sessionId, out var session))
        {
            if (!session.CancellationTokenSource.IsCancellationRequested)
            {
                session.CancellationTokenSource.Cancel();
                session.InterruptedElapsedMs = elapsedAudioMs;
                _logger.LogWarning("[ClassroomSessionManager] Voice INTERRUPTED at {Elapsed}ms: {SessionId}", elapsedAudioMs, sessionId);
                return true;
            }
        }
        return false;
    }

    // ─── METİN (TEXT) MODU ──────────────────────────────────────────────────

    /// <summary>
    /// Metin SSE akışı için oturum başlatır. Hem ses hem metin modlarını destekler.
    /// Race condition koruması: Aynı sessionId için yeni çağrı, eskisini iptal eder.
    /// </summary>
    public CancellationToken StartTextSession(Guid sessionId)
    {
        var cts = new CancellationTokenSource();
        var session = new ActiveClassroomSession { CancellationTokenSource = cts, Mode = SessionMode.Text };

        _activeSessions.AddOrUpdate(sessionId, session, (key, old) => {
            // Önceki akış zaten çalışıyorsa anında iptal et (Race Condition koruması)
            if (!old.CancellationTokenSource.IsCancellationRequested)
                old.CancellationTokenSource.Cancel();
            return session;
        });

        _logger.LogInformation("[ClassroomSessionManager] Text session started: {SessionId}", sessionId);
        return cts.Token;
    }

    /// <summary>
    /// Metin modunda kullanıcı araya girer (Barge-in).
    /// CancellationToken iptal edilir ve kullanıcı mesajı saklanır.
    /// Saklanan mesaj bir sonraki stream'de Context'e enjekte edilir.
    /// </summary>
    public bool InterruptTextSession(Guid sessionId, string userMessage)
    {
        if (_activeSessions.TryGetValue(sessionId, out var session))
        {
            if (!session.CancellationTokenSource.IsCancellationRequested)
            {
                session.CancellationTokenSource.Cancel();
                session.PendingBargeInMessage = userMessage;
                _logger.LogWarning(
                    "[ClassroomSessionManager] Text INTERRUPTED. Message='{Msg}' SessionId={SessionId}",
                    userMessage.Length > 50 ? userMessage[..50] + "..." : userMessage,
                    sessionId);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Bekleyen Barge-in mesajını döndürür ve sıfırlar (tek seferlik okuma).
    /// Mesaj varsa: bir önceki stream kesildi ve bu mesaj bağlama eklenmeli.
    /// </summary>
    public string? PopPendingBargeInMessage(Guid sessionId)
    {
        if (_activeSessions.TryGetValue(sessionId, out var session) &&
            !string.IsNullOrEmpty(session.PendingBargeInMessage))
        {
            var msg = session.PendingBargeInMessage;
            session.PendingBargeInMessage = null;
            return msg;
        }
        return null;
    }

    // ─── ORTAK ──────────────────────────────────────────────────────────────

    /// <summary>Oturumu temizler. Memory leak önlemi.</summary>
    public void EndSession(Guid sessionId)
    {
        if (_activeSessions.TryRemove(sessionId, out var session))
        {
            if (!session.CancellationTokenSource.IsCancellationRequested)
                session.CancellationTokenSource.Cancel();
        }
    }

    /// <summary>Ses modundaki kesinti konumunu (ms) döndürür ve sıfırlar.</summary>
    public int? PopInterruptedState(Guid sessionId)
    {
        if (_activeSessions.TryGetValue(sessionId, out var session) && session.InterruptedElapsedMs > 0)
        {
            int ms = session.InterruptedElapsedMs;
            session.InterruptedElapsedMs = 0;
            return ms;
        }
        return null;
    }

    /// <summary>Verilen session için aktif CancellationToken'ı döndürür (yoksa default).</summary>
    public CancellationToken GetActiveToken(Guid sessionId)
    {
        return _activeSessions.TryGetValue(sessionId, out var session)
            ? session.CancellationTokenSource.Token
            : default;
    }
}

public enum SessionMode { Voice, Text }

public class ActiveClassroomSession
{
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();
    public SessionMode Mode { get; set; } = SessionMode.Text;

    // Ses modu: TTS zaman damgası (ms)
    public int InterruptedElapsedMs { get; set; }

    // Metin modu: Barge-in anında gelen kullanıcı mesajı (Context Reconstruction için)
    public string? PendingBargeInMessage { get; set; }
}

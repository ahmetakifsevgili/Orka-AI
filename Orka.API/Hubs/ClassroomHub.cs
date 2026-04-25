using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Orka.Infrastructure.SemanticKernel.Audio;
using Orka.Core.Interfaces;

namespace Orka.API.Hubs;

/// <summary>
/// Handles real-time Voice Classroom communication and Walkie-Talkie interruptions.
/// </summary>
public class ClassroomHub : Hub
{
    private readonly ClassroomSessionManager _sessionManager;
    private readonly ILogger<ClassroomHub> _logger;

    public ClassroomHub(ClassroomSessionManager sessionManager, ILogger<ClassroomHub> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Katılımcının belirli bir sesli sınıf oturumuna (SessionId) abone olmasını sağlar.
    /// Orchestrator arka planda bu Group ID'ye doğrudan ses yollayacaktır.
    /// </summary>
    public async Task JoinSession(Guid sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId.ToString());
        // Aynı zamanda Context.Items'a atalım ki disconnect olunca yakalayabilelim
        Context.Items["ActiveSessionId"] = sessionId;
        _logger.LogInformation("SignalR: Bağlantı {ConnectionId}, Session {SessionId} grubuna katıldı.", Context.ConnectionId, sessionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Kullanıcının interneti kesilirse veya sayfayı kapatırsa, arka planda çalışan (Token/Para yakan) LLM yayınını ACIMASIZCA sonlandırıyoruz.
        if (Context.Items.TryGetValue("ActiveSessionId", out var sessionIdObj) && sessionIdObj is Guid sessionId)
        {
            _logger.LogWarning("SignalR bağlantısı koptu. Zombie Session {SessionId} imha ediliyor.", sessionId);
            _sessionManager.EndSession(sessionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Invoked by the frontend when the user presses the SPACE bar or the Mic button.
    /// Cancels the LLM generation and Edge TTS streams instantly.
    /// </summary>
    public async Task InterruptSession(Guid sessionId, int elapsedAudioMs)
    {
        _logger.LogInformation("SignalR: Front-end triggered Walkie-Talkie Interrupt for Session {Session} at {Elapsed}ms", sessionId, elapsedAudioMs);
        
        bool success = _sessionManager.InterruptSession(sessionId, elapsedAudioMs);
        if (success)
        {
            // Acknowledge the interruption to the frontend
            await Clients.Caller.SendAsync("OnClassroomInterrupted", sessionId, elapsedAudioMs);
        }
    }

    /// <summary>
    /// TEST VE ENTEGRASYON AMAÇLI: LLM Cümlelerini Frontend'e SignalR üzerinden anlık Byte Array (Base64) olarak pompalar.
    /// Frontend bu veriyi ReceiveAudioChunk dinleyicisi ile karşılar.
    /// </summary>
    public async Task StreamLessonAudio(Guid sessionId, string text)
    {
        Context.Items["ActiveSessionId"] = sessionId;
        var ct = _sessionManager.StartSession(sessionId);
        
        var httpContext = Context.GetHttpContext();
        if (httpContext == null)
        {
            _logger.LogWarning("StreamLessonAudio: HttpContext is null, cannot resolve ITtsStreamService.");
            return;
        }

        var ttsService = httpContext.RequestServices.GetService(typeof(ITtsStreamService)) as ITtsStreamService;
        if (ttsService == null)
        {
            _logger.LogError("StreamLessonAudio: ITtsStreamService could not be resolved.");
            return;
        }

        try
        {
            await foreach (var chunk in ttsService.GetAudioStreamAsync(text, ct))
            {
                // SignalR üzerinden Frontend'in beklediği ReceiveAudioChunk sinyali fırlatılır
                 await Clients.Caller.SendAsync("ReceiveAudioChunk", new { Base64Audio = Convert.ToBase64String(chunk), Speaker = "Hoca" }, cancellationToken: ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("StreamLessonAudio aniden kesildi (Kullanıcı Space'e bastı). Session: {SessionId}", sessionId);
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Orka.Core.Events;
using Orka.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Orka.Infrastructure.Handlers;

public class TopicCompletedHandler : INotificationHandler<TopicCompletedEvent>
{
    private readonly ISummarizerAgent _summarizerAgent;
    private readonly IQuizAgent _quizAgent;
    private readonly ILogger<TopicCompletedHandler> _logger;

    public TopicCompletedHandler(
        ISummarizerAgent summarizerAgent,
        IQuizAgent quizAgent,
        ILogger<TopicCompletedHandler> logger)
    {
        _summarizerAgent = summarizerAgent;
        _quizAgent = quizAgent;
        _logger = logger;
    }

    public async Task Handle(TopicCompletedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 [EVENT] TopicCompletedEvent alındı. İşçiler başlatılıyor: SessionId={SessionId}", notification.SessionId);

        try
        {
            // 1. Özetle ve Wiki'ye kaydet
            await _summarizerAgent.SummarizeAndSaveWikiAsync(notification.SessionId, notification.TopicId, notification.UserId);
            _logger.LogInformation("✅ [SUMMARIZER] Özet ve Wiki tamamlandı.");

            // 2. Quiz üret
            await _quizAgent.GeneratePendingQuizAsync(notification.SessionId, notification.TopicId, notification.UserId);
            _logger.LogInformation("✅ [QUIZ] Pekiştirme soruları hazırlandı.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [EVENT ERROR] Arka plan iş akışı başarısız oldu.");
        }
    }
}

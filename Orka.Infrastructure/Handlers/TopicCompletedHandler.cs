using MediatR;
using Microsoft.Extensions.Logging;
using Orka.Core.Events;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

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
        _logger.LogInformation("[EVENT] TopicCompletedEvent alindi. SessionRef={SessionRef}",
            LogPrivacyGuard.SafeId(notification.SessionId, "session"));

        try
        {
            await _summarizerAgent.SummarizeAndSaveWikiAsync(notification.SessionId, notification.TopicId, notification.UserId);
            _logger.LogInformation("[SUMMARIZER] Ozet ve Wiki tamamlandi.");

            await _quizAgent.GeneratePendingQuizAsync(notification.SessionId, notification.TopicId, notification.UserId);
            _logger.LogInformation("[QUIZ] Pekistirme sorulari hazirlandi.");
        }
        catch (Exception ex)
        {
            _logger.LogError("[EVENT ERROR] Arka plan is akisi basarisiz oldu. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
        }
    }
}

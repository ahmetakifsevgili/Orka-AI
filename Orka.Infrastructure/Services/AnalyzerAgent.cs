using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Sohbet Tamamlanma Analizcisi.
///
/// Faz 7 Yükseltmesi:
///   Artık kendi LLM çağrısını yapmıyor.
///   IntentClassifierAgent'dan aldığı {intent, confidence} ile karar veriyor.
///   Bu sayede AYNI bağlam için iki LLM çağrısı yapmak yerine bir çağrı ikisine hizmet ediyor.
///
/// Karar Mantığı:
///   - intent == "UNDERSTOOD" && confidence >= 0.65 → IsComplete = true
///   - intent == "QUIZ_REQUEST" → TopicCompletedEvent tetikleme (IsComplete = true)
///   - Diğer tüm durumlar → IsComplete = false
/// </summary>
public class AnalyzerAgent : IAnalyzerAgent
{
    private const double ConfidenceThreshold = 0.65;

    private readonly IIntentClassifierAgent _intentClassifier;
    private readonly ILogger<AnalyzerAgent> _logger;

    public AnalyzerAgent(
        IIntentClassifierAgent intentClassifier,
        ILogger<AnalyzerAgent> logger)
    {
        _intentClassifier = intentClassifier;
        _logger           = logger;
    }

    public async Task<AnalyzerResult> AnalyzeCompletionAsync(IEnumerable<Message> messages)
    {
        // IntentClassifier tek LLM çağrısıyla hem bize hem SupervisorAgent'a veri üretir
        var intentResult = await _intentClassifier.ClassifyAsync(messages);

        bool isComplete = intentResult.Intent is "UNDERSTOOD" or "QUIZ_REQUEST"
                          && intentResult.Confidence >= ConfidenceThreshold;

        _logger.LogInformation(
            "[AnalyzerAgent] Intent={Intent} | Confidence={Conf:P0} | IsComplete={Complete} | Sebep={Reason}",
            intentResult.Intent, intentResult.Confidence, isComplete, intentResult.Reasoning);

        return new AnalyzerResult(isComplete, intentResult.Reasoning, intentResult);
    }
}

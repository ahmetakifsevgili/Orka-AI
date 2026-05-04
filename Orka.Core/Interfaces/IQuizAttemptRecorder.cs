using System;
using System.Threading;
using System.Threading.Tasks;
using Orka.Core.DTOs;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

/// <summary>
/// Quiz cevap kaydının tek doğru yolu.
/// Hem UI tarafından (QuizController.RecordAttempt) hem de SK Hub plugin'leri
/// (QuizMasterPlugin.EvaluateAnswer) tarafından çağrılır — böylece skill/mistake/review/xp
/// pipeline'ları ayrışmaz, observability simetrik kalır.
/// </summary>
public interface IQuizAttemptRecorder
{
    /// <summary>
    /// QuizAttempt entity'sini kaydeder ve bağlı tüm pedagoji pipeline'ını çalıştırır:
    /// LearningSignal · MistakeTaxonomy · XpRules (QuizCorrect) · ReviewScheduler · question hash dedupe.
    /// </summary>
    Task<QuizAttemptRecordResult> RecordAsync(Guid userId, RecordQuizAttemptRequest request, CancellationToken ct = default);
}

public sealed record QuizAttemptRecordResult(
    QuizAttempt Attempt,
    XpAwardResult? Xp,
    ReviewItemDto? Review,
    MistakeTaxonomyResult? Mistake);

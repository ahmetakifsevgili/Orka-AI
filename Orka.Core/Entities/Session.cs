using Orka.Core.Enums;
using MediatR;

namespace Orka.Core.Entities;

public class Session
{
    public Guid Id { get; set; }
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public int SessionNumber { get; set; }
    public string? Summary { get; set; }
    public int TotalTokensUsed { get; set; }
    public decimal TotalCostUSD { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public SessionState CurrentState { get; set; } = SessionState.Learning;
    public string? PendingQuiz { get; set; }

    // ── Çoklu Baseline Quiz (Seviye Tespiti) ─────────────────────────────
    /// <summary>Tüm baseline soruların JSON dizisi. 5 soru barındırır.</summary>
    public string? BaselineQuizData { get; set; }
    /// <summary>Kullanıcının şu anda kaçıncı soruda olduğu (0-tabanlı).</summary>
    public int BaselineQuizIndex { get; set; }
    /// <summary>Şu ana kadar doğru cevaplanan soru sayısı.</summary>
    public int BaselineCorrectCount { get; set; }

    /// <summary>Aynı konuda kaç kez telafi dersi önerildi. 2+ olunca önkoşul yönlendirmesi yapılır.</summary>
    public int RemedialAttemptCount { get; set; } = 0;

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}

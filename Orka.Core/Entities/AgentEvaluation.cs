using System;

namespace Orka.Core.Entities;

public class AgentEvaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    // İlişkiler
    public Guid SessionId { get; set; }
    public Session Session { get; set; } = null!;
    
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!; // Bu değerlendirmenin yapıldığı Asistan Mesajı

    // Metrikler
    public string AgentRole { get; set; } = string.Empty; // "Tutor", "DeepPlan"
    public string UserInput { get; set; } = string.Empty; // Kullanıcının o anki sorusu
    public string AgentResponse { get; set; } = string.Empty; // Ajanın verdiği cevap
    
    // Değerlendirme
    public int EvaluationScore { get; set; } // 1 ile 10 arası
    public string EvaluatorFeedback { get; set; } = string.Empty; // Puanın gerekçesi
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

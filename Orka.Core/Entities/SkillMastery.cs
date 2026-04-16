using System;

namespace Orka.Core.Entities;

public class SkillMastery
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid TopicId { get; set; }
    public Topic Topic { get; set; } = null!;
    /// <summary>Alt konunun başlığı (snapshot — silinse bile kayıt korunur).</summary>
    public string SubTopicTitle { get; set; } = string.Empty;
    public DateTime MasteredAt { get; set; }
    /// <summary>Kazanımı sağlayan quiz sorusunun puanı (0–100).</summary>
    public int QuizScore { get; set; }
}

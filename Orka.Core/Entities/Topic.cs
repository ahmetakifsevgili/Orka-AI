using System;
using System.Collections.Generic;
using Orka.Core.Enums;

namespace Orka.Core.Entities;

public class Topic
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public string? Category { get; set; }
    
    // YENİ: Durum ve Hafıza Alanları
    public TopicPhase CurrentPhase { get; set; } = TopicPhase.Discovery;
    public string? PhaseMetadata { get; set; } // JSON: sorular, skorlar, aktif hedefler
    public string? LanguageLevel { get; set; }   // Beginner, Intermediate, Advanced
    public string? LastStudySnapshot { get; set; } // En son kalınan yerin özeti
    
    public int TotalSections { get; set; }
    public int CompletedSections { get; set; }
    
    // YENİ: Başarı ve İlerleme Takibi (Mastery Mode)
    public int SuccessScore { get; set; }         // 0-100 arası başarı puanı
    public double ProgressPercentage { get; set; } // %0-100 arası ilerleme
    public bool IsMastered { get; set; }          // Konu tam öğrenildi mi?
    
    public DateTime LastAccessedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsArchived { get; set; }

    // Hiyerarşik plan desteği (Deep Plan)
    /// <summary>Alt konular arasındaki sıralama. 0-tabanlı, CreatedAt yerine deterministik sıra sağlar.</summary>
    public int Order { get; set; } = 0;
    public Guid? ParentTopicId { get; set; }
    public Topic? Parent { get; set; }
    public ICollection<Topic> SubTopics { get; set; } = new List<Topic>();

    public ICollection<Session> Sessions { get; set; } = new List<Session>();
    public ICollection<WikiPage> WikiPages { get; set; } = new List<WikiPage>();
}

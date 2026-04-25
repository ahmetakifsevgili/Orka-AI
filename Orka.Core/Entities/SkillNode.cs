using System;
using System.Collections.Generic;

namespace Orka.Core.Entities;

/// <summary>
/// FAZ 4: Dinamik Yetenek Ağacı (Gamified Skill Tree)
///
/// Topic entity'sinden bağımsız, kullanıcı bazlı gamified öğrenme haritası.
/// EvaluatorAgent quiz sonuçlarına göre otonom olarak yeni düğümler üretir.
///
/// Düğüm Türleri:
/// - Core: Ana müfredat konusu
/// - RemedialPractice: EvaluatorAgent'ın tespit ettiği eksiklik için oluşturulan pratik
/// - Milestone: Büyük başarı işareti (Örn: "Cebir Tamamlandı")
/// </summary>
public class SkillNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string NodeType { get; set; } = "Core"; // Core | RemedialPractice | Milestone

    public bool IsUnlocked { get; set; } = false;
    public int DifficultyLevel { get; set; } = 1; // 1=Beginner, 2=Intermediate, 3=Advanced

    /// <summary>
    /// LLM tarafından üretilen meta veri JSON'ı.
    /// İçerik: LearningGoal, WeaknessContext, ProposedExercises vb.
    /// </summary>
    public string RuleMetadataJson { get; set; } = "{}";

    /// <summary>
    /// Hangi TopicId ile ilişkili (opsiyonel, null olabilir - serbest düğümler için)
    /// </summary>
    public Guid? RelatedTopicId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UnlockedAt { get; set; }

    // Closure Table navigasyonu
    public ICollection<SkillTreeClosure> AncestorLinks { get; set; } = new List<SkillTreeClosure>();
    public ICollection<SkillTreeClosure> DescendantLinks { get; set; } = new List<SkillTreeClosure>();
}

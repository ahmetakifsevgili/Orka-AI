using System;

namespace Orka.Core.Entities;

/// <summary>
/// FAZ 4: Closure Table — Yönlü Döngüsüz Çizge (DAG) için Ata-Soy İlişkisi Tablosu
///
/// NEDEN Closure Table?
/// - Topic entity'sindeki ParentTopicId: Tek ebeveyn → Ağaç (Tree)
/// - SkillTreeClosure: Çok ebeveyn → DAG (Directed Acyclic Graph)
///
/// Örnek:
///   "Oyun Geliştirme" hem "Matematik" hem "Programlama"ya bağımlı olabilir.
///   Bu çoklu ebeveyn ilişkisi HierarchyId ile modellenemez, Closure Table gerekir.
///
/// Closure Table Avantajı:
///   Tüm ata-soy (ancestor-descendant) ilişkilerini ve derinliklerini tutar.
///   Herhangi bir düğümün tüm atalarını/torunlarını O(1) sorguyla bulur.
///
/// Self-Reference: Her düğüm kendisi için (Depth=0) bir kayıt içerir.
/// Direct Child: Depth=1
/// Grandchild: Depth=2
/// </summary>
public class SkillTreeClosure
{
    public Guid AncestorId { get; set; }
    public SkillNode Ancestor { get; set; } = null!;

    public Guid DescendantId { get; set; }
    public SkillNode Descendant { get; set; } = null!;

    /// <summary>
    /// Ata ile Soy arasındaki mesafe.
    /// 0 = Self-reference (düğümün kendisi)
    /// 1 = Direkt çocuk
    /// 2 = Torun (grandchild)
    /// </summary>
    public int Depth { get; set; }
}

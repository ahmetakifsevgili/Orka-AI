using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

/// <summary>
/// Skill Tree servis arayüzü.
/// EvaluatorAgent, SkillTree API ve xyflow UI bu arayüz üzerinden iletişim kurar.
/// </summary>
public interface ISkillTreeService
{
    /// <summary>
    /// Yeni bir düğüm ekler. DFS Döngü Tespiti yapar.
    /// Döngü tespit edilirse CycleDetectedException fırlatır.
    /// Başarılı eklemede Closure Table'ı günceller (O(n) karmaşıklık).
    /// </summary>
    Task<SkillNode> AddNodeAsync(
        Guid userId,
        AddSkillNodeRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Kullanıcının tüm skill tree düğümlerini döndürür.
    /// xyflow Node listesi için kullanılır.
    /// </summary>
    Task<IEnumerable<SkillNode>> GetAllNodesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Kullanıcının skill tree kenarlarını (edges) döndürür.
    /// xyflow Edge listesi için kullanılır.
    /// </summary>
    Task<IEnumerable<SkillEdge>> GetAllEdgesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Bir düğümün tüm torunlarını (descendants) döndürür.
    /// Closure Table sayesinde O(1) sorgu.
    /// </summary>
    Task<IEnumerable<SkillNode>> GetDescendantsAsync(Guid ancestorNodeId, CancellationToken ct = default);

    /// <summary>
    /// Düğümü kilit açık yap (öğrenci başarıyla tamamladı).
    /// </summary>
    Task<bool> UnlockNodeAsync(Guid nodeId, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// EvaluatorAgent'ın ürettiği yeni düğüm önerisi için DTO.
/// </summary>
public record AddSkillNodeRequest(
    string Title,
    List<Guid> ParentNodeIds, // Çoklu ebeveyn (DAG)
    string NodeType,          // Core | RemedialPractice | Milestone
    int DifficultyLevel,      // 1=Beginner, 2=Intermediate, 3=Advanced
    string LearningGoal,
    Guid? RelatedTopicId = null
);

/// <summary>
/// xyflow Edge DTO'su. Frontend'in anladığı source→target formatı.
/// </summary>
public record SkillEdge(Guid Source, Guid Target);

/// <summary>
/// DFS döngü tespitinde döngü bulunduğunda fırlatılır.
/// EvaluatorAgent bunu yakaladığında LLM'e retry prompt'u gönderir.
/// </summary>
public class CycleDetectedException : Exception
{
    public Guid CyclicNodeId { get; }

    public CycleDetectedException(Guid cyclicNodeId)
        : base($"DAG döngüsü tespit edildi: {cyclicNodeId}. Farklı bir ebeveyn seçin.")
    {
        CyclicNodeId = cyclicNodeId;
    }
}

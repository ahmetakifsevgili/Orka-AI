using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

/// <summary>
/// FAZ 4: Yetenek Ağacı Servisi — DAG + Closure Table implementasyonu.
///
/// Temel Özellikler:
/// 1. DFS Döngü Tespiti: Veritabanına yazmadan önce In-Memory Tarjan/DFS kontrolü
/// 2. Closure Table Yazımı: Yeni düğümün tüm ata ilişkileri O(n) ile hesaplanır
/// 3. xyflow Formatı: GetAllEdgesAsync Depth=1 kenarlarını source→target formatında döndürür
/// </summary>
public class SkillTreeService : ISkillTreeService
{
    private readonly OrkaDbContext _db;
    private readonly ILogger<SkillTreeService> _logger;

    public SkillTreeService(OrkaDbContext db, ILogger<SkillTreeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SkillNode> AddNodeAsync(
        Guid userId,
        AddSkillNodeRequest request,
        CancellationToken ct = default)
    {
        // ── 1. Ebeveyn düğümlerin varlığını doğrula ────────────────────────────
        if (request.ParentNodeIds.Count == 0)
            throw new ArgumentException("En az bir ebeveyn düğüm gereklidir.");

        var existingParents = await _db.SkillNodes
            .Where(n => request.ParentNodeIds.Contains(n.Id) && n.UserId == userId)
            .Select(n => n.Id)
            .ToListAsync(ct);

        var missingParents = request.ParentNodeIds.Except(existingParents).ToList();
        if (missingParents.Count > 0)
            throw new ArgumentException($"Ebeveyn düğümler bulunamadı: {string.Join(", ", missingParents)}");

        // ── 2. DFS Döngü Tespiti (Veritabanına YAZMADAN ÖNCE) ─────────────────
        var newNodeId = Guid.NewGuid();
        await DetectCycleAsync(userId, newNodeId, request.ParentNodeIds, ct);

        // ── 3. Yeni SkillNode oluştur ──────────────────────────────────────────
        var newNode = new SkillNode
        {
            Id = newNodeId,
            UserId = userId,
            Title = request.Title,
            NodeType = request.NodeType,
            DifficultyLevel = request.DifficultyLevel,
            RuleMetadataJson = $"{{\"learningGoal\":\"{request.LearningGoal}\"}}",
            RelatedTopicId = request.RelatedTopicId,
            IsUnlocked = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.SkillNodes.Add(newNode);

        // ── 4. Closure Table Güncellemesi (O(n)) ──────────────────────────────
        // Self-reference: Her düğüm kendisi için Depth=0 kaydı içerir
        _db.SkillTreeClosures.Add(new SkillTreeClosure
        {
            AncestorId = newNodeId,
            DescendantId = newNodeId,
            Depth = 0
        });

        // Her ebeveyn için: Ebeveynin tüm atalarını bul, yeni düğüm için kopyala
        foreach (var parentId in request.ParentNodeIds)
        {
            // Direkt ebeveyn bağlantısı (Depth=1)
            _db.SkillTreeClosures.Add(new SkillTreeClosure
            {
                AncestorId = parentId,
                DescendantId = newNodeId,
                Depth = 1
            });

            // Ebeveynin tüm atalarını kopyala (Depth+1)
            var ancestorRows = await _db.SkillTreeClosures
                .Where(c => c.DescendantId == parentId && c.Depth > 0)
                .ToListAsync(ct);

            foreach (var ancestorRow in ancestorRows)
            {
                // Aynı (AncestorId, DescendantId) çifti zaten varsa atla (çoklu ebeveyn birleşimi)
                var alreadyExists = await _db.SkillTreeClosures.AnyAsync(
                    c => c.AncestorId == ancestorRow.AncestorId && c.DescendantId == newNodeId, ct);

                if (!alreadyExists)
                {
                    _db.SkillTreeClosures.Add(new SkillTreeClosure
                    {
                        AncestorId = ancestorRow.AncestorId,
                        DescendantId = newNodeId,
                        Depth = ancestorRow.Depth + 1
                    });
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[SkillTreeService] Yeni düğüm eklendi. NodeId={NodeId} Title={Title} Parents={Parents}",
            newNodeId, request.Title, string.Join(", ", request.ParentNodeIds));

        return newNode;
    }

    public async Task<IEnumerable<SkillNode>> GetAllNodesAsync(Guid userId, CancellationToken ct = default)
    {
        return await _db.SkillNodes
            .Where(n => n.UserId == userId)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<SkillEdge>> GetAllEdgesAsync(Guid userId, CancellationToken ct = default)
    {
        // Sadece Depth=1 (direkt ebeveyn-çocuk) kenarlarını döndür
        var edges = await _db.SkillTreeClosures
            .Where(c => c.Depth == 1 && c.Ancestor.UserId == userId)
            .Select(c => new SkillEdge(c.AncestorId, c.DescendantId))
            .ToListAsync(ct);

        return edges;
    }

    public async Task<IEnumerable<SkillNode>> GetDescendantsAsync(
        Guid ancestorNodeId, CancellationToken ct = default)
    {
        // Closure Table sayesinde O(1) — tüm torunlar tek sorguda
        return await _db.SkillTreeClosures
            .Where(c => c.AncestorId == ancestorNodeId && c.Depth > 0)
            .Include(c => c.Descendant)
            .Select(c => c.Descendant)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<bool> UnlockNodeAsync(Guid nodeId, Guid userId, CancellationToken ct = default)
    {
        var node = await _db.SkillNodes
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.UserId == userId, ct);

        if (node == null) return false;

        node.IsUnlocked = true;
        node.UnlockedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[SkillTreeService] Düğüm açıldı. NodeId={NodeId} Title={Title}", nodeId, node.Title);
        return true;
    }

    // ─── DFS Döngü Tespiti ─────────────────────────────────────────────────────

    /// <summary>
    /// In-Memory DFS ile döngü tespiti.
    /// Yeni düğüm eklenmeden önce çalışır → Veritabanı bütünlüğü korunur.
    ///
    /// Algoritma: Yeni düğümün herhangi bir ebeveynine ulaşmak için mevcut grafı DFS ile dolaşır.
    /// Eğer herhangi bir ebeveyn, zaten yeni düğümün torunuysa → Döngü var.
    /// </summary>
    private async Task DetectCycleAsync(
        Guid userId,
        Guid newNodeId,
        List<Guid> parentNodeIds,
        CancellationToken ct)
    {
        // Tüm mevcut closure kayıtlarını belleğe al (kullanıcı bazlı)
        var allClosures = await _db.SkillTreeClosures
            .Where(c => c.Ancestor.UserId == userId)
            .Select(c => new { c.AncestorId, c.DescendantId })
            .ToListAsync(ct);

        // Adjacency list oluştur
        var adjList = new Dictionary<Guid, List<Guid>>();
        foreach (var closure in allClosures.Where(c => c.AncestorId != c.DescendantId))
        {
            if (!adjList.ContainsKey(closure.AncestorId))
                adjList[closure.AncestorId] = new List<Guid>();
            adjList[closure.AncestorId].Add(closure.DescendantId);
        }

        // Yeni düğümü geçici olarak grafa ekle
        adjList[newNodeId] = new List<Guid>(); // Henüz çocuğu yok
        foreach (var parentId in parentNodeIds)
        {
            if (!adjList.ContainsKey(parentId))
                adjList[parentId] = new List<Guid>();
            adjList[parentId].Add(newNodeId);
        }

        // DFS ile döngü tespiti (tüm düğümler için)
        var visited = new HashSet<Guid>();
        var inStack = new HashSet<Guid>();

        foreach (var node in adjList.Keys)
        {
            if (!visited.Contains(node))
            {
                var cycleNode = DfsDetectCycle(node, adjList, visited, inStack);
                if (cycleNode.HasValue)
                {
                    _logger.LogError(
                        "[SkillTreeService] DÖNGÜ TESPİT EDİLDİ! NewNodeId={NewId} CyclicNode={CycleId} Parents={Parents}",
                        newNodeId, cycleNode.Value, string.Join(", ", parentNodeIds));
                    throw new CycleDetectedException(cycleNode.Value);
                }
            }
        }
    }

    private static Guid? DfsDetectCycle(
        Guid node,
        Dictionary<Guid, List<Guid>> adjList,
        HashSet<Guid> visited,
        HashSet<Guid> inStack)
    {
        visited.Add(node);
        inStack.Add(node);

        if (adjList.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    var result = DfsDetectCycle(neighbor, adjList, visited, inStack);
                    if (result.HasValue) return result;
                }
                else if (inStack.Contains(neighbor))
                {
                    return neighbor; // Döngü bulundu
                }
            }
        }

        inStack.Remove(node);
        return null;
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface ITopicService
{
    Task<IEnumerable<Topic>> GetUserTopicsAsync(Guid userId);
    Task<(Topic Topic, Session Session)> CreateDiscoveryTopicAsync(Guid userId, string title, string? routeCategory = null);
    Task<List<WikiPage>> GenerateAndApplyPlanAsync(Guid topicId, string intent = "genel öğrenme", string level = "Beginner");
    Task<(Topic Topic, Session Session, List<WikiPage> WikiPages)> CreateTopicWithPlanAsync(Guid userId, string title, string intent = "genel öğrenme", string level = "orta");
    Task<Topic?> GetTopicByIdAsync(Guid topicId, Guid userId);
    Task UpdateTopicAsync(Guid topicId, Guid userId, string? title, string? emoji, bool? isArchived);
    Task DeleteTopicAsync(Guid topicId, Guid userId);
    Task UpdateLastAccessedAsync(Guid topicId);
    Task<List<Topic>> GetSubTopicsAsync(Guid parentTopicId);

    /// <summary>
    /// Hiyerarşi-farkında ders listesi. Parent → Modül → Ders yapısında leaf-level'a iner,
    /// modül sırasına göre ders sıralaması üretir. Düz (tek seviye) plan varsa direct children döner.
    /// Ders geçişi / quiz index / wiki yönlendirme için TEK kaynak.
    /// </summary>
    Task<List<Topic>> GetOrderedLessonsAsync(Guid rootTopicId, Guid userId);
}

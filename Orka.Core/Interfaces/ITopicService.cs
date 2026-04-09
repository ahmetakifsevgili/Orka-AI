using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface ITopicService
{
    Task<IEnumerable<Topic>> GetUserTopicsAsync(Guid userId);
    Task<(Topic Topic, Session Session)> CreateDiscoveryTopicAsync(Guid userId, string title);
    Task<List<WikiPage>> GenerateAndApplyPlanAsync(Guid topicId, string intent = "genel öğrenme", string level = "Beginner");
    Task<(Topic Topic, Session Session, List<WikiPage> WikiPages)> CreateTopicWithPlanAsync(Guid userId, string title, string intent = "genel öğrenme", string level = "orta");
    Task<Topic?> GetTopicByIdAsync(Guid topicId, Guid userId);
    Task UpdateTopicAsync(Guid topicId, Guid userId, string? title, string? emoji, bool? isArchived);
    Task DeleteTopicAsync(Guid topicId, Guid userId);
    Task UpdateLastAccessedAsync(Guid topicId);
}

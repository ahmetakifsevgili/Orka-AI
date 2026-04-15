using System.ComponentModel;
using Microsoft.SemanticKernel;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public class TopicPlugin
{
    private readonly ITopicService _topicService;

    public TopicPlugin(ITopicService topicService)
    {
        _topicService = topicService;
    }

    [KernelFunction, Description("Kullanıcının aktif olan tüm konularını (müfredatını) getirir.")]
    public async Task<string> GetUserTopics([Description("Kullanıcı ID'si (Guid)")] Guid userId)
    {
        var topics = await _topicService.GetUserTopicsAsync(userId);
        return string.Join("\n", topics.Select(t => $"- {t.Title} (ID: {t.Id}, Phase: {t.CurrentPhase})"));
    }

    [KernelFunction, Description("Belirli bir konunun (Topic) detaylarını ve alt başlıklarını getirir.")]
    public async Task<string> GetTopicDetails(
        [Description("Konu ID'si (Guid)")] Guid topicId,
        [Description("Kullanıcı ID'si (Guid)")] Guid userId)
    {
        var topic = await _topicService.GetTopicByIdAsync(topicId, userId);
        if (topic == null) return "Konu bulunamadı.";

        var subtopics = string.Join(", ", topic.SubTopics.Select(s => s.Title));
        return $"Başlık: {topic.Title}\nKategori: {topic.Category}\nFaz: {topic.CurrentPhase}\nAlt Başlıklar: {subtopics}";
    }
}

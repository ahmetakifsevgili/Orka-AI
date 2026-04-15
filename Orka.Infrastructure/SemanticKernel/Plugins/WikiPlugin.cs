using System.ComponentModel;
using Microsoft.SemanticKernel;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public class WikiPlugin
{
    private readonly IWikiService _wikiService;

    public WikiPlugin(IWikiService wikiService)
    {
        _wikiService = wikiService;
    }

    [KernelFunction, Description("Konu hakkındaki mevcut wiki içeriğinin tamamını getirir. RAG (Retrieval) için kullanılır.")]
    public async Task<string> GetWikiContext(
        [Description("Konu ID'si (Guid)")] Guid topicId,
        [Description("Kullanıcı ID'si (Guid)")] Guid userId)
    {
        return await _wikiService.GetWikiFullContentAsync(topicId, userId);
    }

    [KernelFunction, Description("Belirli bir wiki sayfasının detaylarını getirir.")]
    public async Task<string> GetWikiPageDetails(
        [Description("Sayfa ID'si (Guid)")] Guid pageId,
        [Description("Kullanıcı ID'si (Guid)")] Guid userId)
    {
        var page = await _wikiService.GetWikiPageAsync(pageId, userId);
        if (page == null) return "Sayfa bulunamadı.";
        
        return $"Başlık: {page.Title}\nDurum: {page.Status}\nİçerik: {page.Content}";
    }
}

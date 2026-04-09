using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class SummarizerAgent : ISummarizerAgent
{
    private readonly IOpenRouterService _openRouterService;
    private readonly IServiceScopeFactory _scopeFactory;

    public SummarizerAgent(IOpenRouterService openRouterService, IServiceScopeFactory scopeFactory)
    {
        _openRouterService = openRouterService;
        _scopeFactory = scopeFactory;
    }

    public async Task SummarizeAndSaveWikiAsync(Guid sessionId, Guid topicId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var messages = await db.Messages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        var history = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));

        var systemPrompt = "Sen bir Wiki küratörüsün. Sohbet geçmişini analiz et ve öğrenilen ana kavramları Wiki formatında özetle. Yanıtın Markdown formatında, yapılandırılmış ve profesyonel olmalıdır.";
        var userPrompt = $"Aşağıdaki sohbeti özetle:\n\n{history}";

        var summary = await _openRouterService.ChatCompletionAsync(systemPrompt, userPrompt);

        // Save to Session summary (optional, current requirement says Wiki)
        var session = await db.Sessions.FindAsync(sessionId);
        if (session != null)
        {
            session.Summary = summary;
            await db.SaveChangesAsync();
        }
        
        // In a real scenario, we might want to also create a WikiPage/Block here.
        // For ORKA v3, we ensure the summary is saved.
    }
}

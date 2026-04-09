using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class QuizAgent : IQuizAgent
{
    private readonly IOpenRouterService _openRouterService;
    private readonly IServiceScopeFactory _scopeFactory;

    public QuizAgent(IOpenRouterService openRouterService, IServiceScopeFactory scopeFactory)
    {
        _openRouterService = openRouterService;
        _scopeFactory = scopeFactory;
    }

    public async Task GeneratePendingQuizAsync(Guid sessionId, Guid topicId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var session = await db.Sessions.Include(s => s.Messages).FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null) return;

        var context = !string.IsNullOrWhiteSpace(session.Summary)
            ? session.Summary
            : string.Join("\n", session.Messages.TakeLast(5).Select(m => $"{m.Role}: {m.Content}"));

        var systemPrompt = "Sen bir sınav hazırlayıcısısın. Verilen özete veya metne göre 3-4 adet düşündürücü pekiştirme sorusu hazırla. Sorular kısa ve öz olmalı.";
        var userPrompt = $"Bu içerik için sorular hazırla:\n\n{context}";

        var questions = await _openRouterService.ChatCompletionAsync(systemPrompt, userPrompt, "qwen/qwen-2.5-72b-instruct:free");

        session.PendingQuiz = questions;
        session.CurrentState = Orka.Core.Enums.SessionState.QuizPending;

        await db.SaveChangesAsync();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace Orka.Infrastructure.Services;

public class QuizAgent : IQuizAgent
{
    private readonly IGroqService _groq;
    private readonly IServiceScopeFactory _scopeFactory;

    public QuizAgent(IGroqService groq, IServiceScopeFactory scopeFactory)
    {
        _groq = groq;
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

        try
        {
            var prompt = $@"Aşağıdaki ders içeriğine dayanarak 3-5 adet pekiştirme sorusu hazırla.
Sorular kısa ve net cevaplı olmalı. Sadece soruları maddeler halinde dön.

Ders İçeriği:
{context}";

            var questions = await _groq.GenerateResponseAsync(
                "Sen bir eğitim küratörüsün. Sadece pekiştirme soruları hazırlarsın.",
                prompt);

            session.PendingQuiz = questions;
            session.CurrentState = Orka.Core.Enums.SessionState.QuizPending;

            await db.SaveChangesAsync();
        }
        catch
        {
            // Fail safe, do not crash main process
        }
    }
}

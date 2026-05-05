using System.ComponentModel;
using Microsoft.SemanticKernel;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class ReviewQueryPlugin
{
    private readonly IReviewSrsService _review;

    public ReviewQueryPlugin(IReviewSrsService review)
    {
        _review = review;
    }

    [KernelFunction, Description("List due SRS review items for a user, optionally scoped to a topic.")]
    public async Task<string> GetDueReviews(Guid userId, Guid? topicId = null)
    {
        var due = await _review.GetDueAsync(userId, topicId);
        if (due.Count == 0) return "Şu anda due review item yok.";

        return string.Join("\n", due.Take(10).Select(r =>
            $"- {r.Id}: {r.SkillTitle} concept={r.ConceptTag ?? "-"} due={r.DueAt:O}"));
    }
}

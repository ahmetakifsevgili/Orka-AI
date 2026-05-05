using System.ComponentModel;
using Microsoft.SemanticKernel;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class FlashcardPlugin
{
    private readonly IFlashcardService _flashcards;

    public FlashcardPlugin(IFlashcardService flashcards)
    {
        _flashcards = flashcards;
    }

    [KernelFunction, Description("List active flashcards for a user, optionally scoped to a topic.")]
    public async Task<string> ListFlashcards(Guid userId, Guid? topicId = null)
    {
        var cards = await _flashcards.ListAsync(userId, topicId);
        if (cards.Count == 0) return "Aktif flashcard yok.";

        return string.Join("\n", cards.Take(10).Select(c =>
            $"- {c.Id}: {c.Front} (concept={c.ConceptTag ?? "-"})"));
    }
}

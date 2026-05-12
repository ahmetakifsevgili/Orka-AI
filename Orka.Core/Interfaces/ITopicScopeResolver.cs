namespace Orka.Core.Interfaces;

public sealed record TopicScope(
    Guid CurrentTopicId,
    Guid RootTopicId,
    IReadOnlyList<Guid> AncestorTopicIds,
    IReadOnlyList<Guid> DescendantTopicIds,
    IReadOnlyList<Guid> TreeTopicIds,
    Guid? ActiveLessonTopicId,
    bool IsValid)
{
    public bool HasDescendants => DescendantTopicIds.Count > 0;

    public static TopicScope Empty(Guid currentTopicId) => new(
        currentTopicId,
        Guid.Empty,
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        null,
        false);
}

public interface ITopicScopeResolver
{
    Task<TopicScope> ResolveAsync(Guid userId, Guid currentTopicId, CancellationToken ct = default);
}

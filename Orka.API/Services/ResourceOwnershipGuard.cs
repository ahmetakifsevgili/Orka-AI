using Microsoft.EntityFrameworkCore;
using Orka.Infrastructure.Data;

namespace Orka.API.Services;

public sealed class ResourceOwnershipGuard
{
    private readonly OrkaDbContext _db;

    public ResourceOwnershipGuard(OrkaDbContext db)
    {
        _db = db;
    }

    public Task<bool> TopicBelongsToUserAsync(Guid userId, Guid topicId, CancellationToken ct = default) =>
        _db.Topics.AsNoTracking().AnyAsync(t => t.Id == topicId && t.UserId == userId, ct);

    public Task<bool> SessionBelongsToUserAsync(Guid userId, Guid sessionId, CancellationToken ct = default) =>
        _db.Sessions.AsNoTracking().AnyAsync(s => s.Id == sessionId && s.UserId == userId, ct);

    public Task<bool> SourceBelongsToUserAsync(Guid userId, Guid sourceId, CancellationToken ct = default) =>
        _db.LearningSources.AsNoTracking().AnyAsync(s => s.Id == sourceId && s.UserId == userId && !s.IsDeleted, ct);

    public async Task<bool> OptionalTopicBelongsToUserAsync(Guid userId, Guid? topicId, CancellationToken ct = default) =>
        !topicId.HasValue || await TopicBelongsToUserAsync(userId, topicId.Value, ct);

    public async Task<bool> OptionalSessionBelongsToUserAsync(Guid userId, Guid? sessionId, CancellationToken ct = default) =>
        !sessionId.HasValue || await SessionBelongsToUserAsync(userId, sessionId.Value, ct);
}

using Microsoft.EntityFrameworkCore;
using Orka.Infrastructure.Data;

namespace Orka.API.Services;

public sealed class EfCorePendingMigrationsReader : IPendingEfMigrationsReader
{
    private readonly OrkaDbContext _db;

    public EfCorePendingMigrationsReader(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default)
    {
        if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            return Array.Empty<string>();
        }
        return (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
    }
}

namespace Orka.API.Services;

public interface IPendingEfMigrationsReader
{
    Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default);
}

using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface IIntegrationTokenRepository
{
    Task<IReadOnlyList<IntegrationToken>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountActiveForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default);
    Task<IntegrationToken?> GetActiveByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IntegrationToken?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task AddAsync(IntegrationToken token, CancellationToken ct = default);
    Task TouchLastUsedAsync(Guid id, DateTimeOffset now, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

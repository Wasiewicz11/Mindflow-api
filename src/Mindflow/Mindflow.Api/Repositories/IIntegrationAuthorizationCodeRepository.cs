using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface IIntegrationAuthorizationCodeRepository
{
    Task AddAsync(IntegrationAuthorizationCode code, CancellationToken ct = default);
    Task<IntegrationAuthorizationCode?> ConsumeAsync(string codeHash, DateTimeOffset now, CancellationToken ct = default);
    Task DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct = default);
}

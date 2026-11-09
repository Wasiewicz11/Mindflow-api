using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface IUserAiSettingsRepository
{
    Task<UserAiSettings?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task SaveAsync(UserAiSettings settings, CancellationToken ct = default);
}

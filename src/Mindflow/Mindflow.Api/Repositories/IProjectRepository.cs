using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface IProjectRepository
{
    Task<IEnumerable<Project>> GetAllInSpaceAsync(Guid spaceId);
    Task<IReadOnlyList<Project>> GetAccessibleForUserAsync(Guid userId, CancellationToken ct = default);
    Task<(IReadOnlyList<Project> Items, int Total)> GetAccessibleForUserPagedAsync(
        Guid userId,
        int limit,
        int offset,
        CancellationToken ct = default);
    Task<Project?> GetByIdAsync(Guid id);
    Task<Project> CreateInSpaceAsync(Project project);
    Task<Project?> UpdateInSpaceAsync(Guid id, Guid spaceId, string? name, string? color);
    Task<bool> DeleteInSpaceAsync(Guid id, Guid spaceId);
}

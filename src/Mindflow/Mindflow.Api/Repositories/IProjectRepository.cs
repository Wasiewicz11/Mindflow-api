using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface IProjectRepository
{
    Task<IEnumerable<Project>> GetAllInSpaceAsync(Guid spaceId);
    Task<Project?> GetByIdAsync(Guid id);
    Task<Project> CreateInSpaceAsync(Project project);
    Task<Project?> UpdateInSpaceAsync(Guid id, Guid spaceId, string? name, string? color);
    Task<bool> DeleteInSpaceAsync(Guid id, Guid spaceId);
}

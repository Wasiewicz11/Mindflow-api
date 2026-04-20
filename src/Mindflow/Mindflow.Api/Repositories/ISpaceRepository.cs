using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface ISpaceRepository
{
    Task<IEnumerable<Space>> GetAllForUserAsync(Guid userId);
    Task<Space> CreateAsync(Space space);
    Task<Space?> UpdateAsync(Guid id, Guid userId, string? name, string? color);
    Task<bool> DeleteAsync(Guid id, Guid userId);
    Task<bool> CanUserAccessAsync(Guid id, Guid userId);
}

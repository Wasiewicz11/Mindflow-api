using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface IProjectService
{
    Task<IEnumerable<Project>> GetAllInSpaceForCurrentUserAsync(Guid spaceId);
    Task<Project> CreateInSpaceForCurrentUserAsync(Guid spaceId, CreateProjectRequest request);
    Task<Project> UpdateInSpaceForCurrentUserAsync(Guid id, Guid spaceId, UpdateProjectRequest request);
    Task DeleteInSpaceForCurrentUserAsync(Guid id, Guid spaceId);
}

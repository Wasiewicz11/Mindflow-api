using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services;

public class AccessService(
    IProjectRepository projectRepository, 
    ISpaceRepository spaceRepository) : IAccessService
{
    public async Task<bool> CanAccessProjectAsync(Guid projectId, Guid userId)
    {
        var project = await projectRepository.GetByIdAsync(projectId);
        
        if (project is null) return false;
        if (project.UserId == userId) return true;
        if (project.SpaceId is not null)
            return await spaceRepository.CanUserAccessAsync(project.SpaceId.Value, userId);

        return false;
    }
}

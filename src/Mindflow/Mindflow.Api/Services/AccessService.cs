using Mindflow.Api.Repositories;
using Mindflow.Api.Services.Integrations;

namespace Mindflow.Api.Services;

public class AccessService(
    IProjectRepository projectRepository,
    ISpaceRepository spaceRepository,
    ITaskRepository taskRepository,
    IIntegrationSpaceScope spaceScope) : IAccessService
{
    public bool CanUseInbox => spaceScope.SpaceId is null;

    public async Task<bool> CanAccessSpaceAsync(Guid spaceId, Guid userId)
    {
        if (spaceScope.SpaceId is Guid scopedSpaceId && scopedSpaceId != spaceId) return false;

        return await spaceRepository.CanUserAccessAsync(spaceId, userId);
    }

    public async Task<bool> CanAccessProjectAsync(Guid projectId, Guid userId)
    {
        var project = await projectRepository.GetByIdAsync(projectId);

        if (project is null) return false;
        if (spaceScope.SpaceId is Guid scopedSpaceId)
            return project.SpaceId == scopedSpaceId && await spaceRepository.CanUserAccessAsync(scopedSpaceId, userId);
        if (project.UserId == userId) return true;
        if (project.SpaceId is not null)
            return await spaceRepository.CanUserAccessAsync(project.SpaceId.Value, userId);

        return false;
    }

    public async Task<bool> CanAccessTaskAsync(Guid taskId, Guid userId)
    {
        var task = await taskRepository.GetByIdAsync(taskId);
        if (task is null) return false;
        if (spaceScope.SpaceId is not null)
            return task.ProjectId is Guid scopedProjectId && await CanAccessProjectAsync(scopedProjectId, userId);
        if (task.UserId == userId) return true;

        if (task.ProjectId is not null)
            return await CanAccessProjectAsync(task.ProjectId.Value, userId);

        return false;
    }
}

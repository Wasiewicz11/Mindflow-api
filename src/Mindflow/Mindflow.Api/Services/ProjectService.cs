using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services;

public class ProjectService(
    IProjectRepository projectRepository,
    ISpaceRepository spaceRepository,
    ICurrentUserService currentUserService,
    ITaskActivityService taskActivityService) : IProjectService
{
    public async Task<IEnumerable<Project>> GetAllInSpaceForCurrentUserAsync(Guid spaceId)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        await EnsureUserCanAccessSpaceAsync(spaceId, userId);

        return await projectRepository.GetAllInSpaceAsync(spaceId);
    }

    public async Task<Project> CreateInSpaceForCurrentUserAsync(Guid spaceId, CreateProjectRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        await EnsureUserCanAccessSpaceAsync(spaceId, userId);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name,
            Color = request.Color ?? "#9CA3AF",
            SpaceId = spaceId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var created = await projectRepository.CreateInSpaceAsync(project);
        await taskActivityService.RecordProjectEventAsync(TaskActivityEventType.ProjectCreated, userId, created);
        return created;
    }

    public async Task<Project> UpdateInSpaceForCurrentUserAsync(Guid id, Guid spaceId, UpdateProjectRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        await EnsureUserCanAccessSpaceAsync(spaceId, userId);

        var project = await projectRepository.UpdateInSpaceAsync(id, spaceId, request.Name, request.Color)
                      ?? throw new NotFoundException("Project not found.");
        await taskActivityService.RecordProjectEventAsync(TaskActivityEventType.ProjectUpdated, userId, project);
        return project;
    }

    public async Task DeleteInSpaceForCurrentUserAsync(Guid id, Guid spaceId)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        await EnsureUserCanAccessSpaceAsync(spaceId, userId);

        var project = await projectRepository.GetByIdAsync(id);
        var deleted = await projectRepository.DeleteInSpaceAsync(id, spaceId);
        if (!deleted || project is null) throw new NotFoundException("Project not found.");

        await taskActivityService.RecordProjectEventAsync(TaskActivityEventType.ProjectDeleted, userId, project);
    }

    private async Task EnsureUserCanAccessSpaceAsync(Guid spaceId, Guid userId)
    {
        var hasAccess = await spaceRepository.CanUserAccessAsync(spaceId, userId);
        if (!hasAccess)
            throw new ForbiddenException("You do not have access to this space.");
    }
}

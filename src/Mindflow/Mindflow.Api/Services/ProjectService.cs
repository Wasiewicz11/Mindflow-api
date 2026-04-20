using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services;

public class ProjectService(
    IProjectRepository projectRepository,
    ISpaceRepository spaceRepository,
    ICurrentUserService currentUserService) : IProjectService
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

        return await projectRepository.CreateInSpaceAsync(project);
    }

    public async Task<Project> UpdateInSpaceForCurrentUserAsync(Guid id, Guid spaceId, UpdateProjectRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        await EnsureUserCanAccessSpaceAsync(spaceId, userId);

        var project = await projectRepository.UpdateInSpaceAsync(id, spaceId, request.Name, request.Color);
        return project 
               ?? throw new NotFoundException("Project not found.");
    }

    public async Task DeleteInSpaceForCurrentUserAsync(Guid id, Guid spaceId)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        await EnsureUserCanAccessSpaceAsync(spaceId, userId);

        var deleted = await projectRepository.DeleteInSpaceAsync(id, spaceId);
        if (!deleted) throw new NotFoundException("Project not found.");
    }

    private async Task EnsureUserCanAccessSpaceAsync(Guid spaceId, Guid userId)
    {
        var hasAccess = await spaceRepository.CanUserAccessAsync(spaceId, userId);
        if (!hasAccess)
            throw new ForbiddenException("You do not have access to this space.");
    }
}

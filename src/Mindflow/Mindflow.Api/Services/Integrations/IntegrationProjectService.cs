using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services.Integrations;

public class IntegrationProjectService(
    IProjectRepository projectRepository,
    ICurrentUserService currentUserService,
    IAccessService accessService,
    IProjectService projectService,
    ITaskActivityService taskActivityService,
    IIntegrationSpaceScope spaceScope) : IIntegrationProjectService
{
    public async Task<IntegrationProjectPageResponse> GetAccessibleProjectsAsync(
        IntegrationPageQuery query,
        CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var (projects, total) = await projectRepository.GetAccessibleForUserPagedAsync(
            userId,
            query.Limit,
            query.Offset,
            spaceScope.SpaceId,
            ct);

        var items = projects.Select(ToResponse).ToList();

        return new IntegrationProjectPageResponse(items, total, query.Limit, query.Offset);
    }

    public async Task<IntegrationProjectResponse?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        if (!await accessService.CanAccessProjectAsync(projectId, userId)) return null;

        var project = await projectRepository.GetByIdAsync(projectId);
        return project is null ? null : ToResponse(project);
    }

    public async Task<IntegrationProjectResult> CreateProjectAsync(
        IntegrationCreateProjectRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return new IntegrationProjectResult(IntegrationProjectOutcome.NameRequired);

        var spaceId = request.SpaceId ?? spaceScope.SpaceId;
        if (spaceId is not Guid targetSpaceId) return new IntegrationProjectResult(IntegrationProjectOutcome.SpaceRequired);

        var userId = await currentUserService.GetCurrentUserIdAsync();
        if (!await accessService.CanAccessSpaceAsync(targetSpaceId, userId))
            return new IntegrationProjectResult(IntegrationProjectOutcome.NotFound);

        var created = await projectService.CreateInSpaceForCurrentUserAsync(
            targetSpaceId,
            new CreateProjectRequest(request.Name.Trim(), request.Color));

        return new IntegrationProjectResult(IntegrationProjectOutcome.Succeeded, ToResponse(created));
    }

    public async Task<IntegrationProjectResult> UpdateProjectAsync(
        Guid projectId,
        IntegrationUpdateProjectRequest request,
        CancellationToken ct = default)
    {
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
            return new IntegrationProjectResult(IntegrationProjectOutcome.NameRequired);

        var userId = await currentUserService.GetCurrentUserIdAsync();
        if (!await accessService.CanAccessProjectAsync(projectId, userId))
            return new IntegrationProjectResult(IntegrationProjectOutcome.NotFound);

        var updated = await projectRepository.UpdateAsync(projectId, request.Name?.Trim(), request.Color);
        if (updated is null) return new IntegrationProjectResult(IntegrationProjectOutcome.NotFound);

        await taskActivityService.RecordProjectEventAsync(TaskActivityEventType.ProjectUpdated, userId, updated);

        return new IntegrationProjectResult(IntegrationProjectOutcome.Succeeded, ToResponse(updated));
    }

    private static IntegrationProjectResponse ToResponse(Project project) =>
        new(project.Id, project.Name, project.Color, project.SpaceId, project.CreatedAt);
}

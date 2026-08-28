using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services.Integrations;

public class IntegrationProjectService(
    IProjectRepository projectRepository,
    ICurrentUserService currentUserService) : IIntegrationProjectService
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
            ct);

        var items = projects
            .Select(project => new IntegrationProjectResponse(
                project.Id,
                project.Name,
                project.Color,
                project.SpaceId,
                project.CreatedAt))
            .ToList();

        return new IntegrationProjectPageResponse(items, total, query.Limit, query.Offset);
    }
}

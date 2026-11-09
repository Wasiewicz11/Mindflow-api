using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Integrations;

public interface IIntegrationProjectService
{
    Task<IntegrationProjectPageResponse> GetAccessibleProjectsAsync(
        IntegrationPageQuery query,
        CancellationToken ct = default);

    Task<IntegrationProjectResponse?> GetProjectAsync(Guid projectId, CancellationToken ct = default);

    Task<IntegrationProjectResult> CreateProjectAsync(IntegrationCreateProjectRequest request, CancellationToken ct = default);

    Task<IntegrationProjectResult> UpdateProjectAsync(
        Guid projectId,
        IntegrationUpdateProjectRequest request,
        CancellationToken ct = default);
}

public enum IntegrationProjectOutcome
{
    Succeeded,
    NotFound,
    SpaceRequired,
    NameRequired
}

public record IntegrationProjectResult(IntegrationProjectOutcome Outcome, IntegrationProjectResponse? Project = null);

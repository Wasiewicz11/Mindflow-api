using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Integrations;

public interface IIntegrationProjectService
{
    Task<IntegrationProjectPageResponse> GetAccessibleProjectsAsync(
        IntegrationPageQuery query,
        CancellationToken ct = default);
}

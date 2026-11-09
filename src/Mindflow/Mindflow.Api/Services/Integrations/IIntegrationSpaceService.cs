using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Integrations;

public interface IIntegrationSpaceService
{
    Task<IReadOnlyList<IntegrationSpaceResponse>> GetSpacesAsync(CancellationToken ct = default);
    Task<IntegrationSpaceResponse?> GetSpaceAsync(Guid spaceId, CancellationToken ct = default);
}

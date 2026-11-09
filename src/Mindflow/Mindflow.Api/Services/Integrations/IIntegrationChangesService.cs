using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Integrations;

public interface IIntegrationChangesService
{
    Task<IntegrationChangesPageResponse> GetChangesAsync(
        long since,
        int limit,
        Guid? currentTokenId,
        CancellationToken ct = default);

    Task<IntegrationChangesPageResponse> GetLatestCursorAsync(CancellationToken ct = default);
}

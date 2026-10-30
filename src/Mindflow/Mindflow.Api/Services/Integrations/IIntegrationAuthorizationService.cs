using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Integrations;

public interface IIntegrationAuthorizationService
{
    IntegrationAuthorizationPreviewResponse Preview(string? clientId, string? redirectUri, string? scope);
    Task<IntegrationAuthorizationResponse> AuthorizeAsync(IntegrationAuthorizationRequest request, CancellationToken ct = default);
}

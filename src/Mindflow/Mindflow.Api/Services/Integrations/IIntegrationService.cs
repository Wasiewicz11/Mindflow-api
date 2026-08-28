using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Integrations;

public interface IIntegrationService
{
    Task<IntegrationSettingsResponse> GetSettingsAsync(CancellationToken ct = default);
    Task<IntegrationSettingsResponse> UpdateSettingsAsync(UpdateIntegrationSettingsRequest request, CancellationToken ct = default);
    Task<CreateIntegrationTokenResponse> CreateTokenAsync(CreateIntegrationTokenRequest request, CancellationToken ct = default);
    Task<bool> RevokeTokenAsync(Guid id, CancellationToken ct = default);
    Task<IntegrationTokenValidationResult?> ValidateTokenAsync(string token, CancellationToken ct = default);
}

public record IntegrationTokenValidationResult(
    Guid UserId,
    Guid TokenId,
    IReadOnlyCollection<IntegrationTokenScope> Scopes);

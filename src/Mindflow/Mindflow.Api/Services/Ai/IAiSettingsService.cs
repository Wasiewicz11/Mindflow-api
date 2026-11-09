using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Ai;

public interface IAiSettingsService
{
    Task<AiSettingsResponse> GetAsync(CancellationToken ct = default);
    Task<AiSettingsResponse> SaveAsync(UpdateAiSettingsRequest request, CancellationToken ct = default);
    Task<AiSettingsResponse> DeleteKeyAsync(CancellationToken ct = default);
    Task<AiConnectionTestResponse> TestAsync(TestAiConnectionRequest request, CancellationToken ct = default);
}

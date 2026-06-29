using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface IBrainGraphService
{
    Task<BrainGraphDto?> GetDefaultAsync();
    Task<BrainGraphDto> UpsertDefaultAsync(BrainGraphDto request);
}

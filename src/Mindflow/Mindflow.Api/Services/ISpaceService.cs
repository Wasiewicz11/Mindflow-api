using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface ISpaceService
{
    Task<IEnumerable<Space>> GetAllForCurrentUserAsync();
    Task<Space> CreateForCurrentUserAsync(CreateSpaceRequest request);
    Task<Space?> UpdateForCurrentUserAsync(Guid id, UpdateSpaceRequest request);
    Task<bool> DeleteForCurrentUserAsync(Guid id);
}

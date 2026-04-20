using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services;

public class SpaceService(
    ISpaceRepository spaceRepository,
    ICurrentUserService currentUserService) : ISpaceService
{
    public async Task<IEnumerable<Space>> GetAllForCurrentUserAsync()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return await spaceRepository.GetAllForUserAsync(userId);
    }

    public async Task<Space> CreateForCurrentUserAsync(CreateSpaceRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();

        var space = new Space
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name,
            Color = request.Color ?? "#9CA3AF",
            CreatedAt = DateTimeOffset.UtcNow
        };

        return await spaceRepository.CreateAsync(space);
    }

    public async Task<Space?> UpdateForCurrentUserAsync(Guid id, UpdateSpaceRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return await spaceRepository.UpdateAsync(id, userId, request.Name, request.Color);
    }

    public async Task<bool> DeleteForCurrentUserAsync(Guid id)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return await spaceRepository.DeleteAsync(id, userId);
    }
}

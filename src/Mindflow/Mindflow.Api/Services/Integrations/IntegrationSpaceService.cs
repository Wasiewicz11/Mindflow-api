using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Integrations;

public class IntegrationSpaceService(
    MindflowDbContext db,
    ICurrentUserService currentUserService,
    IIntegrationSpaceScope spaceScope) : IIntegrationSpaceService
{
    public async Task<IReadOnlyList<IntegrationSpaceResponse>> GetSpacesAsync(CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return await QueryAsync(userId, spaceScope.SpaceId, ct);
    }

    public async Task<IntegrationSpaceResponse?> GetSpaceAsync(Guid spaceId, CancellationToken ct = default)
    {
        if (spaceScope.SpaceId is Guid scopedSpaceId && scopedSpaceId != spaceId) return null;

        var userId = await currentUserService.GetCurrentUserIdAsync();
        return (await QueryAsync(userId, spaceId, ct)).SingleOrDefault();
    }

    private async Task<IReadOnlyList<IntegrationSpaceResponse>> QueryAsync(Guid userId, Guid? spaceId, CancellationToken ct)
    {
        var query = db.Spaces.AsNoTracking();
        if (spaceId is Guid onlySpaceId) query = query.Where(space => space.Id == onlySpaceId);

        return await query
            .Select(space => new
            {
                Space = space,
                MemberRole = db.SpaceMembers
                    .Where(member => member.SpaceId == space.Id && member.UserId == userId)
                    .Select(member => (SpaceRole?)member.Role)
                    .FirstOrDefault()
            })
            .Where(row => row.Space.UserId == userId || row.MemberRole != null)
            .OrderBy(row => row.Space.CreatedAt)
            .Select(row => new IntegrationSpaceResponse(
                row.Space.Id,
                row.Space.Name,
                row.Space.Color,
                row.Space.UserId == userId ? SpaceRole.Owner : row.MemberRole!.Value,
                row.Space.CreatedAt))
            .ToListAsync(ct);
    }
}

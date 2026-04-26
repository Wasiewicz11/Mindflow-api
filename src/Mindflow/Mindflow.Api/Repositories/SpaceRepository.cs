using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class SpaceRepository(MindflowDbContext db) : ISpaceRepository
{
    public async Task<IEnumerable<Space>> GetAllForUserAsync(Guid userId)
    {
        var memberSpaceIds = await db.SpaceMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.SpaceId)
            .ToListAsync();

        return await db.Spaces
            .Where(s => s.UserId == userId || memberSpaceIds.Contains(s.Id))
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<Space> CreateAsync(Space space)
    {
        db.Spaces.Add(space);
        await db.SaveChangesAsync();
        return space;
    }

    public async Task<Space?> UpdateAsync(Guid id, Guid userId, string? name, string? color)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (space is null) return null;

        if (name is not null) space.Name = name;
        if (color is not null) space.Color = color;

        await db.SaveChangesAsync();
        return space;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (space is null) return false;

        var projects = await db.Projects
            .Where(p => p.SpaceId == id)
            .ToListAsync();

        db.Projects.RemoveRange(projects);
        db.Spaces.Remove(space);
        await db.SaveChangesAsync();
        return true;
    }

    public Task<bool> CanUserAccessAsync(Guid id, Guid userId)
    {
        return db.Spaces.AnyAsync(s => s.Id == id && (
            s.UserId == userId
            || db.SpaceMembers.Any(m => m.SpaceId == id && m.UserId == userId)));
    }

    public async Task<IEnumerable<Guid>> GetUserIdsWithAccessAsync(Guid id)
    {
        var ownerId = await db.Spaces
            .Where(s => s.Id == id)
            .Select(s => (Guid?)s.UserId)
            .FirstOrDefaultAsync();

        if (!ownerId.HasValue) return [];

        var memberIds = await db.SpaceMembers
            .Where(m => m.SpaceId == id)
            .Select(m => m.UserId)
            .ToListAsync();

        return memberIds.Append(ownerId.Value).Distinct();
    }
}

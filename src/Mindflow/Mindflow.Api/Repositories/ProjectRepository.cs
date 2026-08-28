using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class ProjectRepository(MindflowDbContext db) : IProjectRepository
{
    public async Task<Project?> GetByIdAsync(Guid id)
    {
        return await db.Projects.FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<IEnumerable<Project>> GetAllInSpaceAsync(Guid spaceId)
    {
        return await db.Projects
            .Where(p => p.SpaceId == spaceId)
            .OrderBy(p => p.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Project>> GetAccessibleForUserAsync(Guid userId, CancellationToken ct = default)
    {
        return await db.Projects
            .Where(project => project.UserId == userId
                || (project.SpaceId.HasValue && db.SpaceMembers.Any(member =>
                    member.SpaceId == project.SpaceId.Value && member.UserId == userId)))
            .OrderBy(project => project.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<Project> Items, int Total)> GetAccessibleForUserPagedAsync(
        Guid userId,
        int limit,
        int offset,
        CancellationToken ct = default)
    {
        var query = db.Projects
            .AsNoTracking()
            .Where(project => project.UserId == userId
                || (project.SpaceId.HasValue && db.SpaceMembers.Any(member =>
                    member.SpaceId == project.SpaceId.Value && member.UserId == userId)));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(project => project.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<Project> CreateInSpaceAsync(Project project)
    {
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    public async Task<Project?> UpdateInSpaceAsync(
        Guid id,
        Guid spaceId,
        string? name,
        string? color)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.SpaceId == spaceId);
        if (project is null) return null;

        if (name is not null) project.Name = name;
        if (color is not null) project.Color = color;

        await db.SaveChangesAsync();
        return project;
    }

    public async Task<bool> DeleteInSpaceAsync(Guid id, Guid spaceId)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.SpaceId == spaceId);
        if (project is null) return false;

        db.Projects.Remove(project);
        await db.SaveChangesAsync();
        return true;
    }
}

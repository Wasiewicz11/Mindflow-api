using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class ProjectRepository(MindflowDbContext db) : IProjectRepository
{
    public async Task<IEnumerable<Project>> GetAllInSpaceAsync(Guid spaceId)
    {
        return await db.Projects
            .Where(p => p.SpaceId == spaceId)
            .OrderBy(p => p.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
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

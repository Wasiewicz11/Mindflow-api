using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class TaskRepository(MindflowDbContext db) : ITaskRepository
{
    public async Task<IEnumerable<TaskItem>> GetAllForUserAsync(Guid userId)
    {
        var accessibleProjectIds = await GetAccessibleProjectIdsAsync(userId);

        return await db.Tasks
            .Where(t => t.UserId == userId
                        || (t.ProjectId != null && accessibleProjectIds.Contains(t.ProjectId.Value)))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<TaskItem?> GetByIdForUserAsync(Guid id, Guid userId)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return null;

        return await CanAccessTaskAsync(task, userId) ? task : null;
    }

    public async Task<TaskItem?> CreateForUserAsync(TaskItem task, Guid userId)
    {
        if (task.ProjectId.HasValue && !await CanAccessProjectAsync(task.ProjectId.Value, userId))
        {
            return null;
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    public async Task<TaskItem?> UpdateForUserAsync(TaskItem task, Guid userId)
    {
        if (!await CanAccessTaskAsync(task, userId)) return null;
        
        if (task.ProjectId.HasValue && !await CanAccessProjectAsync(task.ProjectId.Value, userId))
            return null;

        await db.SaveChangesAsync();
        return task;
    }

    public async Task<bool> DeleteForUserAsync(Guid id, Guid userId)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return false;
        if (!await CanAccessTaskAsync(task, userId)) return false;

        db.Tasks.Remove(task);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<Guid>> GetAccessibleSpaceIdsAsync(Guid userId)
    {
        return await db.Spaces
            .Where(s => s.UserId == userId
                        || db.SpaceMembers.Any(m => m.SpaceId == s.Id && m.UserId == userId))
            .Select(s => s.Id)
            .ToListAsync();
    }

    public async Task<Guid?> GetSpaceIdForTaskAsync(TaskItem task)
    {
        if (!task.ProjectId.HasValue) return null;

        return await db.Projects
            .Where(p => p.Id == task.ProjectId.Value)
            .Select(p => p.SpaceId)
            .FirstOrDefaultAsync();
    }

    private async Task<bool> CanAccessTaskAsync(TaskItem task, Guid userId)
    {
        if (task.UserId == userId) return true;

        if (task.ProjectId.HasValue)
        {
            return await CanAccessProjectAsync(task.ProjectId.Value, userId);
        }

        return false;
    }

    private Task<bool> CanAccessProjectAsync(Guid projectId, Guid userId)
    {
        return db.Projects.AnyAsync(p => p.Id == projectId && (
            p.UserId == userId
            || (p.SpaceId != null && db.Spaces.Any(s => s.Id == p.SpaceId && (
                s.UserId == userId
                || db.SpaceMembers.Any(m => m.SpaceId == s.Id && m.UserId == userId))))));
    }

    private async Task<List<Guid>> GetAccessibleProjectIdsAsync(Guid userId)
    {
        var accessibleSpaceIds = await GetAccessibleSpaceIdsAsync(userId);

        return await db.Projects
            .Where(p => p.UserId == userId
                        || (p.SpaceId != null && accessibleSpaceIds.Contains(p.SpaceId.Value)))
            .Select(p => p.Id)
            .ToListAsync();
    }
}

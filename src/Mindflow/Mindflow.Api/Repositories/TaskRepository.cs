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
            .Include(t => t.Subtasks)
            .Where(t => t.UserId == userId
                        || (t.ProjectId != null && accessibleProjectIds.Contains(t.ProjectId.Value)))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<TaskItem?> GetByIdAsync(Guid id)
    {
        return await db.Tasks
            .Include(t => t.Subtasks)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<TaskItem?> CreateAsync(TaskItem task)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    public async Task<TaskItem?> UpdateAsync(TaskItem task)
    {
        try
        {
            await db.SaveChangesAsync();
            return task;
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.All(e =>
            e.Entity is TaskSubtask && (e.State == EntityState.Deleted || e.State == EntityState.Modified)))
        {
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            await db.SaveChangesAsync();
            return task;
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null) return false;

        db.Tasks.Remove(task);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<TaskItem>> GetAllForProjectAsync(Guid projectId)
    {
        return await db.Tasks
            .Include(t => t.Subtasks)
            .Where(t => t.ProjectId == projectId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
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

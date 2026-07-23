using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class TaskTimeEntryRepository(MindflowDbContext db) : ITaskTimeEntryRepository
{
    public async Task<IReadOnlyList<TaskTimeEntry>> GetForUserInRangeAsync(Guid userId, DateOnly from, DateOnly to)
    {
        return await db.TaskTimeEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.WorkDate >= from && entry.WorkDate <= to)
            .OrderBy(entry => entry.WorkDate)
            .ThenBy(entry => entry.StartAt ?? DateTimeOffset.MaxValue)
            .ThenBy(entry => entry.CreatedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TaskTimeEntry>> GetForUserTaskAsync(Guid userId, Guid taskId)
    {
        return await db.TaskTimeEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.TaskId == taskId)
            .OrderByDescending(entry => entry.WorkDate)
            .ThenBy(entry => entry.StartAt ?? DateTimeOffset.MaxValue)
            .ThenBy(entry => entry.CreatedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetDurationMinutesByTaskIdsAsync(Guid userId, IReadOnlyCollection<Guid> taskIds)
    {
        if (taskIds.Count == 0) return new Dictionary<Guid, int>();

        return await db.TaskTimeEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.TaskId.HasValue && taskIds.Contains(entry.TaskId.Value))
            .GroupBy(entry => entry.TaskId!.Value)
            .Select(group => new { TaskId = group.Key, DurationMinutes = group.Sum(entry => entry.DurationMinutes) })
            .ToDictionaryAsync(item => item.TaskId, item => item.DurationMinutes);
    }

    public async Task<int> GetDurationMinutesForTaskAsync(Guid userId, Guid taskId)
    {
        return await db.TaskTimeEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.TaskId == taskId)
            .SumAsync(entry => entry.DurationMinutes);
    }

    public async Task<TaskTimeEntry?> GetByIdAsync(Guid id)
    {
        return await db.TaskTimeEntries.FirstOrDefaultAsync(entry => entry.Id == id);
    }

    public async Task<TaskTimeEntry> CreateAsync(TaskTimeEntry entry)
    {
        db.TaskTimeEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    public async Task<TaskTimeEntry> UpdateAsync(TaskTimeEntry entry)
    {
        db.TaskTimeEntries.Update(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    public async Task<bool> DeleteAsync(TaskTimeEntry entry)
    {
        db.TaskTimeEntries.Remove(entry);
        await db.SaveChangesAsync();
        return true;
    }
}

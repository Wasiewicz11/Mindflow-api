using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class TaskSubtaskRepository(MindflowDbContext db) : ITaskSubtaskRepository
{
    public async Task<int> GetNextSortOrderAsync(Guid taskId)
    {
        var maxOrder = await db.TaskSubtasks
            .Where(s => s.TaskItemId == taskId)
            .Select(s => (int?)s.SortOrder)
            .MaxAsync();

        return maxOrder.HasValue ? maxOrder.Value + 1 : 0;
    }

    public async Task<TaskSubtask> CreateAsync(Guid taskId, TaskSubtask subtask)
    {
        subtask.TaskItemId = taskId;
        db.TaskSubtasks.Add(subtask);
        await db.SaveChangesAsync();
        return subtask;
    }

    public async Task<TaskSubtask?> GetByIdForTaskAsync(Guid taskId, Guid subtaskId)
    {
        return await db.TaskSubtasks
            .FirstOrDefaultAsync(s => s.TaskItemId == taskId && s.Id == subtaskId);
    }

    public async Task<bool> UpdateAsync(TaskSubtask subtask)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(Guid taskId, Guid subtaskId)
    {
        var subtasks = await GetOrderedForTaskAsync(taskId);
        var subtask = subtasks.FirstOrDefault(s => s.Id == subtaskId);
        if (subtask is null) return false;

        db.TaskSubtasks.Remove(subtask);
        subtasks.Remove(subtask);
        NormalizeOrder(subtasks);

        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task<bool> ReorderAsync(Guid taskId, IReadOnlyCollection<Guid> subtaskIds)
    {
        var subtasks = await GetOrderedForTaskAsync(taskId);
        if (subtasks.Count == 0) return true;

        var requestedOrder = subtaskIds.ToList();
        var orderById = requestedOrder
            .Select((id, index) => new { id, index })
            .ToDictionary(item => item.id, item => item.index);
        var fallbackOrder = requestedOrder.Count;

        foreach (var subtask in subtasks)
        {
            subtask.SortOrder = orderById.TryGetValue(subtask.Id, out var order)
                ? order
                : fallbackOrder++;
        }

        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private async Task<List<TaskSubtask>> GetOrderedForTaskAsync(Guid taskId)
    {
        return await db.TaskSubtasks
            .Where(s => s.TaskItemId == taskId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync();
    }

    private static void NormalizeOrder(IEnumerable<TaskSubtask> subtasks)
    {
        var index = 0;
        foreach (var subtask in subtasks.OrderBy(s => s.SortOrder).ThenBy(s => s.CreatedAt))
        {
            subtask.SortOrder = index++;
        }
    }
}

using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface ITaskSubtaskRepository
{
    Task<int> GetNextSortOrderAsync(Guid taskId);
    Task<TaskSubtask> CreateAsync(Guid taskId, TaskSubtask subtask);
    Task<TaskSubtask?> GetByIdForTaskAsync(Guid taskId, Guid subtaskId);
    Task<bool> UpdateAsync(TaskSubtask subtask);
    Task<bool> DeleteAsync(Guid taskId, Guid subtaskId);
    Task<bool> ReorderAsync(Guid taskId, IReadOnlyCollection<Guid> subtaskIds);
}

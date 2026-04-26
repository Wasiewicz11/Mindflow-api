using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface ITaskRepository
{
    Task<IEnumerable<TaskItem>> GetAllForUserAsync(Guid userId);
    Task<TaskItem?> GetByIdForUserAsync(Guid id, Guid userId);
    Task<TaskItem?> CreateForUserAsync(TaskItem task, Guid userId);
    Task<TaskItem?> UpdateForUserAsync(TaskItem task, Guid userId);
    Task<bool> DeleteForUserAsync(Guid id, Guid userId);
    Task<IEnumerable<Guid>> GetAccessibleSpaceIdsAsync(Guid userId);
    Task<Guid?> GetSpaceIdForTaskAsync(TaskItem task);
}

using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface ITaskRepository
{
    Task<IEnumerable<TaskItem>> GetAllForUserAsync(Guid userId);
    Task<TaskItem?> GetByIdAsync(Guid id);
    Task<TaskItem?> GetByIdReadOnlyAsync(Guid id);
    Task<TaskItem?> CreateAsync(TaskItem task);
    Task<TaskItem?> UpdateAsync(TaskItem task);
    Task<bool> DeleteAsync(Guid id);
    Task<IEnumerable<TaskItem>> GetAllForProjectAsync(Guid projectId);
    Task<IEnumerable<Guid>> GetAccessibleSpaceIdsAsync(Guid userId);
    Task<Guid?> GetSpaceIdForTaskAsync(TaskItem task);
}

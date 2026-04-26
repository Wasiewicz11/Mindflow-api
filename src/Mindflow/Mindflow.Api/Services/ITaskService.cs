using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface ITaskService
{
    Task<IEnumerable<TaskItem>> GetAllForCurrentUserAsync();
    Task<TaskItem?> GetByIdForCurrentUserAsync(Guid id);
    Task<TaskItem?> CreateForCurrentUserAsync(CreateTaskRequest request);
    Task<TaskItem?> UpdateForCurrentUserAsync(Guid id, UpdateTaskRequest request);
    Task<bool> DeleteForCurrentUserAsync(Guid id);
}

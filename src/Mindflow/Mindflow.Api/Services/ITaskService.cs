using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface ITaskService
{
    Task<IEnumerable<TaskListResponse>> GetAllForCurrentUserAsync();
    Task<TaskDetailResponse?> GetByIdForCurrentUserAsync(Guid id);
    Task<TaskDetailResponse?> CreateForCurrentUserAsync(CreateTaskRequest request);
    Task<TaskDetailResponse?> UpdateForCurrentUserAsync(Guid id, UpdateTaskRequest request);
    Task<bool> DeleteForCurrentUserAsync(Guid id);
}

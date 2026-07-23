using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface ITaskService
{
    Task<IEnumerable<TaskListResponse>> GetAllAsync();
    Task<IEnumerable<TaskListResponse>> GetAllForProjectAsync(Guid projectId);
    Task<TaskDetailResponse?> GetByIdAsync(Guid id);
    Task<TaskDetailResponse?> CreateAsync(CreateTaskRequest request);
    Task<TaskDetailResponse?> UpdateAsync(Guid id, UpdateTaskRequest request);
    Task<CompleteTaskResponse?> CompleteAsync(Guid id, CompleteTaskRequest request);
    Task<bool> DeleteAsync(Guid id);
}

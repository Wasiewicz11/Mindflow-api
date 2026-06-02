using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface ITaskService
{
    Task<IEnumerable<TaskListResponse>> GetAllAsync();
    Task<IEnumerable<TaskListResponse>> GetAllForProjectAsync(Guid projectId);
    Task<TaskDetailResponse?> GetByIdAsync(Guid id);
    Task<TaskDetailResponse?> CreateAsync(CreateTaskRequest request);
    Task<TaskDetailResponse?> UpdateAsync(Guid id, UpdateTaskRequest request);
    Task<bool> DeleteAsync(Guid id);
    Task<TaskDetailResponse?> CreateSubtaskAsync(Guid taskId, TaskSubtaskRequest request);
    Task<TaskDetailResponse?> UpdateSubtaskAsync(Guid taskId, Guid subtaskId, TaskSubtaskRequest request);
    Task<TaskDetailResponse?> DeleteSubtaskAsync(Guid taskId, Guid subtaskId);
    Task<TaskDetailResponse?> ReorderSubtasksAsync(Guid taskId, ReorderTaskSubtasksRequest request);
}

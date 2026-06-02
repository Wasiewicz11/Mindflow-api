using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface ITaskSubtaskService
{
    Task<TaskDetailResponse?> CreateAsync(Guid taskId, TaskSubtaskRequest request);
    Task<TaskDetailResponse?> UpdateAsync(Guid taskId, Guid subtaskId, TaskSubtaskRequest request);
    Task<TaskDetailResponse?> DeleteAsync(Guid taskId, Guid subtaskId);
    Task<TaskDetailResponse?> ReorderAsync(Guid taskId, ReorderTaskSubtasksRequest request);
}

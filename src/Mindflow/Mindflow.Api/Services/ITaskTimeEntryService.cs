using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface ITaskTimeEntryService
{
    Task<IEnumerable<TaskTimeEntryResponse>> GetAsync(DateOnly from, DateOnly to);
    Task<IEnumerable<TaskTimeEntryResponse>?> GetForTaskAsync(Guid taskId);
    Task<TaskTimeEntryMutationResponse?> CreateAsync(Guid taskId, CreateTaskTimeEntryRequest request);
    Task<TaskTimeEntryResponse?> CreateStandaloneAsync(CreateStandaloneTimeEntryRequest request);
    Task<UpdateTaskTimeEntryResponse?> UpdateAsync(Guid id, UpdateTaskTimeEntryRequest request);
    Task<bool> DeleteAsync(Guid id);
    TaskTimeEntry BuildEntry(Guid userId, TaskItem task, CreateTaskTimeEntryRequest request, DateTimeOffset now, bool requireTime);
    TaskTimeEntryResponse ToResponse(TaskTimeEntry entry);
}

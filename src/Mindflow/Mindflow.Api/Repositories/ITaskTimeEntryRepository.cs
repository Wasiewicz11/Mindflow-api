using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface ITaskTimeEntryRepository
{
    Task<IReadOnlyList<TaskTimeEntry>> GetForUserInRangeAsync(Guid userId, DateOnly from, DateOnly to);
    Task<IReadOnlyList<TaskTimeEntry>> GetForUserTaskAsync(Guid userId, Guid taskId);
    Task<IReadOnlyDictionary<Guid, int>> GetDurationMinutesByTaskIdsAsync(Guid userId, IReadOnlyCollection<Guid> taskIds);
    Task<int> GetDurationMinutesForTaskAsync(Guid userId, Guid taskId);
    Task<TaskTimeEntry?> GetByIdAsync(Guid id);
    Task<TaskTimeEntry> CreateAsync(TaskTimeEntry entry);
    Task<bool> DeleteAsync(TaskTimeEntry entry);
}

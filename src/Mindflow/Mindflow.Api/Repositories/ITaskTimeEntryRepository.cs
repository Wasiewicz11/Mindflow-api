using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface ITaskTimeEntryRepository
{
    Task<IReadOnlyList<TaskTimeEntry>> GetForUserInRangeAsync(Guid userId, DateOnly from, DateOnly to);
    Task<IReadOnlyList<TaskTimeEntry>> GetForUserTaskAsync(Guid userId, Guid taskId);
    Task<(IReadOnlyList<TaskTimeEntry> Items, int Total)> GetForUserTaskPagedAsync(
        Guid userId,
        Guid taskId,
        int limit,
        int offset,
        CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, int>> GetDurationMinutesByTaskIdsAsync(Guid userId, IReadOnlyCollection<Guid> taskIds);
    Task<int> GetDurationMinutesForTaskAsync(Guid userId, Guid taskId);
    Task<TaskTimeEntry?> GetByIdAsync(Guid id);
    Task<TaskTimeEntry> CreateAsync(TaskTimeEntry entry);
    Task<TaskTimeEntry> UpdateAsync(TaskTimeEntry entry);
    Task<bool> DeleteAsync(TaskTimeEntry entry);
}

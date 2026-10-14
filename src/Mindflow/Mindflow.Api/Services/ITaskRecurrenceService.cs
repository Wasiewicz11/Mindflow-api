using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface ITaskRecurrenceService
{
    Task<TaskDetailResponse?> UpdateAsync(Guid userId, Guid taskId, UpdateTaskRecurrenceRequest request, CancellationToken ct = default);
    Task<TaskDetailResponse?> DisableAsync(Guid userId, Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyList<RecurringTaskPreviewResponse>> GetUpcomingAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<int> MaterializePastDueForUserAsync(Guid userId, CancellationToken ct = default);
    Task<int> MaterializePastDueOccurrencesAsync(CancellationToken ct = default);
}

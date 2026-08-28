using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services.Integrations;

public class IntegrationTaskQueryService(
    ITaskRepository taskRepository,
    ITaskTimeEntryRepository timeEntryRepository,
    ITaskTimeEntryService timeEntryService,
    IAccessService accessService,
    ICurrentUserService currentUserService) : IIntegrationTaskQueryService
{
    public async Task<IntegrationTaskPageResponse?> GetTasksAsync(
        IntegrationTaskQuery query,
        CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();

        if (query.ProjectId is Guid projectId && !await accessService.CanAccessProjectAsync(projectId, userId))
        {
            return null;
        }

        var filter = new TaskQueryFilter(
            query.ProjectId,
            query.Status,
            query.IsCompleted,
            query.DueBefore,
            query.CreatedAfter,
            query.Limit,
            query.Offset);

        var (items, total) = await taskRepository.GetForUserFilteredAsync(userId, filter, ct);
        var loggedMinutes = await timeEntryRepository.GetDurationMinutesByTaskIdsAsync(
            userId,
            items.Select(task => task.Id).ToArray());

        var responses = items
            .Select(task => TaskResponseMapper.ToListResponse(
                task,
                loggedMinutes.TryGetValue(task.Id, out var minutes) ? minutes : 0))
            .ToList();

        return new IntegrationTaskPageResponse(responses, total, query.Limit, query.Offset);
    }

    public async Task<IntegrationTimeEntryPageResponse?> GetTimeEntriesAsync(
        Guid taskId,
        IntegrationPageQuery query,
        CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();

        if (!await accessService.CanAccessTaskAsync(taskId, userId))
        {
            return null;
        }

        var (entries, total) = await timeEntryRepository.GetForUserTaskPagedAsync(
            userId,
            taskId,
            query.Limit,
            query.Offset,
            ct);

        var items = entries.Select(timeEntryService.ToResponse).ToList();

        return new IntegrationTimeEntryPageResponse(items, total, query.Limit, query.Offset);
    }
}

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services.Integrations;

public class IntegrationChangesService(
    MindflowDbContext db,
    ICurrentUserService currentUserService,
    ITaskRepository taskRepository,
    IIntegrationSpaceScope spaceScope) : IIntegrationChangesService
{
    public async Task<IntegrationChangesPageResponse> GetChangesAsync(
        long since,
        int limit,
        Guid? currentTokenId,
        CancellationToken ct = default)
    {
        var visibility = await ResolveVisibilityAsync(ct);
        if (visibility is null) return Empty(since);

        var rows = await VisibleEvents(visibility)
            .Where(e => e.Sequence > since)
            .OrderBy(e => e.Sequence)
            .Take(limit + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > limit;
        var page = rows.Take(limit).ToList();
        if (page.Count == 0) return Empty(since);

        var tasks = await LoadTaskSnapshotsAsync(page, visibility, ct);
        var projects = await LoadProjectSnapshotsAsync(page, visibility, ct);

        var items = page
            .Select(e => ToResponse(e, visibility, currentTokenId, tasks, projects))
            .ToList();

        return new IntegrationChangesPageResponse(items, FormatCursor(page[^1].Sequence), hasMore);
    }

    public async Task<IntegrationChangesPageResponse> GetLatestCursorAsync(CancellationToken ct = default)
    {
        var visibility = await ResolveVisibilityAsync(ct);
        if (visibility is null) return Empty(0);

        var latest = await VisibleEvents(visibility).MaxAsync(e => (long?)e.Sequence, ct);
        return Empty(latest ?? 0);
    }

    public static string FormatCursor(long sequence) => sequence.ToString(CultureInfo.InvariantCulture);

    private async Task<Visibility?> ResolveVisibilityAsync(CancellationToken ct)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var accessibleSpaceIds = (await taskRepository.GetAccessibleSpaceIdsAsync(userId)).ToList();

        if (spaceScope.SpaceId is Guid scopedSpaceId)
        {
            return accessibleSpaceIds.Contains(scopedSpaceId)
                ? new Visibility(userId, scopedSpaceId, [scopedSpaceId])
                : null;
        }

        return new Visibility(userId, null, accessibleSpaceIds);
    }

    private IQueryable<TaskActivityEvent> VisibleEvents(Visibility visibility)
    {
        var events = db.TaskActivityEvents.AsNoTracking();

        if (visibility.ScopedSpaceId is Guid spaceId)
        {
            var movedOut = JsonSerializer.Serialize(new Dictionary<string, Guid> { ["previous_space_id"] = spaceId });
            return events.Where(e => e.SpaceId == spaceId
                || (e.EventType == TaskActivityEventType.TaskProjectChanged && EF.Functions.JsonContains(e.Metadata, movedOut)));
        }

        var spaceIds = visibility.SpaceIds;
        return events.Where(e => (e.SpaceId != null && spaceIds.Contains(e.SpaceId.Value))
            || (e.SpaceId == null && e.UserId == visibility.UserId));
    }

    private async Task<Dictionary<Guid, IntegrationChangeTaskSnapshot>> LoadTaskSnapshotsAsync(
        IReadOnlyList<TaskActivityEvent> page,
        Visibility visibility,
        CancellationToken ct)
    {
        var taskIds = page.Where(e => e.TaskId.HasValue).Select(e => e.TaskId!.Value).Distinct().ToList();
        if (taskIds.Count == 0) return [];

        var rows = await db.Tasks
            .AsNoTracking()
            .Where(task => taskIds.Contains(task.Id))
            .Select(task => new
            {
                Snapshot = new IntegrationChangeTaskSnapshot(
                    task.Id,
                    task.Content,
                    task.Status,
                    task.IsCompleted,
                    task.ProjectId,
                    task.Priority,
                    task.DueDate,
                    task.EstimatedHours),
                task.UserId,
                SpaceId = db.Projects
                    .Where(project => project.Id == task.ProjectId)
                    .Select(project => project.SpaceId)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows
            .Where(row => visibility.CanSee(row.SpaceId, row.UserId))
            .ToDictionary(row => row.Snapshot.Id, row => row.Snapshot);
    }

    private async Task<Dictionary<Guid, IntegrationChangeProjectSnapshot>> LoadProjectSnapshotsAsync(
        IReadOnlyList<TaskActivityEvent> page,
        Visibility visibility,
        CancellationToken ct)
    {
        var projectIds = page
            .Where(e => IsProjectEvent(e.EventType) && e.ProjectId.HasValue)
            .Select(e => e.ProjectId!.Value)
            .Distinct()
            .ToList();
        if (projectIds.Count == 0) return [];

        var projects = await db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .ToListAsync(ct);

        return projects
            .Where(project => visibility.CanSee(project.SpaceId, project.UserId))
            .ToDictionary(
                project => project.Id,
                project => new IntegrationChangeProjectSnapshot(project.Id, project.Name, project.Color, project.SpaceId));
    }

    private static IntegrationChangeResponse ToResponse(
        TaskActivityEvent e,
        Visibility visibility,
        Guid? currentTokenId,
        IReadOnlyDictionary<Guid, IntegrationChangeTaskSnapshot> tasks,
        IReadOnlyDictionary<Guid, IntegrationChangeProjectSnapshot> projects)
    {
        var leftScope = visibility.ScopedSpaceId is Guid spaceId && e.SpaceId != spaceId;

        return new IntegrationChangeResponse(
            FormatCursor(e.Sequence),
            e.Id,
            e.EventType,
            e.TaskId,
            leftScope ? null : e.ProjectId,
            leftScope ? null : e.SpaceId,
            e.OccurredAt,
            new IntegrationChangeActor(
                e.ActorType.ToString().ToLowerInvariant(),
                currentTokenId.HasValue && e.IntegrationTokenId == currentTokenId),
            e.TaskId is Guid taskId && tasks.TryGetValue(taskId, out var task) ? task : null,
            IsProjectEvent(e.EventType) && e.ProjectId is Guid projectId && projects.TryGetValue(projectId, out var project)
                ? project
                : null);
    }

    private static bool IsProjectEvent(TaskActivityEventType type) =>
        type is TaskActivityEventType.ProjectCreated
            or TaskActivityEventType.ProjectUpdated
            or TaskActivityEventType.ProjectDeleted;

    private static IntegrationChangesPageResponse Empty(long cursor) =>
        new([], FormatCursor(cursor), false);

    private sealed record Visibility(Guid UserId, Guid? ScopedSpaceId, List<Guid> SpaceIds)
    {
        public bool CanSee(Guid? spaceId, Guid ownerId) =>
            ScopedSpaceId is Guid scoped
                ? spaceId == scoped
                : spaceId is Guid space ? SpaceIds.Contains(space) : ownerId == UserId;
    }
}

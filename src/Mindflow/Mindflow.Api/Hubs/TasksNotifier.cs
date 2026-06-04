using Microsoft.AspNetCore.SignalR;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Hubs;

public class TasksNotifier(
    IHubContext<TasksHub> hubContext,
    ISpaceRepository spaceRepository) : ITasksNotifier
{
    private const string TaskCreated = "TaskCreated";
    private const string TaskUpdated = "TaskUpdated";
    private const string TaskDeleted = "TaskDeleted";
    private const string CalendarBlockCreated = "CalendarBlockCreated";
    private const string CalendarBlockUpdated = "CalendarBlockUpdated";
    private const string CalendarBlockDeleted = "CalendarBlockDeleted";

    public System.Threading.Tasks.Task TaskCreatedAsync(TaskItem task, Guid? spaceId)
        => BroadcastAsync(TaskCreated, task.UserId, spaceId, task);

    public System.Threading.Tasks.Task TaskUpdatedAsync(TaskItem task, Guid? spaceId)
        => BroadcastAsync(TaskUpdated, task.UserId, spaceId, task);

    public System.Threading.Tasks.Task TaskDeletedAsync(Guid taskId, Guid ownerUserId, Guid? spaceId)
        => BroadcastAsync(TaskDeleted, ownerUserId, spaceId, new { id = taskId });

    public System.Threading.Tasks.Task TaskRemovedFromSpaceAsync(Guid taskId, Guid spaceId)
        => BroadcastToSpaceUsersAsync(TaskDeleted, spaceId, new { id = taskId });

    public System.Threading.Tasks.Task CalendarBlockCreatedAsync(CalendarBlockResponse block)
        => BroadcastToUserAsync(CalendarBlockCreated, block.UserId, block);

    public System.Threading.Tasks.Task CalendarBlockUpdatedAsync(CalendarBlockResponse block)
        => BroadcastToUserAsync(CalendarBlockUpdated, block.UserId, block);

    public System.Threading.Tasks.Task CalendarBlockDeletedAsync(Guid blockId, Guid userId)
        => BroadcastToUserAsync(CalendarBlockDeleted, userId, new { id = blockId });

    private async System.Threading.Tasks.Task BroadcastAsync(string method, Guid ownerUserId, Guid? spaceId, object payload)
    {
        if (spaceId.HasValue)
        {
            await BroadcastToSpaceAndOwnerAsync(method, ownerUserId, spaceId.Value, payload);
            return;
        }

        await hubContext.Clients.Group(TasksHub.UserGroup(ownerUserId)).SendAsync(method, payload);
    }

    private System.Threading.Tasks.Task BroadcastToUserAsync(string method, Guid userId, object payload)
        => hubContext.Clients.Group(TasksHub.UserGroup(userId)).SendAsync(method, payload);

    private async System.Threading.Tasks.Task BroadcastToSpaceAndOwnerAsync(
        string method,
        Guid ownerUserId,
        Guid spaceId,
        object payload)
    {
        var userGroups = (await spaceRepository.GetUserIdsWithAccessAsync(spaceId))
            .Append(ownerUserId)
            .Distinct()
            .Select(TasksHub.UserGroup)
            .ToArray();

        if (userGroups.Length == 0) return;

        await hubContext.Clients.Groups(userGroups).SendAsync(method, payload);
    }

    private async System.Threading.Tasks.Task BroadcastToSpaceUsersAsync(string method, Guid spaceId, object payload)
    {
        var userGroups = (await spaceRepository.GetUserIdsWithAccessAsync(spaceId))
            .Distinct()
            .Select(TasksHub.UserGroup)
            .ToArray();

        if (userGroups.Length == 0) return;

        await hubContext.Clients.Groups(userGroups).SendAsync(method, payload);
    }
}

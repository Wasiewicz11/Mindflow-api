using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Hubs;

public interface ITasksNotifier
{
    Task TaskCreatedAsync(TaskItem task, Guid? spaceId);
    Task TaskUpdatedAsync(TaskItem task, Guid? spaceId);
    Task TaskDeletedAsync(Guid taskId, Guid ownerUserId, Guid? spaceId);
    Task TaskRemovedFromSpaceAsync(Guid taskId, Guid spaceId);
    Task CalendarBlockCreatedAsync(CalendarBlockResponse block);
    Task CalendarBlockUpdatedAsync(CalendarBlockResponse block);
    Task CalendarBlockDeletedAsync(Guid blockId, Guid userId);
}

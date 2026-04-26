using Mindflow.Api.Models;

namespace Mindflow.Api.Hubs;

public interface ITasksNotifier
{
    Task TaskCreatedAsync(TaskItem task, Guid? spaceId);
    Task TaskUpdatedAsync(TaskItem task, Guid? spaceId);
    Task TaskDeletedAsync(Guid taskId, Guid ownerUserId, Guid? spaceId);
    Task TaskRemovedFromSpaceAsync(Guid taskId, Guid spaceId);
}

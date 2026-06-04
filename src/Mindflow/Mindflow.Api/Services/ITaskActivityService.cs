using Mindflow.Api.Models.Enums;
using Task = System.Threading.Tasks.Task;

namespace Mindflow.Api.Services;

public interface ITaskActivityService
{
    Task RecordUserTaskEventAsync(
        TaskActivityEventType eventType,
        Guid userId,
        Guid? taskId,
        Guid? spaceId,
        Guid? projectId,
        object? metadata = null);
}

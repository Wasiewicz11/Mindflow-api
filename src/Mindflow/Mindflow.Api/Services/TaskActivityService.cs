using System.Text.Json;
using Mindflow.Api.Data;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;
using Task = System.Threading.Tasks.Task;

namespace Mindflow.Api.Services;

public class TaskActivityService(
    MindflowDbContext db,
    IHttpContextAccessor httpContextAccessor,
    ILogger<TaskActivityService> logger) : ITaskActivityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RecordUserTaskEventAsync(
        TaskActivityEventType eventType,
        Guid userId,
        Guid? taskId,
        Guid? spaceId,
        Guid? projectId,
        object? metadata = null)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var httpContext = httpContextAccessor.HttpContext;

            db.TaskActivityEvents.Add(new TaskActivityEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TaskId = taskId,
                SpaceId = spaceId,
                ProjectId = projectId,
                EventType = eventType,
                Source = TaskActivitySource.User,
                ActorType = TaskActivityActorType.User,
                ActorId = userId,
                SessionId = GetHeaderValue(httpContext, "X-Session-Id"),
                RequestId = httpContext?.TraceIdentifier,
                Metadata = JsonSerializer.Serialize(metadata ?? new { }, JsonOptions),
                OccurredAt = now,
                CreatedAt = now
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to record task activity event {EventType} for task {TaskId} and user {UserId}.",
                eventType,
                taskId,
                userId);
        }
    }

    private static string? GetHeaderValue(HttpContext? httpContext, string headerName)
    {
        if (httpContext is null) return null;
        return httpContext.Request.Headers.TryGetValue(headerName, out var value)
            ? value.ToString()
            : null;
    }
}

using System.Text.Json;
using System.Security.Claims;
using Mindflow.Api.Authentication;
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
            var integrationTokenId = GetIntegrationTokenId(httpContext);
            var isIntegration = integrationTokenId.HasValue;

            db.TaskActivityEvents.Add(new TaskActivityEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TaskId = taskId,
                SpaceId = spaceId,
                ProjectId = projectId,
                EventType = eventType,
                Source = isIntegration ? TaskActivitySource.Integration : TaskActivitySource.User,
                ActorType = isIntegration ? TaskActivityActorType.Integration : TaskActivityActorType.User,
                ActorId = userId,
                IntegrationTokenId = integrationTokenId,
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

    private static Guid? GetIntegrationTokenId(HttpContext? httpContext)
    {
        var principal = httpContext?.User;
        if (principal?.FindFirstValue(IntegrationTokenAuthenticationDefaults.AuthProviderClaim)
            != IntegrationTokenAuthenticationDefaults.Scheme)
        {
            return null;
        }

        return Guid.TryParse(
            principal.FindFirstValue(IntegrationTokenAuthenticationDefaults.TokenIdClaim),
            out var tokenId)
            ? tokenId
            : null;
    }
}

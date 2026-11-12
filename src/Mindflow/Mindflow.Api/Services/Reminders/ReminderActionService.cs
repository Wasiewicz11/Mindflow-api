using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Reminders;

public enum ReminderAction
{
    Done,
    Snooze
}

public interface IReminderActionService
{
    Task<ReminderActionResponse?> ApplyAsync(Guid userId, Guid taskId, ReminderAction action, CancellationToken ct);
}

public class ReminderActionService(
    MindflowDbContext db,
    ITaskService taskService,
    IHttpContextAccessor httpContextAccessor) : IReminderActionService
{
    public const int SnoozeMinutes = 10;

    public async Task<ReminderActionResponse?> ApplyAsync(Guid userId, Guid taskId, ReminderAction action, CancellationToken ct)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct);
        if (task is null) return null;

        if (action == ReminderAction.Snooze)
        {
            task.ReminderSnoozedUntil = DateTimeOffset.UtcNow.AddMinutes(SnoozeMinutes);
            await db.SaveChangesAsync(ct);
            return new ReminderActionResponse("snooze", task.Id, task.Content, task.ReminderSnoozedUntil);
        }

        if (!task.IsCompleted)
        {
            ActAs(userId);
            await taskService.CompleteAsync(task.Id, new CompleteTaskRequest(null, false, null, null, null, null, null));
        }

        return new ReminderActionResponse("done", task.Id, task.Content, null);
    }

    private void ActAs(Guid userId)
    {
        var context = httpContextAccessor.HttpContext ??= new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "ReminderAction"));
    }
}

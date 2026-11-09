using Mindflow.Api.Data;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Reminders;

public class InAppReminderChannel(
    MindflowDbContext db,
    ITasksNotifier notifier,
    ILogger<InAppReminderChannel> logger) : IReminderChannel
{
    public string Name => "app";

    public async Task<bool> SendAsync(ReminderMessage message, CancellationToken ct)
    {
        var item = new NotificationInboxItem
        {
            Id = message.NotificationId,
            UserId = message.UserId,
            Kind = NotificationInboxKind.TaskReminder,
            Title = message.Title,
            Body = message.Body,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.NotificationInboxItems.Add(item);
        await db.SaveChangesAsync(ct);

        try
        {
            await notifier.ReminderDueAsync(message.UserId, new ReminderDuePayload(
                item.Id, message.TaskId, message.Title, message.Body, message.DueAt, message.ActionToken));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "ReminderDue broadcast failed for task {TaskId}.", message.TaskId);
        }

        return true;
    }
}

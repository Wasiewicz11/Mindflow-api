namespace Mindflow.Api.Services.Reminders;

public class PushReminderChannel(IWebPushSender sender, IConfiguration configuration) : IReminderChannel
{
    private static readonly int[] VibratePattern = [300, 120, 300, 120, 600];

    public string Name => "push";

    public Task<bool> SendAsync(ReminderMessage message, CancellationToken ct) =>
        sender.SendAsync(message.UserId, new
        {
            kind = "reminder",
            title = message.Title,
            body = message.Body,
            tag = $"reminder:{message.TaskId:N}",
            url = $"/zadania/{message.TaskId}",
            requireInteraction = true,
            renotify = true,
            vibrate = VibratePattern,
            taskId = message.TaskId,
            actionToken = message.ActionToken,
            actionUrl = ReminderLinks.ActionApiUrl(configuration)
        }, ct);
}

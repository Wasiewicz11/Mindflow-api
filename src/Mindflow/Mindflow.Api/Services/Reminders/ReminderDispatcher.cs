namespace Mindflow.Api.Services.Reminders;

public interface IReminderDispatcher
{
    Task<IReadOnlyList<string>> DispatchAsync(ReminderMessage message, CancellationToken ct);
}

public class ReminderDispatcher(IEnumerable<IReminderChannel> channels, ILogger<ReminderDispatcher> logger) : IReminderDispatcher
{
    public async Task<IReadOnlyList<string>> DispatchAsync(ReminderMessage message, CancellationToken ct)
    {
        var delivered = new List<string>();
        foreach (var channel in channels)
        {
            try
            {
                if (await channel.SendAsync(message, ct)) delivered.Add(channel.Name);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Reminder channel {Channel} failed for task {TaskId}.", channel.Name, message.TaskId);
            }
        }

        return delivered;
    }
}

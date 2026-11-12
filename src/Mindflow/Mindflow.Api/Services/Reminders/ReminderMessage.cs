namespace Mindflow.Api.Services.Reminders;

public record ReminderMessage(
    Guid NotificationId,
    Guid UserId,
    Guid TaskId,
    string Title,
    string Body,
    string LocalDue,
    DateTimeOffset DueAt,
    string TaskUrl,
    string ActionToken);

public interface IReminderChannel
{
    string Name { get; }
    Task<bool> SendAsync(ReminderMessage message, CancellationToken ct);
}

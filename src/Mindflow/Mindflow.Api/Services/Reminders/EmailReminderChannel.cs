using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Services.Email;

namespace Mindflow.Api.Services.Reminders;

public class EmailReminderChannel(MindflowDbContext db, IEmailSender sender, IConfiguration configuration) : IReminderChannel
{
    public string Name => "email";

    public async Task<bool> SendAsync(ReminderMessage message, CancellationToken ct)
    {
        if (!EmailOptions.IsConfigured(configuration)) return false;

        var enabled = await db.NotificationSettings.AsNoTracking()
            .Where(settings => settings.UserId == message.UserId)
            .Select(settings => (bool?)settings.EmailRemindersEnabled)
            .SingleOrDefaultAsync(ct) ?? true;
        if (!enabled) return false;

        var email = await db.Users.AsNoTracking()
            .Where(user => user.Id == message.UserId)
            .Select(user => user.Email)
            .SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(email)) return false;

        await sender.SendAsync(ReminderEmail.Build(message, ReminderLinks.ActionPageUrl(configuration, message.ActionToken), email), ct);
        return true;
    }
}

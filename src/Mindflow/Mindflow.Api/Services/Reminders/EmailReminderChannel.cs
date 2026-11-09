using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Services.Email;

namespace Mindflow.Api.Services.Reminders;

public class EmailReminderChannel(MindflowDbContext db, IEmailSender sender, IConfiguration configuration) : IReminderChannel
{
    private bool smtpUnavailable;

    public string Name => "email";

    public async Task<bool> SendAsync(ReminderMessage message, CancellationToken ct)
    {
        if (smtpUnavailable || !EmailOptions.IsConfigured(configuration)) return false;

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

        try
        {
            await sender.SendAsync(ReminderEmail.Build(message, ReminderLinks.ActionPageUrl(configuration, message.ActionToken), email), ct);
        }
        catch (Exception exception) when (exception is TimeoutException or SmtpCommandException or SmtpProtocolException or ServiceNotConnectedException or SocketException or IOException)
        {
            smtpUnavailable = true;
            throw;
        }

        return true;
    }
}

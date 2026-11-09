using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Services.Email;
using Mindflow.Api.Services.Reminders;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Reminders;

public class EmailReminderChannelTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private sealed class RecordingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public Task SendAsync(EmailMessage message, CancellationToken ct) { Sent.Add(message); return Task.CompletedTask; }
    }

    private sealed class FailingSender : IEmailSender
    {
        public int Calls { get; private set; }
        public Task SendAsync(EmailMessage message, CancellationToken ct)
        {
            Calls++;
            throw new TimeoutException("SMTP send timed out.");
        }
    }

    private (IServiceProvider Services, RecordingSender Sender) Configured(bool smtp = true, string frontendUrl = "https://app.test")
    {
        var sender = new RecordingSender();
        var app = factory.WithWebHostBuilder(builder =>
        {
            if (smtp)
            {
                builder.UseSetting("Email:Smtp:Host", "smtp.test");
                builder.UseSetting("Email:Smtp:From", "Mindflow <noreply@mindflow.test>");
            }
            if (!smtp)
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:Smtp:Host"] = "",
                    ["Email:Smtp:From"] = ""
                }));
            builder.UseSetting("Notifications:FrontendUrl", frontendUrl);
            builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(sender));
        });
        return (app.Services, sender);
    }

    private static ReminderMessage MessageFor(Guid userId) => new(
        Guid.NewGuid(), userId, Guid.NewGuid(), "⏰ Lek", "Teraz · termin dziś 08:00", "dziś 08:00", DateTimeOffset.UtcNow, "https://app.test/zadania/x", "tok");

    private static async Task<bool> SendAsync(IServiceProvider services, ReminderMessage message)
    {
        using var scope = services.CreateScope();
        var channel = scope.ServiceProvider.GetServices<IReminderChannel>().Single(c => c.Name == "email");
        return await channel.SendAsync(message, CancellationToken.None);
    }

    [LocalDatabaseFact]
    public async Task Sends_to_the_users_address_when_enabled()
    {
        var user = await factory.CreateUserAsync();
        var (services, sender) = Configured();

        Assert.True(await SendAsync(services, MessageFor(user.Id)));

        var email = Assert.Single(sender.Sent);
        Assert.Equal(user.Email, email.To);
        Assert.Contains("https://app.test/przypomnienie?t=tok", email.Text);
    }

    [LocalDatabaseFact]
    public async Task Skips_when_user_turned_email_off_or_smtp_missing()
    {
        var user = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.NotificationSettings.Add(new NotificationSettings { UserId = user.Id, EmailRemindersEnabled = false, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        var (services, sender) = Configured();
        Assert.False(await SendAsync(services, MessageFor(user.Id)));

        var other = await factory.CreateUserAsync();
        var (unconfigured, otherSender) = Configured(smtp: false);
        Assert.False(await SendAsync(unconfigured, MessageFor(other.Id)));

        Assert.Empty(sender.Sent);
        Assert.Empty(otherSender.Sent);
    }

    [LocalDatabaseFact]
    public async Task Stops_sending_in_the_same_scope_after_smtp_failure()
    {
        var user = await factory.CreateUserAsync();
        var failing = new FailingSender();
        var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Email:Smtp:Host", "smtp.test");
            builder.UseSetting("Email:Smtp:From", "Mindflow <noreply@mindflow.test>");
            builder.UseSetting("Notifications:FrontendUrl", "https://app.test");
            builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(failing));
        });
        using var scope = app.Services.CreateScope();
        var channel = scope.ServiceProvider.GetServices<IReminderChannel>().Single(c => c.Name == "email");

        await Assert.ThrowsAsync<TimeoutException>(() => channel.SendAsync(MessageFor(user.Id), CancellationToken.None));
        Assert.False(await channel.SendAsync(MessageFor(user.Id), CancellationToken.None));

        Assert.Equal(1, failing.Calls);
    }

    [LocalDatabaseFact]
    public async Task Skips_when_frontend_url_is_not_absolute()
    {
        var user = await factory.CreateUserAsync();
        var (services, sender) = Configured(frontendUrl: "/relative");

        Assert.False(await SendAsync(services, MessageFor(user.Id)));
        Assert.Empty(sender.Sent);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Reminders;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Reminders;

public class TaskReminderJobTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    [LocalDatabaseFact]
    public async Task Sends_default_reminder_once_to_the_inbox()
    {
        var now = DateTimeOffset.UtcNow;
        var user = await factory.CreateUserAsync();
        await SeedTaskAsync(user.Id, now.AddMinutes(10), "Telefon do księgowej");

        await RunAsync(now);
        await RunAsync(now.AddMinutes(5));

        var items = await InboxAsync(user.Id);
        var item = Assert.Single(items);
        Assert.Equal(NotificationInboxKind.TaskReminder, item.Kind);
        Assert.Equal("⏰ Telefon do księgowej", item.Title);
        Assert.StartsWith("Za 10 min", item.Body);
    }

    [LocalDatabaseFact]
    public async Task Respects_own_minutes_and_disabled_flag()
    {
        var now = DateTimeOffset.UtcNow;
        var user = await factory.CreateUserAsync();
        await SeedTaskAsync(user.Id, now.AddMinutes(60), "Za godzinę", minutes: 60);
        await SeedTaskAsync(user.Id, now.AddMinutes(10), "Wyłączone", disabled: true);

        await RunAsync(now);

        Assert.Equal(["⏰ Za godzinę"], (await InboxAsync(user.Id)).Select(i => i.Title).ToList());
    }

    [LocalDatabaseFact]
    public async Task Sends_snoozed_reminder_again()
    {
        var now = DateTimeOffset.UtcNow;
        var user = await factory.CreateUserAsync();
        var taskId = await SeedTaskAsync(user.Id, now.AddMinutes(10), "Lek");
        await RunAsync(now);

        await factory.WithDbAsync(async db =>
        {
            var task = await db.Tasks.SingleAsync(t => t.Id == taskId);
            task.ReminderSnoozedUntil = now.AddMinutes(10);
            await db.SaveChangesAsync();
        });
        await RunAsync(now.AddMinutes(11));

        var bodies = (await InboxAsync(user.Id)).Select(i => i.Body).ToList();
        Assert.Equal(2, bodies.Count);
        Assert.Contains(bodies, body => body.StartsWith("Odłożone przypomnienie"));
    }

    private async Task RunAsync(DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITaskReminderService>().SendDueAsync(now);
    }

    private async Task<Guid> SeedTaskAsync(Guid userId, DateTimeOffset dueAtUtc, string content, int? minutes = null, bool disabled = false)
    {
        var local = TimeZoneInfo.ConvertTime(dueAtUtc, Warsaw);
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = content,
            DueDate = DateOnly.FromDateTime(local.DateTime),
            DueTime = new TimeOnly(local.Hour, local.Minute),
            ReminderMinutesBefore = minutes,
            ReminderDisabled = disabled,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await factory.WithDbAsync(async db =>
        {
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
        });
        return task.Id;
    }

    private Task<List<NotificationInboxItem>> InboxAsync(Guid userId) =>
        factory.WithDbAsync(db => db.NotificationInboxItems
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync());
}

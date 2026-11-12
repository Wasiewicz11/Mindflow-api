using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services;
using Mindflow.Api.Services.Reminders;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramBriefsJobTests(TelegramApiFactory factory) : IClassFixture<TelegramApiFactory>
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static readonly DateTimeOffset Now = new(DateTime.UtcNow.Date.AddHours(2).AddMinutes(30), TimeSpan.Zero);
    private static long chatSeed = Random.Shared.NextInt64(1_000_000_000, 2_000_000_000);

    [LocalDatabaseFact]
    public async Task Morning_brief_reaches_telegram_without_push_subscription()
    {
        var (userId, chatId) = await TelegramUserAsync(s => { s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); });
        await SeedTaskAsync(userId, "Raport <Q4> & podsumowanie", LocalDate(Now).AddDays(-1));

        var result = await RunAsync(Now);

        var item = Assert.Single(await InboxAsync(userId));
        Assert.Equal(NotificationInboxKind.MorningBrief, item.Kind);
        Assert.Equal("Dziś bez zadań z terminem. Zaległe (1): Raport <Q4> & podsumowanie.", item.Body);
        var message = Assert.Single(Sent(chatId));
        Assert.Equal("<b>Poranny brief</b>\nDziś bez zadań z terminem. Zaległe (1): Raport &lt;Q4&gt; &amp; podsumowanie.", (string)message["text"]!);
        var button = message["reply_markup"]!["inline_keyboard"]![0]![0]!;
        Assert.Equal("Otwórz Mindflow", (string)button["text"]!);
        Assert.Equal($"https://app.test/?notification={item.Id:N}", (string)button["url"]!);
        Assert.True(await DeliveredAsync(userId, $"brief:morning:{LocalDate(Now):yyyy-MM-dd}"));
        Assert.True(result.BriefsSent >= 1);
        var plan = (string)Assert.Single(DayPlans(chatId))["text"]!;
        Assert.StartsWith("<b>🧠 Plan dnia · ", plan);
        Assert.Contains("<b>🗓 Harmonogram</b>", plan);
        Assert.Contains("Raport &lt;Q4&gt; &amp; podsumowanie", plan);
    }

    [LocalDatabaseFact]
    public async Task Brief_is_not_sent_twice_on_the_next_run()
    {
        var (userId, chatId) = await TelegramUserAsync(s => { s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); });

        await RunAsync(Now);
        await RunAsync(Now.AddMinutes(5));

        Assert.Single(await InboxAsync(userId));
        Assert.Single(Sent(chatId));
        Assert.Single(DayPlans(chatId));
    }

    [LocalDatabaseFact]
    public async Task Evening_summary_reaches_telegram()
    {
        var (userId, chatId) = await TelegramUserAsync(s => { s.EveningSummaryEnabled = true; s.EveningSummaryTime = LocalTime(Now); });
        await SeedTaskAsync(userId, "Zadanie <A> & B", LocalDate(Now));

        await RunAsync(Now);

        var item = Assert.Single(await InboxAsync(userId));
        Assert.Equal(NotificationInboxKind.EveningSummary, item.Kind);
        Assert.Equal("Ukończone dziś: 0. Do zrobienia: Zadanie <A> & B.", item.Body);
        Assert.Equal("<b>Podsumowanie dnia</b>\nUkończone dziś: 0. Do zrobienia: Zadanie &lt;A&gt; &amp; B.", (string)Assert.Single(Sent(chatId))["text"]!);
    }

    [LocalDatabaseFact]
    public async Task Respects_master_switch_and_telegram_switch()
    {
        var (offUserId, offChatId) = await TelegramUserAsync(s => { s.Enabled = false; s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); });
        var (mutedUserId, mutedChatId) = await TelegramUserAsync(s => { s.TelegramRemindersEnabled = false; s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); });

        await RunAsync(Now);

        Assert.Empty(Sent(offChatId));
        Assert.Empty(Sent(mutedChatId));
        Assert.Empty(await InboxAsync(offUserId));
        Assert.Empty(await InboxAsync(mutedUserId));
        Assert.False(await DeliveredAsync(mutedUserId, $"brief:morning:{LocalDate(Now):yyyy-MM-dd}"));
    }

    [LocalDatabaseFact]
    public async Task Block_reminders_stay_push_only()
    {
        var (userId, chatId) = await TelegramUserAsync(s => s.BlockRemindersEnabled = true);
        await factory.WithDbAsync(async db =>
        {
            db.CalendarBlocks.Add(new CalendarBlock
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = "Blok testowy",
                StartAt = Now.AddMinutes(10),
                DurationMinutes = 30,
                CreatedAt = Now,
                UpdatedAt = Now
            });
            await db.SaveChangesAsync();
        });

        await RunAsync(Now);

        Assert.Empty(Sent(chatId));
        Assert.Empty(await InboxAsync(userId));
    }

    [LocalDatabaseFact]
    public async Task Push_user_keeps_delivery_when_telegram_fails()
    {
        var push = new FakePush();
        var (userId, chatId) = await TelegramUserAsync(s => { s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); }, push: true);
        factory.Telegram.Throw = new HttpRequestException("down");
        try
        {
            await RunAsync(Now, push);
        }
        finally
        {
            factory.Telegram.Throw = null;
        }

        Assert.Single(await InboxAsync(userId));
        Assert.Equal(1, push.Calls);
        Assert.True(await DeliveredAsync(userId, $"brief:morning:{LocalDate(Now):yyyy-MM-dd}"));
        Assert.Empty(Sent(chatId));
    }

    [LocalDatabaseFact]
    public async Task Telegram_still_receives_brief_when_push_throws()
    {
        var push = new FakePush { Throw = new InvalidOperationException("bad subscription") };
        var (userId, chatId) = await TelegramUserAsync(s => { s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); }, push: true);

        await RunAsync(Now, push);

        Assert.Single(await InboxAsync(userId));
        Assert.Single(Sent(chatId));
        Assert.True(await DeliveredAsync(userId, $"brief:morning:{LocalDate(Now):yyyy-MM-dd}"));
    }

    [LocalDatabaseFact]
    public async Task Both_channels_failing_leaves_no_trace_and_next_run_delivers()
    {
        var push = new FakePush { Result = false };
        var (userId, chatId) = await TelegramUserAsync(s => { s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); }, push: true);
        factory.Telegram.Throw = new HttpRequestException("down");
        try
        {
            await RunAsync(Now, push);
        }
        finally
        {
            factory.Telegram.Throw = null;
        }

        Assert.Empty(await InboxAsync(userId));
        Assert.False(await DeliveredAsync(userId, $"brief:morning:{LocalDate(Now):yyyy-MM-dd}"));

        push.Result = true;
        await RunAsync(Now.AddMinutes(5), push);

        Assert.Single(await InboxAsync(userId));
        Assert.Single(Sent(chatId));
        Assert.True(await DeliveredAsync(userId, $"brief:morning:{LocalDate(Now):yyyy-MM-dd}"));
    }

    [LocalDatabaseFact]
    public async Task User_with_push_and_telegram_gets_one_delivery()
    {
        var push = new FakePush();
        var (userId, chatId) = await TelegramUserAsync(s => { s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); }, push: true);

        await RunAsync(Now, push);
        await RunAsync(Now.AddMinutes(5), push);

        Assert.Equal(1, push.Calls);
        Assert.Single(Sent(chatId));
        Assert.Equal(1, await DeliveryCountAsync(userId, "brief:"));
    }

    [LocalDatabaseFact]
    public async Task Block_and_recurring_reminders_make_no_telegram_call()
    {
        var push = new FakePush();
        var (userId, chatId) = await TelegramUserAsync(s => s.BlockRemindersEnabled = true, push: true);
        var due = TimeZoneInfo.ConvertTime(Now.AddMinutes(10), Warsaw);
        var templateId = Guid.NewGuid();
        await factory.WithDbAsync(async db =>
        {
            db.CalendarBlocks.Add(new CalendarBlock
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = "Blok testowy",
                StartAt = Now.AddMinutes(10),
                DurationMinutes = 30,
                CreatedAt = Now,
                UpdatedAt = Now
            });
            db.Tasks.Add(new TaskItem
            {
                Id = templateId,
                UserId = userId,
                Content = "Szablon",
                CreatedAt = DateTimeOffset.UtcNow,
                Recurrence = new TaskRecurrence
                {
                    IsEnabled = true,
                    NotificationsEnabled = true,
                    ReminderMinutesBefore = 10,
                    StartDate = LocalDate(Now),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            });
            await db.SaveChangesAsync();
            db.Tasks.Add(new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Content = "Cykliczne",
                RecurringTaskId = templateId,
                RecurrenceOccurrenceDate = DateOnly.FromDateTime(due.DateTime),
                DueDate = DateOnly.FromDateTime(due.DateTime),
                DueTime = TimeOnly.FromDateTime(due.DateTime),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        });

        await RunAsync(Now, push);

        Assert.Equal(1, await DeliveryCountAsync(userId, "block:"));
        Assert.Equal(1, await DeliveryCountAsync(userId, "recurrence:"));
        Assert.Equal(2, push.Calls);
        Assert.Empty(Sent(chatId));
    }

    private sealed class FakePush : IWebPushSender
    {
        public int Calls;
        public bool Result { get; set; } = true;
        public Exception? Throw { get; set; }

        public Task<bool> SendAsync(Guid userId, object payload, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (Throw is not null) throw Throw;
            return Task.FromResult(Result);
        }
    }

    private async Task RunAsync(DateTimeOffset now, FakePush push)
    {
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWebPushSender>();
            services.AddSingleton<IWebPushSender>(push);
        }));
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationService>().ProcessDueNotificationsAsync(now);
    }

    private Task<int> DeliveryCountAsync(Guid userId, string keyPrefix) =>
        factory.WithDbAsync(db => db.PushNotificationDeliveries.CountAsync(d => d.UserId == userId && d.DeliveryKey.StartsWith(keyPrefix)));

    private static TimeOnly LocalTime(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Warsaw);
        return new TimeOnly(local.Hour, local.Minute);
    }

    private static DateOnly LocalDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Warsaw).DateTime);

    private async Task<NotificationJobResponse> RunAsync(DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>().ProcessDueNotificationsAsync(now);
    }

    private async Task<(Guid UserId, long ChatId)> TelegramUserAsync(Action<NotificationSettings> configure, bool push = false)
    {
        var user = await factory.CreateUserAsync();
        var chatId = Interlocked.Increment(ref chatSeed);
        var settings = new NotificationSettings
        {
            UserId = user.Id,
            MorningBriefEnabled = false,
            MiddayBriefEnabled = false,
            EveningSummaryEnabled = false,
            BlockRemindersEnabled = false,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        configure(settings);
        await factory.WithDbAsync(async db =>
        {
            db.NotificationSettings.Add(settings);
            db.TelegramConnections.Add(new TelegramConnection { UserId = user.Id, ChatId = chatId, ConnectedAt = DateTimeOffset.UtcNow });
            if (push)
                db.PushNotificationSubscriptions.Add(new PushNotificationSubscription
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Endpoint = $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}",
                    P256dh = "p",
                    Auth = "a",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            await db.SaveChangesAsync();
        });
        return (user.Id, chatId);
    }

    private Task SeedTaskAsync(Guid userId, string content, DateOnly dueDate) =>
        factory.WithDbAsync(async db =>
        {
            db.Tasks.Add(new TaskItem { Id = Guid.NewGuid(), UserId = userId, Content = content, DueDate = dueDate, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });

    private Task<List<NotificationInboxItem>> InboxAsync(Guid userId) =>
        factory.WithDbAsync(db => db.NotificationInboxItems.AsNoTracking().Where(i => i.UserId == userId).ToListAsync());

    private Task<bool> DeliveredAsync(Guid userId, string key) =>
        factory.WithDbAsync(db => db.PushNotificationDeliveries.AnyAsync(d => d.UserId == userId && d.DeliveryKey == key));

    private List<JsonObject> Sent(long chatId) =>
        AllSent(chatId).Where(m => !IsDayPlan(m)).ToList();

    private List<JsonObject> DayPlans(long chatId) =>
        AllSent(chatId).Where(IsDayPlan).ToList();

    private static bool IsDayPlan(JsonObject message) =>
        ((string?)message["text"])?.StartsWith("<b>🧠 Plan dnia", StringComparison.Ordinal) == true;

    private List<JsonObject> AllSent(long chatId)
    {
        lock (factory.Telegram.Requests)
            return factory.Telegram.Requests
                .Where(r => r.Method == "sendMessage" && (long?)r.Body["chat_id"] == chatId)
                .Select(r => r.Body)
                .ToList();
    }
}

using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services;
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
    }

    [LocalDatabaseFact]
    public async Task Brief_is_not_sent_twice_on_the_next_run()
    {
        var (userId, chatId) = await TelegramUserAsync(s => { s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); });

        await RunAsync(Now);
        await RunAsync(Now.AddMinutes(5));

        Assert.Single(await InboxAsync(userId));
        Assert.Single(Sent(chatId));
    }

    [LocalDatabaseFact]
    public async Task Evening_summary_reaches_telegram()
    {
        var (userId, chatId) = await TelegramUserAsync(s => { s.EveningSummaryEnabled = true; s.EveningSummaryTime = LocalTime(Now); });

        await RunAsync(Now);

        var item = Assert.Single(await InboxAsync(userId));
        Assert.Equal(NotificationInboxKind.EveningSummary, item.Kind);
        Assert.Equal($"<b>Podsumowanie dnia</b>\n{WebUtility.HtmlEncode(item.Body)}", (string)Assert.Single(Sent(chatId))["text"]!);
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

    private async Task<(Guid UserId, long ChatId)> TelegramUserAsync(Action<NotificationSettings> configure)
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

    private List<JsonObject> Sent(long chatId)
    {
        lock (factory.Telegram.Requests)
            return factory.Telegram.Requests
                .Where(r => r.Method == "sendMessage" && (long?)r.Body["chat_id"] == chatId)
                .Select(r => r.Body)
                .ToList();
    }
}

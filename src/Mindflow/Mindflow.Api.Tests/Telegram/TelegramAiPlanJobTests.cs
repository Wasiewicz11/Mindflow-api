using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramAiPlanJobTests(TelegramAiApiFactory factory) : IClassFixture<TelegramAiApiFactory>
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static readonly DateTimeOffset Now = new(DateTime.UtcNow.Date.AddHours(1).AddMinutes(10), TimeSpan.Zero);
    private static long chatSeed = Random.Shared.NextInt64(100_000_000_000, 200_000_000_000);

    [LocalDatabaseFact]
    public async Task Plan_and_suggestions_follow_the_morning_brief_once_a_day()
    {
        var (userId, chatId) = await TelegramUserAsync();
        await SeedTaskAsync(userId, "Raport kwartalny", LocalDate(Now).AddDays(-3), TaskPriority.P2);
        factory.Ai.Respond((_, body) => body.Contains("dayStart")
            ? Gemini("""{"summary":"Dzien na domkniecie raportu.","timeline":[{"start":"08:00","end":"09:30","title":"#1","note":"Najpierw zalegle <pilne>."},{"start":"09:30","end":"09:45","title":"Przerwa","note":""}],"focus":["Domknac #1"],"risks":[],"tip":"Wylacz powiadomienia."}""")
            : Suggestions("""[{"title":"Domknij raport","body":"Raport <Q4> jest po terminie.","actions":[{"taskRef":1,"actionType":"ChangePriority","payload":{"priority":"P1"}}]}]"""));
        var before = factory.Ai.Requests.Count;

        await RunAsync(Now);
        await RunAsync(Now.AddMinutes(5));

        var sent = Sent(chatId);
        Assert.Equal(3, sent.Count);
        Assert.StartsWith("<b>Poranny brief</b>", (string)sent[0]["text"]!);
        var day = (string)sent[1]["text"]!;
        Assert.StartsWith("<b>🧠 Plan dnia od AI · ", day);
        Assert.Contains("Dzien na domkniecie raportu.", day);
        Assert.Contains("<code>08:00–09:30</code> Raport kwartalny\n<i>Najpierw zalegle &lt;pilne&gt;.</i>", day);
        Assert.Contains("<code>09:30–09:45</code> Przerwa", day);
        Assert.Contains("<b>🎯 Najważniejsze</b>\n• Domknac Raport kwartalny", day);
        Assert.DoesNotContain("⚠️", day);
        Assert.EndsWith("<b>💡 Wskazówka</b> Wylacz powiadomienia.", day);
        Assert.Equal("https://app.test/", (string)sent[1]["reply_markup"]!["inline_keyboard"]![0]![0]!["url"]!);
        Assert.Equal("<b>Domknij raport</b>\nRaport &lt;Q4&gt; jest po terminie.", (string)sent[2]["text"]!);
        var suggestion = await factory.WithDbAsync(db => db.AiSuggestions.AsNoTracking()
            .SingleAsync(s => s.UserId == userId && s.Status == SuggestionStatus.Pending));
        var row = sent[2]["reply_markup"]!["inline_keyboard"]![0]!.AsArray();
        Assert.Equal(("Przyjmij", $"sg:ok:{suggestion.Id:N}"), ((string)row[0]!["text"]!, (string)row[0]!["callback_data"]!));
        Assert.Equal(("Odrzuć", $"sg:no:{suggestion.Id:N}"), ((string)row[1]!["text"]!, (string)row[1]!["callback_data"]!));
        Assert.Equal(2, factory.Ai.Requests.Count - before);
        Assert.True(await DeliveredAsync(userId, $"telegram-ai-plan:{LocalDate(Now):yyyy-MM-dd}"));
    }

    [LocalDatabaseFact]
    public async Task Day_plan_is_sent_when_ai_suggests_nothing()
    {
        var (userId, chatId) = await TelegramUserAsync();
        await SeedTaskAsync(userId, "Spokojne zadanie", LocalDate(Now).AddDays(5), TaskPriority.P3);
        factory.Ai.Respond((_, body) => body.Contains("dayStart")
            ? Gemini("""{"summary":"Spokojny dzien.","timeline":[],"focus":[],"risks":[],"tip":""}""")
            : Suggestions("[]"));

        await RunAsync(Now);

        var sent = Sent(chatId);
        Assert.Equal(2, sent.Count);
        Assert.StartsWith("<b>🧠 Plan dnia od AI · ", (string)sent[1]["text"]!);
        Assert.True(await DeliveredAsync(userId, $"telegram-ai-plan:{LocalDate(Now):yyyy-MM-dd}"));
    }

    [LocalDatabaseFact]
    public async Task Ai_failure_sends_a_local_plan_without_suggestions()
    {
        var (userId, chatId) = await TelegramUserAsync();
        await SeedTaskAsync(userId, "Raport kwartalny", LocalDate(Now).AddDays(-3), TaskPriority.P2);
        factory.Ai.Respond((_, _) => FakeAiHandler.Json("""{"error":{"message":"boom"}}""", HttpStatusCode.InternalServerError));

        await RunAsync(Now);

        var sent = Sent(chatId);
        Assert.Equal(2, sent.Count);
        var day = (string)sent[1]["text"]!;
        Assert.StartsWith("<b>🧠 Plan dnia · ", day);
        Assert.Contains("Raport kwartalny", day);
        Assert.Contains("<b>⚠️ Uwaga</b>", day);
        Assert.True(await factory.WithDbAsync(db => db.AiSuggestions.AnyAsync(s => s.UserId == userId && s.Provider == "RuleBased")));
    }

    [LocalDatabaseFact]
    public async Task No_plan_and_no_ai_call_when_telegram_notifications_are_off()
    {
        var (userId, chatId) = await TelegramUserAsync(telegramEnabled: false);
        await SeedTaskAsync(userId, "Raport kwartalny", LocalDate(Now).AddDays(-3), TaskPriority.P2);
        var before = factory.Ai.Requests.Count;

        await RunAsync(Now);

        Assert.Empty(Sent(chatId));
        Assert.Equal(before, factory.Ai.Requests.Count);
        Assert.False(await DeliveredAsync(userId, $"telegram-ai-plan:{LocalDate(Now):yyyy-MM-dd}"));
    }

    private static HttpResponseMessage Gemini(string json) =>
        FakeAiHandler.Json(JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = json } } } } }
        }));

    private static HttpResponseMessage Suggestions(string suggestionsJson) =>
        FakeAiHandler.Json(JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = $$"""{"suggestions":{{suggestionsJson}}}""" } } } } }
        }));

    private static TimeOnly LocalTime(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Warsaw);
        return new TimeOnly(local.Hour, local.Minute);
    }

    private static DateOnly LocalDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Warsaw).DateTime);

    private async Task RunAsync(DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationService>().ProcessDueNotificationsAsync(now);
    }

    private async Task<(Guid UserId, long ChatId)> TelegramUserAsync(bool telegramEnabled = true)
    {
        var user = await factory.CreateUserAsync();
        var chatId = Interlocked.Increment(ref chatSeed);
        await factory.WithDbAsync(async db =>
        {
            db.NotificationSettings.Add(new NotificationSettings
            {
                UserId = user.Id,
                MorningBriefEnabled = true,
                MorningBriefTime = LocalTime(Now),
                MiddayBriefEnabled = false,
                EveningSummaryEnabled = false,
                BlockRemindersEnabled = false,
                TelegramRemindersEnabled = telegramEnabled,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            db.TelegramConnections.Add(new TelegramConnection { UserId = user.Id, ChatId = chatId, ConnectedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        return (user.Id, chatId);
    }

    private Task SeedTaskAsync(Guid userId, string content, DateOnly dueDate, TaskPriority priority) =>
        factory.WithDbAsync(async db =>
        {
            db.Tasks.Add(new TaskItem { Id = Guid.NewGuid(), UserId = userId, Content = content, DueDate = dueDate, Priority = priority, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });

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

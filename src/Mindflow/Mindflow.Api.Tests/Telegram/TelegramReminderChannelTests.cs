using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Services.Reminders;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramReminderChannelTests(TelegramApiFactory factory) : IClassFixture<TelegramApiFactory>
{
    private static long chatSeed = Random.Shared.NextInt64(100_000_000, 900_000_000);

    [LocalDatabaseFact]
    public async Task Sends_message_with_buttons_and_link()
    {
        var (message, chatId) = await ConnectedAsync("https://app.test/zadania/1");

        Assert.True(await SendAsync(message));

        var body = Sent(chatId).Single();
        var text = (string)body["text"]!;
        Assert.StartsWith("<b>⏰ Przypomnienie</b>", text);
        Assert.Contains("A &lt;b&gt; &amp; C", text);
        var rows = body["reply_markup"]!["inline_keyboard"]!.AsArray();
        Assert.Equal($"done:{message.TaskId:N}", (string)rows[0]![0]!["callback_data"]!);
        Assert.Equal($"snooze:{message.TaskId:N}", (string)rows[0]![1]!["callback_data"]!);
        Assert.Equal("https://app.test/zadania/1", (string)rows[1]![0]!["url"]!);
    }

    [LocalDatabaseFact]
    public async Task Omits_link_row_for_non_https_url()
    {
        var (message, chatId) = await ConnectedAsync("http://localhost:4200/zadania/1");

        Assert.True(await SendAsync(message));

        var rows = Sent(chatId).Single()["reply_markup"]!["inline_keyboard"]!.AsArray();
        Assert.Single(rows);
    }

    [LocalDatabaseFact]
    public async Task Skips_when_disabled_in_settings()
    {
        var (message, chatId) = await ConnectedAsync("https://app.test/zadania/1");
        await factory.WithDbAsync(async db =>
        {
            db.NotificationSettings.Add(new NotificationSettings { UserId = message.UserId, TelegramRemindersEnabled = false });
            await db.SaveChangesAsync();
        });

        Assert.False(await SendAsync(message));
        Assert.Empty(Sent(chatId));
    }

    [LocalDatabaseFact]
    public async Task Skips_when_chat_is_not_connected()
    {
        var user = await factory.CreateUserAsync();

        Assert.False(await SendAsync(Message(user.Id, "https://app.test/zadania/1")));
    }

    [LocalDatabaseFact]
    public async Task Stops_sending_in_the_same_scope_after_a_failed_send()
    {
        var (first, firstChat) = await ConnectedAsync("https://app.test/zadania/1");
        var (second, secondChat) = await ConnectedAsync("https://app.test/zadania/2");
        lock (factory.Telegram.Requests) factory.Telegram.FailingMethods.Add("sendMessage");
        try
        {
            using var scope = factory.Services.CreateScope();
            var channel = scope.ServiceProvider.GetServices<IReminderChannel>().Single(c => c.Name == "telegram");

            Assert.False(await channel.SendAsync(first, CancellationToken.None));
            Assert.False(await channel.SendAsync(second, CancellationToken.None));
        }
        finally
        {
            factory.Telegram.FailingMethods.Remove("sendMessage");
        }

        Assert.Single(Sent(firstChat));
        Assert.Empty(Sent(secondChat));
    }

    private async Task<bool> SendAsync(ReminderMessage message)
    {
        using var scope = factory.Services.CreateScope();
        var channel = scope.ServiceProvider.GetServices<IReminderChannel>().Single(c => c.Name == "telegram");
        return await channel.SendAsync(message, CancellationToken.None);
    }

    private async Task<(ReminderMessage Message, long ChatId)> ConnectedAsync(string taskUrl)
    {
        var user = await factory.CreateUserAsync();
        var chatId = Interlocked.Increment(ref chatSeed);
        await factory.WithDbAsync(async db =>
        {
            db.TelegramConnections.Add(new TelegramConnection { UserId = user.Id, ChatId = chatId, ConnectedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        return (Message(user.Id, taskUrl), chatId);
    }

    private static ReminderMessage Message(Guid userId, string taskUrl) =>
        new(Guid.NewGuid(), userId, Guid.NewGuid(), "⏰ A <b> & C", "Termin: dziś 10:00", "dziś 10:00", DateTimeOffset.UtcNow, taskUrl, "token");

    private List<JsonObject> Sent(long chatId)
    {
        lock (factory.Telegram.Requests)
            return factory.Telegram.Requests
                .Where(r => r.Method == "sendMessage" && (long)r.Body["chat_id"]! == chatId)
                .Select(r => r.Body).ToList();
    }
}

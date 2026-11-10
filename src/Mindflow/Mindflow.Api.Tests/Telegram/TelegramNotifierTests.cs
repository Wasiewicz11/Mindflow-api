using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Services.Telegram;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramNotifierTests(TelegramApiFactory factory) : IClassFixture<TelegramApiFactory>
{
    private static long chatSeed = Random.Shared.NextInt64(950_000_000, 990_000_000);

    [LocalDatabaseFact]
    public async Task Sends_bold_title_encoded_body_and_open_button()
    {
        var (userId, chatId) = await ConnectedAsync();

        var sent = await SendAsync(factory, userId, "Poranny <brief>", "A & B", "/?notification=abc");

        Assert.True(sent);
        var body = Assert.Single(Sent(chatId));
        Assert.Equal("<b>Poranny &lt;brief&gt;</b>\nA &amp; B", (string)body["text"]!);
        var button = body["reply_markup"]!["inline_keyboard"]![0]![0]!;
        Assert.Equal("Otwórz Mindflow", (string)button["text"]!);
        Assert.Equal("https://app.test/?notification=abc", (string)button["url"]!);
    }

    [LocalDatabaseFact]
    public async Task Omits_open_button_when_frontend_is_not_https()
    {
        var (userId, chatId) = await ConnectedAsync();
        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:FrontendUrl"] = "http://localhost:4200"
            })));

        Assert.True(await SendAsync(app, userId, "Podsumowanie dnia", "Ukończone dziś: 1.", "/?notification=abc"));

        Assert.Null(Assert.Single(Sent(chatId))["reply_markup"]);
    }

    [LocalDatabaseFact]
    public async Task Skips_user_with_telegram_notifications_off()
    {
        var (userId, chatId) = await ConnectedAsync();
        await factory.WithDbAsync(async db =>
        {
            db.NotificationSettings.Add(new NotificationSettings { UserId = userId, TelegramRemindersEnabled = false, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });

        Assert.False(await SendAsync(factory, userId, "Poranny brief", "x", "/"));
        Assert.Empty(Sent(chatId));
    }

    [LocalDatabaseFact]
    public async Task Skips_user_without_connected_chat()
    {
        var user = await factory.CreateUserAsync();

        using var scope = factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();

        Assert.Null(await notifier.FindChatAsync(user.Id, CancellationToken.None));
        Assert.False(await notifier.SendNotificationAsync(user.Id, "Poranny brief", "x", "/", CancellationToken.None));
    }

    private static async Task<bool> SendAsync(WebApplicationFactory<Program> app, Guid userId, string title, string body, string path)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITelegramNotifier>()
            .SendNotificationAsync(userId, title, body, path, CancellationToken.None);
    }

    private async Task<(Guid UserId, long ChatId)> ConnectedAsync()
    {
        var user = await factory.CreateUserAsync();
        var chatId = Interlocked.Increment(ref chatSeed);
        await factory.WithDbAsync(async db =>
        {
            db.TelegramConnections.Add(new TelegramConnection { UserId = user.Id, ChatId = chatId, ConnectedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        return (user.Id, chatId);
    }

    private List<JsonObject> Sent(long chatId)
    {
        lock (factory.Telegram.Requests)
            return factory.Telegram.Requests
                .Where(r => r.Method == "sendMessage" && (long?)r.Body["chat_id"] == chatId)
                .Select(r => r.Body)
                .ToList();
    }
}

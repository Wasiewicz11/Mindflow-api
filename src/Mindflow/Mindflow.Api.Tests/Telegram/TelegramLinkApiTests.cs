using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Services.Reminders;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramLinkApiTests(TelegramApiFactory factory) : IClassFixture<TelegramApiFactory>
{
    private static long chatSeed = Random.Shared.NextInt64(1_000_000, 9_000_000);

    [LocalDatabaseFact]
    public async Task Start_with_link_code_connects_the_chat()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var chatId = Interlocked.Increment(ref chatSeed);

        var link = await (await client.PostAsync("/integrations/telegram/link", null)).Content.ReadFromJsonAsync<TelegramLinkResponse>(MindflowApiFactory.Json);
        Assert.StartsWith("https://t.me/mindflow_test_bot?start=", link!.Url);
        var code = link.Url.Split("start=")[1];

        Assert.Equal(HttpStatusCode.OK, (await Webhook(Start(chatId, $"/start {code}"))).StatusCode);

        var status = await client.GetFromJsonAsync<TelegramStatusResponse>("/integrations/telegram", MindflowApiFactory.Json);
        Assert.Equal((true, true, "filip"), (status!.Configured, status.Connected, status.Username));
        Assert.Contains(factory.Telegram.Requests, r => r.Method == "sendMessage" && (long)r.Body["chat_id"]! == chatId && ((string)r.Body["text"]!).StartsWith("✅ Połączono"));
    }

    [LocalDatabaseFact]
    public async Task Wrong_or_used_code_does_not_connect()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var chatId = Interlocked.Increment(ref chatSeed);

        await Webhook(Start(chatId, "/start not-a-code"));

        var status = await client.GetFromJsonAsync<TelegramStatusResponse>("/integrations/telegram", MindflowApiFactory.Json);
        Assert.False(status!.Connected);
        Assert.Contains(factory.Telegram.Requests, r => (long?)r.Body["chat_id"] == chatId && ((string)r.Body["text"]!).StartsWith("Ten link wygasł"));
    }

    [LocalDatabaseFact]
    public async Task Stop_and_delete_disconnect()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var chatId = Interlocked.Increment(ref chatSeed);
        var link = await (await client.PostAsync("/integrations/telegram/link", null)).Content.ReadFromJsonAsync<TelegramLinkResponse>(MindflowApiFactory.Json);
        await Webhook(Start(chatId, $"/start {link!.Url.Split("start=")[1]}"));

        await Webhook(Start(chatId, "/stop"));

        var status = await client.GetFromJsonAsync<TelegramStatusResponse>("/integrations/telegram", MindflowApiFactory.Json);
        Assert.False(status!.Connected);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/integrations/telegram")).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Webhook_rejects_wrong_secret()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/integrations/telegram/webhook") { Content = JsonContent.Create(Start(1, "/stop")) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "nope");
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(request)).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Expired_code_does_not_connect()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var chatId = Interlocked.Increment(ref chatSeed);
        var code = await LinkCodeAsync(client);
        await factory.WithDbAsync(async db =>
        {
            var row = await db.TelegramConnections.SingleAsync(c => c.UserId == user.Id);
            row.LinkCodeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });

        await Webhook(Start(chatId, $"/start {code}"));

        Assert.False((await Status(client)).Connected);
        Assert.Contains(factory.Telegram.Requests, r => (long?)r.Body["chat_id"] == chatId && ((string)r.Body["text"]!).StartsWith("Ten link wygasł"));
    }

    [LocalDatabaseFact]
    public async Task Reused_code_does_not_connect_a_second_chat()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var firstChat = Interlocked.Increment(ref chatSeed);
        var secondChat = Interlocked.Increment(ref chatSeed);
        var code = await LinkCodeAsync(client);
        await Webhook(Start(firstChat, $"/start {code}"));

        await Webhook(Start(secondChat, $"/start {code}"));

        var chatId = await factory.WithDbAsync(db => db.TelegramConnections.Where(c => c.UserId == user.Id).Select(c => c.ChatId).SingleAsync());
        Assert.Equal(firstChat, chatId);
        Assert.Contains(factory.Telegram.Requests, r => (long?)r.Body["chat_id"] == secondChat && ((string)r.Body["text"]!).StartsWith("Ten link wygasł"));
    }

    [LocalDatabaseFact]
    public async Task Linking_a_chat_to_another_user_moves_it()
    {
        var userA = await factory.CreateUserAsync();
        var userB = await factory.CreateUserAsync();
        var clientA = factory.CreateUserClient(userA.Id);
        var clientB = factory.CreateUserClient(userB.Id);
        var chatId = Interlocked.Increment(ref chatSeed);
        await Webhook(Start(chatId, $"/start {await LinkCodeAsync(clientA)}"));

        var response = await Webhook(Start(chatId, $"/start {await LinkCodeAsync(clientB)}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await Status(clientA)).Connected);
        Assert.True((await Status(clientB)).Connected);
    }

    [LocalDatabaseFact]
    public async Task Webhook_rejects_missing_secret_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/integrations/telegram/webhook") { Content = JsonContent.Create(Start(1, "/stop")) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(request)).StatusCode);
    }

    private static async Task<string> LinkCodeAsync(HttpClient client)
    {
        var link = await (await client.PostAsync("/integrations/telegram/link", null)).Content.ReadFromJsonAsync<TelegramLinkResponse>(MindflowApiFactory.Json);
        return link!.Url.Split("start=")[1];
    }

    private static async Task<TelegramStatusResponse> Status(HttpClient client) =>
        (await client.GetFromJsonAsync<TelegramStatusResponse>("/integrations/telegram", MindflowApiFactory.Json))!;

    private static object Start(long chatId, string text) => new
    {
        update_id = 1,
        message = new { message_id = 1, chat = new { id = chatId }, from = new { id = chatId, username = "filip" }, text }
    };

    private Task<HttpResponseMessage> Webhook(object update)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/integrations/telegram/webhook") { Content = JsonContent.Create(update) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "hook-secret");
        return factory.CreateClient().SendAsync(request);
    }
}

public class TelegramWithoutWebhookSecretFactory : TelegramApiFactory
{
    protected override IReadOnlyDictionary<string, string> Settings =>
        base.Settings.Where(pair => pair.Key != "Telegram:WebhookSecret").ToDictionary(pair => pair.Key, pair => pair.Value);
}

public class TelegramWithoutWebhookSecretTests(TelegramWithoutWebhookSecretFactory factory) : IClassFixture<TelegramWithoutWebhookSecretFactory>
{
    [LocalDatabaseFact]
    public async Task Webhook_is_rejected_when_secret_is_not_configured()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/integrations/telegram/webhook") { Content = JsonContent.Create(new { update_id = 1 }) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "");
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(request)).StatusCode);
        Assert.Empty(factory.Telegram.Requests);
    }
}

public class TelegramNotConfiguredTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    [LocalDatabaseFact]
    public async Task Status_reports_not_configured_and_link_is_rejected()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);

        var status = await client.GetFromJsonAsync<TelegramStatusResponse>("/integrations/telegram", MindflowApiFactory.Json);
        Assert.False(status!.Configured);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/integrations/telegram/link", null)).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Reminder_channel_does_not_send()
    {
        var user = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.TelegramConnections.Add(new TelegramConnection { UserId = user.Id, ChatId = 4242, ConnectedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        using var scope = factory.Services.CreateScope();
        var channel = scope.ServiceProvider.GetServices<IReminderChannel>().Single(c => c.Name == "telegram");
        var message = new ReminderMessage(Guid.NewGuid(), user.Id, Guid.NewGuid(), "A", "B", "dziś", DateTimeOffset.UtcNow, "https://app.test/1", "token");

        Assert.False(await channel.SendAsync(message, CancellationToken.None));
    }
}

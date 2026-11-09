using System.Net;
using System.Net.Http.Json;
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

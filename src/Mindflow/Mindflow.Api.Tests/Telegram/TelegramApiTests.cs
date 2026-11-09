using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mindflow.Api.Services.Telegram;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramApiTests
{
    private static (TelegramApi Api, FakeTelegramHandler Handler) Create(bool configured = true)
    {
        var handler = new FakeTelegramHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") };
        var values = configured
            ? new Dictionary<string, string?> { ["Telegram:BotToken"] = "123:abc", ["Telegram:BotUsername"] = "mindflow_test_bot" }
            : new Dictionary<string, string?>();
        return (new TelegramApi(http, new ConfigurationBuilder().AddInMemoryCollection(values).Build(), NullLogger<TelegramApi>.Instance), handler);
    }

    [Fact]
    public async Task Sends_html_message_with_inline_keyboard()
    {
        var (api, handler) = Create();

        var ok = await api.SendMessageAsync(42, "<b>Hi</b>", [[new TelegramButton("✅ Zrobione", CallbackData: "done:abc")]], CancellationToken.None);

        Assert.True(ok);
        var (method, body) = Assert.Single(handler.Requests);
        Assert.Equal("sendMessage", method);
        Assert.Equal("https://api.telegram.org/bot123:abc/sendMessage", handler.Uris.Single().ToString());
        Assert.Equal(42, (long)body["chat_id"]!);
        Assert.Equal("HTML", (string)body["parse_mode"]!);
        Assert.Equal("done:abc", (string)body["reply_markup"]!["inline_keyboard"]![0]![0]!["callback_data"]!);
        Assert.Null(body["reply_markup"]!["inline_keyboard"]![0]![0]!["url"]);
    }

    [Fact]
    public async Task Parses_updates()
    {
        var (api, handler) = Create();
        handler.UpdatesResults.Enqueue("""[{"update_id":7,"message":{"message_id":1,"chat":{"id":42},"from":{"id":42,"username":"filip"},"text":"/start abc"}}]""");

        var updates = await api.GetUpdatesAsync(0, 0, CancellationToken.None);

        var update = Assert.Single(updates);
        Assert.Equal((7L, 42L, "/start abc"), (update.UpdateId, update.Message!.Chat.Id, update.Message.Text));
    }

    [Fact]
    public async Task Does_nothing_without_configuration()
    {
        var (api, handler) = Create(configured: false);
        Assert.False(await api.SendMessageAsync(1, "x", null, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Returns_false_on_non_success_status()
    {
        var (api, handler) = Create();
        handler.Status = System.Net.HttpStatusCode.BadGateway;
        Assert.False(await api.SendMessageAsync(1, "x", null, CancellationToken.None));
    }

    [Fact]
    public async Task Returns_false_on_network_error_and_empty_updates()
    {
        var (api, handler) = Create();
        handler.Throw = new HttpRequestException("boom");
        Assert.False(await api.SendMessageAsync(1, "x", null, CancellationToken.None));
        Assert.Null(await api.GetUpdatesAsync(0, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Propagates_caller_cancellation()
    {
        var (api, _) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.SendMessageAsync(1, "x", null, cts.Token));
    }
}

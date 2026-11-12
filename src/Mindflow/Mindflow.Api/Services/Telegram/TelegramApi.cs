using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mindflow.Api.Services.Telegram;

public class TelegramApi(HttpClient http, IConfiguration configuration, ILogger<TelegramApi> logger)
{
    public async Task<bool> SendMessageAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, CancellationToken ct) =>
        await TrySendMessageAsync(chatId, html, keyboard, ct) == TelegramSendStatus.Sent;

    public async Task<TelegramSendStatus> TrySendMessageAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
            ["disable_web_page_preview"] = true
        };
        if (keyboard is { Count: > 0 })
            body["reply_markup"] = new
            {
                inline_keyboard = keyboard.Select(row => row.Select(b => new { text = b.Text, callback_data = b.CallbackData, url = b.Url }))
            };
        var response = await CallAsync<JsonElement>("sendMessage", body, ct);
        if (response.Ok) return TelegramSendStatus.Sent;
        return response.ChatRejected ? TelegramSendStatus.ChatRejected : TelegramSendStatus.Unavailable;
    }

    public async Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, string text, CancellationToken ct) =>
        (await CallAsync<bool>("answerCallbackQuery", new { callback_query_id = callbackQueryId, text }, ct)).Ok;

    public async Task<IReadOnlyList<TelegramUpdate>?> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct)
    {
        var response = await CallAsync<List<TelegramUpdate>>("getUpdates",
            new { offset, timeout = timeoutSeconds, allowed_updates = new[] { "message", "callback_query" } }, ct);
        return response.Ok && response.Result is not null ? response.Result : null;
    }

    public async Task<bool> DeleteWebhookAsync(CancellationToken ct) =>
        (await CallAsync<bool>("deleteWebhook", new { }, ct)).Ok;

    private async Task<(bool Ok, T? Result, bool ChatRejected)> CallAsync<T>(string method, object body, CancellationToken ct)
    {
        var options = TelegramOptions.From(configuration);
        if (options is null) return (false, default, false);

        try
        {
            using var response = await http.PostAsJsonAsync($"/bot{options.BotToken}/{method}", body, TelegramJson.Options, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Telegram {Method} failed with status {Status}", method, (int)response.StatusCode);
                return (false, default, response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden);
            }

            var payload = await response.Content.ReadFromJsonAsync<TelegramResponse<T>>(TelegramJson.Options, ct);
            return (payload?.Ok == true, payload is null ? default : payload.Result, false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Telegram {Method} failed with {ExceptionType}", method, ex.GetType().Name);
            return (false, default, false);
        }
    }

    private record TelegramResponse<T>(bool Ok, T? Result);
}

public enum TelegramSendStatus
{
    Sent,
    ChatRejected,
    Unavailable
}

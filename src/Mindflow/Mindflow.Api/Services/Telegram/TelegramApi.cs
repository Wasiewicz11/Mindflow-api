using System.Net.Http.Json;
using System.Text.Json;

namespace Mindflow.Api.Services.Telegram;

public class TelegramApi(HttpClient http, IConfiguration configuration, ILogger<TelegramApi> logger)
{
    public async Task<bool> SendMessageAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, CancellationToken ct)
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
        return (await CallAsync<bool>("sendMessage", body, ct)).Ok;
    }

    public async Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, string text, CancellationToken ct) =>
        (await CallAsync<bool>("answerCallbackQuery", new { callback_query_id = callbackQueryId, text }, ct)).Ok;

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct)
    {
        var response = await CallAsync<List<TelegramUpdate>>("getUpdates",
            new { offset, timeout = timeoutSeconds, allowed_updates = new[] { "message", "callback_query" } }, ct);
        return response.Ok && response.Result is not null ? response.Result : [];
    }

    public async Task<bool> DeleteWebhookAsync(CancellationToken ct) =>
        (await CallAsync<bool>("deleteWebhook", new { }, ct)).Ok;

    private async Task<(bool Ok, T? Result)> CallAsync<T>(string method, object body, CancellationToken ct)
    {
        var options = TelegramOptions.From(configuration);
        if (options is null) return (false, default);

        try
        {
            using var response = await http.PostAsJsonAsync($"bot{options.BotToken}/{method}", body, TelegramJson.Options, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Telegram {Method} failed with status {Status}", method, (int)response.StatusCode);
                return (false, default);
            }

            var payload = await response.Content.ReadFromJsonAsync<TelegramResponse<T>>(TelegramJson.Options, ct);
            return (payload?.Ok == true, payload is null ? default : payload.Result);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Telegram {Method} failed with {ExceptionType}", method, ex.GetType().Name);
            return (false, default);
        }
    }

    private record TelegramResponse<T>(bool Ok, T? Result);
}

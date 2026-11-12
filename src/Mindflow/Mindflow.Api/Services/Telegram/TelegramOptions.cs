using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mindflow.Api.Services.Telegram;

public record TelegramOptions(string BotToken, string BotUsername, string? WebhookSecret, bool UsePolling)
{
    public static TelegramOptions? From(IConfiguration configuration)
    {
        var token = configuration["Telegram:BotToken"];
        var username = configuration["Telegram:BotUsername"]?.TrimStart('@');
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(username)) return null;
        return new TelegramOptions(token, username, configuration["Telegram:WebhookSecret"],
            bool.TryParse(configuration["Telegram:UsePolling"], out var polling) && polling);
    }
}

public static class TelegramJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

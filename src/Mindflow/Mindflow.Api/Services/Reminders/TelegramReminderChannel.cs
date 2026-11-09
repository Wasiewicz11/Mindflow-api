using System.Net;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Services.Telegram;

namespace Mindflow.Api.Services.Reminders;

public class TelegramReminderChannel(MindflowDbContext db, TelegramApi api, IConfiguration configuration) : IReminderChannel
{
    private const string TitlePrefix = "⏰ ";

    public string Name => "telegram";

    public async Task<bool> SendAsync(ReminderMessage message, CancellationToken ct)
    {
        if (TelegramOptions.From(configuration) is null) return false;

        var enabled = await db.NotificationSettings.AsNoTracking()
            .Where(settings => settings.UserId == message.UserId)
            .Select(settings => (bool?)settings.TelegramRemindersEnabled)
            .SingleOrDefaultAsync(ct) ?? true;
        if (!enabled) return false;

        var chatId = await db.TelegramConnections.AsNoTracking()
            .Where(connection => connection.UserId == message.UserId)
            .Select(connection => connection.ChatId)
            .SingleOrDefaultAsync(ct);
        if (chatId is null) return false;

        var title = message.Title.StartsWith(TitlePrefix, StringComparison.Ordinal) ? message.Title[TitlePrefix.Length..] : message.Title;
        var html = $"<b>⏰ Przypomnienie</b>\n<b>{WebUtility.HtmlEncode(title)}</b>\n{WebUtility.HtmlEncode(message.Body)}";

        var keyboard = new List<IReadOnlyList<TelegramButton>>
        {
            new[]
            {
                new TelegramButton("✅ Zrobione", CallbackData: $"done:{message.TaskId:N}"),
                new TelegramButton("⏰ Odłóż 10 min", CallbackData: $"snooze:{message.TaskId:N}")
            }
        };
        if (message.TaskUrl.StartsWith("https://", StringComparison.Ordinal))
            keyboard.Add([new TelegramButton("Otwórz zadanie →", Url: message.TaskUrl)]);

        return await api.SendMessageAsync(chatId.Value, html, keyboard, ct);
    }
}

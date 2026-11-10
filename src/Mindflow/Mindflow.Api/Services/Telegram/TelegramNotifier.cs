using System.Net;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Services.Telegram;

public interface ITelegramNotifier
{
    Task<long?> FindChatAsync(Guid userId, CancellationToken ct);
    Task<bool> SendAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, CancellationToken ct);
    Task<bool> SendNotificationAsync(Guid userId, string title, string body, string appPath, CancellationToken ct);
}

public class TelegramNotifier(
    MindflowDbContext db,
    TelegramApi api,
    IConfiguration configuration,
    ILogger<TelegramNotifier> logger) : ITelegramNotifier
{
    private const string OpenButtonText = "Otwórz Mindflow";
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private bool telegramUnavailable;

    public async Task<long?> FindChatAsync(Guid userId, CancellationToken ct)
    {
        if (TelegramOptions.From(configuration) is null) return null;

        var enabled = await db.NotificationSettings.AsNoTracking()
            .Where(settings => settings.UserId == userId)
            .Select(settings => (bool?)settings.TelegramRemindersEnabled)
            .SingleOrDefaultAsync(ct) ?? true;
        if (!enabled) return null;

        return await db.TelegramConnections.AsNoTracking()
            .Where(connection => connection.UserId == userId)
            .Select(connection => connection.ChatId)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<bool> SendAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, CancellationToken ct)
    {
        if (telegramUnavailable) return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SendTimeout);
        try
        {
            var sent = await api.SendMessageAsync(chatId, html, keyboard, timeout.Token);
            if (!sent) telegramUnavailable = true;
            return sent;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Telegram notification timed out");
            telegramUnavailable = true;
            return false;
        }
    }

    public async Task<bool> SendNotificationAsync(Guid userId, string title, string body, string appPath, CancellationToken ct)
    {
        if (await FindChatAsync(userId, ct) is not { } chatId) return false;

        var html = $"<b>{WebUtility.HtmlEncode(title)}</b>\n{WebUtility.HtmlEncode(body)}";
        var url = $"{ReminderLinks.FrontendUrl(configuration)}{appPath}";
        IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard = null;
        if (url.StartsWith("https://", StringComparison.Ordinal))
            keyboard = [[new TelegramButton(OpenButtonText, Url: url)]];

        return await SendAsync(chatId, html, keyboard, ct);
    }
}

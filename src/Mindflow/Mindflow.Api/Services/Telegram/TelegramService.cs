using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Services.Telegram;

public interface ITelegramService
{
    Task<TelegramStatusResponse> GetStatusAsync(Guid userId, CancellationToken ct);
    Task<TelegramLinkResponse> CreateLinkAsync(Guid userId, CancellationToken ct);
    Task DisconnectAsync(Guid userId, CancellationToken ct);
    Task HandleUpdateAsync(TelegramUpdate update, CancellationToken ct);
}

public class TelegramService(MindflowDbContext db, TelegramApi api, IReminderActionService reminderActions, IConfiguration configuration) : ITelegramService
{
    private static readonly TimeSpan LinkCodeLifetime = TimeSpan.FromMinutes(15);

    private const string ConnectedText = "✅ Połączono z Mindflow. Tutaj będą przychodzić przypomnienia o zadaniach.";
    private const string ExpiredText = "Ten link wygasł. Wygeneruj nowy w Mindflow: Ustawienia → Powiadomienia → Telegram.";
    private const string HelpText = "Połącz konto w Mindflow: Ustawienia → Powiadomienia → Telegram.";
    private const string NotLinkedText = "Najpierw połącz konto w Mindflow";
    private const string TaskNotFoundText = "Nie znaleziono zadania";
    private const string DoneAnswerText = "✅ Ukończono";
    private const string SnoozeAnswerText = "⏰ Przypomnę za 10 min";
    private const string StoppedText = "Odłączono. Nie będę już wysyłać przypomnień.";

    public async Task<TelegramStatusResponse> GetStatusAsync(Guid userId, CancellationToken ct)
    {
        var row = await db.TelegramConnections.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
        return new TelegramStatusResponse(TelegramOptions.From(configuration) is not null, row?.ChatId is not null, row?.Username);
    }

    public async Task<TelegramLinkResponse> CreateLinkAsync(Guid userId, CancellationToken ct)
    {
        var options = TelegramOptions.From(configuration)
            ?? throw new BadRequestException("Telegram is not configured on the server.");

        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        var row = await db.TelegramConnections.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (row is null)
        {
            row = new TelegramConnection { UserId = userId };
            db.TelegramConnections.Add(row);
        }

        row.LinkCodeHash = HashCode(code);
        row.LinkCodeExpiresAt = DateTimeOffset.UtcNow.Add(LinkCodeLifetime);
        await db.SaveChangesAsync(ct);

        return new TelegramLinkResponse($"https://t.me/{options.BotUsername}?start={code}");
    }

    public async Task DisconnectAsync(Guid userId, CancellationToken ct) =>
        await db.TelegramConnections.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);

    public async Task HandleUpdateAsync(TelegramUpdate update, CancellationToken ct)
    {
        if (update.CallbackQuery is { } callback)
        {
            await HandleCallbackAsync(callback, ct);
            return;
        }

        var message = update.Message;
        if (message?.Text is not { } text) return;

        var chatId = message.Chat.Id;
        var parts = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var command = parts.Length > 0 ? parts[0].Split('@')[0] : string.Empty;

        if (command == "/start" && parts.Length == 2)
            await ConnectAsync(chatId, message.From?.Username, parts[1], ct);
        else if (command == "/stop")
        {
            await db.TelegramConnections.Where(c => c.ChatId == chatId).ExecuteDeleteAsync(ct);
            await api.SendMessageAsync(chatId, StoppedText, null, ct);
        }
        else
            await api.SendMessageAsync(chatId, HelpText, null, ct);
    }

    private async Task HandleCallbackAsync(TelegramCallbackQuery callback, CancellationToken ct)
    {
        var chatId = callback.Message?.Chat.Id ?? callback.From.Id;
        var row = await db.TelegramConnections.AsNoTracking().FirstOrDefaultAsync(c => c.ChatId == chatId, ct);
        if (row is null)
        {
            await api.AnswerCallbackQueryAsync(callback.Id, NotLinkedText, ct);
            return;
        }

        var response = TryParseAction(callback.Data, out var action, out var taskId)
            ? await reminderActions.ApplyAsync(row.UserId, taskId, action, ct)
            : null;
        if (response is null)
        {
            await api.AnswerCallbackQueryAsync(callback.Id, TaskNotFoundText, ct);
            return;
        }

        if (action == ReminderAction.Done)
        {
            await api.AnswerCallbackQueryAsync(callback.Id, DoneAnswerText, ct);
            await api.SendMessageAsync(chatId, $"✅ Ukończono: {WebUtility.HtmlEncode(response.TaskTitle)}", null, ct);
        }
        else
            await api.AnswerCallbackQueryAsync(callback.Id, SnoozeAnswerText, ct);
    }

    private static bool TryParseAction(string? data, out ReminderAction action, out Guid taskId)
    {
        action = default;
        taskId = default;
        var parts = data?.Split(':', 2);
        if (parts is not [var name, var id] || !Guid.TryParseExact(id, "N", out taskId)) return false;
        switch (name)
        {
            case "done": action = ReminderAction.Done; return true;
            case "snooze": action = ReminderAction.Snooze; return true;
            default: return false;
        }
    }

    private async Task ConnectAsync(long chatId, string? username, string code, CancellationToken ct)
    {
        var hash = HashCode(code);
        var now = DateTimeOffset.UtcNow;
        var row = await db.TelegramConnections.FirstOrDefaultAsync(c => c.LinkCodeHash == hash && c.LinkCodeExpiresAt > now, ct);
        if (row is null)
        {
            await api.SendMessageAsync(chatId, ExpiredText, null, ct);
            return;
        }

        await db.TelegramConnections
            .Where(c => c.ChatId == chatId && c.UserId != row.UserId)
            .ExecuteDeleteAsync(ct);

        row.ChatId = chatId;
        row.Username = username;
        row.ConnectedAt = now;
        row.LinkCodeHash = null;
        row.LinkCodeExpiresAt = null;
        await db.SaveChangesAsync(ct);

        await api.SendMessageAsync(chatId, ConnectedText, null, ct);
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}

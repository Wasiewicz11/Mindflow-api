using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Services.Telegram;

public interface ITelegramAiPlan
{
    Task<bool> SendAsync(Guid userId, DateOnly localDate, CancellationToken ct);
    Task HandleCallbackAsync(Guid userId, TelegramCallbackQuery callback, CancellationToken ct);
}

public class TelegramAiPlan(
    MindflowDbContext db,
    ITelegramNotifier notifier,
    IDayPlanWriter dayPlanWriter,
    ISuggestionService suggestions,
    TelegramApi api,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    ILogger<TelegramAiPlan> logger) : ITelegramAiPlan
{
    public const int MaxMessageLength = 4096;
    public const string AcceptedText = "✅ Przyjęto";
    public const string RejectedText = "Odrzucono";
    public const string StaleText = "Ta sugestia jest już nieaktualna.";

    private const int MaxSuggestions = 3;
    private const string LocalProvider = "RuleBased";
    private const string OpenButtonText = "Otwórz Mindflow";

    private static readonly string[] DayNames = ["niedziela", "poniedziałek", "wtorek", "środa", "czwartek", "piątek", "sobota"];
    private static readonly string[] MonthNames =
        ["stycznia", "lutego", "marca", "kwietnia", "maja", "czerwca", "lipca", "sierpnia", "września", "października", "listopada", "grudnia"];

    public async Task<bool> SendAsync(Guid userId, DateOnly localDate, CancellationToken ct)
    {
        if (await notifier.FindChatAsync(userId, ct) is not { } chatId) return false;
        if (!await ClaimAsync(userId, $"telegram-ai-plan:{localDate:yyyy-MM-dd}", ct)) return false;

        var plan = await dayPlanWriter.WriteAsync(userId, localDate, ct);
        if (!await notifier.SendAsync(chatId, FormatPlan(plan, localDate), OpenButton(), ct)) return false;

        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            await suggestions.GenerateForUserAsync(userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AI plan generation failed for user {UserId}.", userId);
            return true;
        }

        var cards = await db.AiSuggestions.AsNoTracking()
            .Where(s => s.UserId == userId
                        && s.Status == SuggestionStatus.Pending
                        && s.CreatedAt >= startedAt
                        && s.Provider != LocalProvider)
            .OrderBy(s => s.CreatedAt)
            .ThenBy(s => s.Title)
            .Take(MaxSuggestions)
            .ToListAsync(ct);
        if (cards.Count == 0)
        {
            logger.LogWarning("AI plan for user {UserId} has no AI suggestions.", userId);
            return true;
        }

        foreach (var suggestion in cards)
            await notifier.SendAsync(chatId, FormatSuggestion(suggestion), Buttons(suggestion.Id), ct);
        return true;
    }

    public async Task HandleCallbackAsync(Guid userId, TelegramCallbackQuery callback, CancellationToken ct)
    {
        var parts = callback.Data?.Split(':');
        if (parts is not ["sg", var verb, var id] || verb is not ("ok" or "no") || !Guid.TryParseExact(id, "N", out var suggestionId))
        {
            await api.AnswerCallbackQueryAsync(callback.Id, StaleText, ct);
            return;
        }

        TelegramActor.ActAs(httpContextAccessor, userId);
        var accept = verb == "ok";
        var decided = accept ? await suggestions.AcceptAsync(suggestionId) : await suggestions.RejectAsync(suggestionId);
        if (!decided)
        {
            await api.AnswerCallbackQueryAsync(callback.Id, StaleText, ct);
            return;
        }

        await api.AnswerCallbackQueryAsync(callback.Id, accept ? AcceptedText : RejectedText, ct);
        if (callback.Message is { } message)
            await api.RemoveKeyboardAsync(message.Chat.Id, message.MessageId, ct);
    }

    public static string FormatPlan(DayPlan plan, DateOnly date)
    {
        var shown = plan.Timeline.Count;
        while (true)
        {
            var text = ComposePlan(plan, date, shown);
            if (text.Length <= MaxMessageLength || shown == 0) return text;
            shown--;
        }
    }

    private static string ComposePlan(DayPlan plan, DateOnly date, int shownItems)
    {
        var day = $"{DayNames[(int)date.DayOfWeek]}, {date.Day} {MonthNames[date.Month - 1]}";
        var title = plan.FromAi ? "🧠 Plan dnia od AI" : "🧠 Plan dnia";
        var lines = new List<string> { $"<b>{title} · {day}</b>", Encode(plan.Summary), "", "<b>🗓 Harmonogram</b>" };

        if (plan.Timeline.Count == 0)
            lines.Add("<i>Brak punktów w planie.</i>");
        foreach (var item in plan.Timeline.Take(shownItems))
        {
            lines.Add($"<code>{Time(item.Start)}–{Time(item.End)}</code> {Encode(item.Title)}");
            if (!string.IsNullOrWhiteSpace(item.Note)) lines.Add($"<i>{Encode(item.Note)}</i>");
        }

        if (shownItems < plan.Timeline.Count)
            lines.Add($"… i jeszcze {plan.Timeline.Count - shownItems}");

        AddList(lines, "<b>🎯 Najważniejsze</b>", plan.Focus);
        AddList(lines, "<b>⚠️ Uwaga</b>", plan.Risks);
        if (!string.IsNullOrWhiteSpace(plan.Tip))
        {
            lines.Add("");
            lines.Add($"<b>💡 Wskazówka</b> {Encode(plan.Tip)}");
        }

        return string.Join('\n', lines);
    }

    private static void AddList(List<string> lines, string header, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return;
        lines.Add("");
        lines.Add(header);
        lines.AddRange(items.Select(item => $"• {Encode(item)}"));
    }

    private static string FormatSuggestion(AiSuggestion suggestion) =>
        $"<b>{Encode(suggestion.Title)}</b>\n{Encode(suggestion.Body)}";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private IReadOnlyList<IReadOnlyList<TelegramButton>>? OpenButton()
    {
        var url = $"{ReminderLinks.FrontendUrl(configuration)}/";
        return url.StartsWith("https://", StringComparison.Ordinal) ? [[new TelegramButton(OpenButtonText, Url: url)]] : null;
    }

    private static IReadOnlyList<IReadOnlyList<TelegramButton>> Buttons(Guid suggestionId) =>
    [[
        new TelegramButton("Przyjmij", CallbackData: $"sg:ok:{suggestionId:N}"),
        new TelegramButton("Odrzuć", CallbackData: $"sg:no:{suggestionId:N}")
    ]];

    private async Task<bool> ClaimAsync(Guid userId, string deliveryKey, CancellationToken ct)
    {
        if (await db.PushNotificationDeliveries.AnyAsync(d => d.UserId == userId && d.DeliveryKey == deliveryKey, ct))
            return false;

        var delivery = new PushNotificationDelivery
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeliveryKey = deliveryKey,
            SentAt = DateTimeOffset.UtcNow
        };
        db.PushNotificationDeliveries.Add(delivery);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.Entry(delivery).State = EntityState.Detached;
            return false;
        }
    }
}

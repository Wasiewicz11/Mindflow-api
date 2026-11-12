using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai;
using Mindflow.Api.Services.Ai.Dictation;
using Mindflow.Api.Services.Ai.Providers;
using Npgsql;

namespace Mindflow.Api.Services.Telegram;

public interface ITelegramTaskCapture
{
    Task HandleMessageAsync(Guid userId, TelegramMessage message, CancellationToken ct);
    Task HandleCallbackAsync(Guid userId, TelegramCallbackQuery callback, CancellationToken ct);
}

public class TelegramTaskCapture(
    MindflowDbContext db,
    TelegramApi api,
    IDictationService dictation,
    IOptions<AiOptions> aiOptions,
    ITaskService taskService,
    IHttpContextAccessor httpContextAccessor,
    ILogger<TelegramTaskCapture> logger) : ITelegramTaskCapture
{
    public const string NoTasksText = "Nie znalazłem zadań w tej wiadomości.";
    public const string LimitText = "Wykorzystano dzienny limit AI. Spróbuj jutro.";
    public const string AiErrorText = "Nie udało się teraz rozpoznać zadań. Spróbuj za chwilę.";
    public const string VoiceTooLongText = "Głosówka może mieć najwyżej 3 minuty.";
    public const string DownloadFailedText = "Nie udało się pobrać głosówki. Spróbuj jeszcze raz.";
    public const string CancelledText = "Anulowano";
    public const string HandledDraftText = "Ta lista jest już obsłużona.";
    public const string ExpiredDraftText = "Ta lista wygasła. Wyślij wiadomość jeszcze raz.";
    public const string NothingAddedText = "Nie udało się dodać zadań.";
    public const string LiteText = "<i>⚡ Tryb Lite: dzienny limit lepszego modelu jest wykorzystany, więc rozpoznanie może być mniej dokładne.</i>";

    private const int MaxVoiceSeconds = 180;
    private const string DefaultVoiceMime = "audio/ogg";

    private const int MaxListedTasks = 15;
    private const int MaxListedTitle = 80;
    private static readonly TimeSpan DraftLifetime = TimeSpan.FromHours(24);

    public static readonly JsonSerializerOptions DraftJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task HandleMessageAsync(Guid userId, TelegramMessage message, CancellationToken ct)
    {
        if (message.Voice is null && string.IsNullOrWhiteSpace(message.Text)) return;
        if (await IsCapturedAsync(message, ct)) return;

        var input = message.Voice is { } voice
            ? await ReadVoiceAsync(message.Chat.Id, voice, ct)
            : new DictationInput(message.Text, null, null);
        if (input is not null) await CaptureAsync(userId, message, input, ct);
    }

    private async Task<DictationInput?> ReadVoiceAsync(long chatId, TelegramVoice voice, CancellationToken ct)
    {
        var maxBytes = aiOptions.Value.Dictation.MaxAudioBytes;
        if (voice.Duration > MaxVoiceSeconds || voice.FileSize > maxBytes)
        {
            await api.SendMessageAsync(chatId, VoiceTooLongText, null, ct);
            return null;
        }

        var file = await api.GetFileAsync(voice.FileId, ct);
        var audio = file?.FilePath is { } path ? await api.DownloadFileAsync(path, maxBytes, ct) : null;
        if (audio is not { Length: > 0 })
        {
            await api.SendMessageAsync(chatId, DownloadFailedText, null, ct);
            return null;
        }

        return new DictationInput(null, audio, AudioFormats.Normalize(voice.MimeType) ?? DefaultVoiceMime);
    }

    private Task<bool> IsCapturedAsync(TelegramMessage message, CancellationToken ct) =>
        db.TelegramTaskDrafts.AsNoTracking()
            .AnyAsync(d => d.SourceChatId == message.Chat.Id && d.SourceMessageId == message.MessageId, ct);

    private async Task CaptureAsync(Guid userId, TelegramMessage message, DictationInput input, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        DictationResponse result;
        try
        {
            TelegramActor.ActAs(httpContextAccessor, userId);
            result = await dictation.ProcessAsync(input, ct);
        }
        catch (TooManyRequestsException)
        {
            await api.SendMessageAsync(chatId, LimitText, null, ct);
            return;
        }
        catch (ApiException ex)
        {
            logger.LogWarning("Telegram dictation failed with status {Status}", ex.StatusCode);
            await api.SendMessageAsync(chatId, AiErrorText, null, ct);
            return;
        }

        if (result.Tasks.Count == 0)
        {
            await api.SendMessageAsync(chatId, NoTasksText, null, ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await db.TelegramTaskDrafts.Where(d => d.UserId == userId && d.ExpiresAt < now).ExecuteDeleteAsync(ct);
        var draft = new TelegramTaskDraft
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SourceChatId = chatId,
            SourceMessageId = message.MessageId,
            Tasks = JsonSerializer.Serialize(result.Tasks, DraftJson),
            Status = TelegramTaskDraftStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.Add(DraftLifetime)
        };
        db.TelegramTaskDrafts.Add(draft);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(draft).State = EntityState.Detached;
            return;
        }

        await api.SendMessageAsync(chatId, FormatDraft(result.Tasks, result.Lite),
        [[
            new TelegramButton("Dodaj wszystkie", CallbackData: $"cap:add:{draft.Id:N}"),
            new TelegramButton("Anuluj", CallbackData: $"cap:no:{draft.Id:N}")
        ]], ct);
    }

    public async Task HandleCallbackAsync(Guid userId, TelegramCallbackQuery callback, CancellationToken ct)
    {
        var parts = callback.Data?.Split(':');
        if (parts is not ["cap", var verb, var id] || verb is not ("add" or "no") || !Guid.TryParseExact(id, "N", out var draftId))
        {
            await api.AnswerCallbackQueryAsync(callback.Id, ExpiredDraftText, ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var target = verb == "add" ? TelegramTaskDraftStatus.Added : TelegramTaskDraftStatus.Cancelled;
        var claimed = await db.TelegramTaskDrafts
            .Where(d => d.Id == draftId && d.UserId == userId && d.Status == TelegramTaskDraftStatus.Pending && d.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(d => d.Status, target), ct);
        if (claimed == 0)
        {
            var handled = await db.TelegramTaskDrafts.AnyAsync(
                d => d.Id == draftId && d.UserId == userId && d.Status != TelegramTaskDraftStatus.Pending, ct);
            await api.AnswerCallbackQueryAsync(callback.Id, handled ? HandledDraftText : ExpiredDraftText, ct);
            return;
        }

        var answer = verb == "add" ? AddedText(await AddTasksAsync(userId, draftId, ct)) : CancelledText;
        await api.AnswerCallbackQueryAsync(callback.Id, answer, ct);
        if (callback.Message is { } message)
            await api.RemoveKeyboardAsync(message.Chat.Id, message.MessageId, ct);
    }

    public static string AddedText(int count) =>
        count == 0 ? NothingAddedText : $"✅ Dodano {count} {TaskWord(count)}";

    private static string TaskWord(int count) =>
        count == 1 ? "zadanie"
        : count % 10 is >= 2 and <= 4 && count % 100 is < 12 or > 14 ? "zadania"
        : "zadań";

    private async Task<int> AddTasksAsync(Guid userId, Guid draftId, CancellationToken ct)
    {
        var json = await db.TelegramTaskDrafts.AsNoTracking()
            .Where(d => d.Id == draftId)
            .Select(d => d.Tasks)
            .SingleAsync(ct);
        var tasks = JsonSerializer.Deserialize<List<DictatedTaskResponse>>(json, DraftJson) ?? [];

        TelegramActor.ActAs(httpContextAccessor, userId);
        var added = 0;
        foreach (var task in tasks)
        {
            try
            {
                if (await taskService.CreateAsync(DictatedTaskRequests.ToCreateRequest(task)) is not null) added++;
            }
            catch (Exception ex) when (ex is ApiException or DbUpdateException)
            {
                logger.LogWarning("Telegram draft task was not created: {ExceptionType}", ex.GetType().Name);
            }
        }

        return added;
    }

    public static string FormatDraft(IReadOnlyList<DictatedTaskResponse> tasks, bool lite = false)
    {
        var lines = new List<string> { $"<b>📝 Rozpoznane zadania ({tasks.Count})</b>" };
        var number = 0;
        foreach (var task in tasks.Take(MaxListedTasks))
        {
            lines.Add($"{++number}. <b>{WebUtility.HtmlEncode(Truncate(task.Title, MaxListedTitle))}</b>");
            lines.Add(Details(task));
        }

        if (tasks.Count > MaxListedTasks)
            lines.Add($"… i jeszcze {tasks.Count - MaxListedTasks}");
        if (lite)
        {
            lines.Add("");
            lines.Add(LiteText);
        }
        return string.Join('\n', lines);
    }

    private static string Details(DictatedTaskResponse task)
    {
        var due = task.DueDate is { } date
            ? $"📅 {date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}{(task.DueTime is { } time ? " " + time.ToString("HH:mm", CultureInfo.InvariantCulture) : "")}"
            : "📅 bez terminu";
        return $"{due} · 📁 {WebUtility.HtmlEncode(task.ProjectName ?? "bez projektu")} · {task.Priority}";
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max) return value;
        var cut = char.IsHighSurrogate(value[max - 2]) ? max - 2 : max - 1;
        return $"{value[..cut]}…";
    }
}

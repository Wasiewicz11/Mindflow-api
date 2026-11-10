using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Services.Ai;

public class DayPlanWriter(
    MindflowDbContext db,
    IDaySnapshotBuilder snapshotBuilder,
    IAiCredentialResolver credentialResolver,
    GeminiApi gemini,
    OpenAiApi openAi,
    IOptions<AiOptions> options,
    ILogger<DayPlanWriter> logger) : IDayPlanWriter
{
    private const string DefaultTimeZone = "Europe/Warsaw";
    private const string DefaultBlockTitle = "Blok w kalendarzu";
    private static readonly TimeOnly MorningStart = new(8, 0);
    private static readonly TimeOnly LatestStart = new(21, 0);

    private readonly AiOptions _options = options.Value;

    public async Task<DayPlan> WriteAsync(Guid userId, DateOnly localDate, CancellationToken ct = default)
    {
        var context = await BuildContextAsync(userId, localDate, ct);
        if (await TryAiAsync(userId, context, ct) is { } plan) return plan;

        logger.LogWarning("Day plan for user {UserId} uses the local fallback.", userId);
        return DayPlanFallback.Build(context);
    }

    private async Task<DayPlanContext> BuildContextAsync(Guid userId, DateOnly localDate, CancellationToken ct)
    {
        var timeZone = await TimeZoneAsync(userId, ct);
        var snapshot = await snapshotBuilder.BuildAsync(userId, ct);
        var taskIds = snapshot.RefToTaskId.Values.ToList();
        var details = await db.Tasks.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id))
            .Select(t => new TaskTiming(t.Id, t.DueDate, t.DueTime, t.EstimatedHours))
            .ToDictionaryAsync(t => t.Id, ct);

        var tasks = snapshot.Snapshot.Tasks.Select(t =>
        {
            var timing = details.GetValueOrDefault(snapshot.RefToTaskId[t.Ref]);
            var due = timing?.DueDate;
            return new DayPlanTask(
                t.Ref,
                t.Title,
                t.Priority,
                t.Status,
                due is { } date ? date.DayNumber - localDate.DayNumber : null,
                due == localDate ? timing!.DueTime : null,
                timing?.EstimatedHours,
                t.TimesPostponed);
        }).ToList();

        var refByTaskId = snapshot.RefToTaskId.ToDictionary(p => p.Value, p => p.Key);
        var blocks = await BlocksAsync(userId, localDate, timeZone, tasks, refByTaskId, ct);
        return new DayPlanContext(localDate, DayStart(localDate, timeZone), blocks, tasks);
    }

    private async Task<List<DayPlanBlock>> BlocksAsync(
        Guid userId,
        DateOnly localDate,
        TimeZoneInfo timeZone,
        IReadOnlyList<DayPlanTask> tasks,
        IReadOnlyDictionary<Guid, int> refByTaskId,
        CancellationToken ct)
    {
        var dayStartUtc = StartOfDayUtc(localDate, timeZone);
        var dayEndUtc = StartOfDayUtc(localDate.AddDays(1), timeZone);
        var rows = await db.CalendarBlocks.AsNoTracking()
            .Where(b => b.UserId == userId && !b.IsAllDay && b.StartAt < dayEndUtc && b.StartAt >= dayStartUtc.AddDays(-1))
            .OrderBy(b => b.StartAt)
            .Select(b => new { b.TaskId, b.Title, b.StartAt, b.DurationMinutes })
            .ToListAsync(ct);

        var otherTaskIds = rows.Where(b => b.TaskId is { } id && !refByTaskId.ContainsKey(id)).Select(b => b.TaskId!.Value).Distinct().ToList();
        var otherTitles = await db.Tasks.AsNoTracking()
            .Where(t => otherTaskIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Content, ct);
        var titleByRef = tasks.ToDictionary(t => t.Ref, t => t.Title);

        var blocks = new List<DayPlanBlock>();
        foreach (var row in rows)
        {
            var endUtc = row.StartAt.AddMinutes(row.DurationMinutes);
            if (endUtc <= dayStartUtc) continue;

            var start = row.StartAt < dayStartUtc ? TimeOnly.MinValue : LocalTime(row.StartAt, timeZone);
            var end = endUtc >= dayEndUtc ? new TimeOnly(23, 59) : LocalTime(endUtc, timeZone);
            if (end <= start) continue;

            int? taskRef = row.TaskId is { } taskId && refByTaskId.TryGetValue(taskId, out var reference) ? reference : null;
            var title = taskRef is { } r ? titleByRef[r]
                : row.TaskId is { } other && otherTitles.TryGetValue(other, out var content) ? content
                : string.IsNullOrWhiteSpace(row.Title) ? DefaultBlockTitle
                : row.Title;
            blocks.Add(new DayPlanBlock(start, end, title, taskRef));
        }

        return blocks;
    }

    private async Task<DayPlan?> TryAiAsync(Guid userId, DayPlanContext context, CancellationToken ct)
    {
        var credentials = await credentialResolver.ResolveAsync(userId, ct);
        if (credentials.Count == 0) return null;

        var system = DayPlanPromptBuilder.SystemInstruction();
        var payload = DayPlanPromptBuilder.BuildUserPayload(context);
        foreach (var creds in credentials)
        {
            try
            {
                var text = creds.Provider switch
                {
                    AiProvider.Gemini => await GeminiAsync(creds, system, payload, ct),
                    AiProvider.OpenAi => await OpenAiAsync(creds, system, payload, ct),
                    _ => null
                };
                if (text is null) continue;

                if (DayPlanPromptBuilder.Parse(text, context) is not { } plan)
                {
                    logger.LogWarning("Day plan from {Provider} is not valid JSON.", creds.Provider);
                    continue;
                }

                return plan;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Day plan from {Provider} failed.", creds.Provider);
            }
        }

        return null;
    }

    private async Task<string> GeminiAsync(AiCredentials credentials, string system, string payload, CancellationToken ct)
    {
        var fallbackModel = _options.Dictation.GeminiFallbackModel;
        try
        {
            return await gemini.GenerateAsync(credentials.ApiKey, credentials.Model, GeminiBody(credentials.Model, system, payload), ct);
        }
        catch (AiProviderException ex) when (ex.Retryable && !string.Equals(credentials.Model, fallbackModel, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Gemini {Model} failed with {Status}; retrying on {Fallback}.", credentials.Model, ex.StatusCode, fallbackModel);
            return await gemini.GenerateAsync(credentials.ApiKey, fallbackModel, GeminiBody(fallbackModel, system, payload), ct);
        }
    }

    private static object GeminiBody(string model, string system, string payload)
    {
        var generationConfig = new Dictionary<string, object>
        {
            ["responseMimeType"] = "application/json",
            ["temperature"] = 0.4
        };
        if (GeminiApi.ThinkingConfig(model) is { } thinking) generationConfig["thinkingConfig"] = thinking;

        return new
        {
            system_instruction = new { parts = new[] { new { text = system } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = payload } } } },
            generationConfig
        };
    }

    private Task<string> OpenAiAsync(AiCredentials credentials, string system, string payload, CancellationToken ct) =>
        openAi.ChatAsync(credentials.ApiKey, credentials.Model, new
        {
            model = credentials.Model,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = payload }
            },
            response_format = new { type = "json_object" },
            temperature = 0.4
        }, ct);

    private async Task<TimeZoneInfo> TimeZoneAsync(Guid userId, CancellationToken ct)
    {
        var id = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.TimeZone).SingleOrDefaultAsync(ct);
        return FindTimeZone(id) ?? FindTimeZone(DefaultTimeZone) ?? TimeZoneInfo.Utc;
    }

    private static TimeZoneInfo? FindTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static TimeOnly DayStart(DateOnly localDate, TimeZoneInfo timeZone)
    {
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        if (DateOnly.FromDateTime(localNow.DateTime) != localDate) return MorningStart;

        var minutes = localNow.Hour * 60 + localNow.Minute + (localNow.Second > 0 ? 1 : 0);
        var rounded = Math.Clamp((minutes + 14) / 15 * 15, MorningStart.Hour * 60, LatestStart.Hour * 60);
        return new TimeOnly(rounded / 60, rounded % 60);
    }

    private static TimeOnly LocalTime(DateTimeOffset utc, TimeZoneInfo timeZone) =>
        TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, timeZone).DateTime);

    private static DateTimeOffset StartOfDayUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local)) local = local.AddMinutes(30);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone));
    }

    private record TaskTiming(Guid Id, DateOnly? DueDate, TimeOnly? DueTime, decimal? EstimatedHours);
}

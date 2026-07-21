using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using WebPush;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Services;

public class NotificationService(
    MindflowDbContext db,
    IConfiguration configuration,
    ILogger<NotificationService> logger) : INotificationService
{
    private const int ScheduleGraceMinutes = 12;
    private const int BlockReminderToleranceMinutes = 3;
    private static readonly TimeSpan ScheduleGrace = TimeSpan.FromMinutes(ScheduleGraceMinutes);

    public async Task<NotificationSettingsResponse> GetSettingsAsync(Guid userId, CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(userId, ct);
        var subscriptionCount = await db.PushNotificationSubscriptions
            .CountAsync(s => s.UserId == userId, ct);
        return ToResponse(settings, subscriptionCount);
    }

    public async Task<NotificationSettingsResponse> UpdateSettingsAsync(
        Guid userId,
        UpdateNotificationSettingsRequest request,
        CancellationToken ct = default)
    {
        var morningTime = ParseTime(request.MorningBriefTime, "morningBriefTime");
        var middayTime = ParseTime(request.MiddayBriefTime, "middayBriefTime");
        var eveningTime = ParseTime(request.EveningSummaryTime, "eveningSummaryTime");

        if (request.BlockReminderMinutes is < 1 or > 60)
            throw new BadRequestException("Block reminder must be between 1 and 60 minutes.");

        var settings = await GetOrCreateSettingsAsync(userId, ct);
        settings.Enabled = request.Enabled;
        settings.MorningBriefEnabled = request.MorningBriefEnabled;
        settings.MorningBriefTime = morningTime;
        settings.MiddayBriefEnabled = request.MiddayBriefEnabled;
        settings.MiddayBriefTime = middayTime;
        settings.EveningSummaryEnabled = request.EveningSummaryEnabled;
        settings.EveningSummaryTime = eveningTime;
        settings.BlockRemindersEnabled = request.BlockRemindersEnabled;
        settings.BlockReminderMinutes = request.BlockReminderMinutes;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var subscriptionCount = await db.PushNotificationSubscriptions
            .CountAsync(s => s.UserId == userId, ct);
        return ToResponse(settings, subscriptionCount);
    }

    public async Task<IReadOnlyList<PushNotificationSubscriptionResponse>> GetSubscriptionsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        return await db.PushNotificationSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .OrderByDescending(subscription => subscription.UpdatedAt)
            .Select(subscription => new PushNotificationSubscriptionResponse(
                subscription.Id,
                subscription.Endpoint,
                subscription.DeviceName ?? "Przeglądarka",
                subscription.CreatedAt,
                subscription.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task SubscribeAsync(Guid userId, PushNotificationSubscriptionRequest request, CancellationToken ct = default)
    {
        var endpoint = request.Endpoint?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 2048 || !IsTrustedPushEndpoint(endpoint))
            throw new BadRequestException("Invalid push subscription endpoint.");

        if (string.IsNullOrWhiteSpace(request.P256dh) || request.P256dh.Length > 255
            || string.IsNullOrWhiteSpace(request.Auth) || request.Auth.Length > 255)
            throw new BadRequestException("Invalid push subscription keys.");

        var timeZone = GetTimeZoneOrThrow(request.TimeZone);
        var deviceName = NormalizeDeviceName(request.DeviceName);
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new NotFoundException("User not found.");
        var now = DateTimeOffset.UtcNow;
        var subscription = await db.PushNotificationSubscriptions
            .SingleOrDefaultAsync(s => s.Endpoint == endpoint, ct);

        if (subscription is null)
        {
            subscription = new PushNotificationSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth,
                DeviceName = deviceName,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.PushNotificationSubscriptions.Add(subscription);
        }
        else
        {
            subscription.UserId = userId;
            subscription.P256dh = request.P256dh;
            subscription.Auth = request.Auth;
            subscription.DeviceName = deviceName;
            subscription.UpdatedAt = now;
        }

        user.TimeZone = timeZone.Id;
        await GetOrCreateSettingsAsync(userId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UnsubscribeAsync(Guid userId, string endpoint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;

        var subscription = await db.PushNotificationSubscriptions
            .SingleOrDefaultAsync(s => s.UserId == userId && s.Endpoint == endpoint.Trim(), ct);
        if (subscription is null) return;

        db.PushNotificationSubscriptions.Remove(subscription);
        await db.SaveChangesAsync(ct);
    }

    public async Task UnsubscribeAsync(Guid userId, Guid subscriptionId, CancellationToken ct = default)
    {
        var subscription = await db.PushNotificationSubscriptions
            .SingleOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId, ct);
        if (subscription is null) return;

        db.PushNotificationSubscriptions.Remove(subscription);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationInboxItemResponse>> GetInboxItemsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        return await db.NotificationInboxItems
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new NotificationInboxItemResponse(
                item.Id,
                item.Kind.ToString(),
                item.Title,
                item.Body,
                item.CreatedAt,
                item.ReadAt))
            .ToListAsync(ct);
    }

    public async Task MarkInboxItemReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var item = await db.NotificationInboxItems
            .SingleOrDefaultAsync(item => item.Id == notificationId && item.UserId == userId, ct);
        if (item is null || item.ReadAt.HasValue) return;

        item.ReadAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<NotificationTestResponse> SendTestAsync(Guid userId, CancellationToken ct = default)
    {
        var sent = await SendToUserAsync(
            userId,
            "Mindflow jest gotowy",
            "Powiadomienia będą tu przypominać o planie dnia i nadchodzących blokach.",
            "mindflow:test",
            "/",
            ct);

        if (!sent)
            throw new BadRequestException("No active push subscription found for this device.");

        return new NotificationTestResponse(true);
    }

    public async Task<NotificationJobResponse> ProcessDueNotificationsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var users = await db.Users
            .AsNoTracking()
            .Where(user => db.PushNotificationSubscriptions.Any(subscription => subscription.UserId == user.Id))
            .ToListAsync(ct);
        var userIds = users.Select(user => user.Id).ToArray();
        var settingsByUser = await db.NotificationSettings
            .AsNoTracking()
            .Where(settings => userIds.Contains(settings.UserId))
            .ToDictionaryAsync(settings => settings.UserId, ct);

        var briefsSent = 0;
        var eveningSummariesSent = 0;
        foreach (var user in users)
        {
            var settings = settingsByUser.TryGetValue(user.Id, out var existing)
                ? existing
                : new NotificationSettings();
            if (!settings.Enabled) continue;

            var localNow = TimeZoneInfo.ConvertTime(now, GetTimeZoneOrUtc(user.TimeZone));
            var localDate = DateOnly.FromDateTime(localNow.DateTime);
            var localTime = TimeOnly.FromDateTime(localNow.DateTime);

            if (settings.MorningBriefEnabled && IsWithinScheduleWindow(localTime, settings.MorningBriefTime))
            {
                if (await SendBriefOnceAsync(user.Id, localDate, "morning", "Poranny brief", ct))
                    briefsSent++;
            }

            if (settings.MiddayBriefEnabled && IsWithinScheduleWindow(localTime, settings.MiddayBriefTime))
            {
                if (await SendBriefOnceAsync(user.Id, localDate, "midday", "Brief na popołudnie", ct))
                    briefsSent++;
            }

            if (settings.EveningSummaryEnabled && IsWithinScheduleWindow(localTime, settings.EveningSummaryTime))
            {
                if (await SendEveningSummaryOnceAsync(user.Id, localDate, GetTimeZoneOrUtc(user.TimeZone), ct))
                    eveningSummariesSent++;
            }
        }

        var blockRemindersSent = await SendDueBlockRemindersAsync(now, userIds, settingsByUser, ct);
        return new NotificationJobResponse(briefsSent, blockRemindersSent, eveningSummariesSent);
    }

    private async Task<int> SendDueBlockRemindersAsync(
        DateTimeOffset now,
        IReadOnlyCollection<Guid> userIds,
        IReadOnlyDictionary<Guid, NotificationSettings> settingsByUser,
        CancellationToken ct)
    {
        if (userIds.Count == 0) return 0;

        var earliestReminder = settingsByUser.Values
            .Where(settings => settings.Enabled && settings.BlockRemindersEnabled)
            .Select(settings => settings.BlockReminderMinutes)
            .DefaultIfEmpty(10)
            .Min();
        var latestReminder = settingsByUser.Values
            .Where(settings => settings.Enabled && settings.BlockRemindersEnabled)
            .Select(settings => settings.BlockReminderMinutes)
            .DefaultIfEmpty(10)
            .Max();
        var rangeStart = now.AddMinutes(earliestReminder - BlockReminderToleranceMinutes);
        var rangeEnd = now.AddMinutes(latestReminder + BlockReminderToleranceMinutes);

        var blocks = await db.CalendarBlocks
            .AsNoTracking()
            .Where(block => userIds.Contains(block.UserId)
                            && block.StartAt >= rangeStart
                            && block.StartAt < rangeEnd)
            .ToListAsync(ct);
        var taskIds = blocks.Where(block => block.TaskId.HasValue)
            .Select(block => block.TaskId!.Value)
            .Distinct()
            .ToArray();
        var tasksById = await db.Tasks
            .AsNoTracking()
            .Where(task => taskIds.Contains(task.Id))
            .ToDictionaryAsync(task => task.Id, ct);

        var sent = 0;
        foreach (var block in blocks)
        {
            if (!settingsByUser.TryGetValue(block.UserId, out var settings)
                || !settings.Enabled
                || !settings.BlockRemindersEnabled)
                continue;

            if (block.StartAt <= now)
                continue;

            var minutesUntilStart = (int)Math.Round((block.StartAt - now).TotalMinutes);
            if (Math.Abs(minutesUntilStart - settings.BlockReminderMinutes) > BlockReminderToleranceMinutes)
                continue;

            if (block.TaskId is Guid taskId
                && tasksById.TryGetValue(taskId, out var task)
                && (task.IsCompleted || task.Status == TaskStatus.Completed))
                continue;

            var title = block.TaskId is Guid assignedTaskId && tasksById.TryGetValue(assignedTaskId, out var assignedTask)
                ? assignedTask.Content
                : block.Title ?? "Blok czasu";
            var exactMinutes = Math.Max(1, minutesUntilStart);
            var deliveryKey = $"block:{block.Id:N}:{block.StartAt.UtcDateTime.Ticks}:reminder";

            if (await SendOnceAsync(
                    block.UserId,
                    deliveryKey,
                    "Nadchodzący blok",
                    $"Za {exactMinutes} min zaczyna się „{Truncate(title, 120)}”",
                    deliveryKey,
                    null,
                    ct))
                sent++;
        }

        return sent;
    }

    private async Task<bool> SendBriefOnceAsync(
        Guid userId,
        DateOnly localDate,
        string briefKind,
        string title,
        CancellationToken ct)
    {
        var tasks = await GetOpenTasksAsync(userId, ct);
        var todayTasks = tasks.Where(task => task.DueDate == localDate).ToArray();
        var overdueTasks = tasks.Where(task => task.DueDate.HasValue && task.DueDate.Value < localDate).ToArray();
        var projectNames = await GetProjectNamesAsync(todayTasks, ct);
        var body = BuildBriefBody(todayTasks, overdueTasks, projectNames);

        return await SendOnceAsync(
            userId,
            $"brief:{briefKind}:{localDate:yyyy-MM-dd}",
            title,
            body,
            $"mindflow:brief:{briefKind}",
            briefKind == "morning" ? NotificationInboxKind.MorningBrief : NotificationInboxKind.MiddayBrief,
            ct);
    }

    private async Task<bool> SendEveningSummaryOnceAsync(
        Guid userId,
        DateOnly localDate,
        TimeZoneInfo timeZone,
        CancellationToken ct)
    {
        var dayStartUtc = ToUtc(localDate, timeZone);
        var dayEndUtc = ToUtc(localDate.AddDays(1), timeZone);
        var completedCount = await db.TaskActivityEvents
            .AsNoTracking()
            .Where(eventItem => eventItem.UserId == userId
                                && eventItem.EventType == TaskActivityEventType.TaskCompleted
                                && eventItem.TaskId.HasValue
                                && eventItem.OccurredAt >= dayStartUtc
                                && eventItem.OccurredAt < dayEndUtc)
            .Select(eventItem => eventItem.TaskId)
            .Distinct()
            .CountAsync(ct);
        var incompleteTasks = (await GetOpenTasksAsync(userId, ct))
            .Where(task => task.DueDate.HasValue && task.DueDate.Value <= localDate)
            .OrderBy(task => task.DueDate)
            .ThenBy(task => task.CreatedAt)
            .ToArray();
        var body = incompleteTasks.Length == 0
            ? $"Ukończone dziś: {completedCount}. Nie ma już zadań z terminem na dziś ani zaległych."
            : $"Ukończone dziś: {completedCount}. Do zrobienia: {FormatTaskTitles(incompleteTasks)}.";

        return await SendOnceAsync(
            userId,
            $"summary:evening:{localDate:yyyy-MM-dd}",
            "Podsumowanie dnia",
            body,
            "mindflow:summary:evening",
            NotificationInboxKind.EveningSummary,
            ct);
    }

    private async Task<IReadOnlyList<TaskItem>> GetOpenTasksAsync(Guid userId, CancellationToken ct)
    {
        return await db.Tasks
            .AsNoTracking()
            .Where(task => task.UserId == userId && !task.IsCompleted && task.Status != TaskStatus.Completed)
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> GetProjectNamesAsync(
        IEnumerable<TaskItem> tasks,
        CancellationToken ct)
    {
        var projectIds = tasks.Where(task => task.ProjectId.HasValue)
            .Select(task => task.ProjectId!.Value)
            .Distinct()
            .ToArray();
        if (projectIds.Length == 0) return new Dictionary<Guid, string>();

        return await db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .ToDictionaryAsync(project => project.Id, project => project.Name, ct);
    }

    private async Task<bool> SendOnceAsync(
        Guid userId,
        string deliveryKey,
        string title,
        string body,
        string tag,
        NotificationInboxKind? inboxKind,
        CancellationToken ct)
    {
        var alreadySent = await db.PushNotificationDeliveries
            .AsNoTracking()
            .AnyAsync(delivery => delivery.UserId == userId && delivery.DeliveryKey == deliveryKey, ct);
        if (alreadySent) return false;

        var now = DateTimeOffset.UtcNow;
        var inboxItem = inboxKind is NotificationInboxKind kind
            ? new NotificationInboxItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Kind = kind,
                Title = title,
                Body = body,
                CreatedAt = now
            }
            : null;
        var targetUrl = inboxItem is null ? "/" : $"/?notification={inboxItem.Id:N}";

        if (inboxItem is not null)
        {
            db.NotificationInboxItems.Add(inboxItem);
            await db.SaveChangesAsync(ct);
        }

        bool sent;
        try
        {
            sent = await SendToUserAsync(userId, title, body, tag, targetUrl, ct);
        }
        catch
        {
            if (inboxItem is not null)
            {
                db.NotificationInboxItems.Remove(inboxItem);
                await db.SaveChangesAsync(ct);
            }

            throw;
        }

        if (!sent)
        {
            if (inboxItem is not null)
            {
                db.NotificationInboxItems.Remove(inboxItem);
                await db.SaveChangesAsync(ct);
            }

            return false;
        }

        var delivery = new PushNotificationDelivery
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeliveryKey = deliveryKey,
            SentAt = now
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
            if (inboxItem is not null)
            {
                db.NotificationInboxItems.Remove(inboxItem);
                await db.SaveChangesAsync(ct);
            }

            return false;
        }
    }

    private async Task<bool> SendToUserAsync(
        Guid userId,
        string title,
        string body,
        string tag,
        string targetUrl,
        CancellationToken ct)
    {
        var subscriptions = await db.PushNotificationSubscriptions
            .Where(subscription => subscription.UserId == userId)
            .ToListAsync(ct);
        if (subscriptions.Count == 0) return false;

        var vapidDetails = GetVapidDetails();
        var client = new WebPushClient();
        var payload = JsonSerializer.Serialize(new
        {
            title,
            body,
            tag,
            url = targetUrl
        });
        var expiredSubscriptions = new List<PushNotificationSubscription>();
        var sent = false;

        foreach (var subscription in subscriptions)
        {
            try
            {
                await client.SendNotificationAsync(
                    new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                    payload,
                    vapidDetails,
                    ct);
                sent = true;
            }
            catch (WebPushException exception) when (
                exception.StatusCode == HttpStatusCode.Gone || exception.StatusCode == HttpStatusCode.NotFound)
            {
                expiredSubscriptions.Add(subscription);
            }
            catch (WebPushException exception)
            {
                logger.LogWarning(
                    "Web Push delivery failed for user {UserId} with status {StatusCode}.",
                    userId,
                    exception.StatusCode);
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Web Push request failed for user {UserId}.", userId);
            }
        }

        if (expiredSubscriptions.Count > 0)
        {
            db.PushNotificationSubscriptions.RemoveRange(expiredSubscriptions);
            await db.SaveChangesAsync(ct);
        }

        return sent;
    }

    private async Task<NotificationSettings> GetOrCreateSettingsAsync(Guid userId, CancellationToken ct)
    {
        var settings = await db.NotificationSettings.FindAsync([userId], ct);
        if (settings is not null) return settings;

        settings = new NotificationSettings
        {
            UserId = userId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.NotificationSettings.Add(settings);
        return settings;
    }

    private VapidDetails GetVapidDetails()
    {
        var subject = configuration["WebPush:Subject"];
        var publicKey = configuration["WebPush:PublicKey"];
        var privateKey = configuration["WebPush:PrivateKey"];
        if (string.IsNullOrWhiteSpace(subject)
            || string.IsNullOrWhiteSpace(publicKey)
            || string.IsNullOrWhiteSpace(privateKey))
            throw new BadRequestException("Web Push is not configured on the server.");

        return new VapidDetails(subject, publicKey, privateKey);
    }

    private TimeZoneInfo GetTimeZoneOrUtc(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone)) return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            logger.LogWarning("Unknown time zone {TimeZone}; using UTC for notifications.", timeZone);
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            logger.LogWarning("Invalid time zone {TimeZone}; using UTC for notifications.", timeZone);
            return TimeZoneInfo.Utc;
        }
    }

    private static TimeZoneInfo GetTimeZoneOrThrow(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
            throw new BadRequestException("Time zone is required.");

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZone.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            throw new BadRequestException("Unsupported time zone.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new BadRequestException("Unsupported time zone.");
        }
    }

    private static TimeOnly ParseTime(string value, string fieldName)
    {
        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time;

        throw new BadRequestException($"{fieldName} must use HH:mm format.");
    }

    private static bool IsTrustedPushEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.Host;
        return host.Equals("fcm.googleapis.com", StringComparison.OrdinalIgnoreCase)
               || host.Equals("updates.push.services.mozilla.com", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".push.apple.com", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".notify.windows.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDeviceName(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return "Przeglądarka";

        var normalized = deviceName.Trim();
        if (normalized.Length > 120)
            throw new BadRequestException("Device name is too long.");

        return normalized;
    }

    private static bool IsWithinScheduleWindow(TimeOnly currentTime, TimeOnly scheduledTime)
    {
        var elapsed = currentTime.ToTimeSpan() - scheduledTime.ToTimeSpan();
        return elapsed >= TimeSpan.Zero && elapsed < ScheduleGrace;
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var localMidnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localMidnight, timeZone));
    }

    private static NotificationSettingsResponse ToResponse(NotificationSettings settings, int subscriptionCount) => new(
        settings.Enabled,
        settings.MorningBriefEnabled,
        settings.MorningBriefTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        settings.MiddayBriefEnabled,
        settings.MiddayBriefTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        settings.EveningSummaryEnabled,
        settings.EveningSummaryTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        settings.BlockRemindersEnabled,
        settings.BlockReminderMinutes,
        subscriptionCount);

    private static string BuildBriefBody(
        IReadOnlyCollection<TaskItem> todayTasks,
        IReadOnlyCollection<TaskItem> overdueTasks,
        IReadOnlyDictionary<Guid, string> projectNames)
    {
        var groupedToday = todayTasks
            .GroupBy(task => task.ProjectId)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.HasValue && projectNames.TryGetValue(group.Key.Value, out var name) ? name : "Bez projektu")
            .ToArray();
        var projectParts = groupedToday.Take(5)
            .Select(group => $"{(group.Key.HasValue && projectNames.TryGetValue(group.Key.Value, out var name) ? name : "Bez projektu")} - {group.Count()}")
            .ToList();
        if (groupedToday.Length > projectParts.Count)
            projectParts.Add($"+{groupedToday.Length - projectParts.Count} projektów");

        var todayText = projectParts.Count == 0
            ? "Dziś bez zadań z terminem"
            : $"Dziś: {string.Join(", ", projectParts)}";
        return overdueTasks.Count == 0
            ? $"{todayText}."
            : $"{todayText}. Zaległe ({overdueTasks.Count}): {FormatTaskTitles(overdueTasks)}.";
    }

    private static string FormatTaskTitles(IReadOnlyCollection<TaskItem> tasks)
    {
        var titles = tasks.Take(3).Select(task => Truncate(task.Content, 60)).ToList();
        if (tasks.Count > titles.Count) titles.Add($"+{tasks.Count - titles.Count} więcej");
        return string.Join(", ", titles);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : $"{value[..Math.Max(0, maxLength - 1)]}…";
}

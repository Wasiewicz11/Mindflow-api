using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models.Dtos;

public record NotificationSettingsResponse(
    bool Enabled,
    bool MorningBriefEnabled,
    string MorningBriefTime,
    bool MiddayBriefEnabled,
    string MiddayBriefTime,
    bool EveningSummaryEnabled,
    string EveningSummaryTime,
    bool BlockRemindersEnabled,
    int BlockReminderMinutes,
    int SubscriptionCount);

public record UpdateNotificationSettingsRequest(
    bool Enabled,
    bool MorningBriefEnabled,
    [Required, StringLength(5, MinimumLength = 5)] string MorningBriefTime,
    bool MiddayBriefEnabled,
    [Required, StringLength(5, MinimumLength = 5)] string MiddayBriefTime,
    bool EveningSummaryEnabled,
    [Required, StringLength(5, MinimumLength = 5)] string EveningSummaryTime,
    bool BlockRemindersEnabled,
    [Range(1, 60)] int BlockReminderMinutes);

public record PushNotificationSubscriptionRequest(
    [Required, StringLength(2048)] string Endpoint,
    [Required, StringLength(255)] string P256dh,
    [Required, StringLength(255)] string Auth,
    [Required, StringLength(100)] string TimeZone,
    [StringLength(120)] string? DeviceName);

public record PushNotificationSubscriptionResponse(
    Guid Id,
    string Endpoint,
    string DeviceName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record DeletePushNotificationSubscriptionRequest(
    [Required, StringLength(2048)] string Endpoint);

public record NotificationTestResponse(bool Sent);

public record NotificationJobResponse(
    int BriefsSent,
    int BlockRemindersSent,
    int EveningSummariesSent);

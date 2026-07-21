using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface INotificationService
{
    Task<NotificationSettingsResponse> GetSettingsAsync(Guid userId, CancellationToken ct = default);
    Task<NotificationSettingsResponse> UpdateSettingsAsync(Guid userId, UpdateNotificationSettingsRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PushNotificationSubscriptionResponse>> GetSubscriptionsAsync(Guid userId, CancellationToken ct = default);
    Task SubscribeAsync(Guid userId, PushNotificationSubscriptionRequest request, CancellationToken ct = default);
    Task UnsubscribeAsync(Guid userId, string endpoint, CancellationToken ct = default);
    Task UnsubscribeAsync(Guid userId, Guid subscriptionId, CancellationToken ct = default);
    Task<NotificationTestResponse> SendTestAsync(Guid userId, CancellationToken ct = default);
    Task<NotificationJobResponse> ProcessDueNotificationsAsync(CancellationToken ct = default);
}

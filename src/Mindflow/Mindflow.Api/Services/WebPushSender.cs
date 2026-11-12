using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using WebPush;

namespace Mindflow.Api.Services;

public interface IWebPushSender
{
    Task<bool> SendAsync(Guid userId, object payload, CancellationToken ct);
}

public class WebPushSender(
    MindflowDbContext db,
    IConfiguration configuration,
    ILogger<WebPushSender> logger) : IWebPushSender
{
    public async Task<bool> SendAsync(Guid userId, object payload, CancellationToken ct)
    {
        var subscriptions = await db.PushNotificationSubscriptions
            .Where(subscription => subscription.UserId == userId)
            .ToListAsync(ct);
        if (subscriptions.Count == 0) return false;

        var vapidDetails = GetVapidDetails();
        var client = new WebPushClient();
        var body = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
        var expiredSubscriptions = new List<PushNotificationSubscription>();
        var sent = false;

        foreach (var subscription in subscriptions)
        {
            try
            {
                await client.SendNotificationAsync(
                    new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                    body,
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
}

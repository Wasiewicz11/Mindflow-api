using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("notifications")]
[Authorize]
public class NotificationsController(
    ICurrentUserService currentUserService,
    INotificationService notificationService) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<ActionResult<NotificationSettingsResponse>> GetSettings(CancellationToken ct)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return Ok(await notificationService.GetSettingsAsync(userId, ct));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<NotificationSettingsResponse>> UpdateSettings(
        UpdateNotificationSettingsRequest request,
        CancellationToken ct)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return Ok(await notificationService.UpdateSettingsAsync(userId, request, ct));
    }

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Subscribe(PushNotificationSubscriptionRequest request, CancellationToken ct)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        await notificationService.SubscribeAsync(userId, request, ct);
        return NoContent();
    }

    [HttpGet("subscriptions")]
    public async Task<ActionResult<IReadOnlyList<PushNotificationSubscriptionResponse>>> GetSubscriptions(CancellationToken ct)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return Ok(await notificationService.GetSubscriptionsAsync(userId, ct));
    }

    [HttpDelete("subscriptions")]
    public async Task<IActionResult> Unsubscribe(DeletePushNotificationSubscriptionRequest request, CancellationToken ct)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        await notificationService.UnsubscribeAsync(userId, request.Endpoint, ct);
        return NoContent();
    }

    [HttpDelete("subscriptions/{subscriptionId:guid}")]
    public async Task<IActionResult> Unsubscribe(Guid subscriptionId, CancellationToken ct)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        await notificationService.UnsubscribeAsync(userId, subscriptionId, ct);
        return NoContent();
    }

    [HttpPost("test")]
    public async Task<ActionResult<NotificationTestResponse>> SendTest(CancellationToken ct)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return Ok(await notificationService.SendTestAsync(userId, ct));
    }
}

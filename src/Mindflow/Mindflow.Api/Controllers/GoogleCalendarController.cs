using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.GoogleCalendar;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("integrations/google/calendar")]
public class GoogleCalendarController(
    IGoogleCalendarConnectionService connectionService,
    IOAuthStateProtector stateProtector,
    IOptions<GoogleCalendarOptions> options,
    IConfiguration configuration,
    ILogger<GoogleCalendarController> logger) : ControllerBase
{
    private readonly GoogleCalendarOptions _options = options.Value;

    [Authorize]
    [HttpGet("connect")]
    public async Task<IActionResult> Connect()
    {
        var url = await connectionService.BeginConnectAsync();
        return Ok(new GoogleCalendarConnectResponse(url));
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return Redirect(ReturnUrl("error"));

        if (!stateProtector.TryRead(state, out var userId))
            return Redirect(ReturnUrl("error"));

        try
        {
            var ok = await connectionService.CompleteConnectAsync(code, userId, ct);
            return Redirect(ReturnUrl(ok ? "select-calendar" : "error"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Google Calendar connect callback failed for user {UserId}.", userId);
            return Redirect(ReturnUrl("error"));
        }
    }

    [Authorize]
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        return Ok(await connectionService.GetStatusAsync());
    }

    [Authorize]
    [HttpGet("calendars")]
    public async Task<IActionResult> Calendars(CancellationToken ct)
    {
        return Ok(await connectionService.GetCalendarsAsync(ct));
    }

    [Authorize]
    [HttpPut("source")]
    public async Task<IActionResult> SetSource([FromBody] SetSourceCalendarRequest request, CancellationToken ct)
    {
        await connectionService.SetSourceCalendarAsync(request.CalendarId, ct);
        return NoContent();
    }

    [Authorize]
    [HttpDelete]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await connectionService.DisconnectAsync(ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        return Ok(await connectionService.SyncCurrentUserAsync(ct));
    }

    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        var channelId = Request.Headers["X-Goog-Channel-Id"].FirstOrDefault();
        var token = Request.Headers["X-Goog-Channel-Token"].FirstOrDefault();
        var resourceState = Request.Headers["X-Goog-Resource-State"].FirstOrDefault();

        await connectionService.HandleWebhookAsync(channelId, token, resourceState, ct);
        return Ok();
    }

    private string ReturnUrl(string status)
    {
        var baseUrl = (_options.FrontendReturnUrl ?? configuration["Cors:FrontendUrl"] ?? "http://localhost:5173")
            .TrimEnd('/');
        return $"{baseUrl}/?google={status}";
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services;
using Mindflow.Api.Services.Telegram;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("integrations/telegram")]
[Authorize]
public class TelegramController(
    ICurrentUserService currentUserService,
    ITelegramService telegramService,
    IConfiguration configuration,
    ILogger<TelegramController> logger) : ControllerBase
{
    private const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    [HttpGet]
    public async Task<ActionResult<TelegramStatusResponse>> GetStatus(CancellationToken ct)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return Ok(await telegramService.GetStatusAsync(userId, ct));
    }

    [HttpPost("link")]
    public async Task<ActionResult<TelegramLinkResponse>> CreateLink(CancellationToken ct)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return Ok(await telegramService.CreateLinkAsync(userId, ct));
    }

    [HttpDelete]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        await telegramService.DisconnectAsync(userId, ct);
        return NoContent();
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        var expected = TelegramOptions.From(configuration)?.WebhookSecret;
        var provided = Request.Headers[SecretHeader].ToString();
        if (string.IsNullOrEmpty(expected) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
            return Unauthorized();

        try
        {
            var update = await JsonSerializer.DeserializeAsync<TelegramUpdate>(Request.Body, TelegramJson.Options, ct);
            if (update is not null) await telegramService.HandleUpdateAsync(update, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram webhook update failed");
        }

        return Ok();
    }
}

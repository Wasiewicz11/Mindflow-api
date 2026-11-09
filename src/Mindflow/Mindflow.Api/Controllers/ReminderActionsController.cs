using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("notifications/reminder-actions")]
[AllowAnonymous]
public class ReminderActionsController(
    IReminderActionTokens tokens,
    IReminderActionService actions) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ReminderActionResponse>> Apply(ReminderActionRequest request, CancellationToken ct)
    {
        var claims = tokens.Validate(request.Token, DateTimeOffset.UtcNow);
        if (claims is null) return BadRequest(new { message = "Invalid or expired reminder link." });

        ReminderAction? action = request.Action.ToLowerInvariant() switch
        {
            "done" => ReminderAction.Done,
            "snooze" => ReminderAction.Snooze,
            _ => null
        };
        if (action is null) return BadRequest(new { message = "Unknown reminder action." });

        var result = await actions.ApplyAsync(claims.UserId, claims.TaskId, action.Value, ct);
        return result is null ? NotFound() : Ok(result);
    }
}

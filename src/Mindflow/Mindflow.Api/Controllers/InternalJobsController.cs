using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Services;
using Mindflow.Api.Services.GoogleCalendar;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("internal/jobs")]
public class InternalJobsController(
    ISuggestionService suggestionService,
    IGoogleCalendarConnectionService googleCalendarConnectionService,
    MindflowDbContext db,
    IConfiguration configuration,
    ILogger<InternalJobsController> logger) : ControllerBase
{
    [HttpPost("daily-suggestions")]
    public async Task<IActionResult> DailySuggestions([FromHeader(Name = "X-Job-Key")] string? jobKey)
    {
        if (!IsAuthorized(jobKey)) return Unauthorized();

        var userIds = await db.Users.Select(u => u.Id).ToListAsync();
        var totalCreated = 0;

        foreach (var userId in userIds)
        {
            try
            {
                totalCreated += await suggestionService.GenerateForUserAsync(userId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Generowanie sugestii nie powiodło się dla usera {UserId}.", userId);
            }
        }

        return Ok(new { users = userIds.Count, suggestionsCreated = totalCreated });
    }

    [HttpPost("google-calendar-renew")]
    public async Task<IActionResult> RenewGoogleCalendarWatches([FromHeader(Name = "X-Job-Key")] string? jobKey, CancellationToken ct)
    {
        if (!IsAuthorized(jobKey)) return Unauthorized();

        await googleCalendarConnectionService.RenewExpiringWatchesAsync(ct);
        return Ok();
    }

    private bool IsAuthorized(string? jobKey)
    {
        var expected = configuration["Jobs:ApiKey"];
        return !string.IsNullOrWhiteSpace(expected) && string.Equals(jobKey, expected, StringComparison.Ordinal);
    }
}

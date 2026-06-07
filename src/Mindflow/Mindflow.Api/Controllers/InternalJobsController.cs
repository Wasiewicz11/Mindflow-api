using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("internal/jobs")]
public class InternalJobsController(
    ISuggestionService suggestionService,
    MindflowDbContext db,
    IConfiguration configuration,
    ILogger<InternalJobsController> logger) : ControllerBase
{
    [HttpPost("daily-suggestions")]
    public async Task<IActionResult> DailySuggestions([FromHeader(Name = "X-Job-Key")] string? jobKey)
    {
        var expected = configuration["Jobs:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(jobKey, expected, StringComparison.Ordinal))
            return Unauthorized();

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
}

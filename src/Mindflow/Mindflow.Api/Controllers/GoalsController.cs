using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("goals/days")]
[Authorize]
public class GoalsController(IGoalDayService goalDayService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var days = await goalDayService.GetAllAsync();
        return Ok(days);
    }

    [HttpGet("{date}")]
    public async Task<IActionResult> GetByDate(string date)
    {
        var parsedDate = ParseDate(date);
        var day = await goalDayService.GetByDateAsync(parsedDate);
        return day is null ? NotFound() : Ok(day);
    }

    [HttpPut("{date}")]
    public async Task<IActionResult> Upsert(string date, [FromBody] UpsertGoalDayRequest request)
    {
        var parsedDate = ParseDate(date);
        var day = await goalDayService.UpsertAsync(parsedDate, request);
        return Ok(day);
    }

    [HttpDelete("{date}")]
    public async Task<IActionResult> Delete(string date)
    {
        var parsedDate = ParseDate(date);
        var deleted = await goalDayService.DeleteAsync(parsedDate);
        return deleted ? NoContent() : NotFound();
    }

    private static DateOnly ParseDate(string date)
    {
        if (DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        throw new BadRequestException("Date must use yyyy-MM-dd format.");
    }
}

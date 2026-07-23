using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("time-entries")]
[Authorize]
public class TimeEntriesController(ITaskTimeEntryService timeEntryService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var entries = await timeEntryService.GetAsync(from, to);
        return Ok(entries);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await timeEntryService.DeleteAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStandaloneTimeEntryRequest request)
    {
        var created = await timeEntryService.CreateStandaloneAsync(request);
        if (created is null) return NotFound();
        return Ok(created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTaskTimeEntryRequest request)
    {
        var updated = await timeEntryService.UpdateAsync(id, request);
        if (updated is null) return NotFound();
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await timeEntryService.DeleteAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }
}

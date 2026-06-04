using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("calendar/blocks")]
[Authorize]
public class CalendarBlocksController(ICalendarBlockService calendarBlockService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var blocks = await calendarBlockService.GetAsync(from, to);
        return Ok(blocks);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCalendarBlockRequest request)
    {
        var created = await calendarBlockService.CreateAsync(request);
        if (created is null) return NotFound();
        return Ok(created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCalendarBlockRequest request)
    {
        var updated = await calendarBlockService.UpdateAsync(id, request);
        if (updated is null) return NotFound();
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await calendarBlockService.DeleteAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }
}

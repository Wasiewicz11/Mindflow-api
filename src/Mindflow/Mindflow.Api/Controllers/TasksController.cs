using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("tasks")]
[Authorize]
public class TasksController(ITaskService taskService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tasks = await taskService.GetAllForCurrentUserAsync();
        return Ok(tasks);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var task = await taskService.GetByIdForCurrentUserAsync(id);
        if (task is null) return NotFound();
        return Ok(task);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest request)
    {
        var created = await taskService.CreateForCurrentUserAsync(request);
        if (created is null) return NotFound();
        return Ok(created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTaskRequest request)
    {
        var updated = await taskService.UpdateForCurrentUserAsync(id, request);
        if (updated is null) return NotFound();
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await taskService.DeleteForCurrentUserAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }
}

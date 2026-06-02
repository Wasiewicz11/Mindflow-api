using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("tasks")]
[Authorize]
public class TasksController(ITaskService taskService, ITaskSubtaskService subtaskService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tasks = await taskService.GetAllAsync();
        return Ok(tasks);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var task = await taskService.GetByIdAsync(id);
        if (task is null) return NotFound();
        return Ok(task);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest request)
    {
        var created = await taskService.CreateAsync(request);
        if (created is null) return NotFound();
        return Ok(created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTaskRequest request)
    {
        var updated = await taskService.UpdateAsync(id, request);
        if (updated is null) return NotFound();
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await taskService.DeleteAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/subtasks")]
    public async Task<IActionResult> CreateSubtask(Guid id, [FromBody] TaskSubtaskRequest request)
    {
        var updated = await subtaskService.CreateAsync(id, request);
        if (updated is null) return NotFound();
        return Ok(updated);
    }

    [HttpPut("{id:guid}/subtasks/{subtaskId:guid}")]
    public async Task<IActionResult> UpdateSubtask(Guid id, Guid subtaskId, [FromBody] TaskSubtaskRequest request)
    {
        var updated = await subtaskService.UpdateAsync(id, subtaskId, request);
        if (updated is null) return NotFound();
        return Ok(updated);
    }

    [HttpDelete("{id:guid}/subtasks/{subtaskId:guid}")]
    public async Task<IActionResult> DeleteSubtask(Guid id, Guid subtaskId)
    {
        var updated = await subtaskService.DeleteAsync(id, subtaskId);
        if (updated is null) return NotFound();
        return Ok(updated);
    }

    [HttpPut("{id:guid}/subtasks/reorder")]
    public async Task<IActionResult> ReorderSubtasks(Guid id, [FromBody] ReorderTaskSubtasksRequest request)
    {
        var updated = await subtaskService.ReorderAsync(id, request);
        if (updated is null) return NotFound();
        return Ok(updated);
    }
}

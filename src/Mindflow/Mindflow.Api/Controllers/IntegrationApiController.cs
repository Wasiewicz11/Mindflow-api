using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mindflow.Api.Authentication;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services;
using Mindflow.Api.Services.Integrations;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route(IntegrationRoutes.Base)]
[Authorize(AuthenticationSchemes = IntegrationTokenAuthenticationDefaults.Scheme)]
[EnableRateLimiting(IntegrationRoutes.RateLimitPolicy)]
public class IntegrationApiController(
    ITaskService taskService,
    ITaskSubtaskService subtaskService,
    ITaskTimeEntryService timeEntryService,
    IIntegrationProjectService integrationProjectService,
    IIntegrationTaskQueryService integrationTaskQueryService,
    IIntegrationDocsService integrationDocsService) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    [HttpGet("docs")]
    public IActionResult Docs()
    {
        return Ok(integrationDocsService.Build());
    }

    [HttpGet("projects")]
    [RequireIntegrationScope(IntegrationTokenScope.ProjectsRead)]
    public async Task<IActionResult> GetProjects(
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken ct)
    {
        return Ok(await integrationProjectService.GetAccessibleProjectsAsync(BuildPageQuery(limit, offset), ct));
    }

    [HttpGet("projects/{projectId:guid}/tasks")]
    [RequireIntegrationScope(IntegrationTokenScope.TasksRead)]
    public async Task<IActionResult> GetProjectTasks(
        Guid projectId,
        [FromQuery] TaskStatus? status,
        [FromQuery] bool? isCompleted,
        [FromQuery] DateOnly? dueBefore,
        [FromQuery] DateTimeOffset? createdAfter,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken ct)
    {
        var query = BuildQuery(projectId, status, isCompleted, dueBefore, createdAfter, limit, offset);
        var page = await integrationTaskQueryService.GetTasksAsync(query, ct);
        if (page is null) return NotFound();

        return Ok(page);
    }

    [HttpGet("tasks")]
    [RequireIntegrationScope(IntegrationTokenScope.TasksRead)]
    public async Task<IActionResult> GetTasks(
        [FromQuery] Guid? projectId,
        [FromQuery] TaskStatus? status,
        [FromQuery] bool? isCompleted,
        [FromQuery] DateOnly? dueBefore,
        [FromQuery] DateTimeOffset? createdAfter,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken ct)
    {
        var query = BuildQuery(projectId, status, isCompleted, dueBefore, createdAfter, limit, offset);
        var page = await integrationTaskQueryService.GetTasksAsync(query, ct);
        if (page is null) return NotFound();

        return Ok(page);
    }

    [HttpGet("tasks/{id:guid}")]
    [RequireIntegrationScope(IntegrationTokenScope.TasksRead)]
    public async Task<IActionResult> GetTask(Guid id)
    {
        var task = await taskService.GetByIdAsync(id);
        if (task is null) return NotFound();

        return Ok(task);
    }

    [HttpPost("tasks")]
    [RequireIntegrationScope(IntegrationTokenScope.TasksCreate)]
    public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
    {
        var task = await taskService.CreateAsync(request);
        if (task is null) return NotFound();

        return CreatedAtAction(nameof(GetTask), new { id = task.Id }, task);
    }

    [HttpPatch("tasks/{id:guid}")]
    [RequireIntegrationScope(IntegrationTokenScope.TasksUpdate)]
    public async Task<IActionResult> UpdateTask(Guid id, [FromBody] UpdateTaskRequest request)
    {
        var task = await taskService.UpdateAsync(id, request);
        if (task is null) return NotFound();

        return Ok(task);
    }

    [HttpDelete("tasks/{id:guid}")]
    [RequireIntegrationScope(IntegrationTokenScope.TasksDelete)]
    public async Task<IActionResult> DeleteTask(Guid id)
    {
        var deleted = await taskService.DeleteAsync(id);
        if (!deleted) return NotFound();

        return NoContent();
    }

    [HttpGet("tasks/{taskId:guid}/subtasks")]
    [RequireIntegrationScope(IntegrationTokenScope.SubtasksRead)]
    public async Task<IActionResult> GetSubtasks(Guid taskId)
    {
        var task = await taskService.GetByIdAsync(taskId);
        if (task is null) return NotFound();

        return Ok(task.Subtasks);
    }

    [HttpPost("tasks/{taskId:guid}/subtasks")]
    [RequireIntegrationScope(IntegrationTokenScope.SubtasksCreate)]
    public async Task<IActionResult> CreateSubtask(Guid taskId, [FromBody] TaskSubtaskRequest request)
    {
        var updated = await subtaskService.CreateAsync(taskId, request);
        if (updated is null) return NotFound();

        return Ok(updated);
    }

    [HttpPatch("tasks/{taskId:guid}/subtasks/{subtaskId:guid}")]
    [RequireIntegrationScope(IntegrationTokenScope.SubtasksUpdate)]
    public async Task<IActionResult> UpdateSubtask(Guid taskId, Guid subtaskId, [FromBody] TaskSubtaskRequest request)
    {
        var updated = await subtaskService.UpdateAsync(taskId, subtaskId, request);
        if (updated is null) return NotFound();

        return Ok(updated);
    }

    [HttpDelete("tasks/{taskId:guid}/subtasks/{subtaskId:guid}")]
    [RequireIntegrationScope(IntegrationTokenScope.SubtasksDelete)]
    public async Task<IActionResult> DeleteSubtask(Guid taskId, Guid subtaskId)
    {
        var updated = await subtaskService.DeleteAsync(taskId, subtaskId);
        if (updated is null) return NotFound();

        return Ok(updated);
    }

    [HttpGet("tasks/{taskId:guid}/time-entries")]
    [RequireIntegrationScope(IntegrationTokenScope.TimeEntriesRead)]
    public async Task<IActionResult> GetTimeEntries(
        Guid taskId,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken ct)
    {
        var page = await integrationTaskQueryService.GetTimeEntriesAsync(taskId, BuildPageQuery(limit, offset), ct);
        if (page is null) return NotFound();

        return Ok(page);
    }

    [HttpPost("tasks/{taskId:guid}/time-entries")]
    [RequireIntegrationScope(IntegrationTokenScope.TimeEntriesCreate)]
    public async Task<IActionResult> CreateTimeEntry(Guid taskId, [FromBody] CreateTaskTimeEntryRequest request)
    {
        var created = await timeEntryService.CreateAsync(taskId, request);
        if (created is null) return NotFound();

        return Ok(created);
    }

    [HttpPatch("time-entries/{entryId:guid}")]
    [RequireIntegrationScope(IntegrationTokenScope.TimeEntriesUpdate)]
    public async Task<IActionResult> UpdateTimeEntry(Guid entryId, [FromBody] UpdateTaskTimeEntryRequest request)
    {
        var updated = await timeEntryService.UpdateAsync(entryId, request);
        if (updated is null) return NotFound();

        return Ok(updated);
    }

    [HttpDelete("time-entries/{entryId:guid}")]
    [RequireIntegrationScope(IntegrationTokenScope.TimeEntriesDelete)]
    public async Task<IActionResult> DeleteTimeEntry(Guid entryId)
    {
        var deleted = await timeEntryService.DeleteAsync(entryId);
        if (!deleted) return NotFound();

        return NoContent();
    }

    private static IntegrationPageQuery BuildPageQuery(int? limit, int? offset)
    {
        return new IntegrationPageQuery(
            Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize),
            Math.Max(offset ?? 0, 0));
    }

    private static IntegrationTaskQuery BuildQuery(
        Guid? projectId,
        TaskStatus? status,
        bool? isCompleted,
        DateOnly? dueBefore,
        DateTimeOffset? createdAfter,
        int? limit,
        int? offset)
    {
        return new IntegrationTaskQuery(
            projectId,
            status,
            isCompleted,
            dueBefore,
            createdAfter,
            Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize),
            Math.Max(offset ?? 0, 0));
    }
}

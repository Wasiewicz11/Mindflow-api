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
    IIntegrationSpaceService integrationSpaceService,
    IIntegrationTaskQueryService integrationTaskQueryService,
    IIntegrationDocsService integrationDocsService,
    IIntegrationExternalTaskService externalTaskService,
    IWebHostEnvironment environment) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    [HttpGet("docs")]
    public IActionResult Docs()
    {
        return Ok(integrationDocsService.Build());
    }

    [HttpGet("spaces")]
    [RequireIntegrationScope(IntegrationTokenScope.SpacesRead)]
    public async Task<IActionResult> GetSpaces(CancellationToken ct)
    {
        return Ok(await integrationSpaceService.GetSpacesAsync(ct));
    }

    [HttpGet("spaces/{spaceId:guid}")]
    [RequireIntegrationScope(IntegrationTokenScope.SpacesRead)]
    public async Task<IActionResult> GetSpace(Guid spaceId, CancellationToken ct)
    {
        var space = await integrationSpaceService.GetSpaceAsync(spaceId, ct);
        if (space is null) return NotFound();

        return Ok(space);
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

    [HttpGet("projects/{projectId:guid}")]
    [RequireIntegrationScope(IntegrationTokenScope.ProjectsRead)]
    public async Task<IActionResult> GetProject(Guid projectId, CancellationToken ct)
    {
        var project = await integrationProjectService.GetProjectAsync(projectId, ct);
        if (project is null) return NotFound();

        return Ok(project);
    }

    [HttpPost("projects")]
    [RequireIntegrationScope(IntegrationTokenScope.ProjectsCreate)]
    public async Task<IActionResult> CreateProject([FromBody] IntegrationCreateProjectRequest request, CancellationToken ct)
    {
        var result = await integrationProjectService.CreateProjectAsync(request, ct);
        if (result.Outcome == IntegrationProjectOutcome.Succeeded)
        {
            return CreatedAtAction(nameof(GetProject), new { projectId = result.Project!.Id }, result.Project);
        }

        return ProjectProblem(result.Outcome);
    }

    [HttpPatch("projects/{projectId:guid}")]
    [RequireIntegrationScope(IntegrationTokenScope.ProjectsUpdate)]
    public async Task<IActionResult> UpdateProject(
        Guid projectId,
        [FromBody] IntegrationUpdateProjectRequest request,
        CancellationToken ct)
    {
        var result = await integrationProjectService.UpdateProjectAsync(projectId, request, ct);
        return result.Outcome == IntegrationProjectOutcome.Succeeded ? Ok(result.Project) : ProjectProblem(result.Outcome);
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
        if (request.ProjectId is null && User.GetIntegrationSpaceId() is not null)
        {
            var defaultProjectId = User.GetIntegrationDefaultProjectId();
            if (defaultProjectId is null) return SpaceProjectRequired();

            request = request with { ProjectId = defaultProjectId };
        }

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

    [HttpGet("tasks/external/{source}/{externalId}")]
    [RequireIntegrationScope(IntegrationTokenScope.TasksRead)]
    public async Task<IActionResult> GetExternalTask(string source, string externalId, CancellationToken ct)
    {
        var rejection = RejectExternalReference(source, externalId, null);
        if (rejection is not null) return rejection;

        var task = await externalTaskService.GetAsync(source, externalId, ct);
        if (task is null) return NotFound();

        return Ok(task);
    }

    [HttpPut("tasks/external/{source}/{externalId}")]
    [IntegrationEndpointDoc(
        "tasks:create (new task) or tasks:update (existing task)",
        "Create or update the task linked to an id in an external system.")]
    public async Task<IActionResult> UpsertExternalTask(
        string source,
        string externalId,
        [FromBody] UpsertExternalTaskRequest request,
        CancellationToken ct)
    {
        var rejection = RejectExternalReference(source, externalId, request.ExternalUrl);
        if (rejection is not null) return rejection;

        var permissions = new ExternalTaskPermissions(
            User.HasIntegrationScope(IntegrationTokenScope.TasksCreate),
            User.HasIntegrationScope(IntegrationTokenScope.TasksUpdate));
        if (!permissions.CanCreate && !permissions.CanUpdate)
        {
            return Forbid(IntegrationTokenAuthenticationDefaults.Scheme);
        }

        var reference = new TaskExternalReference(source, externalId, request.ExternalUrl);
        var result = await externalTaskService.UpsertAsync(
            reference,
            request,
            permissions,
            User.GetIntegrationDefaultProjectId(),
            ct);

        return result.Outcome switch
        {
            ExternalTaskUpsertOutcome.Created => CreatedAtAction(nameof(GetExternalTask), new { source, externalId }, result.Task),
            ExternalTaskUpsertOutcome.Updated => Ok(result.Task),
            ExternalTaskUpsertOutcome.Forbidden => Forbid(IntegrationTokenAuthenticationDefaults.Scheme),
            ExternalTaskUpsertOutcome.ContentRequired => ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["content"] = ["Content is required to create a task."] })),
            ExternalTaskUpsertOutcome.ProjectRequired => SpaceProjectRequired(),
            _ => NotFound()
        };
    }

    [HttpDelete("tasks/external/{source}/{externalId}")]
    [RequireIntegrationScope(IntegrationTokenScope.TasksDelete)]
    public async Task<IActionResult> DeleteExternalTask(string source, string externalId, CancellationToken ct)
    {
        var rejection = RejectExternalReference(source, externalId, null);
        if (rejection is not null) return rejection;

        await externalTaskService.DeleteAsync(source, externalId, ct);
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
    public async Task<IActionResult> UpdateTimeEntry(
        Guid entryId,
        [FromBody] UpdateTaskTimeEntryRequest request,
        CancellationToken ct)
    {
        if (!await integrationTaskQueryService.CanReachTimeEntryAsync(entryId, ct)) return NotFound();

        var updated = await timeEntryService.UpdateAsync(entryId, request);
        if (updated is null) return NotFound();

        return Ok(updated);
    }

    [HttpDelete("time-entries/{entryId:guid}")]
    [RequireIntegrationScope(IntegrationTokenScope.TimeEntriesDelete)]
    public async Task<IActionResult> DeleteTimeEntry(Guid entryId, CancellationToken ct)
    {
        if (!await integrationTaskQueryService.CanReachTimeEntryAsync(entryId, ct)) return NotFound();

        var deleted = await timeEntryService.DeleteAsync(entryId);
        if (!deleted) return NotFound();

        return NoContent();
    }

    private IActionResult ProjectProblem(IntegrationProjectOutcome outcome)
    {
        return outcome switch
        {
            IntegrationProjectOutcome.SpaceRequired => ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["spaceId"] = ["Pass the spaceId the project belongs to."] })),
            IntegrationProjectOutcome.NameRequired => ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["name"] = ["Project name cannot be empty."] })),
            _ => NotFound()
        };
    }

    private IActionResult SpaceProjectRequired()
    {
        return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["projectId"] = ["This key is limited to one space: pass a projectId from that space or give the key a default project."]
        }));
    }

    private IActionResult? RejectExternalReference(string source, string externalId, string? externalUrl)
    {
        var errors = ExternalTaskReferenceRules.Validate(source, externalId, externalUrl, environment.IsDevelopment());
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));

        var clientId = User.GetIntegrationClientId();
        if (clientId is not null && !string.Equals(clientId, source, StringComparison.Ordinal))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Source not allowed",
                detail: $"This token can only use the source '{clientId}'.");
        }

        return null;
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

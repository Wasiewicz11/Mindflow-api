using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Repositories;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("projects/{projectId:guid}")]
[Authorize]
public class ProjectController(
    ITaskService taskService,
    IProjectTagRepository projectTagRepository,
    IAccessService accessService,
    ICurrentUserService currentUserService) : ControllerBase
{
    [HttpGet("tasks")]
    public async Task<IActionResult> GetAll(Guid projectId)
    {
        var tasks = await taskService.GetAllForProjectAsync(projectId);
        return Ok(tasks);
    }

    [HttpGet("tags")]
    public async Task<IActionResult> GetTags(Guid projectId)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        if (!await accessService.CanAccessProjectAsync(projectId, userId))
            throw new ForbiddenException("Access to this project is denied.");

        var tags = await projectTagRepository.GetNamesForProjectAsync(projectId);
        return Ok(tags);
    }
}

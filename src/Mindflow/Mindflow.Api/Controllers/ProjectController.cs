using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("projects/{projectId:guid}")]
[Authorize]
public class ProjectController(ITaskService taskService) : ControllerBase
{
    [HttpGet("tasks")]
    public async Task<IActionResult> GetAll(Guid projectId)
    {
        var tasks = await taskService.GetAllForProjectAsync(projectId);
        return Ok(tasks);
    }
}

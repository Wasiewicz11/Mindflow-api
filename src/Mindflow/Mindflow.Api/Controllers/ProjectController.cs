using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("projects/{projectId:guid}")]
[Authorize]
public class ProjectController(
    ITaskService taskService,
    IProjectTagService projectTagService) : ControllerBase
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
        var tags = await projectTagService.GetForProjectAsync(projectId);
        return Ok(tags);
    }

    [HttpPost("tags")]
    public async Task<IActionResult> CreateTag(Guid projectId, [FromBody] ProjectTagRequest request)
    {
        var tags = await projectTagService.CreateAsync(projectId, request);
        return Ok(tags);
    }

    [HttpPut("tags/{name}")]
    public async Task<IActionResult> RenameTag(Guid projectId, string name, [FromBody] ProjectTagRequest request)
    {
        var tags = await projectTagService.RenameAsync(projectId, name, request);
        return Ok(tags);
    }

    [HttpDelete("tags/{name}")]
    public async Task<IActionResult> DeleteTag(Guid projectId, string name)
    {
        var tags = await projectTagService.DeleteAsync(projectId, name);
        return Ok(tags);
    }
}

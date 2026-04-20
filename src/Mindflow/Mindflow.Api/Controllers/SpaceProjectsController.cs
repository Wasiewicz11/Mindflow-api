using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("spaces/{spaceId:guid}/projects")]
[Authorize]
public class SpaceProjectsController(IProjectService projectService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid spaceId)
    {
        var projects = await projectService.GetAllInSpaceForCurrentUserAsync(spaceId);
        return Ok(projects);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid spaceId, [FromBody] CreateProjectRequest request)
    {
        var created = await projectService.CreateInSpaceForCurrentUserAsync(spaceId, request);
        return Ok(created);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid spaceId, Guid id, [FromBody] UpdateProjectRequest request)
    {
        var updated = await projectService.UpdateInSpaceForCurrentUserAsync(id, spaceId, request);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid spaceId, Guid id)
    {
        await projectService.DeleteInSpaceForCurrentUserAsync(id, spaceId);

        return NoContent();
    }
}

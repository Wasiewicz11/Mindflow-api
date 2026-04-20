using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("spaces")]
[Authorize]
public class SpacesController(ISpaceService spaceService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var spaces = await spaceService.GetAllForCurrentUserAsync();
        return Ok(spaces);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSpaceRequest request)
    {
        var created = await spaceService.CreateForCurrentUserAsync(request);
        return Ok(created);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSpaceRequest request)
    {
        var updated = await spaceService.UpdateForCurrentUserAsync(id, request);

        if (updated is null)
            return NotFound();

        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await spaceService.DeleteForCurrentUserAsync(id);

        if (!deleted) return NotFound();

        return NoContent();
    }
}

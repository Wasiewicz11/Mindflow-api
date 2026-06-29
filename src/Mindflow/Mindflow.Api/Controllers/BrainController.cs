using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("brain")]
[Authorize]
public class BrainController(IBrainGraphService brainGraphService) : ControllerBase
{
    [HttpGet("graph")]
    public async Task<IActionResult> GetGraph()
    {
        var graph = await brainGraphService.GetDefaultAsync();
        return graph is null ? NotFound() : Ok(graph);
    }

    [HttpPut("graph")]
    public async Task<IActionResult> UpsertGraph([FromBody] BrainGraphDto request)
    {
        var saved = await brainGraphService.UpsertDefaultAsync(request);
        return Ok(saved);
    }
}

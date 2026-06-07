using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("suggestions")]
[Authorize]
public class SuggestionsController(ISuggestionService suggestionService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPending()
        => Ok(await suggestionService.GetPendingAsync());

    [HttpPost("{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id)
        => await suggestionService.AcceptAsync(id) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id)
        => await suggestionService.RejectAsync(id) ? NoContent() : NotFound();
}

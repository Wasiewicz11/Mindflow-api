using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Ai;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("ai/settings")]
[Authorize]
public class AiSettingsController(IAiSettingsService settingsService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await settingsService.GetAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] UpdateAiSettingsRequest request, CancellationToken ct)
        => Ok(await settingsService.SaveAsync(request, ct));

    [HttpDelete("key")]
    public async Task<IActionResult> DeleteKey(CancellationToken ct)
        => Ok(await settingsService.DeleteKeyAsync(ct));

    [HttpPost("test")]
    [EnableRateLimiting(AiRateLimits.Policy)]
    public async Task<IActionResult> Test([FromBody] TestAiConnectionRequest request, CancellationToken ct)
        => Ok(await settingsService.TestAsync(request, ct));
}

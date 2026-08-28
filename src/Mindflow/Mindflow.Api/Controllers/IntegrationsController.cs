using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Integrations;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("integrations")]
[Authorize]
public class IntegrationsController(IIntegrationService integrationService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        return Ok(await integrationService.GetSettingsAsync(ct));
    }

    [HttpGet("scopes")]
    public IActionResult GetScopes()
    {
        return Ok(IntegrationScopeCatalog.All
            .Select(definition => new IntegrationScopeDoc(
                definition.Name,
                definition.Scope.ToString(),
                definition.Description)));
    }

    [HttpPatch]
    public async Task<IActionResult> Update([FromBody] UpdateIntegrationSettingsRequest request, CancellationToken ct)
    {
        return Ok(await integrationService.UpdateSettingsAsync(request, ct));
    }

    [HttpPost("tokens")]
    public async Task<IActionResult> CreateToken([FromBody] CreateIntegrationTokenRequest request, CancellationToken ct)
    {
        var created = await integrationService.CreateTokenAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { }, created);
    }

    [HttpDelete("tokens/{id:guid}")]
    public async Task<IActionResult> RevokeToken(Guid id, CancellationToken ct)
    {
        var revoked = await integrationService.RevokeTokenAsync(id, ct);
        if (!revoked) return NotFound();

        return NoContent();
    }
}

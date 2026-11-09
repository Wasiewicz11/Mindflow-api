using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Integrations;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("integrations")]
[Authorize]
public class IntegrationsController(
    IIntegrationService integrationService,
    IIntegrationAuthorizationService authorizationService) : ControllerBase
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

    [HttpGet("authorize/preview")]
    public IActionResult PreviewAuthorization(
        [FromQuery(Name = "client_id")] string? clientId,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery(Name = "scope")] string? scope)
    {
        return Ok(authorizationService.Preview(clientId, redirectUri, scope));
    }

    [HttpPost("authorize")]
    public async Task<IActionResult> Authorize([FromBody] IntegrationAuthorizationRequest request, CancellationToken ct)
    {
        return Ok(await authorizationService.AuthorizeAsync(request, ct));
    }

    [HttpDelete("tokens/{id:guid}")]
    public async Task<IActionResult> RevokeToken(Guid id, CancellationToken ct)
    {
        var revoked = await integrationService.RevokeTokenAsync(id, ct);
        if (!revoked) return NotFound();

        return NoContent();
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mindflow.Api.Authentication;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Integrations;

namespace Mindflow.Api.Controllers;

[Route(IntegrationRoutes.OAuth)]
[EnableRateLimiting(IntegrationRoutes.OAuthRateLimitPolicy)]
public class IntegrationOAuthController(
    IIntegrationAuthorizationService authorizationService,
    IIntegrationService integrationService) : ControllerBase
{
    [HttpPost("token")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [IntegrationEndpointDoc("client_id + client_secret", "Exchange an authorization code for an integration token.")]
    public Task<IActionResult> TokenFromJson([FromBody] OAuthTokenRequest? request, CancellationToken ct) =>
        ExchangeAsync(request, ct);

    [HttpPost("token")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    [IntegrationEndpointDoc("client_id + client_secret", "Exchange an authorization code for an integration token.")]
    public Task<IActionResult> TokenFromForm([FromForm] OAuthTokenRequest? request, CancellationToken ct) =>
        ExchangeAsync(request, ct);

    [HttpPost("revoke")]
    [Authorize(AuthenticationSchemes = IntegrationTokenAuthenticationDefaults.Scheme)]
    [EnableRateLimiting(IntegrationRoutes.RateLimitPolicy)]
    [IntegrationEndpointDoc("any valid token", "Revoke the token sent in the Authorization header.")]
    public async Task<IActionResult> Revoke(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(IntegrationTokenAuthenticationDefaults.TokenIdClaim), out var tokenId))
        {
            return Unauthorized();
        }

        await integrationService.RevokeTokenAsync(tokenId, ct);
        return NoContent();
    }

    private async Task<IActionResult> ExchangeAsync(OAuthTokenRequest? request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        if (!ModelState.IsValid || request is null) throw new OAuthException(OAuthException.InvalidRequest);

        return Ok(await authorizationService.ExchangeCodeAsync(request, ct));
    }
}

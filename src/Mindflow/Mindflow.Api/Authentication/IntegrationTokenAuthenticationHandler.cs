using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Mindflow.Api.Services.Integrations;

namespace Mindflow.Api.Authentication;

public class IntegrationTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IIntegrationService integrationService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return AuthenticateResult.NoResult();
        }

        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("Integration API requires a bearer token.");
        }

        var token = authorization["Bearer ".Length..].Trim();
        var validation = await integrationService.ValidateTokenAsync(token, Context.RequestAborted);
        if (validation is null)
        {
            return AuthenticateResult.Fail("Invalid or disabled integration token.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, validation.UserId.ToString()),
            new(IntegrationTokenAuthenticationDefaults.AuthProviderClaim, IntegrationTokenAuthenticationDefaults.Scheme),
            new(IntegrationTokenAuthenticationDefaults.TokenIdClaim, validation.TokenId.ToString())
        };

        claims.AddRange(validation.Scopes.Select(scope =>
            new Claim(IntegrationTokenAuthenticationDefaults.ScopeClaim, IntegrationScopeCatalog.ToName(scope))));

        var identity = new ClaimsIdentity(claims, IntegrationTokenAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, IntegrationTokenAuthenticationDefaults.Scheme);

        return AuthenticateResult.Success(ticket);
    }
}

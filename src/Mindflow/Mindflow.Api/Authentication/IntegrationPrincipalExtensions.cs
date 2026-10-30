using System.Security.Claims;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Integrations;

namespace Mindflow.Api.Authentication;

public static class IntegrationPrincipalExtensions
{
    public static bool HasIntegrationScope(this ClaimsPrincipal user, IntegrationTokenScope scope)
    {
        var required = IntegrationScopeCatalog.ToName(scope);
        return user.Claims.Any(claim =>
            claim.Type == IntegrationTokenAuthenticationDefaults.ScopeClaim
            && string.Equals(claim.Value, required, StringComparison.Ordinal));
    }

    public static string? GetIntegrationClientId(this ClaimsPrincipal user) =>
        user.FindFirstValue(IntegrationTokenAuthenticationDefaults.ClientIdClaim);

    public static Guid? GetIntegrationDefaultProjectId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(IntegrationTokenAuthenticationDefaults.DefaultProjectIdClaim), out var projectId)
            ? projectId
            : null;
}

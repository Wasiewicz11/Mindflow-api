using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Integrations;

namespace Mindflow.Api.Authentication;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireIntegrationScopeAttribute(IntegrationTokenScope scope) : Attribute, IAuthorizationFilter
{
    public IntegrationTokenScope Scope => scope;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var required = IntegrationScopeCatalog.ToName(scope);
        var hasScope = user.Claims.Any(claim =>
            claim.Type == IntegrationTokenAuthenticationDefaults.ScopeClaim
            && string.Equals(claim.Value, required, StringComparison.Ordinal));

        if (!hasScope)
        {
            context.Result = new ForbidResult(IntegrationTokenAuthenticationDefaults.Scheme);
        }
    }
}

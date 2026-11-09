using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Mindflow.Api.Models.Enums;

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

        if (!user.HasIntegrationScope(scope))
        {
            context.Result = new ForbidResult(IntegrationTokenAuthenticationDefaults.Scheme);
        }
    }
}

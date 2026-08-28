using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Mindflow.Api.Authentication;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Integrations;

namespace Mindflow.Api.Tests;

public class RequireIntegrationScopeAttributeTests
{
    [Fact]
    public void Anonymous_request_is_rejected()
    {
        var context = BuildContext(new ClaimsPrincipal(new ClaimsIdentity()));

        new RequireIntegrationScopeAttribute(IntegrationTokenScope.TasksRead).OnAuthorization(context);

        Assert.IsType<UnauthorizedResult>(context.Result);
    }

    [Fact]
    public void Token_without_the_required_scope_is_forbidden()
    {
        var context = BuildContext(BuildPrincipal(IntegrationTokenScope.TasksRead));

        new RequireIntegrationScopeAttribute(IntegrationTokenScope.TasksDelete).OnAuthorization(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public void Token_with_the_required_scope_passes()
    {
        var context = BuildContext(BuildPrincipal(IntegrationTokenScope.TasksRead, IntegrationTokenScope.TasksCreate));

        new RequireIntegrationScopeAttribute(IntegrationTokenScope.TasksCreate).OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void Scope_of_another_kind_does_not_leak_across_resources()
    {
        var context = BuildContext(BuildPrincipal(IntegrationTokenScope.SubtasksDelete));

        new RequireIntegrationScopeAttribute(IntegrationTokenScope.TasksDelete).OnAuthorization(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    private static ClaimsPrincipal BuildPrincipal(params IntegrationTokenScope[] scopes)
    {
        var claims = scopes
            .Select(scope => new Claim(
                IntegrationTokenAuthenticationDefaults.ScopeClaim,
                IntegrationScopeCatalog.ToName(scope)))
            .ToList();

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, IntegrationTokenAuthenticationDefaults.Scheme));
    }

    private static AuthorizationFilterContext BuildContext(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        return new AuthorizationFilterContext(actionContext, []);
    }
}

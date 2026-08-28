using Microsoft.AspNetCore.Routing;
using Mindflow.Api.Authentication;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Integrations;

/// <summary>Builds the self-description of the integration API from the routing table, so it cannot drift from the real endpoints.</summary>
public class IntegrationDocsService(EndpointDataSource endpointDataSource) : IIntegrationDocsService
{
    private const string RoutePrefix = IntegrationRoutes.Base;

    public IntegrationDocsResponse Build()
    {
        var endpoints = endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(RoutePrefix, StringComparison.OrdinalIgnoreCase) == true)
            .Select(ToEndpointDoc)
            .OrderBy(endpoint => endpoint.Path, StringComparer.Ordinal)
            .ThenBy(endpoint => endpoint.Method, StringComparer.Ordinal)
            .ToList();

        var scopes = IntegrationScopeCatalog.All
            .Select(definition => new IntegrationScopeDoc(
                definition.Name,
                definition.Scope.ToString(),
                definition.Description))
            .ToList();

        return new IntegrationDocsResponse(
            "Mindflow Integration API",
            new IntegrationAuthDoc("bearer", "Authorization: Bearer <integration_token>"),
            scopes,
            endpoints);
    }

    private static IntegrationEndpointDoc ToEndpointDoc(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [];
        var required = endpoint.Metadata.GetMetadata<RequireIntegrationScopeAttribute>();

        var scope = required is null
            ? "any valid token"
            : IntegrationScopeCatalog.ToName(required.Scope);

        var description = required is null
            ? "Returns this API description."
            : IntegrationScopeCatalog.All.First(definition => definition.Scope == required.Scope).Description;

        return new IntegrationEndpointDoc(
            string.Join(", ", methods),
            $"/{endpoint.RoutePattern.RawText}",
            scope,
            description);
    }
}

using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Tests.Infrastructure;

public record AuthorizationGrant(string Code, string CodeVerifier);

public static class OAuthFlow
{
    public const string TokenUrl = "/api/integration/v1/oauth/token";

    public static async Task<AuthorizationGrant> AuthorizeAsync(
        MindflowApiFactory factory,
        Guid userId,
        Guid? defaultProjectId = null,
        params IntegrationTokenScope[] scopes)
    {
        var verifier = Pkce.CreateVerifier();
        using var client = factory.CreateUserClient(userId);
        var response = await client.PostAsJsonAsync(
            "/integrations/authorize",
            new IntegrationAuthorizationRequest(
                MindflowApiFactory.ClientId,
                MindflowApiFactory.RedirectUri,
                scopes.Length > 0 ? scopes : [IntegrationTokenScope.TasksRead, IntegrationTokenScope.TasksCreate, IntegrationTokenScope.TasksUpdate],
                "state",
                Pkce.Challenge(verifier),
                "S256",
                defaultProjectId),
            MindflowApiFactory.Json);
        response.EnsureSuccessStatusCode();

        var redirect = (await response.Content.ReadFromJsonAsync<IntegrationAuthorizationResponse>(MindflowApiFactory.Json))!.RedirectUrl;
        var code = QueryHelpers.ParseQuery(new Uri(redirect).Query)["code"].ToString();
        return new AuthorizationGrant(code, verifier);
    }

    public static Dictionary<string, string> TokenRequest(AuthorizationGrant grant) => new()
    {
        ["grant_type"] = "authorization_code",
        ["code"] = grant.Code,
        ["redirect_uri"] = MindflowApiFactory.RedirectUri,
        ["client_id"] = MindflowApiFactory.ClientId,
        ["client_secret"] = MindflowApiFactory.ClientSecret,
        ["code_verifier"] = grant.CodeVerifier
    };

    public static async Task<string> ConnectAsync(MindflowApiFactory factory, Guid userId, Guid? defaultProjectId = null)
    {
        var grant = await AuthorizeAsync(factory, userId, defaultProjectId);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(TokenUrl, TokenRequest(grant));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<OAuthTokenResponse>())!.AccessToken;
    }
}

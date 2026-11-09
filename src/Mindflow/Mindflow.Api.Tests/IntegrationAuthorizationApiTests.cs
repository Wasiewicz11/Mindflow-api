using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests;

public class IntegrationAuthorizationApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private const string Scope = "ProjectsRead TasksRead TasksCreate TasksUpdate";

    [LocalDatabaseFact]
    public async Task Preview_describes_the_registered_client()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateUserClient(user.Id);

        var response = await client.GetAsync(PreviewUrl(MindflowApiFactory.ClientId, MindflowApiFactory.RedirectUri, Scope));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<IntegrationAuthorizationPreviewResponse>(MindflowApiFactory.Json);
        Assert.Equal("Rendmark", preview!.Name);
        Assert.Equal(MindflowApiFactory.RedirectUri, preview.RedirectUri);
        Assert.Equal(
            [IntegrationTokenScope.ProjectsRead, IntegrationTokenScope.TasksRead, IntegrationTokenScope.TasksCreate, IntegrationTokenScope.TasksUpdate],
            preview.Scopes);
    }

    [LocalDatabaseFact]
    public async Task Preview_rejects_unknown_client_foreign_redirect_and_disallowed_scope()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateUserClient(user.Id);

        var unknownClient = await client.GetAsync(PreviewUrl("evil", MindflowApiFactory.RedirectUri, Scope));
        var foreignRedirect = await client.GetAsync(PreviewUrl(MindflowApiFactory.ClientId, "https://evil.example.com/callback", Scope));
        var disallowedScope = await client.GetAsync(PreviewUrl(MindflowApiFactory.ClientId, MindflowApiFactory.RedirectUri, "TasksRead SubtasksDelete"));
        var numericScope = await client.GetAsync(PreviewUrl(MindflowApiFactory.ClientId, MindflowApiFactory.RedirectUri, "1"));

        Assert.Equal(HttpStatusCode.BadRequest, unknownClient.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreignRedirect.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, disallowedScope.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, numericScope.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Preview_requires_a_signed_in_user()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(PreviewUrl(MindflowApiFactory.ClientId, MindflowApiFactory.RedirectUri, Scope));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Authorize_redirects_with_code_and_state_and_enables_integrations()
    {
        var user = await factory.CreateUserAsync(integrationsEnabled: false);
        using var client = factory.CreateUserClient(user.Id);

        var response = await client.PostAsJsonAsync("/integrations/authorize", Request(Pkce.Challenge(Pkce.CreateVerifier())), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var redirect = new Uri((await response.Content.ReadFromJsonAsync<IntegrationAuthorizationResponse>(MindflowApiFactory.Json))!.RedirectUrl);
        Assert.StartsWith(MindflowApiFactory.RedirectUri + "?", redirect.ToString());

        var query = QueryHelpers.ParseQuery(redirect.Query);
        Assert.Equal("state-123", query["state"].ToString());
        var code = query["code"].ToString();
        Assert.False(string.IsNullOrEmpty(code));

        var stored = await factory.WithDbAsync(db => db.IntegrationAuthorizationCodes.SingleAsync(item => item.UserId == user.Id));
        Assert.NotEqual(code, stored.CodeHash);
        Assert.Equal(MindflowApiFactory.Sha256Hex(code), stored.CodeHash);
        Assert.InRange(stored.ExpiresAt - stored.CreatedAt, TimeSpan.FromMinutes(4.9), TimeSpan.FromMinutes(5.1));
        Assert.True(await factory.WithDbAsync(db => db.Users.Where(item => item.Id == user.Id).Select(item => item.IntegrationsEnabled).SingleAsync()));
    }

    [LocalDatabaseFact]
    public async Task Authorize_rejects_plain_pkce_and_malformed_challenge()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateUserClient(user.Id);
        var challenge = Pkce.Challenge(Pkce.CreateVerifier());

        var plain = await client.PostAsJsonAsync("/integrations/authorize", Request(challenge) with { CodeChallengeMethod = "plain" }, MindflowApiFactory.Json);
        var malformed = await client.PostAsJsonAsync("/integrations/authorize", Request("too-short"), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, plain.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Authorize_rejects_a_default_project_of_another_user()
    {
        var owner = await factory.CreateUserAsync();
        var stranger = await factory.CreateUserAsync();
        var project = await factory.CreateProjectAsync(owner.Id);
        using var client = factory.CreateUserClient(stranger.Id);

        var response = await client.PostAsJsonAsync(
            "/integrations/authorize",
            Request(Pkce.Challenge(Pkce.CreateVerifier())) with { DefaultProjectId = project.Id },
            MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string PreviewUrl(string clientId, string redirectUri, string scope) =>
        QueryHelpers.AddQueryString("/integrations/authorize/preview", new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = scope
        });

    private static IntegrationAuthorizationRequest Request(string codeChallenge) =>
        new(
            MindflowApiFactory.ClientId,
            MindflowApiFactory.RedirectUri,
            [IntegrationTokenScope.TasksRead, IntegrationTokenScope.TasksCreate],
            "state-123",
            codeChallenge,
            "S256",
            null);
}

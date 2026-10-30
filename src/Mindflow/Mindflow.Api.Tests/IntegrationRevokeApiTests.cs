using System.Net;
using System.Net.Http.Json;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests;

public class IntegrationRevokeApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private const string RevokeUrl = "/api/integration/v1/oauth/revoke";

    [LocalDatabaseFact]
    public async Task App_revokes_its_own_token()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateIntegrationClient(await OAuthFlow.ConnectAsync(factory, user.Id));

        var revoke = await client.PostAsync(RevokeUrl, null);
        var afterwards = await client.GetAsync("/api/integration/v1/tasks");

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);

        using var settingsClient = factory.CreateUserClient(user.Id);
        var settings = await settingsClient.GetFromJsonAsync<IntegrationSettingsResponse>("/integrations", MindflowApiFactory.Json);
        Assert.True(Assert.Single(settings!.Tokens).IsRevoked);
    }

    [LocalDatabaseFact]
    public async Task Revoke_requires_a_valid_token()
    {
        using var anonymous = factory.CreateClient();
        using var bogus = factory.CreateIntegrationClient("mf_not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(RevokeUrl, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bogus.PostAsync(RevokeUrl, null)).StatusCode);
    }
}

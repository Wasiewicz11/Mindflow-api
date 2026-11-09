using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests;

public class IntegrationTokenExchangeApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    [LocalDatabaseFact]
    public async Task Code_is_exchanged_for_a_token_bound_to_the_client()
    {
        var user = await factory.CreateUserAsync();
        var project = await factory.CreateProjectAsync(user.Id);
        var grant = await OAuthFlow.AuthorizeAsync(factory, user.Id, project.Id);
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, OAuthFlow.TokenRequest(grant));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var token = (await response.Content.ReadFromJsonAsync<OAuthTokenResponse>())!;
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal("TasksRead TasksCreate TasksUpdate", token.Scope);
        Assert.Equal(user.Email, token.Account.Email);
        Assert.Equal(project.Id, token.DefaultProjectId);
        Assert.InRange(token.ExpiresIn, TimeSpan.FromDays(364).TotalSeconds, TimeSpan.FromDays(365).TotalSeconds);

        using var settingsClient = factory.CreateUserClient(user.Id);
        var settings = await settingsClient.GetFromJsonAsync<IntegrationSettingsResponse>("/integrations", MindflowApiFactory.Json);
        var listed = Assert.Single(settings!.Tokens);
        Assert.Equal(("rendmark", "Rendmark", project.Id), (listed.ClientId, listed.ClientName, listed.DefaultProjectId));
    }

    [LocalDatabaseFact]
    public async Task Form_encoded_token_request_is_accepted()
    {
        var user = await factory.CreateUserAsync();
        var grant = await OAuthFlow.AuthorizeAsync(factory, user.Id);
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsync(OAuthFlow.TokenUrl, new FormUrlEncodedContent(OAuthFlow.TokenRequest(grant)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("mf_", (await response.Content.ReadFromJsonAsync<OAuthTokenResponse>())!.AccessToken);
    }

    [LocalDatabaseFact]
    public async Task Code_can_be_used_only_once()
    {
        var user = await factory.CreateUserAsync();
        var grant = await OAuthFlow.AuthorizeAsync(factory, user.Id);
        using var anonymous = factory.CreateClient();

        var first = await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, OAuthFlow.TokenRequest(grant));
        var second = await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, OAuthFlow.TokenRequest(grant));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await AssertOAuthErrorAsync(second, "invalid_grant");
    }

    [LocalDatabaseFact]
    public async Task Wrong_verifier_is_rejected_and_burns_the_code()
    {
        var user = await factory.CreateUserAsync();
        var grant = await OAuthFlow.AuthorizeAsync(factory, user.Id);
        using var anonymous = factory.CreateClient();

        var wrong = await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, OAuthFlow.TokenRequest(grant with { CodeVerifier = Pkce.CreateVerifier() }));
        var retry = await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, OAuthFlow.TokenRequest(grant));

        await AssertOAuthErrorAsync(wrong, "invalid_grant");
        await AssertOAuthErrorAsync(retry, "invalid_grant");
    }

    [LocalDatabaseFact]
    public async Task Wrong_secret_or_unknown_client_is_rejected_without_using_the_code()
    {
        var user = await factory.CreateUserAsync();
        var grant = await OAuthFlow.AuthorizeAsync(factory, user.Id);
        using var anonymous = factory.CreateClient();

        var wrongSecret = OAuthFlow.TokenRequest(grant);
        wrongSecret["client_secret"] = "not-the-secret";
        var unknownClient = OAuthFlow.TokenRequest(grant);
        unknownClient["client_id"] = "evil";

        await AssertOAuthErrorAsync(await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, wrongSecret), "invalid_client");
        await AssertOAuthErrorAsync(await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, unknownClient), "invalid_client");
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, OAuthFlow.TokenRequest(grant))).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Redirect_uri_must_match_the_authorization()
    {
        var user = await factory.CreateUserAsync();
        var grant = await OAuthFlow.AuthorizeAsync(factory, user.Id);
        using var anonymous = factory.CreateClient();

        var request = OAuthFlow.TokenRequest(grant);
        request["redirect_uri"] = "https://rendmark.test/other";

        await AssertOAuthErrorAsync(await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, request), "invalid_grant");
    }

    [LocalDatabaseFact]
    public async Task Expired_code_is_rejected()
    {
        var user = await factory.CreateUserAsync();
        var grant = await OAuthFlow.AuthorizeAsync(factory, user.Id);
        await factory.WithDbAsync(db => db.IntegrationAuthorizationCodes
            .Where(code => code.UserId == user.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(code => code.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1))));
        using var anonymous = factory.CreateClient();

        await AssertOAuthErrorAsync(await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, OAuthFlow.TokenRequest(grant)), "invalid_grant");
    }

    [LocalDatabaseFact]
    public async Task Malformed_requests_get_oauth_errors()
    {
        using var anonymous = factory.CreateClient();
        var grant = new AuthorizationGrant("code", Pkce.CreateVerifier());

        var unsupported = OAuthFlow.TokenRequest(grant);
        unsupported["grant_type"] = "client_credentials";
        var missing = OAuthFlow.TokenRequest(grant);
        missing.Remove("code_verifier");

        await AssertOAuthErrorAsync(await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, unsupported), "unsupported_grant_type");
        await AssertOAuthErrorAsync(await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, missing), "invalid_request");
        await AssertOAuthErrorAsync(
            await anonymous.PostAsync(OAuthFlow.TokenUrl, new StringContent("{not json", System.Text.Encoding.UTF8, "application/json")),
            "invalid_request");
    }

    [LocalDatabaseFact]
    public async Task Reconnecting_revokes_the_previous_token_of_the_client()
    {
        var user = await factory.CreateUserAsync();
        var first = await OAuthFlow.ConnectAsync(factory, user.Id);
        var second = await OAuthFlow.ConnectAsync(factory, user.Id);

        using var firstClient = factory.CreateIntegrationClient(first);
        using var secondClient = factory.CreateIntegrationClient(second);

        Assert.Equal(HttpStatusCode.Unauthorized, (await firstClient.GetAsync("/api/integration/v1/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondClient.GetAsync("/api/integration/v1/tasks")).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task App_token_uses_only_its_own_source_and_default_project()
    {
        var user = await factory.CreateUserAsync();
        var project = await factory.CreateProjectAsync(user.Id);
        using var client = factory.CreateIntegrationClient(await OAuthFlow.ConnectAsync(factory, user.Id, project.Id));
        var body = new UpsertExternalTaskRequest("Zlecenie · Montaż", null, null, null, null, null, null, null, null, null);

        var foreign = await client.PutAsJsonAsync("/api/integration/v1/tasks/external/jira/1", body, MindflowApiFactory.Json);
        var own = await client.PutAsJsonAsync("/api/integration/v1/tasks/external/rendmark/1", body, MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Created, own.StatusCode);
        Assert.Equal(project.Id, (await own.Content.ReadFromJsonAsync<TaskDetailResponse>(MindflowApiFactory.Json))!.ProjectId);
    }

    private static async Task AssertOAuthErrorAsync(HttpResponseMessage response, string error)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(error, document.RootElement.GetProperty("error").GetString());
    }
}

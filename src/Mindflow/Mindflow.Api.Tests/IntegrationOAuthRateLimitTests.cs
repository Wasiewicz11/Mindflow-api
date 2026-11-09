using System.Net;
using System.Net.Http.Json;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests;

public class LowOAuthLimitFactory : MindflowApiFactory
{
    protected override int OAuthRateLimitPermitsPerMinute => 2;
}

public class IntegrationOAuthRateLimitTests(LowOAuthLimitFactory factory) : IClassFixture<LowOAuthLimitFactory>
{
    [LocalDatabaseFact]
    public async Task Token_endpoint_is_rate_limited_per_client()
    {
        using var anonymous = factory.CreateClient();
        var request = OAuthFlow.TokenRequest(new AuthorizationGrant("code", Pkce.CreateVerifier()));
        request["client_secret"] = "wrong";

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            statuses.Add((await anonymous.PostAsJsonAsync(OAuthFlow.TokenUrl, request)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests], statuses);
    }
}

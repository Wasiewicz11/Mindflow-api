namespace Mindflow.Api.Services.Integrations;

public static class IntegrationRoutes
{
    /// <summary>Version lives in the path: the integration API can move to v2 without touching the rest of the API.</summary>
    public const string Base = "api/integration/v1";

    public const string RateLimitPolicy = "integration-api";

    public const string OAuth = Base + "/oauth";

    public const string OAuthTokenPath = "/" + OAuth + "/token";

    public const string OAuthRateLimitPolicy = "integration-oauth";
}

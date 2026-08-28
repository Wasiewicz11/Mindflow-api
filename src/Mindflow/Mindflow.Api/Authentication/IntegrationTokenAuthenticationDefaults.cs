namespace Mindflow.Api.Authentication;

public static class IntegrationTokenAuthenticationDefaults
{
    public const string Scheme = "IntegrationToken";
    public const string AuthProviderClaim = "auth_provider";
    public const string TokenIdClaim = "integration_token_id";
    public const string ScopeClaim = "integration_scope";
}

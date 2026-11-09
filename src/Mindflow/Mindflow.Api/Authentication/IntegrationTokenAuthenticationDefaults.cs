namespace Mindflow.Api.Authentication;

public static class IntegrationTokenAuthenticationDefaults
{
    public const string Scheme = "IntegrationToken";
    public const string AuthProviderClaim = "auth_provider";
    public const string TokenIdClaim = "integration_token_id";
    public const string ScopeClaim = "integration_scope";
    public const string ClientIdClaim = "integration_client_id";
    public const string DefaultProjectIdClaim = "integration_default_project_id";
    public const string SpaceIdClaim = "integration_space_id";
}

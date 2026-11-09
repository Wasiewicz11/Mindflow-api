using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Integrations;

public class IntegrationClientsOptions
{
    public const string SectionName = "Integrations";

    public Dictionary<string, IntegrationClientOptions> Clients { get; set; } = new(StringComparer.Ordinal);
}

public class IntegrationClientOptions
{
    public string Name { get; set; } = "";

    /// <summary>Lowercase hex SHA-256 of the client secret. Empty keeps the client disabled.</summary>
    public string? SecretHash { get; set; }

    public List<string> RedirectUris { get; set; } = [];

    public List<IntegrationTokenScope> AllowedScopes { get; set; } = [];

    public int TokenLifetimeDays { get; set; } = 365;
}

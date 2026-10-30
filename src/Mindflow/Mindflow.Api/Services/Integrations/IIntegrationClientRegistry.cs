using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Integrations;

public interface IIntegrationClientRegistry
{
    IntegrationClient? Find(string? clientId);
    bool VerifySecret(IntegrationClient client, string? secret);
}

public record IntegrationClient(
    string Id,
    string Name,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyCollection<IntegrationTokenScope> AllowedScopes,
    int TokenLifetimeDays,
    byte[] SecretHash);

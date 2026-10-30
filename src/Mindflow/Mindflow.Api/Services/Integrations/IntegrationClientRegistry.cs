using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Mindflow.Api.Services.Integrations;

public class IntegrationClientRegistry(IOptions<IntegrationClientsOptions> options) : IIntegrationClientRegistry
{
    private readonly Dictionary<string, IntegrationClient> clients = options.Value.Clients
        .Where(entry => IsUsable(entry.Value))
        .ToDictionary(
            entry => entry.Key,
            entry => new IntegrationClient(
                entry.Key,
                entry.Value.Name.Trim(),
                entry.Value.RedirectUris.Where(uri => !string.IsNullOrWhiteSpace(uri)).ToList(),
                entry.Value.AllowedScopes.Distinct().ToList(),
                entry.Value.TokenLifetimeDays,
                Convert.FromHexString(entry.Value.SecretHash!)),
            StringComparer.Ordinal);

    public IntegrationClient? Find(string? clientId)
    {
        if (string.IsNullOrEmpty(clientId)) return null;
        return clients.GetValueOrDefault(clientId);
    }

    public bool VerifySecret(IntegrationClient client, string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return false;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return CryptographicOperations.FixedTimeEquals(hash, client.SecretHash);
    }

    public static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsUsable(IntegrationClientOptions client) =>
        !string.IsNullOrWhiteSpace(client.Name)
        && IsSha256Hex(client.SecretHash)
        && client.RedirectUris.Any(uri => !string.IsNullOrWhiteSpace(uri))
        && client.AllowedScopes.Count > 0
        && client.TokenLifetimeDays > 0;
}

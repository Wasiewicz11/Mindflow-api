using System.Security.Claims;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services;

internal static class AuthProviderResolver
{
    private const string ProviderClaimType = "auth_provider";
    private static readonly HashSet<string> GoogleIssuers = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts.google.com",
        "https://accounts.google.com"
    };

    public static AuthProvider Resolve(ClaimsPrincipal claims)
    {
        var providerClaim = claims.FindFirstValue(ProviderClaimType);
        if (!string.IsNullOrWhiteSpace(providerClaim) &&
            Enum.TryParse<AuthProvider>(providerClaim, ignoreCase: true, out var parsedProvider))
        {
            return parsedProvider;
        }

        var issuer = claims.FindFirstValue("iss")?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(issuer))
            throw new UnauthorizedAccessException("Missing token issuer.");

        return GoogleIssuers.Contains(issuer) 
            ? AuthProvider.Google 
            : throw new UnauthorizedAccessException($"Unsupported token issuer '{issuer}'.");
    }
}

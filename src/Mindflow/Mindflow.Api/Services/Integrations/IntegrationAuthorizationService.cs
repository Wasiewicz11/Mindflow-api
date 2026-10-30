using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services.Integrations;

public partial class IntegrationAuthorizationService(
    MindflowDbContext db,
    ICurrentUserService currentUserService,
    IAccessService accessService,
    IIntegrationClientRegistry clientRegistry,
    IIntegrationAuthorizationCodeRepository codeRepository) : IIntegrationAuthorizationService
{
    public const string CodeChallengeMethod = "S256";
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    [GeneratedRegex(@"^[A-Za-z0-9_-]{43}\z")]
    private static partial Regex CodeChallengePattern();

    public IntegrationAuthorizationPreviewResponse Preview(string? clientId, string? redirectUri, string? scope)
    {
        var client = ResolveClient(clientId, redirectUri);
        var scopes = ParseScopes(scope);
        EnsureScopesAllowed(client, scopes);

        return new IntegrationAuthorizationPreviewResponse(client.Id, client.Name, scopes, redirectUri!);
    }

    public async Task<IntegrationAuthorizationResponse> AuthorizeAsync(
        IntegrationAuthorizationRequest request,
        CancellationToken ct = default)
    {
        var client = ResolveClient(request.ClientId, request.RedirectUri);
        var scopes = (request.Scopes ?? []).Distinct().ToList();
        if (scopes.Count == 0) throw new BadRequestException("At least one scope is required.");
        EnsureScopesAllowed(client, scopes);

        if (!string.Equals(request.CodeChallengeMethod, CodeChallengeMethod, StringComparison.Ordinal))
        {
            throw new BadRequestException($"Only the {CodeChallengeMethod} code challenge method is supported.");
        }

        if (request.CodeChallenge is null || !CodeChallengePattern().IsMatch(request.CodeChallenge))
        {
            throw new BadRequestException("Code challenge must be a base64url SHA-256 hash (43 characters).");
        }

        var userId = await currentUserService.GetCurrentUserIdAsync();
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new NotFoundException("User not found.");

        if (request.DefaultProjectId is Guid projectId && !await accessService.CanAccessProjectAsync(projectId, userId))
        {
            throw new BadRequestException("Default project not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var plainTextCode = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        user.IntegrationsEnabled = true;
        await codeRepository.DeleteExpiredAsync(now.AddDays(-1), ct);
        await codeRepository.AddAsync(new IntegrationAuthorizationCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ClientId = client.Id,
            CodeHash = HashCode(plainTextCode),
            RedirectUri = request.RedirectUri,
            Scopes = scopes.Select(scope => scope.ToString()).ToList(),
            CodeChallenge = request.CodeChallenge,
            DefaultProjectId = request.DefaultProjectId,
            CreatedAt = now,
            ExpiresAt = now.Add(CodeLifetime)
        }, ct);

        var query = new Dictionary<string, string?> { ["code"] = plainTextCode };
        if (!string.IsNullOrEmpty(request.State)) query["state"] = request.State;

        return new IntegrationAuthorizationResponse(QueryHelpers.AddQueryString(request.RedirectUri, query));
    }

    public static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();

    private IntegrationClient ResolveClient(string? clientId, string? redirectUri)
    {
        var client = clientRegistry.Find(clientId)
            ?? throw new BadRequestException("Unknown integration client.");

        if (redirectUri is null || !client.RedirectUris.Contains(redirectUri, StringComparer.Ordinal))
        {
            throw new BadRequestException("Redirect URI is not registered for this client.");
        }

        return client;
    }

    private static void EnsureScopesAllowed(IntegrationClient client, IReadOnlyCollection<IntegrationTokenScope> scopes)
    {
        if (scopes.Any(scope => !client.AllowedScopes.Contains(scope)))
        {
            throw new BadRequestException("Requested scope is not allowed for this client.");
        }
    }

    private static List<IntegrationTokenScope> ParseScopes(string? scope)
    {
        var names = (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0) throw new BadRequestException("At least one scope is required.");

        var scopes = new List<IntegrationTokenScope>();
        foreach (var name in names)
        {
            if (!IntegrationScopeCatalog.TryParse(name, out var parsed))
            {
                throw new BadRequestException($"Unknown scope '{name}'.");
            }

            if (!scopes.Contains(parsed)) scopes.Add(parsed);
        }

        return scopes;
    }
}

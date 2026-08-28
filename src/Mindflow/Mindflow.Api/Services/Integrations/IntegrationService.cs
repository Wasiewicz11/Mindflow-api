using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services.Integrations;

public class IntegrationService(
    MindflowDbContext db,
    ICurrentUserService currentUserService,
    IIntegrationTokenRepository tokenRepository,
    IOptions<IntegrationTokenOptions> optionsAccessor,
    ILogger<IntegrationService> logger) : IIntegrationService
{
    private const string TokenPrefix = "mf_";

    private readonly IntegrationTokenOptions options = optionsAccessor.Value;
    private readonly byte[]? tokenHashKey = optionsAccessor.Value.IsConfigured
        ? Encoding.UTF8.GetBytes(optionsAccessor.Value.HashPepper!)
        : null;

    public async Task<IntegrationSettingsResponse> GetSettingsAsync(CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new NotFoundException("User not found.");

        return await BuildSettingsAsync(userId, user.IntegrationsEnabled, ct);
    }

    public async Task<IntegrationSettingsResponse> UpdateSettingsAsync(
        UpdateIntegrationSettingsRequest request,
        CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new NotFoundException("User not found.");

        user.IntegrationsEnabled = request.Enabled;
        await db.SaveChangesAsync(ct);

        return await BuildSettingsAsync(userId, user.IntegrationsEnabled, ct);
    }

    public async Task<CreateIntegrationTokenResponse> CreateTokenAsync(
        CreateIntegrationTokenRequest request,
        CancellationToken ct = default)
    {
        if (tokenHashKey is null)
        {
            throw new ServiceUnavailableException(
                $"Integration tokens are unavailable: {IntegrationTokenOptions.SectionName}:HashPepper is not configured.");
        }

        var userId = await currentUserService.GetCurrentUserIdAsync();
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new NotFoundException("User not found.");

        if (!user.IntegrationsEnabled)
        {
            throw new BadRequestException("Integrations must be enabled before creating tokens.");
        }

        var now = DateTimeOffset.UtcNow;
        if (await tokenRepository.CountActiveForUserAsync(userId, now, ct) >= options.MaxActiveTokensPerUser)
        {
            throw new BadRequestException(
                $"A user can have at most {options.MaxActiveTokensPerUser} active integration tokens.");
        }

        var name = NormalizeName(request.Name);
        var scopes = NormalizeScopes(request.Scopes);
        var expiresAt = NormalizeExpiresAt(request.ExpiresAt, now);
        var plainTextToken = GenerateToken();

        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            TokenHash = HashToken(plainTextToken),
            TokenPrefix = DisplayPrefix(plainTextToken),
            CreatedAt = now,
            ExpiresAt = expiresAt,
            IsRevoked = false
        };

        foreach (var scope in scopes)
        {
            token.Permissions.Add(new IntegrationTokenPermission
            {
                Id = Guid.NewGuid(),
                IntegrationTokenId = token.Id,
                Scope = scope
            });
        }

        await tokenRepository.AddAsync(token, ct);

        return new CreateIntegrationTokenResponse(
            token.Id,
            token.Name,
            token.TokenPrefix,
            scopes,
            token.CreatedAt,
            token.ExpiresAt,
            plainTextToken);
    }

    public async Task<bool> RevokeTokenAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var token = await tokenRepository.GetByIdForUserAsync(id, userId, ct);
        if (token is null) return false;
        if (token.IsRevoked) return true;

        token.IsRevoked = true;
        token.RevokedAt = DateTimeOffset.UtcNow;
        await tokenRepository.SaveChangesAsync(ct);

        return true;
    }

    public async Task<IntegrationTokenValidationResult?> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        if (tokenHashKey is null)
        {
            logger.LogWarning(
                "Integration token rejected: {Section}:HashPepper is not configured.",
                IntegrationTokenOptions.SectionName);
            return null;
        }

        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var integrationToken = await tokenRepository.GetActiveByHashAsync(HashToken(token), ct);
        var now = DateTimeOffset.UtcNow;

        if (integrationToken?.User is null
            || !integrationToken.User.IntegrationsEnabled
            || integrationToken.ExpiresAt <= now)
        {
            return null;
        }

        if (integrationToken.LastUsedAt is null
            || now - integrationToken.LastUsedAt.Value >= TimeSpan.FromMinutes(options.LastUsedThrottleMinutes))
        {
            await tokenRepository.TouchLastUsedAsync(integrationToken.Id, now, ct);
        }

        return new IntegrationTokenValidationResult(
            integrationToken.UserId,
            integrationToken.Id,
            ToScopes(integrationToken));
    }

    private async Task<IntegrationSettingsResponse> BuildSettingsAsync(
        Guid userId,
        bool integrationsEnabled,
        CancellationToken ct)
    {
        var tokens = await tokenRepository.GetForUserAsync(userId, ct);

        return new IntegrationSettingsResponse(
            integrationsEnabled,
            tokens.Select(MapToken).ToList());
    }

    private static IntegrationTokenResponse MapToken(IntegrationToken token)
    {
        return new IntegrationTokenResponse(
            token.Id,
            token.Name,
            token.TokenPrefix,
            ToScopes(token),
            token.CreatedAt,
            token.ExpiresAt,
            token.LastUsedAt,
            token.IsRevoked,
            token.RevokedAt);
    }

    private static IReadOnlyCollection<IntegrationTokenScope> ToScopes(IntegrationToken token)
    {
        return token.Permissions
            .Select(permission => permission.Scope)
            .Where(IntegrationScopeCatalog.IsKnown)
            .Distinct()
            .ToList();
    }

    private static IReadOnlyCollection<IntegrationTokenScope> NormalizeScopes(
        IReadOnlyCollection<IntegrationTokenScope>? scopes)
    {
        if (scopes is null || scopes.Count == 0)
        {
            throw new BadRequestException("At least one integration scope is required.");
        }

        var normalized = scopes.Distinct().ToList();
        if (normalized.Any(scope => !IntegrationScopeCatalog.IsKnown(scope)))
        {
            throw new BadRequestException("Invalid integration scope.");
        }

        return normalized;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BadRequestException("Integration token name is required.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length > 100)
        {
            throw new BadRequestException("Integration token name cannot be longer than 100 characters.");
        }

        return trimmed;
    }

    private DateTimeOffset NormalizeExpiresAt(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        var normalized = expiresAt.ToUniversalTime();

        if (normalized <= now)
        {
            throw new BadRequestException("Integration token expiry must be in the future.");
        }

        if (normalized > now.AddDays(options.MaxLifetimeDays))
        {
            throw new BadRequestException(
                $"Integration token expiry cannot be more than {options.MaxLifetimeDays} days away.");
        }

        return normalized;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return $"{TokenPrefix}{Base64UrlEncoder.Encode(bytes)}";
    }

    private string HashToken(string token)
    {
        var bytes = HMACSHA256.HashData(tokenHashKey!, Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Shown in the UI so a token can be told apart: scheme prefix plus the first few random characters.</summary>
    private static string DisplayPrefix(string token)
    {
        return token[..Math.Min(TokenPrefix.Length + 6, token.Length)];
    }
}

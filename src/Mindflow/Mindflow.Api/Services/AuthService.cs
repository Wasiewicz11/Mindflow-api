using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services;

public class AuthService(
    MindflowDbContext db,
    TokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IStorageService storageService) : IAuthService
{
    public async Task<(string AccessToken, Guid RefreshToken)> RegisterAsync(
        string sub, string email, string firstName, string lastName,
        string? googleAvatarUrl, AuthProvider provider)
    {
        var exists = await db.UserIdentities.AnyAsync(ui =>
            ui.Provider == provider && ui.ProviderUserId == sub);

        if (exists)
            throw new InvalidOperationException("User already exists.");

        var userId = Guid.NewGuid();
        string? avatarPath = null;

        if (googleAvatarUrl is not null)
            avatarPath = await storageService.UploadFromUrlAsync(googleAvatarUrl, $"avatars/{userId}");

        var user = new User
        {
            Id = userId,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            AvatarUrl = avatarPath,
            TimeZone = "UTC",
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Users.Add(user);
        db.UserIdentities.Add(new UserIdentity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Provider = provider,
            ProviderUserId = sub,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        return await GenerateTokensAsync(user);
    }

    public async Task<(string AccessToken, Guid RefreshToken)> LoginAsync(string sub, AuthProvider provider)
    {
        var identity = await db.UserIdentities
            .Include(ui => ui.User)
            .FirstOrDefaultAsync(ui => ui.Provider == provider && ui.ProviderUserId == sub);

        if (identity is null)
            throw new InvalidOperationException("User not found.");

        return await GenerateTokensAsync(identity.User);
    }

    public async Task LogoutAsync(Guid refreshToken)
    {
        await refreshTokenRepository.RevokeAsync(refreshToken);
    }

    public async Task<(string AccessToken, Guid RefreshToken)> RefreshAsync(Guid refreshToken)
    {
        var existing = await refreshTokenRepository.GetByTokenAsync(refreshToken);

        if (existing is null || existing.IsRevoked || existing.ExpiresAt < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Invalid or expired refresh token.");

        await refreshTokenRepository.RevokeAsync(refreshToken);

        var user = await db.Users.FindAsync(existing.UserId)
            ?? throw new InvalidOperationException("User not found.");

        return await GenerateTokensAsync(user);
    }

    private async Task<(string AccessToken, Guid RefreshToken)> GenerateTokensAsync(User user)
    {
        var accessToken = tokenService.GenerateAccessToken(user.Id, user.Email);
        var refreshToken = tokenService.GenerateRefreshToken(user.Id);

        await refreshTokenRepository.AddAsync(refreshToken);

        return (accessToken, refreshToken.Token);
    }
}

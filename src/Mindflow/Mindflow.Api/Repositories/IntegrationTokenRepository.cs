using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class IntegrationTokenRepository(MindflowDbContext db) : IIntegrationTokenRepository
{
    public async Task<IReadOnlyList<IntegrationToken>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        return await db.IntegrationTokens
            .AsNoTracking()
            .Include(token => token.Permissions)
            .Where(token => token.UserId == userId)
            .OrderByDescending(token => token.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<int> CountActiveForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        return db.IntegrationTokens.CountAsync(token =>
            token.UserId == userId
            && !token.IsRevoked
            && token.ExpiresAt > now, ct);
    }

    public async Task<IntegrationToken?> GetActiveByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        return await db.IntegrationTokens
            .AsNoTracking()
            .Include(token => token.Permissions)
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash && !token.IsRevoked, ct);
    }

    public async Task<IntegrationToken?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        return await db.IntegrationTokens
            .FirstOrDefaultAsync(token => token.Id == id && token.UserId == userId, ct);
    }

    public async Task AddAsync(IntegrationToken token, CancellationToken ct = default)
    {
        await db.IntegrationTokens.AddAsync(token, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task TouchLastUsedAsync(Guid id, DateTimeOffset now, CancellationToken ct = default)
    {
        return db.IntegrationTokens
            .Where(token => token.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.LastUsedAt, now), ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class IntegrationAuthorizationCodeRepository(MindflowDbContext db) : IIntegrationAuthorizationCodeRepository
{
    public async Task AddAsync(IntegrationAuthorizationCode code, CancellationToken ct = default)
    {
        await db.IntegrationAuthorizationCodes.AddAsync(code, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IntegrationAuthorizationCode?> ConsumeAsync(
        string codeHash,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var consumed = await db.IntegrationAuthorizationCodes
            .Where(code => code.CodeHash == codeHash && code.ConsumedAt == null && code.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(code => code.ConsumedAt, now), ct);

        if (consumed == 0) return null;

        return await db.IntegrationAuthorizationCodes
            .AsNoTracking()
            .Include(code => code.User)
            .FirstOrDefaultAsync(code => code.CodeHash == codeHash, ct);
    }

    public Task DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct = default)
    {
        return db.IntegrationAuthorizationCodes
            .Where(code => code.ExpiresAt < before)
            .ExecuteDeleteAsync(ct);
    }
}

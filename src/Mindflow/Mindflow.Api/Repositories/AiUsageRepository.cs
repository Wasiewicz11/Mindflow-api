using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class AiUsageRepository(MindflowDbContext db) : IAiUsageRepository
{
    public async Task<int> GetAiCallsAsync(Guid userId, DateOnly date)
    {
        var row = await db.AiUsageDaily
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId && u.Date == date);
        return row?.AiCalls ?? 0;
    }

    public async Task IncrementAiCallsAsync(Guid userId, DateOnly date)
    {
        var row = await db.AiUsageDaily
            .FirstOrDefaultAsync(u => u.UserId == userId && u.Date == date);

        if (row is null)
        {
            db.AiUsageDaily.Add(new AiUsageDaily { UserId = userId, Date = date, AiCalls = 1 });
        }
        else
        {
            row.AiCalls++;
        }

        await db.SaveChangesAsync();
    }

    public async Task<int> GetDictationCallsAsync(Guid userId, DateOnly date, CancellationToken ct = default)
    {
        var row = await db.AiUsageDaily
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId && u.Date == date, ct);
        return row?.DictationCalls ?? 0;
    }

    public async Task IncrementDictationCallsAsync(Guid userId, DateOnly date, CancellationToken ct = default)
    {
        var updated = await db.AiUsageDaily
            .Where(u => u.UserId == userId && u.Date == date)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DictationCalls, u => u.DictationCalls + 1), ct);
        if (updated > 0) return;

        db.AiUsageDaily.Add(new AiUsageDaily { UserId = userId, Date = date, DictationCalls = 1 });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            await db.AiUsageDaily
                .Where(u => u.UserId == userId && u.Date == date)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DictationCalls, u => u.DictationCalls + 1), ct);
        }
    }
}

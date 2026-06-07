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
}

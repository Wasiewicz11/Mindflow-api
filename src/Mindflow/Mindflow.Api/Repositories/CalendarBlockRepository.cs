using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Repositories;

public class CalendarBlockRepository(MindflowDbContext db) : ICalendarBlockRepository
{
    public async Task<IEnumerable<CalendarBlock>> GetForUserInRangeAsync(Guid userId, DateTimeOffset from, DateTimeOffset to)
    {
        return await db.CalendarBlocks
            .Where(b => b.UserId == userId && b.StartAt >= from && b.StartAt < to)
            .OrderBy(b => b.StartAt)
            .ToListAsync();
    }

    public async Task<CalendarBlock?> GetByIdAsync(Guid id)
    {
        return await db.CalendarBlocks.FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<CalendarBlock?> GetByExternalEventIdAsync(Guid userId, string externalEventId)
    {
        return await db.CalendarBlocks
            .FirstOrDefaultAsync(b => b.UserId == userId && b.ExternalEventId == externalEventId);
    }

    public async Task<IReadOnlyList<CalendarBlock>> GetByProviderAsync(Guid userId, CalendarBlockProvider provider)
    {
        return await db.CalendarBlocks
            .Where(b => b.UserId == userId && b.Provider == provider)
            .ToListAsync();
    }

    public async Task<CalendarBlock> CreateAsync(CalendarBlock block)
    {
        db.CalendarBlocks.Add(block);
        await db.SaveChangesAsync();
        return block;
    }

    public async Task<CalendarBlock> UpdateAsync(CalendarBlock block)
    {
        db.CalendarBlocks.Update(block);
        await db.SaveChangesAsync();
        return block;
    }

    public async Task<bool> DeleteAsync(CalendarBlock block)
    {
        db.CalendarBlocks.Remove(block);
        await db.SaveChangesAsync();
        return true;
    }
}

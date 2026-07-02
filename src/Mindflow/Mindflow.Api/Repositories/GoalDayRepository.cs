using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class GoalDayRepository(MindflowDbContext db) : IGoalDayRepository
{
    public async Task<IReadOnlyCollection<GoalDay>> GetAllForUserAsync(Guid userId)
    {
        return await db.GoalDays
            .AsNoTracking()
            .Where(day => day.UserId == userId)
            .OrderBy(day => day.Date)
            .ToListAsync();
    }

    public async Task<GoalDay?> GetByDateAsync(Guid userId, DateOnly date)
    {
        return await db.GoalDays
            .FirstOrDefaultAsync(day => day.UserId == userId && day.Date == date);
    }

    public async Task<GoalDay> UpsertAsync(GoalDay goalDay)
    {
        var existing = await db.GoalDays
            .FirstOrDefaultAsync(day => day.UserId == goalDay.UserId && day.Date == goalDay.Date);

        if (existing is null)
        {
            db.GoalDays.Add(goalDay);
            await db.SaveChangesAsync();
            return goalDay;
        }

        existing.DayShort = goalDay.DayShort;
        existing.DateLabel = goalDay.DateLabel;
        existing.Title = goalDay.Title;
        existing.MarkerLevel = goalDay.MarkerLevel;
        existing.SectionsJson = goalDay.SectionsJson;
        existing.LinkedTaskIdsJson = goalDay.LinkedTaskIdsJson;
        existing.UpdatedAt = goalDay.UpdatedAt;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(Guid userId, DateOnly date)
    {
        var existing = await db.GoalDays
            .FirstOrDefaultAsync(day => day.UserId == userId && day.Date == date);
        if (existing is null) return false;

        db.GoalDays.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }
}

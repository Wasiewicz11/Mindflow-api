using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class GoogleCalendarConnectionRepository(MindflowDbContext db) : IGoogleCalendarConnectionRepository
{
    public Task<GoogleCalendarConnection?> GetByUserIdAsync(Guid userId) =>
        db.GoogleCalendarConnections.FirstOrDefaultAsync(c => c.UserId == userId);

    public Task<GoogleCalendarConnection?> GetByWatchChannelIdAsync(string channelId) =>
        db.GoogleCalendarConnections.FirstOrDefaultAsync(c => c.WatchChannelId == channelId);

    public async Task<IReadOnlyList<GoogleCalendarConnection>> GetWatchesExpiringBeforeAsync(DateTimeOffset threshold) =>
        await db.GoogleCalendarConnections
            .Where(c => c.WatchExpiresAt == null || c.WatchExpiresAt < threshold)
            .ToListAsync();

    public async Task<GoogleCalendarConnection> CreateAsync(GoogleCalendarConnection connection)
    {
        db.GoogleCalendarConnections.Add(connection);
        await db.SaveChangesAsync();
        return connection;
    }

    public async Task<GoogleCalendarConnection> UpdateAsync(GoogleCalendarConnection connection)
    {
        db.GoogleCalendarConnections.Update(connection);
        await db.SaveChangesAsync();
        return connection;
    }

    public async Task DeleteAsync(GoogleCalendarConnection connection)
    {
        db.GoogleCalendarConnections.Remove(connection);
        await db.SaveChangesAsync();
    }
}

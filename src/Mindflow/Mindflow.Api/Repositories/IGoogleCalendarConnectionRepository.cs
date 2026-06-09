using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface IGoogleCalendarConnectionRepository
{
    Task<GoogleCalendarConnection?> GetByUserIdAsync(Guid userId);
    Task<GoogleCalendarConnection?> GetByWatchChannelIdAsync(string channelId);
    Task<IReadOnlyList<GoogleCalendarConnection>> GetWatchesExpiringBeforeAsync(DateTimeOffset threshold);
    Task<GoogleCalendarConnection> CreateAsync(GoogleCalendarConnection connection);
    Task<GoogleCalendarConnection> UpdateAsync(GoogleCalendarConnection connection);
    Task DeleteAsync(GoogleCalendarConnection connection);
}

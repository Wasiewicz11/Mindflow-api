using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Repositories;

public interface ICalendarBlockRepository
{
    Task<IEnumerable<CalendarBlock>> GetForUserInRangeAsync(Guid userId, DateTimeOffset from, DateTimeOffset to);
    Task<CalendarBlock?> GetByIdAsync(Guid id);
    Task<CalendarBlock?> GetByExternalEventIdAsync(Guid userId, string externalEventId);
    Task<IReadOnlyList<CalendarBlock>> GetByProviderAsync(Guid userId, CalendarBlockProvider provider);
    Task<CalendarBlock> CreateAsync(CalendarBlock block);
    Task<CalendarBlock> UpdateAsync(CalendarBlock block);
    Task<bool> DeleteAsync(CalendarBlock block);
}

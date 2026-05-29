using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface ICalendarBlockRepository
{
    Task<IEnumerable<CalendarBlock>> GetForUserInRangeAsync(Guid userId, DateTimeOffset from, DateTimeOffset to);
    Task<CalendarBlock?> GetByIdAsync(Guid id);
    Task<CalendarBlock> CreateAsync(CalendarBlock block);
    Task<CalendarBlock> UpdateAsync(CalendarBlock block);
    Task<bool> DeleteAsync(CalendarBlock block);
}

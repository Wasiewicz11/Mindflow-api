using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface ICalendarBlockService
{
    Task<IEnumerable<CalendarBlockResponse>> GetAsync(DateOnly from, DateOnly to);
    Task<CalendarBlockResponse?> CreateAsync(CreateCalendarBlockRequest request);
    Task<CalendarBlockResponse?> UpdateAsync(Guid id, UpdateCalendarBlockRequest request);
    Task<bool> DeleteAsync(Guid id);
}

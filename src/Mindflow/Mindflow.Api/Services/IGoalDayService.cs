using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface IGoalDayService
{
    Task<IReadOnlyCollection<GoalDayResponse>> GetAllAsync();
    Task<GoalDayResponse?> GetByDateAsync(DateOnly date);
    Task<GoalDayResponse> UpsertAsync(DateOnly date, UpsertGoalDayRequest request);
    Task<bool> DeleteAsync(DateOnly date);
}

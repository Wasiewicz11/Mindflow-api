using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface IGoalDayRepository
{
    Task<IReadOnlyCollection<GoalDay>> GetAllForUserAsync(Guid userId);
    Task<GoalDay?> GetByDateAsync(Guid userId, DateOnly date);
    Task<GoalDay> UpsertAsync(GoalDay goalDay);
    Task<bool> DeleteAsync(Guid userId, DateOnly date);
}

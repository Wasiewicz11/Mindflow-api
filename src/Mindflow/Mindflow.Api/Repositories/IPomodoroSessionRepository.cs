using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface IPomodoroSessionRepository
{
    Task<PomodoroSessionState?> GetByUserIdAsync(Guid userId);
    Task<PomodoroSessionState> SaveAsync(PomodoroSessionState session);
    Task DeleteAsync(PomodoroSessionState session);
}

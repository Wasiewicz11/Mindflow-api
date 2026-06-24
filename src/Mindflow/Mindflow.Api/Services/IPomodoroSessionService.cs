using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface IPomodoroSessionService
{
    Task<PomodoroSessionResponse?> GetAsync();
    Task<PomodoroSessionResponse> UpsertAsync(UpsertPomodoroSessionRequest request);
    Task DeleteAsync();
}

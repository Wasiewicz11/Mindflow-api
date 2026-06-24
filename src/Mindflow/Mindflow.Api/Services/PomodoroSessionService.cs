using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services;

public class PomodoroSessionService(
    IPomodoroSessionRepository repository,
    ICurrentUserService currentUserService,
    IAccessService accessService,
    IPomodoroEventBroker eventBroker) : IPomodoroSessionService
{
    public async Task<PomodoroSessionResponse?> GetAsync()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var session = await repository.GetByUserIdAsync(userId);
        return session is null ? null : ToResponse(session);
    }

    public async Task<PomodoroSessionResponse> UpsertAsync(UpsertPomodoroSessionRequest request)
    {
        var title = request.Title.Trim();
        if (title.Length == 0)
            throw new BadRequestException("Pomodoro session title is required.");
        if (request.RemainingSeconds > request.TotalSeconds)
            throw new BadRequestException("Remaining seconds cannot exceed total seconds.");
        if (!Enum.IsDefined(request.Phase))
            throw new BadRequestException("Pomodoro phase is invalid.");
        if (request.IsRunning && request.EndsAt is null)
            throw new BadRequestException("Running Pomodoro session requires an end time.");

        var userId = await currentUserService.GetCurrentUserIdAsync();
        if (request.TaskId is Guid taskId && !await accessService.CanAccessTaskAsync(taskId, userId))
            throw new NotFoundException($"Task with id {taskId} not found");

        var now = DateTimeOffset.UtcNow;
        var session = await repository.GetByUserIdAsync(userId);
        if (session is null)
        {
            session = new PomodoroSessionState
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = title,
                CreatedAt = now
            };
        }

        session.TaskId = request.TaskId;
        session.Title = title;
        session.Phase = request.Phase;
        session.TotalSeconds = request.TotalSeconds;
        session.RemainingSeconds = request.RemainingSeconds;
        session.IsRunning = request.IsRunning;
        session.EndsAt = request.IsRunning ? request.EndsAt?.ToUniversalTime() : null;
        session.UpdatedAt = now;

        var saved = await repository.SaveAsync(session);
        eventBroker.Publish(userId, "updated");
        return ToResponse(saved);
    }

    public async Task DeleteAsync()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var session = await repository.GetByUserIdAsync(userId);
        if (session is not null)
        {
            await repository.DeleteAsync(session);
            eventBroker.Publish(userId, "deleted");
        }
    }

    private static PomodoroSessionResponse ToResponse(PomodoroSessionState session) => new(
        session.Id,
        session.TaskId,
        session.Title,
        session.Phase,
        session.TotalSeconds,
        session.RemainingSeconds,
        session.IsRunning,
        session.EndsAt,
        session.UpdatedAt);
}

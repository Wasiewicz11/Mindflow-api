using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models.Dtos;

public record UpsertPomodoroSessionRequest(
    Guid? TaskId,
    [Required, MaxLength(255)] string Title,
    PomodoroPhase Phase,
    [Range(1, 86400)] int TotalSeconds,
    [Range(0, 86400)] int RemainingSeconds,
    bool IsRunning,
    DateTimeOffset? EndsAt);

public record PomodoroSessionResponse(
    Guid Id,
    Guid? TaskId,
    string Title,
    PomodoroPhase Phase,
    int TotalSeconds,
    int RemainingSeconds,
    bool IsRunning,
    DateTimeOffset? EndsAt,
    DateTimeOffset UpdatedAt);

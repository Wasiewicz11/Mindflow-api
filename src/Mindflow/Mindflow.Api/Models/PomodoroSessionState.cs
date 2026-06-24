using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

public class PomodoroSessionState
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TaskId { get; set; }

    [MaxLength(255)]
    public required string Title { get; set; }

    public PomodoroPhase Phase { get; set; }
    public int TotalSeconds { get; set; }
    public int RemainingSeconds { get; set; }
    public bool IsRunning { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}

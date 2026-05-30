using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

public class TaskActivityEvent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? SpaceId { get; set; }
    public Guid? ProjectId { get; set; }
    public TaskActivityEventType EventType { get; set; }
    public TaskActivitySource Source { get; set; }
    public TaskActivityActorType ActorType { get; set; }
    public Guid? ActorId { get; set; }
    [MaxLength(255)]
    public string? SessionId { get; set; }
    [MaxLength(255)]
    public string? RequestId { get; set; }
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

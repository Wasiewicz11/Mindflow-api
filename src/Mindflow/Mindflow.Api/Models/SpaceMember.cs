using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

public class SpaceMember
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public Guid UserId { get; set; }
    public SpaceRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class SpaceInvitation
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    [MaxLength(20)]
    public required string Code { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

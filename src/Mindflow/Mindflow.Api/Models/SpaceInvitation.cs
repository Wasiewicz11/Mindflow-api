namespace Mindflow.Api.Models;

public class SpaceInvitation
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public string Code { get; set; } = "";
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

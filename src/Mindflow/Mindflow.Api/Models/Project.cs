namespace Mindflow.Api.Models;

public class Project
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#9CA3AF";
    public Guid? SpaceId { get; set; }
    public DateTime CreatedAt { get; set; }
}

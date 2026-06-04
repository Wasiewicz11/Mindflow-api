using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class ProjectTag
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    [MaxLength(50)]
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

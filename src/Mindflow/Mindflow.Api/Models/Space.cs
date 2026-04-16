using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class Space
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(40)]
    public required string Name { get; set; }
    [MaxLength(7)]
    public required string Color { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

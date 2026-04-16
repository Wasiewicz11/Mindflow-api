using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class User
{
    public Guid Id { get; set; }
    [MaxLength(50)]
    public required string Email { get; set; }
    [MaxLength(50)]
    public required string TimeZone { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

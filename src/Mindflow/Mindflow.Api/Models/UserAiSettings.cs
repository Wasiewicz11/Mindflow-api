using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

public class UserAiSettings
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AiProvider Provider { get; set; }

    public string? ApiKeyEncrypted { get; set; }

    [MaxLength(4)]
    public string? ApiKeyLast4 { get; set; }

    [MaxLength(100)]
    public string? Model { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}

using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

/// <summary>Telegram chat linked to a user, or a pending link code.</summary>
public class TelegramConnection
{
    public Guid UserId { get; set; }
    public long? ChatId { get; set; }

    [MaxLength(64)]
    public string? Username { get; set; }

    [MaxLength(64)]
    public string? LinkCodeHash { get; set; }

    public DateTimeOffset? LinkCodeExpiresAt { get; set; }
    public DateTimeOffset? ConnectedAt { get; set; }
}

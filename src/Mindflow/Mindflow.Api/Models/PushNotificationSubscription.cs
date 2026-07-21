using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class PushNotificationSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(2048)]
    public required string Endpoint { get; set; }
    [MaxLength(255)]
    public required string P256dh { get; set; }
    [MaxLength(255)]
    public required string Auth { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

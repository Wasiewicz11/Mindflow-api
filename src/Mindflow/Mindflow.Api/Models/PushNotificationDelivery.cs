using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class PushNotificationDelivery
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(255)]
    public required string DeliveryKey { get; set; }
    public DateTimeOffset SentAt { get; set; }
}

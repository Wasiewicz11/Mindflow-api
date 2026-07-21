using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

public class NotificationInboxItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationInboxKind Kind { get; set; }
    [MaxLength(160)]
    public required string Title { get; set; }
    [MaxLength(2000)]
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

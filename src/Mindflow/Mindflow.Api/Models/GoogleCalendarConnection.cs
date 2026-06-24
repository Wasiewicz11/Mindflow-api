using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class GoogleCalendarConnection
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    [MaxLength(255)]
    public required string GoogleAccountEmail { get; set; }

    public required string RefreshTokenEncrypted { get; set; }
    public string? AccessTokenEncrypted { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    [MaxLength(255)]
    public required string DedicatedCalendarId { get; set; }

    [MaxLength(255)]
    public string? SourceCalendarId { get; set; }

    public string? SyncToken { get; set; }

    [MaxLength(255)]
    public string? WatchChannelId { get; set; }
    [MaxLength(512)]
    public string? WatchResourceId { get; set; }
    [MaxLength(255)]
    public string? WatchToken { get; set; }
    public DateTimeOffset? WatchExpiresAt { get; set; }

    public bool RequiresReconnect { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
